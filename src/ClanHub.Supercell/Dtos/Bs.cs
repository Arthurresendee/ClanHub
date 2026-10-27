namespace ClanHub.Supercell.Dtos;

public record BsMemberDto(string Tag, string? Name, string? Role, int? Trophies);

public record BsClubDto(
    string Tag, string? Name, string? Description, int? BadgeId, int? Trophies, List<BsMemberDto>? Members);

public record BsPlayerDto(string Tag, string? Name, int? ExpLevel, int? Trophies, TagNameDto? Club);
public record BsNamedDto(string? Name, string? Mode, string? Map);
public record BsBrawlerDto(string? Name);
public record BsBattlePlayerDto(string? Tag, string? Name, BsBrawlerDto? Brawler);
public record BsBattleDetailDto(
    string? Mode, string? Type, string? Result, int? TrophyChange, int? Rank, List<List<BsBattlePlayerDto>>? Teams);
public record BsBattleDto(string? BattleTime, BsNamedDto? Event, BsBattleDetailDto? Battle);

public record BsEventSlotEventDto(int? Id, string? Mode, string? Map);
public record BsEventSlotDto(string? StartTime, string? EndTime, BsEventSlotEventDto? Event);