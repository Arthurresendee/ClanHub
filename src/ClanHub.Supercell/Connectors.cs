using System.Globalization;
using ClanHub.Application.Games;
using ClanHub.Domain;
using ClanHub.Supercell.Dtos;

namespace ClanHub.Supercell;

internal static class Parse
{
    /// <summary>Datas do CoC/CR: 20240101T123456.000Z</summary>
    public static DateTime? Date(string? s) =>
        s is not null && DateTime.TryParseExact(s, "yyyyMMdd'T'HHmmss.fff'Z'", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d) ? d : null;

    public static WarState WarState(string? s) => s?.ToLowerInvariant() switch
    {
        "preparation" => Domain.WarState.Preparation,
        "inwar" => Domain.WarState.InWar,
        "warended" => Domain.WarState.WarEnded,
        _ => Domain.WarState.NotInWar,
    };
}

public sealed class ClashOfClansConnector(CocApiClient api) : IGameConnector
{
    public Game Game => Game.ClashOfClans;
    public GameFeatures Features { get; } = new(true, true, true, true, true, false,
        BattleLog: true, Rankings: true, Calendar: true);

    private static ClanMemberSnapshot Map(CocMemberDto m, int i) => new(
        Tags.Normalize(m.Tag), m.Name ?? "", m.Role ?? "member", m.ExpLevel ?? 0, m.Trophies ?? 0,
        m.Donations ?? 0, m.DonationsReceived ?? 0, m.TownHallLevel, m.ClanRank ?? i + 1);

    public async Task<ClanSnapshot> GetClanAsync(string clanTag, CancellationToken ct = default)
    {
        var c = await api.GetClan(clanTag, ct);
        return new ClanSnapshot(Tags.Normalize(c.Tag), c.Name ?? "", c.Description, c.BadgeUrls?.Medium ?? c.BadgeUrls?.Small,
            c.Members ?? c.MemberList?.Count ?? 0, (c.MemberList ?? []).Select(Map).ToList());
    }

    public async Task<IReadOnlyList<ClanMemberSnapshot>> GetMembersAsync(string clanTag, CancellationToken ct = default) =>
        (await api.GetMembers(clanTag, ct)).Select(Map).ToList();

    public async Task<IReadOnlyList<ClanSearchResult>> SearchClansAsync(string name, CancellationToken ct = default) =>
        (await api.Search(name, ct)).Select(c =>
            new ClanSearchResult(Tags.Normalize(c.Tag), c.Name ?? "", c.BadgeUrls?.Small, c.Members ?? 0, c.ClanLevel)).ToList();

    public async Task<PlayerSnapshot> GetPlayerAsync(string playerTag, CancellationToken ct = default)
    {
        var (p, raw) = await api.GetPlayer(playerTag, ct);
        return new PlayerSnapshot(Tags.Normalize(p.Tag), p.Name ?? "", p.ExpLevel ?? 0, p.Trophies ?? 0,
            p.Clan?.Tag is { } t ? Tags.Normalize(t) : null, p.Role, p.TownHallLevel, raw);
    }

    public Task<bool> VerifyPlayerTokenAsync(string playerTag, string token, CancellationToken ct = default) =>
        api.VerifyToken(playerTag, token, ct);

    public async Task<WarSnapshot?> GetCurrentWarAsync(string clanTag, CancellationToken ct = default)
    {
        var w = await api.GetCurrentWar(clanTag, ct);
        var state = Parse.WarState(w.State);
        if (state == WarState.NotInWar || w.Clan is null) return null;
        return MapWar(w, w.Clan, w.Opponent, state, defaultAttacks: 2);
    }

    private static WarSnapshot MapWar(CocWarDto w, CocWarSideDto ours, CocWarSideDto? theirs, WarState state, int defaultAttacks)
    {
        var members = (ours.Members ?? []).Select(m => new WarMemberSnapshot(
            Tags.Normalize(m.Tag), m.Name ?? "", m.MapPosition ?? 0, m.TownhallLevel ?? 0, m.Attacks?.Count ?? 0)).ToList();
        var attacks = (ours.Members ?? []).SelectMany(m => m.Attacks ?? []).Select(a => new WarAttackSnapshot(
            Tags.Normalize(a.AttackerTag), Tags.Normalize(a.DefenderTag), a.Stars ?? 0,
            a.DestructionPercentage ?? 0, a.Order ?? 0, a.Duration ?? 0)).ToList();

        return new WarSnapshot(state, w.TeamSize ?? 0, w.AttacksPerMember ?? defaultAttacks,
            Tags.Normalize(theirs?.Tag), theirs?.Name ?? "",
            Parse.Date(w.StartTime), Parse.Date(w.EndTime),
            ours.Stars ?? 0, ours.DestructionPercentage ?? 0,
            theirs?.Stars ?? 0, theirs?.DestructionPercentage ?? 0, members, attacks);
    }

