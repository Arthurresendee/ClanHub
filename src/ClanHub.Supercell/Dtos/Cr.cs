namespace ClanHub.Supercell.Dtos;

public record CrMemberDto(
    string Tag, string? Name, string? Role, int? ExpLevel, int? Trophies, int? ClanRank,
    int? Donations, int? DonationsReceived);

public record CrClanDto(
    string Tag, string? Name, string? Description, int? BadgeId, string? BadgeUrl, int? Members,
    int? ClanScore, List<CrMemberDto>? MemberList, LocationDto? Location = null);

public record CrPlayerDto(
    string Tag, string? Name, int? ExpLevel, int? Trophies, string? Role, TagNameDto? Clan);

public record CrRaceParticipantDto(
    string Tag, string? Name, int? Fame, int? DecksUsed, int? DecksUsedToday);

public record CrRaceClanDto(
    string? Tag, string? Name, int? Fame, List<CrRaceParticipantDto>? Participants);

public record CrCurrentRaceDto(
    string? State, int? SectionIndex, int? PeriodIndex, CrRaceClanDto? Clan, List<CrRaceClanDto>? Clans);

public record CrRaceStandingDto(int? Rank, CrRaceClanDto? Clan);

public record CrRaceLogEntryDto(
    int? SeasonId, int? SectionIndex, string? CreatedDate, List<CrRaceStandingDto>? Standings);
public record CrIconUrlsDto(string? Medium);
public record CrCardDto(string? Name, int? Level, CrIconUrlsDto? IconUrls);
public record CrBattleSideDto(string? Tag, string? Name, int? Crowns, int? TrophyChange, List<CrCardDto>? Cards);
public record CrNamedDto(string? Name);
public record CrBattleDto(
    string? Type, string? BattleTime, CrNamedDto? GameMode, CrNamedDto? Arena,
    List<CrBattleSideDto>? Team, List<CrBattleSideDto>? Opponent);

public record CrChestDto(int? Index, string? Name);

public record CrTournamentDto(
    string? Tag, string? Name, string? Status, string? Type, string? Description, int? Capacity, int? MaxCapacity,
    string? StartedTime, string? EndedTime, List<CrTournamentMemberDto>? MembersList);
public record CrTournamentMemberDto(string? Tag, string? Name);