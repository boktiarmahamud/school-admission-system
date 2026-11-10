using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using SchoolAdmission.Api.Data;
using SchoolAdmission.Api.DTOs;
using SchoolAdmission.Api.Entities;
using SchoolAdmission.Api.Options;

namespace SchoolAdmission.Api.Services;

public class HoldService
{
    private readonly ApplicationDbContext _db;
    private readonly SeatCountService _seats;
    private readonly AdmissionOptions _options;

    public HoldService(
        ApplicationDbContext db,
        SeatCountService seats,
        IOptions<AdmissionOptions> options)
    {
        _db = db;
        _seats = seats;
        _options = options.Value;
    }

    // ---------- CREATE (the critical path) ----------
    public async Task<HoldResponse> CreateAsync(
        Guid classId, string studentNumber, CancellationToken ct)
    {
        var studentId = await GetOrCreateStudentIdAsync(studentNumber, ct);

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            // 1. Lock the class row. Concurrent requests for this class queue up here.
            var locked = (await _db.Classes
                .FromSqlInterpolated($"SELECT * FROM classes WHERE \"Id\" = {classId} FOR UPDATE")
                .AsNoTracking()
                .ToListAsync(ct)).FirstOrDefault();

            if (locked is null)
                throw new AppException(404, "Class not found.");

            var now = DateTime.UtcNow;

            // 2. Mark this student's stale holds as Expired so they don't block the unique index.
            await _db.Holds
                .Where(h => h.StudentId == studentId
                         && h.Status == HoldStatus.Held
                         && h.ExpiresAt <= now)
                .ExecuteUpdateAsync(s => s.SetProperty(h => h.Status, HoldStatus.Expired), ct);

            // 3. One-active-hold rule
            if (await _db.Holds.AnyAsync(h => h.StudentId == studentId
                                           && h.Status == HoldStatus.Confirmed, ct))
                throw new AppException(409, "This student already has a confirmed admission.");

            if (await _db.Holds.AnyAsync(h => h.StudentId == studentId
                                           && h.Status == HoldStatus.Held, ct))
                throw new AppException(409, "This student already has an active hold.");

            // 4. Recount INSIDE the lock (same DbContext = same transaction)
            var counts = await _seats.GetCountsAsync(classId, ct);
            var available = locked.TotalSeats - counts.Held - counts.Confirmed;
            if (available <= 0)
                throw new AppException(409, "No seats are available for this class.");

            // 5. Insert the hold
            var hold = new Hold
            {
                Id = Guid.NewGuid(),
                ClassId = classId,
                StudentId = studentId,
                Status = HoldStatus.Held,
                CreatedAt = now,
                ExpiresAt = now.AddMinutes(_options.HoldDurationMinutes)
            };
            _db.Holds.Add(hold);
            await _db.SaveChangesAsync(ct);

            await tx.CommitAsync(ct);

            return Map(hold, studentNumber, locked.Name, now);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            // Safety net: the same student raced into two holds at once
            throw new AppException(409, "This student already has an active hold.");
        }
    }

    // ---------- GET ONE ----------
    public async Task<HoldResponse> GetAsync(Guid holdId, string? studentNumber, CancellationToken ct)
    {
        var hold = await LoadAsync(holdId, studentNumber, ct);
        if (hold.Status == HoldStatus.Held && hold.ExpiresAt <= DateTime.UtcNow)
        {
            await MarkExpiredAsync(holdId, ct);
            hold = await LoadAsync(holdId, studentNumber, ct);
        }
        return Map(hold);
    }

    // ---------- CONFIRM ----------
    public async Task<HoldResponse> ConfirmAsync(Guid holdId, string studentNumber, CancellationToken ct)
    {
        var hold = await LoadAsync(holdId, studentNumber, ct);

        if (hold.Status == HoldStatus.Confirmed)
            return Map(hold);                       // idempotent

        var now = DateTime.UtcNow;

        if (hold.Status == HoldStatus.Held && hold.ExpiresAt > now)
        {
            // Atomic: only one request can flip Held -> Confirmed
            var rows = await _db.Holds
                .Where(h => h.Id == holdId
                         && h.Status == HoldStatus.Held
                         && h.ExpiresAt > now)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(h => h.Status, HoldStatus.Confirmed)
                    .SetProperty(h => h.ConfirmedAt, (DateTime?)now), ct);

            hold = await LoadAsync(holdId, studentNumber, ct);
            if (rows == 1 || hold.Status == HoldStatus.Confirmed)
                return Map(hold);
        }

        if (hold.Status == HoldStatus.Held)
        {
            await MarkExpiredAsync(holdId, ct);
            throw new AppException(409, "This hold has expired.");
        }

        throw new AppException(409,
            $"Only an active hold can be confirmed (current status: {hold.Status.ToString().ToUpperInvariant()}).");
    }

    // ---------- CANCEL ----------
    public async Task<HoldResponse> CancelAsync(Guid holdId, string studentNumber, CancellationToken ct)
    {
        var hold = await LoadAsync(holdId, studentNumber, ct);
        var now = DateTime.UtcNow;

        if (hold.Status == HoldStatus.Held && hold.ExpiresAt > now)
        {
            var rows = await _db.Holds
                .Where(h => h.Id == holdId
                         && h.Status == HoldStatus.Held
                         && h.ExpiresAt > now)
                .ExecuteUpdateAsync(s => s.SetProperty(h => h.Status, HoldStatus.Cancelled), ct);

            if (rows == 1)
                return Map(await LoadAsync(holdId, studentNumber, ct));

            hold = await LoadAsync(holdId, studentNumber, ct);
        }

        if (hold.Status == HoldStatus.Held)
            await MarkExpiredAsync(holdId, ct);

        throw new AppException(409,
            "Only an active hold can be cancelled (it is already confirmed, cancelled or expired).");
    }

    // ---------- MY HOLDS ----------
    public async Task<List<HoldResponse>> GetMyHoldsAsync(string studentNumber, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        await _db.Holds
            .Where(h => h.Student!.BirthRegistrationNumber == studentNumber
                     && h.Status == HoldStatus.Held
                     && h.ExpiresAt <= now)
            .ExecuteUpdateAsync(s => s.SetProperty(h => h.Status, HoldStatus.Expired), ct);

        var holds = await _db.Holds.AsNoTracking()
            .Include(h => h.Class)
            .Include(h => h.Student)
            .Where(h => h.Student!.BirthRegistrationNumber == studentNumber)
            .OrderByDescending(h => h.CreatedAt)
            .ToListAsync(ct);

        return holds.Select(h => Map(h)).ToList();
    }

    // ---------- helpers ----------
    private async Task<Guid> GetOrCreateStudentIdAsync(string number, CancellationToken ct)
    {
        var existing = await _db.Students.AsNoTracking()
            .Where(s => s.BirthRegistrationNumber == number)
            .Select(s => (Guid?)s.Id)
            .FirstOrDefaultAsync(ct);
        if (existing.HasValue) return existing.Value;

        // Safe if two requests with the same new ID arrive together
        await _db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO students (""Id"", ""BirthRegistrationNumber"", ""CreatedAt"")
            VALUES ({Guid.NewGuid()}, {number}, {DateTime.UtcNow})
            ON CONFLICT (""BirthRegistrationNumber"") DO NOTHING", ct);

        return await _db.Students.AsNoTracking()
            .Where(s => s.BirthRegistrationNumber == number)
            .Select(s => s.Id)
            .FirstAsync(ct);
    }

    // studentNumber == null means "don't check the owner"
    private async Task<Hold> LoadAsync(Guid holdId, string? studentNumber, CancellationToken ct)
    {
        var hold = await _db.Holds.AsNoTracking()
            .Include(h => h.Class)
            .Include(h => h.Student)
            .FirstOrDefaultAsync(h => h.Id == holdId, ct);

        if (hold is null ||
            (studentNumber is not null && hold.Student!.BirthRegistrationNumber != studentNumber))
            throw new AppException(404, "Hold not found.");

        return hold;
    }

    private Task<int> MarkExpiredAsync(Guid holdId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        return _db.Holds
            .Where(h => h.Id == holdId && h.Status == HoldStatus.Held && h.ExpiresAt <= now)
            .ExecuteUpdateAsync(s => s.SetProperty(h => h.Status, HoldStatus.Expired), CancellationToken.None);
    }

    private static HoldResponse Map(Hold h) =>
        Map(h, h.Student!.BirthRegistrationNumber, h.Class!.Name, DateTime.UtcNow);

    private static HoldResponse Map(Hold h, string studentNumber, string className, DateTime now)
    {
        var effective = h.Status == HoldStatus.Held && h.ExpiresAt <= now
            ? HoldStatus.Expired
            : h.Status;

        return new HoldResponse
        {
            Id = h.Id,
            ClassId = h.ClassId,
            ClassName = className,
            StudentId = studentNumber,
            Status = effective.ToString().ToUpperInvariant(),
            CreatedAt = h.CreatedAt,
            ExpiresAt = h.ExpiresAt,
            ConfirmedAt = h.ConfirmedAt
        };
    }
}