using ClanHub.Application.Games;
using ClanHub.Domain;
using ClanHub.Supercell.Dtos;
using Microsoft.Extensions.Caching.Memory;

namespace ClanHub.Supercell;

public sealed class CocApiClient(HttpClient http, IMemoryCache cache) : SupercellApiClient(http, cache)
{
    protected override string Name => "CoC";

    public Task<CocClanDto> GetClan(string tag, CancellationToken ct) => GetAsync<CocClanDto>($"clans/{Tag(tag)}", ct);
    public async Task<List<CocMemberDto>> GetMembers(string tag, CancellationToken ct) =>
        (await GetAsync<ItemsDto<CocMemberDto>>($"clans/{Tag(tag)}/members", ct)).Items ?? [];
    public async Task<List<CocClanDto>> Search(string name, CancellationToken ct) =>
        (await GetAsync<ItemsDto<CocClanDto>>($"clans?name={Uri.EscapeDataString(name)}&limit=20", ct)).Items ?? [];
    public Task<(CocPlayerDto Value, string Raw)> GetPlayer(string tag, CancellationToken ct) =>
        GetWithRawAsync<CocPlayerDto>($"players/{Tag(tag)}", ct);
    public Task<CocWarDto> GetCurrentWar(string tag, CancellationToken ct) =>
        PrivateLogAware(() => GetAsync<CocWarDto>($"clans/{Tag(tag)}/currentwar", ct));
    public async Task<List<CocWarLogEntryDto>> GetWarLog(string tag, CancellationToken ct) =>
        (await PrivateLogAware(() => GetAsync<ItemsDto<CocWarLogEntryDto>>($"clans/{Tag(tag)}/warlog", ct))).Items ?? [];

    public Task<CocLeagueGroupDto> GetLeagueGroup(string tag, CancellationToken ct) =>
        GetAsync<CocLeagueGroupDto>($"clans/{Tag(tag)}/currentwar/leaguegroup", ct);
    public Task<CocWarDto> GetCwlWar(string warTag, CancellationToken ct) =>
        GetAsync<CocWarDto>($"clanwarleagues/wars/{Tag(warTag)}", ct);
    public async Task<List<CocRaidSeasonDto>> GetRaidSeasons(string tag, CancellationToken ct) =>
        (await GetAsync<ItemsDto<CocRaidSeasonDto>>($"clans/{Tag(tag)}/capitalraidseasons?limit=6", ct)).Items ?? [];
    public async Task<List<RankedClanDto>> GetLocationRankings(int locationId, CancellationToken ct) =>
        (await GetAsync<ItemsDto<RankedClanDto>>($"locations/{locationId}/rankings/clans?limit=200", ct)).Items ?? [];
    public async Task<List<CocBattleDto>> GetBattleLog(string playerTag, CancellationToken ct) =>
        (await GetAsync<ItemsDto<CocBattleDto>>($"players/{Tag(playerTag)}/battlelog", ct)).Items ?? [];
    public Task<CocGoldPassDto> GetGoldPass(CancellationToken ct) => GetAsync<CocGoldPassDto>("goldpass/seasons/current", ct);