    public async Task<IReadOnlyList<WarLogEntry>> GetWarLogAsync(string clanTag, CancellationToken ct = default) =>
        (await api.GetWarLog(clanTag, ct))
            .Where(e => Parse.Date(e.EndTime) is not null)
            .Select(e => new WarLogEntry(
                e.Result ?? "", Parse.Date(e.EndTime)!.Value,
                Tags.Normalize(e.Opponent?.Tag), e.Opponent?.Name ?? "", e.TeamSize ?? 0,
                e.Clan?.Stars ?? 0, e.Clan?.DestructionPercentage ?? 0,
                e.Opponent?.Stars ?? 0, e.Opponent?.DestructionPercentage ?? 0)).ToList();

    // ---- Fase 2: CWL e Capital ----
    public async Task<CwlGroupSnapshot?> GetCwlGroupAsync(string clanTag, CancellationToken ct = default)
    {
        CocLeagueGroupDto g;
        try { g = await api.GetLeagueGroup(clanTag, ct); }
        catch (SupercellException ex) when (ex.Kind == SupercellErrorKind.NotFound) { return null; }
        if (string.Equals(g.State, "notInWar", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(g.Season)) return null;
        return new CwlGroupSnapshot(g.State ?? "", g.Season,
            (g.Clans ?? []).Select(c => new CwlClanSnapshot(Tags.Normalize(c.Tag), c.Name ?? "")).ToList(),
            (g.Rounds ?? []).Select(r => (IReadOnlyList<string>)(r.WarTags ?? []).Select(Tags.Normalize).ToList()).ToList());
    }

    public async Task<WarSnapshot?> GetCwlWarAsync(string warTag, string clanTag, CancellationToken ct = default)
    {
        var w = await api.GetCwlWar(warTag, ct);
        var state = Parse.WarState(w.State);
        var mine = Tags.Normalize(clanTag);
        if (Tags.Normalize(w.Clan?.Tag) == mine) return MapWar(w, w.Clan!, w.Opponent, state, defaultAttacks: 1);
        if (Tags.Normalize(w.Opponent?.Tag) == mine) return MapWar(w, w.Opponent!, w.Clan, state, defaultAttacks: 1);
        return null;
    }

    public async Task<IReadOnlyList<CapitalRaidSnapshot>> GetCapitalRaidSeasonsAsync(string clanTag, CancellationToken ct = default) =>
        (await api.GetRaidSeasons(clanTag, ct))
            .Where(s => Parse.Date(s.StartTime) is not null && Parse.Date(s.EndTime) is not null)
            .Select(s => new CapitalRaidSnapshot(s.State ?? "", Parse.Date(s.StartTime)!.Value, Parse.Date(s.EndTime)!.Value,
                s.CapitalTotalLoot ?? 0, s.TotalAttacks ?? 0, s.EnemyDistrictsDestroyed ?? 0,
                (s.Members ?? []).Select(m => new CapitalRaidMemberSnapshot(Tags.Normalize(m.Tag), m.Name ?? "",
                    m.Attacks ?? 0, m.AttackLimit ?? 0, m.BonusAttackLimit ?? 0, m.CapitalResourcesLooted ?? 0)).ToList())).ToList();

    // ---- Fase 3 ----
    public async Task<ClanRankingSnapshot?> GetClanRankingAsync(string clanTag, string? countryCode, CancellationToken ct = default)
    {
        var clan = await api.GetClan(clanTag, ct);
        if (clan.Location?.Id is not { } locationId) return null;
        var tag = Tags.Normalize(clanTag);
        var ranked = (await api.GetLocationRankings(locationId, ct)).FirstOrDefault(r => Tags.Normalize(r.Tag) == tag);
        return new ClanRankingSnapshot($"location:{locationId}", clan.Location.Name, ranked?.Rank, ranked?.ClanPoints ?? clan.ClanPoints ?? 0);
    }

    public async Task<IReadOnlyList<BattleSnapshot>> GetBattleLogAsync(string playerTag, CancellationToken ct = default) =>
        (await api.GetBattleLog(playerTag, ct)).Select(b => new BattleSnapshot(
            null, b.BattleType ?? "", b.Attack == false ? "defense" : "attack", null,
            $"{b.Stars ?? 0} estrela(s), {b.DestructionPercentage ?? 0}%", [])).ToList();

    public async Task<IReadOnlyList<CalendarEntrySnapshot>> GetCalendarAsync(CancellationToken ct = default)
    {
        var gp = await api.GetGoldPass(ct);
        return Parse.Date(gp.StartTime) is { } s && Parse.Date(gp.EndTime) is { } e
            ? [new CalendarEntrySnapshot("goldpass", "Temporada do Passe de Ouro", s, e)]
            : [];
    }
}

public sealed partial class ClashRoyaleConnector(CrApiClient api) : IGameConnector
{
    public Game Game => Game.ClashRoyale;
    public GameFeatures Features { get; } = new(true, false, false, false, false, true,
        BattleLog: true, Rankings: true, Tournaments: true, UpcomingChests: true);

    private static ClanMemberSnapshot Map(CrMemberDto m, int i) => new(
        Tags.Normalize(m.Tag), m.Name ?? "", m.Role ?? "member", m.ExpLevel ?? 0, m.Trophies ?? 0,
        m.Donations ?? 0, m.DonationsReceived ?? 0, null, m.ClanRank ?? i + 1);

