using ClanHub.Application.Games;
using ClanHub.Domain;
using ClanHub.Supercell.Dtos;

namespace ClanHub.Supercell;

public sealed partial class ClashRoyaleConnector
{
    public async Task<ClanRankingSnapshot?> GetClanRankingAsync(string clanTag, string? countryCode, CancellationToken ct = default)
    {
        var clan = await api.GetClan(clanTag, ct);
        if (clan.Location?.Id is not { } locationId) return null;
        var tag = Tags.Normalize(clanTag);
        var ranked = (await api.GetLocationRankings(locationId, ct)).FirstOrDefault(r => Tags.Normalize(r.Tag) == tag);
        return new ClanRankingSnapshot($"location:{locationId}", clan.Location.Name, ranked?.Rank, ranked?.ClanScore ?? clan.ClanScore ?? 0);
    }

    public async Task<IReadOnlyList<BattleSnapshot>> GetBattleLogAsync(string playerTag, CancellationToken ct = default)
    {
        var list = new List<BattleSnapshot>();
        foreach (var b in await api.GetBattleLog(playerTag, ct))
        {
            var me = b.Team?.FirstOrDefault();
            var opp = b.Opponent?.FirstOrDefault();
            var mine = me?.Crowns ?? 0;
            var theirs = opp?.Crowns ?? 0;
            list.Add(new BattleSnapshot(
                Parse.Date(b.BattleTime), b.GameMode?.Name ?? b.Type ?? "",
                mine > theirs ? "win" : mine < theirs ? "loss" : "draw", me?.TrophyChange,
                $"vs {opp?.Name ?? "?"} ({mine}-{theirs})",
                (me?.Cards ?? []).Select(c => new BattleCardSnapshot(c.Name ?? "", c.Level ?? 0, c.IconUrls?.Medium)).ToList()));
        }
        return list;
    }

    public async Task<IReadOnlyList<ChestSnapshot>> GetUpcomingChestsAsync(string playerTag, CancellationToken ct = default) =>
        (await api.GetUpcomingChests(playerTag, ct)).Select(c => new ChestSnapshot(c.Index ?? 0, c.Name ?? "")).ToList();

    private static TournamentSnapshot Map(CrTournamentDto t) => new(
        Tags.Normalize(t.Tag), t.Name ?? "", t.Status ?? "", t.Type, t.MembersList?.Count ?? 0, t.MaxCapacity ?? t.Capacity ?? 0,
        t.Description, Parse.Date(t.StartedTime), Parse.Date(t.EndedTime));

    public async Task<IReadOnlyList<TournamentSnapshot>> SearchTournamentsAsync(string name, CancellationToken ct = default) =>
        (await api.SearchTournaments(name, ct)).Select(Map).ToList();

    public async Task<TournamentSnapshot> GetTournamentAsync(string tag, CancellationToken ct = default) =>
        Map(await api.GetTournament(tag, ct));
}

public sealed partial class BrawlStarsConnector
{
    public async Task<ClanRankingSnapshot?> GetClanRankingAsync(string clanTag, string? countryCode, CancellationToken ct = default)
    {
        var scope = string.IsNullOrWhiteSpace(countryCode) ? "global" : countryCode.Trim().ToLowerInvariant();
        var tag = Tags.Normalize(clanTag);
        var ranked = (await api.GetClubRankings(scope, ct)).FirstOrDefault(r => Tags.Normalize(r.Tag) == tag);
        var club = await api.GetClub(clanTag, ct);
        return new ClanRankingSnapshot($"country:{scope}", scope == "global" ? "Global" : scope.ToUpperInvariant(),
            ranked?.Rank, club.Trophies ?? 0);
    }

    public async Task<IReadOnlyList<BattleSnapshot>> GetBattleLogAsync(string playerTag, CancellationToken ct = default) =>
        (await api.GetBattleLog(playerTag, ct)).Select(b =>
        {
            var detail = b.Battle;
            var result = detail?.Result ?? (detail?.Rank is { } r ? $"rank {r}" : "unknown");
            var brawlers = (detail?.Teams ?? []).SelectMany(t => t).Where(p => p.Brawler?.Name is not null)
                .Select(p => p.Brawler!.Name!).Distinct().Take(6);
            return new BattleSnapshot(Parse.Date(b.BattleTime), detail?.Mode ?? b.Event?.Mode ?? "", result,
                detail?.TrophyChange, b.Event?.Map is { } map ? $"{map} · {string.Join(", ", brawlers)}" : null, []);
        }).ToList();

    public async Task<IReadOnlyList<CalendarEntrySnapshot>> GetCalendarAsync(CancellationToken ct = default) =>
        (await api.GetEventRotation(ct))
            .Where(s => Parse.Date(s.StartTime) is not null && Parse.Date(s.EndTime) is not null)
            .Select(s => new CalendarEntrySnapshot("event", $"{s.Event?.Mode} — {s.Event?.Map}",
                Parse.Date(s.StartTime)!.Value, Parse.Date(s.EndTime)!.Value)).ToList();
}