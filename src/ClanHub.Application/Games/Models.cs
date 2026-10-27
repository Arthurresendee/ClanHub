using ClanHub.Domain;

namespace ClanHub.Application.Games;

public record GameFeatures(
    bool ClanSearch,
    bool PlayerTokenVerification,
    bool Wars,
    bool Cwl,
    bool CapitalRaids,
    bool RiverRace,
    bool BattleLog = false,
    bool Rankings = false,
    bool Tournaments = false,
    bool Calendar = false,
    bool UpcomingChests = false);

public record ClanMemberSnapshot(
    string Tag, string Name, string Role, int ExpLevel, int Trophies,
    int Donations, int DonationsReceived, int? TownHall, int ClanRank);

public record ClanSnapshot(
    string Tag, string Name, string? Description, string? BadgeUrl, int MemberCount,
    IReadOnlyList<ClanMemberSnapshot> Members);

public record ClanSearchResult(string Tag, string Name, string? BadgeUrl, int Members, int? Level);

public record PlayerSnapshot(
    string Tag, string Name, int ExpLevel, int Trophies, string? ClanTag, string? ClanRole,
    int? TownHall, string RawJson);

public record WarMemberSnapshot(string Tag, string Name, int MapPosition, int TownHall, int AttacksUsed);

public record WarAttackSnapshot(string AttackerTag, string DefenderTag, int Stars, double Destruction, int Order, int Duration);

public record WarSnapshot(
    WarState State, int TeamSize, int AttacksPerMember, string OpponentTag, string OpponentName,
    DateTime? StartTime, DateTime? EndTime, int Stars, double Destruction,
    int OpponentStars, double OpponentDestruction,
    IReadOnlyList<WarMemberSnapshot> Members, IReadOnlyList<WarAttackSnapshot> Attacks);

public record WarLogEntry(
    string Result, DateTime EndTime, string OpponentTag, string OpponentName, int TeamSize,
    int Stars, double Destruction, int OpponentStars, double OpponentDestruction);

public record RiverRaceParticipantSnapshot(string Tag, string Name, int Fame, int DecksUsed, int DecksUsedToday);

public record RiverRaceSnapshot(
    int SeasonId, int SectionIndex, int Fame, int Rank,
    IReadOnlyList<RiverRaceParticipantSnapshot> Participants);

public record RiverRaceLogEntry(
    int SeasonId, int SectionIndex, DateTime CreatedAt, int Rank, int Fame,
    IReadOnlyList<RiverRaceParticipantSnapshot> Participants);
// ---- Fase 2 ----
public record CwlClanSnapshot(string Tag, string Name);

public record CwlGroupSnapshot(
    string State, string Season, IReadOnlyList<CwlClanSnapshot> Clans, IReadOnlyList<IReadOnlyList<string>> Rounds);

public record CapitalRaidMemberSnapshot(string Tag, string Name, int Attacks, int AttackLimit, int BonusAttackLimit, int Looted);

public record CapitalRaidSnapshot(
    string State, DateTime StartTime, DateTime EndTime, long TotalLoot, int Attacks, int DistrictsDestroyed,
    IReadOnlyList<CapitalRaidMemberSnapshot> Members);

// ---- Fase 3 ----
public record ClanRankingSnapshot(string Scope, string? LocationName, int? Rank, int Score);

public record BattleCardSnapshot(string Name, int Level, string? IconUrl);

public record BattleSnapshot(
    DateTime? Time, string Mode, string Result, int? TrophyChange, string? Detail, IReadOnlyList<BattleCardSnapshot> Deck);

public record ChestSnapshot(int Index, string Name);

public record TournamentSnapshot(
    string Tag, string Name, string Status, string? Type, int Players, int Capacity, string? Description,
    DateTime? StartedTime, DateTime? EndedTime);

public record CalendarEntrySnapshot(string Kind, string Title, DateTime Start, DateTime End);