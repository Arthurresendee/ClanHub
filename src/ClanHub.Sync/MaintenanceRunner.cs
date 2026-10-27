using ClanHub.Domain;
using ClanHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ClanHub.Sync;

public record MaintenanceResult(int SnapshotsCreated, int EventsDeleted, int SnapshotsDeleted, int ClaimsExpired, int TargetsCreated = 0);

/// <summary>Rotina diária (idempotente): fotos diárias, limpeza por retenção e expiração de pedidos de posse.</summary>
public class MaintenanceRunner(AppDbContext db, IOptions<SyncOptions> options, TimeProvider clock)
{
    public async Task<MaintenanceResult> RunDailyAsync(CancellationToken ct)
    {
        var o = options.Value;
        var now = clock.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(now);

        var current = await (from m in db.ClanMemberships
                             join c in db.TrackedClans on m.TrackedClanId equals c.Id
                             where m.IsCurrent
                             select new { m.TrackedClanId, m.PlayerId, m.Trophies, m.Donations, m.DonationsReceived, m.ExpLevel, m.TownHall })
            .ToListAsync(ct);

        var done = (await db.MemberDailySnapshots.Where(s => s.Date == today)
                .Select(s => new { s.TrackedClanId, s.PlayerId }).ToListAsync(ct))
            .Select(s => (s.TrackedClanId, s.PlayerId)).ToHashSet();

        var created = 0;
        foreach (var m in current.Where(m => !done.Contains((m.TrackedClanId, m.PlayerId))))
        {
            db.MemberDailySnapshots.Add(new MemberDailySnapshot
            {
                TrackedClanId = m.TrackedClanId, PlayerId = m.PlayerId, Date = today, Trophies = m.Trophies,
                Donations = m.Donations, Received = m.DonationsReceived, ExpLevel = m.ExpLevel, TownHall = m.TownHall,
            });
            created++;
        }
        await db.SaveChangesAsync(ct);

        var eventCutoff = now.AddDays(-o.EventRetentionDays);
        var snapCutoff = today.AddDays(-o.SnapshotRetentionDays);
        var events = await db.ClanEvents.Where(e => e.OccurredAt < eventCutoff).ExecuteDeleteAsync(ct);
        var snaps = await db.MemberDailySnapshots.Where(s => s.Date < snapCutoff).ExecuteDeleteAsync(ct);
        var claims = await db.ClanClaims.Where(c => c.Status == ClaimStatus.Pending && c.ExpiresAt < now)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, ClaimStatus.Expired), ct);

        // Clãs antigos ganham as rotinas de sincronização criadas em versões novas.
        var clans = await db.TrackedClans.Select(c => new { c.Id, c.Game }).ToListAsync(ct);
        var have = (await db.SyncTargets.Select(t => new { t.TrackedClanId, t.Kind }).ToListAsync(ct))
            .Select(t => (t.TrackedClanId, t.Kind)).ToHashSet();
        var targets = 0;
        foreach (var clan in clans)
            foreach (var kind in SyncTargets.KindsFor(clan.Game).Where(k => !have.Contains((clan.Id, k))))
            {
                db.SyncTargets.Add(new SyncTarget { TrackedClanId = clan.Id, Kind = kind, NextRunAt = now });
                targets++;
            }
        await db.SaveChangesAsync(ct);

        return new MaintenanceResult(created, events, snaps, claims, targets);
    }
}