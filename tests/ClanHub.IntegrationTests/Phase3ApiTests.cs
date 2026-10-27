using System.Net;
using System.Net.Http.Json;
using ClanHub.Application.Games;
using ClanHub.Contracts;
using ClanHub.Domain;
using ClanHub.IntegrationTests.Infrastructure;

namespace ClanHub.IntegrationTests;

[Collection("api")]
public class Phase3ApiTests(ApiFactory factory) : ApiTestBase(factory)
{
    private static ClanMemberSnapshot M(string tag, string name) => new(tag, name, "member", 100, 4000, 0, 0, 15, 1);

    private async Task<(HttpClient Owner, Guid ClanId, WorkspaceSummaryDto Ws)> Setup()
    {
        factory.Connector.Clan = new ClanSnapshot("#2PP", "Clã Teste", "x", null, 2, [M("#PPP2", "Líder"), M("#QQQ2", "Membro")]);
        var owner = await factory.RegisterAndLogin();
        var (ws, clanId) = await factory.CreateWorkspaceWithClan(owner);
        await factory.Tick();
        return (owner, clanId, ws);
    }

    [DockerFact]
    public async Task Ranking_EndToEnd_ThroughTheTick()
    {
        factory.Connector.Ranking = new ClanRankingSnapshot("location:1", "Brazil", 12, 41000);
        var (owner, clanId, _) = await Setup();

        var points = await (await owner.GetAsync($"/api/v1/clans/{clanId}/ranking?days=7")).ReadAs<List<ClanRankingDto>>();
        var p = Assert.Single(points);
        Assert.Equal(("Brazil", 12, 41000), (p.LocationName, p.Rank, p.Score));

        // mesma data: atualiza em vez de duplicar
        factory.Connector.Ranking = new ClanRankingSnapshot("location:1", "Brazil", 10, 42000);
        await factory.ForceDue(clanId, SyncKind.Rankings);
        await factory.Tick();
        var again = Assert.Single(await (await owner.GetAsync($"/api/v1/clans/{clanId}/ranking")).ReadAs<List<ClanRankingDto>>());
        Assert.Equal((10, 42000), (again.Rank, again.Score));

        var stranger = await factory.RegisterAndLogin();
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.GetAsync($"/api/v1/clans/{clanId}/ranking")).StatusCode);
    }

    [DockerFact]
    public async Task Battles_AreServedFromTheGame_WithShortCache_AndOnlyForMembers()
    {
        factory.Connector.Battles = [new BattleSnapshot(new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc), "Ladder", "win", 30, "vs Peao (3-1)",
            [new BattleCardSnapshot("Knight", 14, "https://img/k.png")])];
        var (owner, clanId, _) = await Setup();
        factory.Connector.BattleCalls = 0;

        var list = await (await owner.GetAsync($"/api/v1/clans/{clanId}/members/%23PPP2/battles")).ReadAs<List<BattleDto>>();
        var b = Assert.Single(list);
        Assert.Equal(("Ladder", "win", 30), (b.Mode, b.Result, b.TrophyChange));
        Assert.Equal("Knight", Assert.Single(b.Deck).Name);

        // segunda consulta vem do cache: o jogo não é chamado de novo
        await owner.GetAsync($"/api/v1/clans/{clanId}/members/%23PPP2/battles");
        Assert.Equal(1, factory.Connector.BattleCalls);

        // quem não é membro do clã não pode ser consultado por aqui
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/v1/clans/{clanId}/members/%23ZZZ2/battles")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.NewClient().GetAsync($"/api/v1/clans/{clanId}/members/%23PPP2/battles")).StatusCode);
    }

    [DockerFact]
    public async Task Chests_Work_ForMembers()
    {
        factory.Connector.Chests = [new ChestSnapshot(0, "Silver Chest"), new ChestSnapshot(1, "Golden Chest")];
        var (owner, clanId, _) = await Setup();
        var chests = await (await owner.GetAsync($"/api/v1/clans/{clanId}/members/%23QQQ2/chests")).ReadAs<List<ChestDto>>();
        Assert.Equal(["Silver Chest", "Golden Chest"], chests.Select(c => c.Name));
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/v1/clans/{clanId}/members/%23ZZZ2/chests")).StatusCode);
    }

    [DockerFact]
    public async Task Tournaments_SearchAndDetail_WithValidation()
    {
        factory.Connector.Tournaments = [new TournamentSnapshot("#PYQ2", "Torneio Aberto", "inPreparation", "open", 10, 50, "Vale tudo", null, null)];
        var user = await factory.RegisterAndLogin();

        Assert.Equal(HttpStatusCode.BadRequest, (await user.GetAsync("/api/v1/games/ClashRoyale/tournaments/search?name=ab")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await user.GetAsync("/api/v1/games/Inexistente/tournaments/search?name=abc")).StatusCode);
        var found = Assert.Single(await (await user.GetAsync("/api/v1/games/ClashRoyale/tournaments/search?name=aberto")).ReadAs<List<TournamentDto>>());
        Assert.Equal(("#PYQ2", 50), (found.Tag, found.Capacity));
        var detail = await (await user.GetAsync("/api/v1/games/ClashRoyale/tournaments/%23PYQ2")).ReadAs<TournamentDto>();
        Assert.Equal("Vale tudo", detail.Description);
        Assert.Equal(HttpStatusCode.BadRequest, (await user.GetAsync("/api/v1/games/ClashRoyale/tournaments/INVALIDA")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await user.GetAsync("/api/v1/games/ClashRoyale/tournaments/%23LLL2")).StatusCode);   // erro da Supercell vira 404
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.NewClient().GetAsync("/api/v1/games/ClashRoyale/tournaments/search?name=abc")).StatusCode);
    }

    [DockerFact]
    public async Task Calendar_ReturnsEntries_ForLoggedUsers()
    {
        factory.Connector.Calendar = [new CalendarEntrySnapshot("goldpass", "Temporada do Passe de Ouro",
            new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc), new DateTime(2026, 2, 1, 8, 0, 0, DateTimeKind.Utc))];
        var user = await factory.RegisterAndLogin();
        var list = await (await user.GetAsync("/api/v1/games/ClashOfClans/calendar")).ReadAs<List<CalendarEntryDto>>();
        Assert.Equal("goldpass", Assert.Single(list).Kind);
        Assert.Equal(HttpStatusCode.BadRequest, (await user.GetAsync("/api/v1/games/Nada/calendar")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.NewClient().GetAsync("/api/v1/games/ClashOfClans/calendar")).StatusCode);
    }

    [DockerFact]
    public async Task GamesList_AdvertisesPhase3Features()
    {
        var games = await (await factory.NewClient().GetAsync("/api/v1/games")).ReadAs<List<GameInfoDto>>();
        var f = games.Single().Features;   // o conector falso responde por todos os jogos
        Assert.True(f.BattleLog && f.Rankings && f.Tournaments && f.Calendar && f.UpcomingChests && f.Cwl && f.CapitalRaids);
    }
}