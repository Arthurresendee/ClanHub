using ClanHub.Domain;
using Microsoft.EntityFrameworkCore;

namespace ClanHub.Sync.Handlers;

/// <summary>Ataques à Capital do Clã (CoC): fins de semana de ataque, saque e ataques por membro.</summary>
public class CapitalRaidsHandler : ISyncHandler
{
    public SyncKind Kind => SyncKind.CapitalRaids;

    public async Task<TimeSpan> HandleAsync(SyncContext c, CancellationToken ct)
    {
        var db = c.Db;
        var seasons = await c.Connector.GetCapitalRaidSeasonsAsync(c.Clan.Tag, ct);
        var starts = seasons.Select(s => s.StartTime).ToList();
        var rows = await db.CapitalRaidSeasons.Include(s => s.Members)
            .Where(s => s.TrackedClanId == c.Clan.Id && starts.Contains(s.StartTime)).ToListAsync(ct);
        var firstImport = !await db.CapitalRaidSeasons.AnyAsync(s => s.TrackedClanId == c.Clan.Id, ct);

        foreach (var s in seasons)
        {
            var row = rows.FirstOrDefault(r => r.StartTime == s.StartTime);
            var isNew = row is null;
            if (row is null)
            {
                row = new CapitalRaidSeason { TrackedClanId = c.Clan.Id, StartTime = s.StartTime };
                db.CapitalRaidSeasons.Add(row);
            }
            row.EndTime = s.EndTime;
            row.State = s.State;
            row.TotalLoot = s.TotalLoot;
            row.AttackCount = s.Attacks;
            row.DistrictsDestroyed = s.DistrictsDestroyed;

            foreach (var m in s.Members)
            {
                var member = row.Members.FirstOrDefault(x => x.PlayerTag == m.Tag);
                if (member is null)
                {
                    member = new CapitalRaidMember { CapitalRaidSeasonId = row.Id, PlayerTag = m.Tag };
                    row.Members.Add(member);
                }
                // Não inunda o feed com o histórico na primeira importação.
                var emit = !firstImport && (!isNew || s.State.Equals("ongoing", StringComparison.OrdinalIgnoreCase));
                if (emit && m.Attacks > member.Attacks)
                {
                    db.AddEvent(c.Clan.Id, ClanEventType.RaidAttackMade, m.Tag,
                        new { name = m.Name, attacks = m.Attacks, limit = m.AttackLimit + m.BonusAttackLimit, looted = m.Looted - member.Looted }, c.Now);
                    var ms = await db.ClanMemberships.Include(x => x.Player)
                        .FirstOrDefaultAsync(x => x.TrackedClanId == c.Clan.Id && x.IsCurrent && x.Player!.Tag == m.Tag, ct);
                    if (ms is not null) ms.LastActivityAt = c.Now;
                }
                member.Name = m.Name;
                member.Attacks = m.Attacks;
                member.AttackLimit = m.AttackLimit;
                member.BonusAttackLimit = m.BonusAttackLimit;
                member.Looted = m.Looted;
            }
        }

        await db.SaveChangesAsync(ct);

        if (RaidSchedule.IsRaidWeekend(c.Now)) return TimeSpan.FromMinutes(c.Options.CapitalActiveMinutes);
        var untilStart = RaidSchedule.NextStart(c.Now) - c.Now;
        var idle = TimeSpan.FromMinutes(c.Options.CapitalIdleMinutes);
        return untilStart < idle ? untilStart + TimeSpan.FromMinutes(1) : idle;
    }
}