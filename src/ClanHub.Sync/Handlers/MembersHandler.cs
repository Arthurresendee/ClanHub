using ClanHub.Application.Members;
using ClanHub.Domain;
using Microsoft.EntityFrameworkCore;

namespace ClanHub.Sync.Handlers;

public class MembersHandler : ISyncHandler
{
    public SyncKind Kind => SyncKind.Members;

    public async Task<TimeSpan> HandleAsync(SyncContext c, CancellationToken ct)
    {
        var snap = await c.Connector.GetClanAsync(c.Clan.Tag, ct);
        var db = c.Db;
        var clanId = c.Clan.Id;
        var game = c.Clan.Game;

        var memberships = await db.ClanMemberships.Include(m => m.Player)
            .Where(m => m.TrackedClanId == clanId).ToListAsync(ct);
        var isFirst = memberships.Count == 0;
        var current = memberships.Where(m => m.IsCurrent).ToList();
        var known = current.Select(m => new KnownMember(m.Player!.Tag, m.Name, m.Role, m.TownHall, m.Donations)).ToList();

        var events = MemberDiffer.Diff(known, snap.Members, isFirst);

        if (!isFirst && game == Game.ClashOfClans && MemberDiffer.DetectSeasonReset(known, snap.Members))
            await CloseSeasonAsync(c, current, ct);

        var tags = snap.Members.Select(m => m.Tag).ToList();
        var players = await db.Players.Where(p => p.Game == game && tags.Contains(p.Tag))
            .ToDictionaryAsync(p => p.Tag, ct);
        var byPlayer = memberships.ToDictionary(m => m.PlayerId);

        foreach (var m in snap.Members)
        {
            if (!players.TryGetValue(m.Tag, out var player))
            {
                player = new Player { Game = game, Tag = m.Tag, Name = m.Name };
                db.Players.Add(player);
                players[m.Tag] = player;
            }
            player.Name = m.Name;

            if (!byPlayer.TryGetValue(player.Id, out var ms))
            {
                ms = new ClanMembership { TrackedClanId = clanId, PlayerId = player.Id, JoinedAt = c.Now };
                db.ClanMemberships.Add(ms);
                byPlayer[player.Id] = ms;
            }
            else if (!ms.IsCurrent)
            {
                ms.IsCurrent = true;
                ms.LeftAt = null;
                ms.JoinedAt = c.Now;
            }
            else if (m.Donations > ms.Donations || m.DonationsReceived > ms.DonationsReceived || m.Trophies != ms.Trophies)
            {
                ms.LastActivityAt = c.Now;
            }

            ms.Name = m.Name;
            ms.Role = m.Role;
            ms.ExpLevel = m.ExpLevel;
            ms.Trophies = m.Trophies;
            ms.Donations = m.Donations;
            ms.DonationsReceived = m.DonationsReceived;
            ms.TownHall = m.TownHall ?? ms.TownHall;
            ms.ClanRank = m.ClanRank;
        }

        var present = tags.ToHashSet();
        foreach (var ms in current.Where(x => !present.Contains(x.Player!.Tag)))
        {
            ms.IsCurrent = false;
            ms.LeftAt = c.Now;
        }

        foreach (var e in events) db.AddEvent(clanId, e.Type, e.PlayerTag, e.Payload, c.Now);

        if (!isFirst && c.Clan.Name != snap.Name)
            db.AddEvent(clanId, ClanEventType.ClanSettingsChanged, null, new { From = c.Clan.Name, To = snap.Name }, c.Now);

        c.Clan.Name = snap.Name;
        c.Clan.BadgeUrl = snap.BadgeUrl ?? c.Clan.BadgeUrl;
        if (c.Clan.Status == TrackedClanStatus.NotFound) c.Clan.Status = TrackedClanStatus.Active;

        await db.SaveChangesAsync(ct);
        return TimeSpan.FromMinutes(c.Options.MembersMinutes);
    }

    /// <summary>As doações do CoC zeram a cada temporada: guarda os totais da anterior antes de zerar.</summary>
    private static async Task CloseSeasonAsync(SyncContext c, List<ClanMembership> current, CancellationToken ct)
    {
        var previousMonth = c.Now.AddMonths(-1);
        var season = $"{previousMonth:yyyy-MM}";
        var existing = await c.Db.SeasonStats
            .Where(s => s.TrackedClanId == c.Clan.Id && s.Season == season)
            .ToDictionaryAsync(s => s.PlayerId, ct);

        foreach (var m in current)
        {
            if (!existing.TryGetValue(m.PlayerId, out var row))
            {
                row = new SeasonStats { TrackedClanId = c.Clan.Id, PlayerId = m.PlayerId, Season = season };
                c.Db.SeasonStats.Add(row);
            }
            row.Donations = m.Donations;
            row.Received = m.DonationsReceived;
        }
    }
}