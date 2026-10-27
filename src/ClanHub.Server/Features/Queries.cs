using ClanHub.Contracts;
using ClanHub.Domain;
using ClanHub.Infrastructure.Persistence;
using ClanHub.Server.Common;
using Microsoft.EntityFrameworkCore;

namespace ClanHub.Server.Features;

public static class Queries
{
    public static async Task<List<ClanSummaryDto>> SummariesAsync(AppDbContext db, IQueryable<TrackedClan> clans, CancellationToken ct = default)
    {
        var list = await clans.OrderBy(c => c.Name).ToListAsync(ct);
        var ids = list.Select(c => c.Id).ToList();
        var counts = await db.ClanMemberships.Where(m => ids.Contains(m.TrackedClanId) && m.IsCurrent)
            .GroupBy(m => m.TrackedClanId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        var syncs = await db.SyncTargets.Where(t => ids.Contains(t.TrackedClanId) && t.Kind == SyncKind.Members)
            .Select(t => new { t.TrackedClanId, t.LastSuccessAt }).ToDictionaryAsync(x => x.TrackedClanId, x => x.LastSuccessAt, ct);

        return list.Select(c => new ClanSummaryDto(c.Id, c.WorkspaceId, GameMap.Id(c.Game), c.Tag, c.Name, c.BadgeUrl,
            c.Status.ToString(), counts.GetValueOrDefault(c.Id), syncs.GetValueOrDefault(c.Id))).ToList();
    }

    public static void Audit(AppDbContext db, Guid workspaceId, Guid? userId, string action, object? data = null) =>
        db.AuditLogs.Add(new AuditLog
        {
            WorkspaceId = workspaceId, UserId = userId, Action = action,
            DataJson = data is null ? null : System.Text.Json.JsonSerializer.Serialize(data),
        });
}