    public async Task<ClanSnapshot> GetClanAsync(string clanTag, CancellationToken ct = default)
    {
        var c = await api.GetClan(clanTag, ct);
        return new ClanSnapshot(Tags.Normalize(c.Tag), c.Name ?? "", c.Description, c.BadgeUrl,
            c.Members ?? c.MemberList?.Count ?? 0, (c.MemberList ?? []).Select(Map).ToList());
    }

    public async Task<IReadOnlyList<ClanMemberSnapshot>> GetMembersAsync(string clanTag, CancellationToken ct = default) =>
        (await api.GetMembers(clanTag, ct)).Select(Map).ToList();

    public async Task<IReadOnlyList<ClanSearchResult>> SearchClansAsync(string name, CancellationToken ct = default) =>
        (await api.Search(name, ct)).Select(c =>
            new ClanSearchResult(Tags.Normalize(c.Tag), c.Name ?? "", c.BadgeUrl, c.Members ?? 0, c.ClanScore)).ToList();

    public async Task<PlayerSnapshot> GetPlayerAsync(string playerTag, CancellationToken ct = default)
    {
        var (p, raw) = await api.GetPlayer(playerTag, ct);
        return new PlayerSnapshot(Tags.Normalize(p.Tag), p.Name ?? "", p.ExpLevel ?? 0, p.Trophies ?? 0,
            p.Clan?.Tag is { } t ? Tags.Normalize(t) : null, p.Role, null, raw);
    }

    private static IReadOnlyList<RiverRaceParticipantSnapshot> Participants(CrRaceClanDto? c) =>
        (c?.Participants ?? []).Select(p => new RiverRaceParticipantSnapshot(
            Tags.Normalize(p.Tag), p.Name ?? "", p.Fame ?? 0, p.DecksUsed ?? 0, p.DecksUsedToday ?? 0)).ToList();

    public async Task<RiverRaceSnapshot?> GetCurrentRiverRaceAsync(string clanTag, CancellationToken ct = default)
    {
        var r = await api.GetCurrentRiverRace(clanTag, ct);
        if (r.Clan is null) return null;
        var fame = r.Clan.Fame ?? 0;
        var rank = 1 + (r.Clans ?? []).Count(c => (c.Fame ?? 0) > fame);
        return new RiverRaceSnapshot(0, r.SectionIndex ?? 0, fame, rank, Participants(r.Clan));
    }

    public async Task<IReadOnlyList<RiverRaceLogEntry>> GetRiverRaceLogAsync(string clanTag, CancellationToken ct = default)
    {
        var tag = Tags.Normalize(clanTag);
        var list = new List<RiverRaceLogEntry>();
        foreach (var e in await api.GetRiverRaceLog(clanTag, ct))
        {
            var mine = (e.Standings ?? []).FirstOrDefault(s => Tags.Normalize(s.Clan?.Tag) == tag);
            if (mine?.Clan is null) continue;
            list.Add(new RiverRaceLogEntry(e.SeasonId ?? 0, e.SectionIndex ?? 0,
                Parse.Date(e.CreatedDate) ?? DateTime.UtcNow, mine.Rank ?? 0, mine.Clan.Fame ?? 0, Participants(mine.Clan)));
        }
        return list;
    }
}

public sealed partial class BrawlStarsConnector(BsApiClient api) : IGameConnector
{
    public Game Game => Game.BrawlStars;
    public GameFeatures Features { get; } = new(false, false, false, false, false, false,
        BattleLog: true, Rankings: true, Calendar: true);

    private static ClanMemberSnapshot Map(BsMemberDto m, int i) => new(
        Tags.Normalize(m.Tag), m.Name ?? "", m.Role ?? "member", 0, m.Trophies ?? 0, 0, 0, null, i + 1);

    public async Task<ClanSnapshot> GetClanAsync(string clanTag, CancellationToken ct = default)
    {
        var c = await api.GetClub(clanTag, ct);
        return new ClanSnapshot(Tags.Normalize(c.Tag), c.Name ?? "", c.Description, null,
            c.Members?.Count ?? 0, (c.Members ?? []).Select(Map).ToList());
    }

    public async Task<IReadOnlyList<ClanMemberSnapshot>> GetMembersAsync(string clanTag, CancellationToken ct = default) =>
        (await api.GetMembers(clanTag, ct)).Select(Map).ToList();

    public async Task<PlayerSnapshot> GetPlayerAsync(string playerTag, CancellationToken ct = default)
    {
        var (p, raw) = await api.GetPlayer(playerTag, ct);
        return new PlayerSnapshot(Tags.Normalize(p.Tag), p.Name ?? "", p.ExpLevel ?? 0, p.Trophies ?? 0,
            p.Club?.Tag is { } t ? Tags.Normalize(t) : null, null, null, raw);
    }
}

public sealed class GameConnectorFactory(IEnumerable<IGameConnector> connectors) : IGameConnectorFactory
{
    private readonly Dictionary<Game, IGameConnector> _map = connectors.ToDictionary(c => c.Game);
    public IEnumerable<IGameConnector> All => _map.Values;
    public IGameConnector Get(Game game) => _map[game];
}