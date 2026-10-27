using ClanHub.Application.Games;
using ClanHub.Domain;
using Microsoft.EntityFrameworkCore;

namespace ClanHub.Sync.Handlers;

/// <summary>Regras comuns de guerra (guerra normal e rodada da CWL): campos, escalação, ataques e eventos.</summary>
internal static class WarSync
{
    public static async Task ApplyAsync(SyncContext c, War war, WarSnapshot snap, bool touchWarLog, CancellationToken ct)
    {
        var db = c.Db;
        war.TeamSize = snap.TeamSize;
        war.AttacksPerMember = snap.AttacksPerMember;
        war.EndTime = snap.EndTime;
        war.StartTime ??= snap.StartTime;
        war.Stars = snap.Stars;
        war.Destruction = snap.Destruction;
        war.OpponentStars = snap.OpponentStars;
        war.OpponentDestruction = snap.OpponentDestruction;
        war.OpponentTag = snap.OpponentTag;
        war.OpponentName = snap.OpponentName;
        war.State = snap.State;

        foreach (var m in snap.Members)
        {
            var p = war.Participants.FirstOrDefault(x => x.PlayerTag == m.Tag);
            if (p is null)
            {
                p = new WarParticipant { WarId = war.Id, PlayerTag = m.Tag };
                war.Participants.Add(p);
            }
            p.Name = m.Name;
            p.MapPosition = m.MapPosition;
            p.TownHall = m.TownHall;
            p.AttacksUsed = m.AttacksUsed;
        }

        var known = war.Attacks.Select(a => a.Order).ToHashSet();
        foreach (var a in snap.Attacks.Where(a => !known.Contains(a.Order)))
        {
            war.Attacks.Add(new WarAttack
            {
                WarId = war.Id, AttackerTag = a.AttackerTag, DefenderTag = a.DefenderTag,
                Stars = a.Stars, Destruction = a.Destruction, Order = a.Order, Duration = a.Duration,
            });
            db.AddEvent(c.Clan.Id, ClanEventType.WarAttackMade, a.AttackerTag,
                new { a.Stars, a.Destruction, a.DefenderTag, cwl = war.Type == WarType.Cwl }, c.Now);
            await TouchActivityAsync(c, a.AttackerTag, ct);
        }

        if (snap.State == WarState.WarEnded) await EndAsync(c, war, snap, touchWarLog, ct);
    }

    private static async Task TouchActivityAsync(SyncContext c, string playerTag, CancellationToken ct)
    {
        var ms = await c.Db.ClanMemberships.Include(m => m.Player)
            .FirstOrDefaultAsync(m => m.TrackedClanId == c.Clan.Id && m.IsCurrent && m.Player!.Tag == playerTag, ct);
        if (ms is not null) ms.LastActivityAt = c.Now;
    }

    public static async Task EndAsync(SyncContext c, War war, WarSnapshot? snap, bool touchWarLog, CancellationToken ct)
    {
        if (war.State == WarState.WarEnded && war.Result is not null) return;
        war.State = WarState.WarEnded;
        war.Result ??= snap is null ? "unknown" : ResultOf(snap);
        c.Db.AddEvent(c.Clan.Id, ClanEventType.WarEnded, null,
            new { war.OpponentName, war.Result, war.Stars, cwl = war.Type == WarType.Cwl }, c.Now);

        if (!touchWarLog) return;
        // Atualiza o histórico logo após o fim da guerra.
        var log = await c.Db.SyncTargets.FirstOrDefaultAsync(t => t.TrackedClanId == c.Clan.Id && t.Kind == SyncKind.WarLog, ct);
        if (log is not null && log.NextRunAt > c.Now) log.NextRunAt = c.Now;
    }

    public static string ResultOf(WarSnapshot s) =>
        s.Stars != s.OpponentStars ? (s.Stars > s.OpponentStars ? "win" : "lose")
        : s.Destruction != s.OpponentDestruction ? (s.Destruction > s.OpponentDestruction ? "win" : "lose")
        : "tie";
}