using ClanHub.Application.Games;
using ClanHub.Domain;
using Microsoft.EntityFrameworkCore;

namespace ClanHub.Sync.Handlers;

public class CurrentWarHandler : ISyncHandler
{
    public SyncKind Kind => SyncKind.CurrentWar;

    public async Task<TimeSpan> HandleAsync(SyncContext c, CancellationToken ct)
    {
        var db = c.Db;
        var idle = TimeSpan.FromMinutes(c.Options.WarIdleMinutes);

        WarSnapshot? snap;
        try
        {
            snap = await c.Connector.GetCurrentWarAsync(c.Clan.Tag, ct);
        }
        catch (SupercellException ex) when (ex.Kind == SupercellErrorKind.PrivateWarLog)
        {
            c.Clan.Status = TrackedClanStatus.PrivateWarLog;
            await db.SaveChangesAsync(ct);
            return TimeSpan.FromMinutes(c.Options.PrivateWarLogRetryMinutes);
        }
        if (c.Clan.Status == TrackedClanStatus.PrivateWarLog) c.Clan.Status = TrackedClanStatus.Active;

        var open = await db.Wars.Include(w => w.Participants).Include(w => w.Attacks)
            .Where(w => w.TrackedClanId == c.Clan.Id && w.Type == WarType.Normal && w.State != WarState.WarEnded)
            .OrderByDescending(w => w.StartTime).FirstOrDefaultAsync(ct);

        if (snap is null)
        {
            if (open is not null) await WarSync.EndAsync(c, open, null, true, ct);
            await db.SaveChangesAsync(ct);
            return idle;
        }

        if (open is not null && (open.OpponentTag != snap.OpponentTag || (open.StartTime is not null && snap.StartTime is not null && open.StartTime != snap.StartTime)))
        {
            await WarSync.EndAsync(c, open, null, true, ct);
            open = null;
        }

        var war = open;
        if (war is null)
        {
            var alreadyEnded = await db.Wars.AnyAsync(w => w.TrackedClanId == c.Clan.Id && w.Type == WarType.Normal
                && w.OpponentTag == snap.OpponentTag && w.StartTime == snap.StartTime, ct);
            if (alreadyEnded) return idle;

            war = new War
            {
                TrackedClanId = c.Clan.Id, Type = WarType.Normal, OpponentTag = snap.OpponentTag,
                OpponentName = snap.OpponentName, StartTime = snap.StartTime, State = snap.State,
            };
            db.Wars.Add(war);
            db.AddEvent(c.Clan.Id, ClanEventType.WarStarted, null,
                new { snap.OpponentName, snap.TeamSize }, c.Now);
        }

        var delay = snap.State switch
        {
            WarState.InWar => TimeSpan.FromMinutes(c.Options.WarActiveMinutes),
            WarState.Preparation => TimeSpan.FromMinutes(c.Options.WarPrepMinutes),
            _ => idle,
        };

        await WarSync.ApplyAsync(c, war, snap, touchWarLog: true, ct);

        await db.SaveChangesAsync(ct);
        return delay;
    }
    public static string ResultOf(WarSnapshot s) => WarSync.ResultOf(s);
}

public class WarLogHandler : ISyncHandler
{
    public SyncKind Kind => SyncKind.WarLog;

    public async Task<TimeSpan> HandleAsync(SyncContext c, CancellationToken ct)
    {
        var db = c.Db;
        IReadOnlyList<WarLogEntry> entries;
        try
        {
            entries = await c.Connector.GetWarLogAsync(c.Clan.Tag, ct);
        }
        catch (SupercellException ex) when (ex.Kind == SupercellErrorKind.PrivateWarLog)
        {
            c.Clan.Status = TrackedClanStatus.PrivateWarLog;
            await db.SaveChangesAsync(ct);
            return TimeSpan.FromMinutes(c.Options.PrivateWarLogRetryMinutes);
        }

        var existing = await db.Wars.Where(w => w.TrackedClanId == c.Clan.Id && w.Type == WarType.Normal)
            .Select(w => new { w.Id, w.OpponentTag, w.EndTime, w.Result }).ToListAsync(ct);

        foreach (var e in entries)
        {
            var match = existing.FirstOrDefault(w => w.OpponentTag == e.OpponentTag && w.EndTime is { } t
                && Math.Abs((t - e.EndTime).TotalMinutes) < 10);
            if (match is not null)
            {
                if (match.Result is null or "unknown")
                    await db.Wars.Where(w => w.Id == match.Id)
                        .ExecuteUpdateAsync(s => s.SetProperty(w => w.Result, e.Result), ct);
                continue;
            }
            db.Wars.Add(new War
            {
                TrackedClanId = c.Clan.Id, Type = WarType.Normal, OpponentTag = e.OpponentTag,
                OpponentName = e.OpponentName, TeamSize = e.TeamSize, State = WarState.WarEnded,
                EndTime = e.EndTime, Result = e.Result, Stars = e.Stars, Destruction = e.Destruction,
                OpponentStars = e.OpponentStars, OpponentDestruction = e.OpponentDestruction,
            });
        }

        if (c.Clan.Status == TrackedClanStatus.PrivateWarLog) c.Clan.Status = TrackedClanStatus.Active;
        await db.SaveChangesAsync(ct);
        return TimeSpan.FromMinutes(c.Options.WarLogMinutes);
    }
}