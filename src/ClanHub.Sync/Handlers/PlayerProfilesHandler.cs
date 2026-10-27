using ClanHub.Application.Games;
using ClanHub.Domain;
using Microsoft.EntityFrameworkCore;

namespace ClanHub.Sync.Handlers;

/// <summary>Atualiza o perfil dos membros aos poucos, espalhando as chamadas ao longo do dia.</summary>
public class PlayerProfilesHandler : ISyncHandler
{
    public SyncKind Kind => SyncKind.PlayerProfiles;

    public async Task<TimeSpan> HandleAsync(SyncContext c, CancellationToken ct)
    {
        var db = c.Db;
        var staleBefore = c.Now.AddHours(-c.Options.PlayerProfileHours);

        var members = await db.ClanMemberships.Include(m => m.Player)
            .Where(m => m.TrackedClanId == c.Clan.Id && m.IsCurrent).ToListAsync(ct);
        var stale = members.Where(m => m.Player!.LastFetchedAt is null || m.Player.LastFetchedAt < staleBefore)
            .OrderBy(m => m.Player!.LastFetchedAt ?? DateTime.MinValue)
            .Take(c.Options.PlayersPerRun).ToList();

        foreach (var m in stale)
        {
            try
            {
                var p = await c.Connector.GetPlayerAsync(m.Player!.Tag, ct);
                m.Player.ProfileJson = p.RawJson;
                m.Player.Name = p.Name;
                if (p.TownHall is { } th) m.TownHall = th;
            }
            catch (SupercellException ex) when (ex.Kind == SupercellErrorKind.NotFound) { }
            m.Player!.LastFetchedAt = c.Now;
        }

        await db.SaveChangesAsync(ct);

        var runsPerCycle = Math.Max(1, (int)Math.Ceiling(members.Count / (double)c.Options.PlayersPerRun));
        var interval = TimeSpan.FromHours(c.Options.PlayerProfileHours) / runsPerCycle;
        return interval < TimeSpan.FromMinutes(5) ? TimeSpan.FromMinutes(5) : interval;
    }
}