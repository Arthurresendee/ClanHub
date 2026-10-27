using ClanHub.Domain;

namespace ClanHub.Application.Games;

public interface IGameConnector
{
    Game Game { get; }
    GameFeatures Features { get; }

    Task<ClanSnapshot> GetClanAsync(string clanTag, CancellationToken ct = default);
    Task<IReadOnlyList<ClanMemberSnapshot>> GetMembersAsync(string clanTag, CancellationToken ct = default);
    Task<PlayerSnapshot> GetPlayerAsync(string playerTag, CancellationToken ct = default);

    Task<IReadOnlyList<ClanSearchResult>> SearchClansAsync(string name, CancellationToken ct = default)
        => throw new GameFeatureNotSupportedException(Game, nameof(Features.ClanSearch));

    Task<bool> VerifyPlayerTokenAsync(string playerTag, string token, CancellationToken ct = default)
        => throw new GameFeatureNotSupportedException(Game, nameof(Features.PlayerTokenVerification));

    Task<WarSnapshot?> GetCurrentWarAsync(string clanTag, CancellationToken ct = default)
        => throw new GameFeatureNotSupportedException(Game, nameof(Features.Wars));

    Task<IReadOnlyList<WarLogEntry>> GetWarLogAsync(string clanTag, CancellationToken ct = default)
        => throw new GameFeatureNotSupportedException(Game, nameof(Features.Wars));

    Task<RiverRaceSnapshot?> GetCurrentRiverRaceAsync(string clanTag, CancellationToken ct = default)
        => throw new GameFeatureNotSupportedException(Game, nameof(Features.RiverRace));

    Task<IReadOnlyList<RiverRaceLogEntry>> GetRiverRaceLogAsync(string clanTag, CancellationToken ct = default)
        => throw new GameFeatureNotSupportedException(Game, nameof(Features.RiverRace));

    // ---- Fase 2 ----
    Task<CwlGroupSnapshot?> GetCwlGroupAsync(string clanTag, CancellationToken ct = default)
        => throw new GameFeatureNotSupportedException(Game, nameof(Features.Cwl));

    /// <summary>Guerra da CWL vista do lado do clã informado; null se o clã não participa dela.</summary>
    Task<WarSnapshot?> GetCwlWarAsync(string warTag, string clanTag, CancellationToken ct = default)
        => throw new GameFeatureNotSupportedException(Game, nameof(Features.Cwl));

    Task<IReadOnlyList<CapitalRaidSnapshot>> GetCapitalRaidSeasonsAsync(string clanTag, CancellationToken ct = default)
        => throw new GameFeatureNotSupportedException(Game, nameof(Features.CapitalRaids));

    // ---- Fase 3 ----
    Task<ClanRankingSnapshot?> GetClanRankingAsync(string clanTag, string? countryCode, CancellationToken ct = default)
        => throw new GameFeatureNotSupportedException(Game, nameof(Features.Rankings));

    Task<IReadOnlyList<BattleSnapshot>> GetBattleLogAsync(string playerTag, CancellationToken ct = default)
        => throw new GameFeatureNotSupportedException(Game, nameof(Features.BattleLog));

    Task<IReadOnlyList<ChestSnapshot>> GetUpcomingChestsAsync(string playerTag, CancellationToken ct = default)
        => throw new GameFeatureNotSupportedException(Game, nameof(Features.UpcomingChests));

    Task<IReadOnlyList<TournamentSnapshot>> SearchTournamentsAsync(string name, CancellationToken ct = default)
        => throw new GameFeatureNotSupportedException(Game, nameof(Features.Tournaments));

    Task<TournamentSnapshot> GetTournamentAsync(string tag, CancellationToken ct = default)
        => throw new GameFeatureNotSupportedException(Game, nameof(Features.Tournaments));

    Task<IReadOnlyList<CalendarEntrySnapshot>> GetCalendarAsync(CancellationToken ct = default)
        => throw new GameFeatureNotSupportedException(Game, nameof(Features.Calendar));
}

public interface IGameConnectorFactory
{
    IGameConnector Get(Game game);
    IEnumerable<IGameConnector> All { get; }
}