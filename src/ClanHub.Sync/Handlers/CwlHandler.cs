using System.Text.Json;
using ClanHub.Application.Games;
using ClanHub.Domain;
using Microsoft.EntityFrameworkCore;

namespace ClanHub.Sync.Handlers;

/// <summary>
/// Liga de Guerra de Clãs (CoC): lê o grupo, descobre em cada rodada qual das guerras é a do nosso clã
/// (uma vez só; depois só atualiza essa) e grava a guerra como War do tipo Cwl.
/// </summary>
public class CwlHandler : ISyncHandler
{
    public SyncKind Kind => SyncKind.CwlGroup;

    public async Task<TimeSpan> HandleAsync(SyncContext c, CancellationToken ct)
    {
        var db = c.Db;
        var idle = TimeSpan.FromMinutes(c.Now.Day <= 12 ? c.Options.CwlIdleMinutesEarlyMonth : c.Options.CwlIdleMinutes);

        var group = await c.Connector.GetCwlGroupAsync(c.Clan.Tag, ct);
        if (group is null) return idle;

        var season = await db.CwlSeasons.Include(s => s.Rounds)
            .FirstOrDefaultAsync(s => s.TrackedClanId == c.Clan.Id && s.Season == group.Season, ct);
        if (season is null)
        {
            season = new CwlSeason { TrackedClanId = c.Clan.Id, Season = group.Season };
            db.CwlSeasons.Add(season);
        }
        season.State = group.State;
        season.UpdatedAt = c.Now;
        season.ClansJson = JsonSerializer.Serialize(group.Clans.Select(x => new { tag = x.Tag, name = x.Name }));

        for (var i = 0; i < group.Rounds.Count; i++)
        {
            if (season.Rounds.All(r => r.RoundIndex != i))
                season.Rounds.Add(new CwlRound { CwlSeasonId = season.Id, RoundIndex = i });
            season.Rounds.First(r => r.RoundIndex == i).WarTagsJson = JsonSerializer.Serialize(group.Rounds[i]);
        }

        var active = false;
        foreach (var round in season.Rounds.OrderBy(r => r.RoundIndex))
        {
            // "#0" = rodada que ainda não começou
            var tags = group.Rounds.ElementAtOrDefault(round.RoundIndex)?.Where(t => t != "#0").ToList() ?? [];
            if (tags.Count == 0) break;

            War? war = round.WarId is { } wid
                ? await db.Wars.Include(w => w.Participants).Include(w => w.Attacks).FirstOrDefaultAsync(w => w.Id == wid, ct)
                : null;
            if (war is { State: WarState.WarEnded, Result: not null }) continue;

            WarSnapshot? snap = null;
            if (round.OurWarTag is { } known)
                snap = await c.Connector.GetCwlWarAsync(known, c.Clan.Tag, ct);
            else
                foreach (var tag in tags)
                {
                    snap = await c.Connector.GetCwlWarAsync(tag, c.Clan.Tag, ct);
                    if (snap is null) continue;
                    round.OurWarTag = tag;
                    break;
                }
            if (snap is null || snap.State == WarState.NotInWar) continue;

            if (war is null)
            {
                war = new War
                {
                    TrackedClanId = c.Clan.Id, Type = WarType.Cwl, WarTag = round.OurWarTag,
                    OpponentTag = snap.OpponentTag, OpponentName = snap.OpponentName, StartTime = snap.StartTime, State = snap.State,
                };
                db.Wars.Add(war);
                db.AddEvent(c.Clan.Id, ClanEventType.CwlRoundStarted, null,
                    new { round = round.RoundIndex + 1, snap.OpponentName }, c.Now);
            }
            round.WarId = war.Id;

            await WarSync.ApplyAsync(c, war, snap, touchWarLog: false, ct);
            if (snap.State is WarState.InWar or WarState.Preparation) active = true;
        }

        await db.SaveChangesAsync(ct);
        return active ? TimeSpan.FromMinutes(c.Options.CwlActiveMinutes) : idle;
    }
}