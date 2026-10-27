using ClanHub.Application.Clans;
using ClanHub.Domain;
using Microsoft.EntityFrameworkCore;

namespace ClanHub.Sync.Handlers;

/// <summary>Posição do clã no ranking local, uma foto por dia (para acompanhar a evolução).</summary>
public class RankingsHandler : ISyncHandler
{
    public SyncKind Kind => SyncKind.Rankings;

    public async Task<TimeSpan> HandleAsync(SyncContext c, CancellationToken ct)
    {
        var delay = TimeSpan.FromMinutes(c.Options.RankingsMinutes);
        if (!c.Connector.Features.Rankings) return delay;

        var settings = ClanSettings.Parse(c.Clan.SettingsJson);
        var snap = await c.Connector.GetClanRankingAsync(c.Clan.Tag, settings.CountryCode, ct);
        if (snap is null) return delay;

        var today = DateOnly.FromDateTime(c.Now);
        var row = await c.Db.ClanRankings.FirstOrDefaultAsync(r => r.TrackedClanId == c.Clan.Id && r.Date == today && r.Scope == snap.Scope, ct);
        if (row is null)
        {
            row = new ClanRanking { TrackedClanId = c.Clan.Id, Date = today, Scope = snap.Scope };
            c.Db.ClanRankings.Add(row);
        }
        row.LocationName = snap.LocationName;
        row.Rank = snap.Rank;
        row.Score = snap.Score;

        await c.Db.SaveChangesAsync(ct);
        return delay;
    }
}