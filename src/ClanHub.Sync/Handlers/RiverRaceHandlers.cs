using ClanHub.Application.Games;
using ClanHub.Domain;
using Microsoft.EntityFrameworkCore;

namespace ClanHub.Sync.Handlers;

public class RiverRaceHandler : ISyncHandler
{
    public SyncKind Kind => SyncKind.RiverRace;

    // Dias de batalha da Guerra de Clãs do CR: quinta a domingo (UTC), com folga na segunda de manhã.
    internal static bool IsBattleDay(DateTime utc) =>
        utc.DayOfWeek is DayOfWeek.Thursday or DayOfWeek.Friday or DayOfWeek.Saturday or DayOfWeek.Sunday
        || (utc.DayOfWeek == DayOfWeek.Monday && utc.Hour < 11);

    public async Task<TimeSpan> HandleAsync(SyncContext c, CancellationToken ct)
    {
        var db = c.Db;
        var snap = await c.Connector.GetCurrentRiverRaceAsync(c.Clan.Tag, ct);
        var delay = TimeSpan.FromMinutes(IsBattleDay(c.Now) ? c.Options.RiverRaceActiveMinutes : c.Options.RiverRaceIdleMinutes);
        if (snap is null) return delay;

        var race = await db.RiverRaces.Include(r => r.Participants)
            .FirstOrDefaultAsync(r => r.TrackedClanId == c.Clan.Id && r.IsCurrent, ct);

        if (race is not null && race.SectionIndex != snap.SectionIndex)
        {
            race.IsCurrent = false;
            db.AddEvent(c.Clan.Id, ClanEventType.RiverRaceEnded, null, new { race.SectionIndex, race.Fame, race.Rank }, c.Now);
            var log = await db.SyncTargets.FirstOrDefaultAsync(t => t.TrackedClanId == c.Clan.Id && t.Kind == SyncKind.RiverRaceLog, ct);
            if (log is not null && log.NextRunAt > c.Now) log.NextRunAt = c.Now;
            race = null;
        }

        if (race is null)
        {
            race = new RiverRace { TrackedClanId = c.Clan.Id, SeasonId = snap.SeasonId, SectionIndex = snap.SectionIndex, IsCurrent = true };
            db.RiverRaces.Add(race);
        }

        race.Fame = snap.Fame;
        race.Rank = snap.Rank;
        race.UpdatedAt = c.Now;

        foreach (var p in snap.Participants)
        {
            var row = race.Participants.FirstOrDefault(x => x.PlayerTag == p.Tag);
            if (row is null)
            {
                row = new RiverRaceParticipant { RiverRaceId = race.Id, PlayerTag = p.Tag };
                race.Participants.Add(row);
            }
            else if (p.DecksUsedToday > row.DecksUsedToday)
            {
                db.AddEvent(c.Clan.Id, ClanEventType.RiverRaceDecksUsed, p.Tag,
                    new { Decks = p.DecksUsedToday, p.Fame }, c.Now);
            }
            row.Name = p.Name;
            row.Fame = p.Fame;
            row.DecksUsed = p.DecksUsed;
            row.DecksUsedToday = p.DecksUsedToday;
        }

        await db.SaveChangesAsync(ct);
        return delay;
    }
}

public class RiverRaceLogHandler : ISyncHandler
{
    public SyncKind Kind => SyncKind.RiverRaceLog;

    public async Task<TimeSpan> HandleAsync(SyncContext c, CancellationToken ct)
    {
        var db = c.Db;
        var entries = await c.Connector.GetRiverRaceLogAsync(c.Clan.Tag, ct);
        var existing = (await db.RiverRaces.Where(r => r.TrackedClanId == c.Clan.Id && !r.IsCurrent)
            .Select(r => new { r.SeasonId, r.SectionIndex }).ToListAsync(ct))
            .Select(r => (r.SeasonId, r.SectionIndex)).ToHashSet();

        foreach (var e in entries.Where(e => !existing.Contains((e.SeasonId, e.SectionIndex))))
        {
            // Se a semana já foi acompanhada ao vivo (linha corrente encerrada com SeasonId 0), completa o SeasonId.
            var live = await db.RiverRaces.Include(r => r.Participants).FirstOrDefaultAsync(r =>
                r.TrackedClanId == c.Clan.Id && r.SeasonId == 0 && r.SectionIndex == e.SectionIndex && !r.IsCurrent, ct);
            var race = live ?? new RiverRace { TrackedClanId = c.Clan.Id, IsCurrent = false };
            if (live is null) db.RiverRaces.Add(race);

            race.SeasonId = e.SeasonId;
            race.SectionIndex = e.SectionIndex;
            race.Fame = e.Fame;
            race.Rank = e.Rank;
            race.UpdatedAt = e.CreatedAt;

            foreach (var p in e.Participants)
            {
                var row = race.Participants.FirstOrDefault(x => x.PlayerTag == p.Tag);
                if (row is null)
                {
                    row = new RiverRaceParticipant { RiverRaceId = race.Id, PlayerTag = p.Tag };
                    race.Participants.Add(row);
                }
                row.Name = p.Name;
                row.Fame = p.Fame;
                row.DecksUsed = p.DecksUsed;
                row.DecksUsedToday = 0;
            }
        }

        await db.SaveChangesAsync(ct);
        return TimeSpan.FromMinutes(c.Options.RiverRaceLogMinutes);
    }
}