    public async Task<bool> VerifyToken(string playerTag, string token, CancellationToken ct)
    {
        using var response = await PostAsync($"players/{Tag(playerTag)}/verifytoken", new CocVerifyTokenRequest(token), ct);
        var dto = await System.Net.Http.Json.HttpContentJsonExtensions.ReadFromJsonAsync<CocVerifyTokenDto>(
            response.Content, Json, ct);
        return string.Equals(dto?.Status, "ok", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class CrApiClient(HttpClient http, IMemoryCache cache) : SupercellApiClient(http, cache)
{
    protected override string Name => "CR";

    public Task<CrClanDto> GetClan(string tag, CancellationToken ct) => GetAsync<CrClanDto>($"clans/{Tag(tag)}", ct);
    public async Task<List<CrMemberDto>> GetMembers(string tag, CancellationToken ct) =>
        (await GetAsync<ItemsDto<CrMemberDto>>($"clans/{Tag(tag)}/members", ct)).Items ?? [];
    public async Task<List<CrClanDto>> Search(string name, CancellationToken ct) =>
        (await GetAsync<ItemsDto<CrClanDto>>($"clans?name={Uri.EscapeDataString(name)}&limit=20", ct)).Items ?? [];
    public Task<(CrPlayerDto Value, string Raw)> GetPlayer(string tag, CancellationToken ct) =>
        GetWithRawAsync<CrPlayerDto>($"players/{Tag(tag)}", ct);
    public Task<CrCurrentRaceDto> GetCurrentRiverRace(string tag, CancellationToken ct) =>
        GetAsync<CrCurrentRaceDto>($"clans/{Tag(tag)}/currentriverrace", ct);
    public async Task<List<RankedClanDto>> GetLocationRankings(int locationId, CancellationToken ct) =>
        (await GetAsync<ItemsDto<RankedClanDto>>($"locations/{locationId}/rankings/clans?limit=200", ct)).Items ?? [];
    public Task<List<CrBattleDto>> GetBattleLog(string playerTag, CancellationToken ct) =>
        GetAsync<List<CrBattleDto>>($"players/{Tag(playerTag)}/battlelog", ct);
    public async Task<List<CrChestDto>> GetUpcomingChests(string playerTag, CancellationToken ct) =>
        (await GetAsync<ItemsDto<CrChestDto>>($"players/{Tag(playerTag)}/upcomingchests", ct)).Items ?? [];
    public async Task<List<CrTournamentDto>> SearchTournaments(string name, CancellationToken ct) =>
        (await GetAsync<ItemsDto<CrTournamentDto>>($"tournaments?name={Uri.EscapeDataString(name)}&limit=25", ct)).Items ?? [];
    public Task<CrTournamentDto> GetTournament(string tag, CancellationToken ct) =>
        GetAsync<CrTournamentDto>($"tournaments/{Tag(tag)}", ct);
    public async Task<List<CrRaceLogEntryDto>> GetRiverRaceLog(string tag, CancellationToken ct) =>
        (await GetAsync<ItemsDto<CrRaceLogEntryDto>>($"clans/{Tag(tag)}/riverracelog", ct)).Items ?? [];
}

public sealed class BsApiClient(HttpClient http, IMemoryCache cache) : SupercellApiClient(http, cache)
{
    protected override string Name => "BS";

    public Task<BsClubDto> GetClub(string tag, CancellationToken ct) => GetAsync<BsClubDto>($"clubs/{Tag(tag)}", ct);
    public async Task<List<BsMemberDto>> GetMembers(string tag, CancellationToken ct) =>
        (await GetAsync<ItemsDto<BsMemberDto>>($"clubs/{Tag(tag)}/members", ct)).Items ?? [];
    public Task<(BsPlayerDto Value, string Raw)> GetPlayer(string tag, CancellationToken ct) =>
        GetWithRawAsync<BsPlayerDto>($"players/{Tag(tag)}", ct);
    public async Task<List<RankedClanDto>> GetClubRankings(string countryCode, CancellationToken ct) =>
        (await GetAsync<ItemsDto<RankedClanDto>>($"rankings/{Uri.EscapeDataString(countryCode)}/clubs?limit=200", ct)).Items ?? [];
    public async Task<List<BsBattleDto>> GetBattleLog(string playerTag, CancellationToken ct) =>
        (await GetAsync<ItemsDto<BsBattleDto>>($"players/{Tag(playerTag)}/battlelog", ct)).Items ?? [];
    public Task<List<BsEventSlotDto>> GetEventRotation(CancellationToken ct) =>
        GetAsync<List<BsEventSlotDto>>("events/rotation", ct);
}