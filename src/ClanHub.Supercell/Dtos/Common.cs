namespace ClanHub.Supercell.Dtos;

public record ItemsDto<T>(List<T>? Items);

public record BadgeUrlsDto(string? Small, string? Medium, string? Large);

public record TagNameDto(string? Tag, string? Name);

public record LocationDto(int? Id, string? Name, string? CountryCode);

public record RankedClanDto(string Tag, int? Rank, int? ClanPoints, int? ClanScore);