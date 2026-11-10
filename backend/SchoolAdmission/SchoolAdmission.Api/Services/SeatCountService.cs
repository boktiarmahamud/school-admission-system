using Microsoft.EntityFrameworkCore;
using SchoolAdmission.Api.Data;
using SchoolAdmission.Api.Entities;

namespace SchoolAdmission.Api.Services;

public record SeatCounts(int Held, int Confirmed);

public class SeatCountService
{
    private readonly ApplicationDbContext _db;

    public SeatCountService(ApplicationDbContext db) => _db = db;

    public async Task<Dictionary<Guid, SeatCounts>> GetCountsAsync(
        IReadOnlyCollection<Guid> classIds, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        var rows = await _db.Holds
            .AsNoTracking()
            .Where(h => classIds.Contains(h.ClassId))
            .GroupBy(h => h.ClassId)
            .Select(g => new
            {
                ClassId = g.Key,
                Held = g.Count(h => h.Status == HoldStatus.Held && h.ExpiresAt > now),
                Confirmed = g.Count(h => h.Status == HoldStatus.Confirmed)
            })
            .ToListAsync(ct);

        return rows.ToDictionary(r => r.ClassId, r => new SeatCounts(r.Held, r.Confirmed));
    }

    public async Task<SeatCounts> GetCountsAsync(Guid classId, CancellationToken ct = default)
    {
        var all = await GetCountsAsync(new[] { classId }, ct);
        return all.TryGetValue(classId, out var c) ? c : new SeatCounts(0, 0);
    }
}