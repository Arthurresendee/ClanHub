namespace ClanHub.Supercell.Dtos;

public record CocMemberDto(
    string Tag, string? Name, string? Role, int? ExpLevel, int? Trophies, int? ClanRank,
    int? Donations, int? DonationsReceived, int? TownHallLevel);

public record CocClanDto(
    string Tag, string? Name, string? Description, BadgeUrlsDto? BadgeUrls, int? Members,
    int? ClanLevel, List<CocMemberDto>? MemberList, LocationDto? Location = null, int? ClanPoints = null);

public record CocPlayerDto(
    string Tag, string? Name, int? ExpLevel, int? Trophies, int? TownHallLevel, string? Role, TagNameDto? Clan);

public record CocWarAttackDto(
    string? AttackerTag, string? DefenderTag, int? Stars, double? DestructionPercentage, int? Order, int? Duration);

public record CocWarMemberDto(
    string Tag, string? Name, int? TownhallLevel, int? MapPosition, List<CocWarAttackDto>? Attacks);

public record CocWarSideDto(
    string? Tag, string? Name, int? Stars, double? DestructionPercentage, List<CocWarMemberDto>? Members);

public record CocWarDto(
    string? State, int? TeamSize, int? AttacksPerMember, string? StartTime, string? EndTime,
    CocWarSideDto? Clan, CocWarSideDto? Opponent);

public record CocWarLogEntryDto(
    string? Result, string? EndTime, int? TeamSize, CocWarSideDto? Clan, CocWarSideDto? Opponent);

public record CocVerifyTokenDto(string? Tag, string? Token, string? Status);

public record CocVerifyTokenRequest(string Token);
public record CocLeagueGroupClanDto(string? Tag, string? Name);
public record CocLeagueRoundDto(List<string>? WarTags);
public record CocLeagueGroupDto(string? State, string? Season, List<CocLeagueGroupClanDto>? Clans, List<CocLeagueRoundDto>? Rounds);

public record CocRaidMemberDto(string Tag, string? Name, int? Attacks, int? AttackLimit, int? BonusAttackLimit, int? CapitalResourcesLooted);
public record CocRaidSeasonDto(
    string? State, string? StartTime, string? EndTime, long? CapitalTotalLoot, int? TotalAttacks,
    int? EnemyDistrictsDestroyed, List<CocRaidMemberDto>? Members);

public record CocBattleDto(
    string? BattleType, bool? Attack, string? OpponentPlayerTag, int? Stars, double? DestructionPercentage);

public record CocGoldPassDto(string? StartTime, string? EndTime);