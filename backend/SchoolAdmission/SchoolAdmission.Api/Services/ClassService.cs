using Microsoft.EntityFrameworkCore;
using SchoolAdmission.Api.Data;
using SchoolAdmission.Api.DTOs;
using SchoolAdmission.Api.Entities;

namespace SchoolAdmission.Api.Services;

public class ClassService
{
    private readonly ApplicationDbContext _db;
    private readonly SeatCountService _seats;

    public ClassService(ApplicationDbContext db, SeatCountService seats)
    {
        _db = db;
        _seats = seats;
    }

    public async Task<ClassResponse> CreateAsync(CreateClassRequest request, CancellationToken ct)
    {
        var entity = new SchoolClass
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            TotalSeats = request.TotalSeats,
            CreatedAt = DateTime.UtcNow
        };

        _db.Classes.Add(entity);
        await _db.SaveChangesAsync(ct);

        return ToResponse(entity, new SeatCounts(0, 0));
    }

    public async Task<List<ClassResponse>> GetAllAsync(CancellationToken ct)
    {
        var classes = await _db.Classes.AsNoTracking()
            .OrderBy(c => c.CreatedAt)
            .ToListAsync(ct);

        var counts = await _seats.GetCountsAsync(classes.Select(c => c.Id).ToList(), ct);

        return classes
            .Select(c => ToResponse(c, counts.GetValueOrDefault(c.Id, new SeatCounts(0, 0))))
            .ToList();
    }

    public async Task<ClassResponse?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var entity = await _db.Classes.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (entity is null) return null;

        var counts = await _seats.GetCountsAsync(id, ct);
        return ToResponse(entity, counts);
    }

    private static ClassResponse ToResponse(SchoolClass c, SeatCounts counts) => new()
    {
        Id = c.Id,
        Name = c.Name,
        TotalSeats = c.TotalSeats,
        HeldSeats = counts.Held,
        ConfirmedSeats = counts.Confirmed,
        AvailableSeats = Math.Max(0, c.TotalSeats - counts.Held - counts.Confirmed),
        CreatedAt = c.CreatedAt
    };
}