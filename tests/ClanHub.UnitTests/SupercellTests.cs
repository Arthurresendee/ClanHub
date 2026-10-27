using System.Net;
using ClanHub.Application.Games;
using ClanHub.Domain;
using ClanHub.Supercell;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace ClanHub.UnitTests;

/// <summary>Os clientes da Supercell contra um servidor WireMock que devolve as fixtures de tests/fixtures.</summary>
public sealed class SupercellTests : IDisposable
{
    private readonly WireMockServer _server = WireMockServer.Start();
    private readonly ServiceProvider _sp;

    public SupercellTests()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Supercell:ClashOfClans:BaseUrl"] = _server.Url + "/coc/v1",
            ["Supercell:ClashOfClans:ApiKey"] = "coc-key",
            ["Supercell:ClashRoyale:BaseUrl"] = _server.Url + "/cr/v1",
            ["Supercell:ClashRoyale:ApiKey"] = "cr-key",
            ["Supercell:BrawlStars:BaseUrl"] = _server.Url + "/bs/v1",
            ["Supercell:BrawlStars:ApiKey"] = "bs-key",
        }).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSupercell(config);
        // Retentativas rápidas nos testes.
        services.ConfigureAll<Microsoft.Extensions.Http.Resilience.HttpStandardResilienceOptions>(o =>
        {
            o.Retry.Delay = TimeSpan.FromMilliseconds(1);
            o.Retry.MaxRetryAttempts = 2;
            o.Retry.UseJitter = false;
        });
        _sp = services.BuildServiceProvider();
    }

    public void Dispose() { _server.Dispose(); _sp.Dispose(); }

    private static string Fixture(string path) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", path));

    private void Stub(string path, string body, int status = 200, string? cacheControl = null)
    {
        var r = Response.Create().WithStatusCode(status).WithHeader("Content-Type", "application/json").WithBody(body);
        if (cacheControl is not null) r = r.WithHeader("Cache-Control", cacheControl);
        _server.Given(Request.Create().WithPath(path.Replace("%23", "#")).UsingGet()).RespondWith(r);
    }

    private IGameConnector Connector(Game game) => _sp.GetRequiredService<IGameConnectorFactory>().Get(game);

    [Fact]
    public async Task Coc_Clan_MapsFixture_AndSendsBearerKey_AndEncodedTag()
    {
        Stub("/coc/v1/clans/%232PP", Fixture("coc/clan.json"));

        var clan = await Connector(Game.ClashOfClans).GetClanAsync("2pp");

        Assert.Equal("#2PP", clan.Tag);
        Assert.Equal("Clã Teste", clan.Name);
        Assert.Equal("https://img/m.png", clan.BadgeUrl);
        Assert.Equal(2, clan.Members.Count);
        Assert.Equal(16, clan.Members[0].TownHall);
        Assert.Equal("leader", clan.Members[0].Role);

        var req = Assert.Single(_server.LogEntries).RequestMessage!;
        Assert.Equal("Bearer coc-key", req.Headers!["Authorization"].Single());
        Assert.Contains("%232PP", req.AbsoluteUrl ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Coc_CurrentWar_And_WarLog_MapFixtures()
    {
        Stub("/coc/v1/clans/%232PP/currentwar", Fixture("coc/currentwar.json"));
        Stub("/coc/v1/clans/%232PP/warlog", Fixture("coc/warlog.json"));
        var c = Connector(Game.ClashOfClans);

        var war = await c.GetCurrentWarAsync("#2PP");
        Assert.NotNull(war);
        Assert.Equal(WarState.InWar, war!.State);
        Assert.Equal(new DateTime(2026, 1, 2, 10, 0, 0, DateTimeKind.Utc), war.EndTime);
        Assert.Equal("#RRR2", war.OpponentTag);
        Assert.Single(war.Attacks);
        Assert.Equal(1, war.Members.Single(m => m.Tag == "#PPP2").AttacksUsed);
        Assert.Equal(0, war.Members.Single(m => m.Tag == "#QQQ2").AttacksUsed);

        var log = await c.GetWarLogAsync("#2PP");
        Assert.Equal("win", Assert.Single(log).Result);
    }

    [Fact]
    public async Task Coc_NotInWar_ReturnsNull()
    {
        Stub("/coc/v1/clans/%232PP/currentwar", "{\"state\":\"notInWar\"}");
        Assert.Null(await Connector(Game.ClashOfClans).GetCurrentWarAsync("#2PP"));
    }

    [Fact]
    public async Task Coc_PrivateWarLog_IsTyped()
    {
        Stub("/coc/v1/clans/%232PP/warlog", "{\"reason\":\"accessDenied\"}", 403);
        var ex = await Assert.ThrowsAsync<SupercellException>(() => Connector(Game.ClashOfClans).GetWarLogAsync("#2PP"));
        Assert.Equal(SupercellErrorKind.PrivateWarLog, ex.Kind);
    }

    [Theory]
    [InlineData(404, SupercellErrorKind.NotFound)]
    [InlineData(403, SupercellErrorKind.AccessDenied)]
    [InlineData(400, SupercellErrorKind.BadRequest)]
    public async Task ErrorStatuses_AreTyped(int status, SupercellErrorKind kind)
    {
        Stub("/coc/v1/clans/%232PP", "{}", status);
        var ex = await Assert.ThrowsAsync<SupercellException>(() => Connector(Game.ClashOfClans).GetClanAsync("#2PP"));
        Assert.Equal(kind, ex.Kind);
    }

    [Fact]
    public async Task Throttling_IsRetried_ThenSurfacesAsThrottled()
    {
        Stub("/coc/v1/clans/%232PP", "{}", 429);
        var ex = await Assert.ThrowsAsync<SupercellException>(() => Connector(Game.ClashOfClans).GetClanAsync("#2PP"));
        Assert.Equal(SupercellErrorKind.Throttled, ex.Kind);
        Assert.True(_server.LogEntries.Count() > 1, "deveria ter tentado de novo");
    }

    [Fact]
    public async Task Maintenance_503_IsTyped()
    {
        Stub("/coc/v1/clans/%232PP", "{}", 503);
        var ex = await Assert.ThrowsAsync<SupercellException>(() => Connector(Game.ClashOfClans).GetClanAsync("#2PP"));
        Assert.Equal(SupercellErrorKind.Maintenance, ex.Kind);
    }

    [Fact]
    public async Task CacheControl_MaxAge_AvoidsSecondCall()
    {
        Stub("/coc/v1/clans/%232PP", Fixture("coc/clan.json"), cacheControl: "public, max-age=60");
        var c = Connector(Game.ClashOfClans);
        await c.GetClanAsync("#2PP");
        await c.GetClanAsync("#2PP");
        Assert.Single(_server.LogEntries);
    }

    [Fact]
    public async Task Coc_VerifyToken_PostsToken_AndReadsStatus()
    {
        _server.Given(Request.Create().WithPath("/coc/v1/players/#PPP2/verifytoken").UsingPost().WithBody("{\"token\":\"abc\"}"))
            .RespondWith(Response.Create().WithBody(Fixture("coc/verifytoken-ok.json")).WithHeader("Content-Type", "application/json"));
        _server.Given(Request.Create().WithPath("/coc/v1/players/#PPP2/verifytoken").UsingPost().WithBody("{\"token\":\"bad\"}"))
            .RespondWith(Response.Create().WithBody("{\"status\":\"invalid\"}").WithHeader("Content-Type", "application/json"));
        var c = Connector(Game.ClashOfClans);

        Assert.True(await c.VerifyPlayerTokenAsync("#PPP2", "abc"));
        Assert.False(await c.VerifyPlayerTokenAsync("#PPP2", "bad"));
    }

    [Fact]
    public async Task Coc_Search_And_Player()
    {
        Stub("/coc/v1/clans", "{\"items\":[{\"tag\":\"#2PP\",\"name\":\"Clã Teste\",\"members\":2,\"clanLevel\":12,\"badgeUrls\":{\"small\":\"s\"}}]}");
        Stub("/coc/v1/players/%23PPP2", Fixture("coc/player.json"));
        var c = Connector(Game.ClashOfClans);

        var found = Assert.Single(await c.SearchClansAsync("clã"));
        Assert.Equal("#2PP", found.Tag);

        var p = await c.GetPlayerAsync("#PPP2");
        Assert.Equal("#2PP", p.ClanTag);
        Assert.Equal("leader", p.ClanRole);
        Assert.Contains("heroes", p.RawJson);
    }

    [Fact]
    public async Task Cr_Clan_RiverRace_AndLog()
    {
        Stub("/cr/v1/clans/%23CR22", Fixture("cr/clan.json"));
        Stub("/cr/v1/clans/%23CR22/currentriverrace", Fixture("cr/currentriverrace.json"));
        Stub("/cr/v1/clans/%23CR22/riverracelog", Fixture("cr/riverracelog.json"));
        var c = Connector(Game.ClashRoyale);

        var clan = await c.GetClanAsync("#CR22");
        Assert.Equal("coLeader", clan.Members.Single().Role);

        var race = await c.GetCurrentRiverRaceAsync("#CR22");
        Assert.Equal(1200, race!.Fame);
        Assert.Equal(2, race.Rank); // o outro clã tem mais fama
        Assert.Equal(3, race.Participants.Single(p => p.Tag == "#P22").DecksUsedToday);

        var log = Assert.Single(await c.GetRiverRaceLogAsync("#CR22"));
        Assert.Equal(120, log.SeasonId);
        Assert.Equal(1, log.Rank);
        Assert.Equal(6000, log.Fame);
    }

    [Fact]
    public async Task Bs_Club_And_NoSearch()
    {
        Stub("/bs/v1/clubs/%23GG22", Fixture("bs/club.json"));
        Stub("/bs/v1/players/%23LL22", Fixture("bs/player.json"));
        var c = Connector(Game.BrawlStars);

        var club = await c.GetClanAsync("#GG22");
        Assert.Equal(2, club.Members.Count);
        Assert.Equal("president", club.Members[0].Role);
        Assert.Equal("#GG22", (await c.GetPlayerAsync("#LL22")).ClanTag);

        Assert.False(c.Features.ClanSearch);
        await Assert.ThrowsAsync<GameFeatureNotSupportedException>(() => c.SearchClansAsync("x"));
    }

    [Fact]
    public void Features_PerGame()
    {
        Assert.True(Connector(Game.ClashOfClans).Features.PlayerTokenVerification);
        Assert.True(Connector(Game.ClashRoyale).Features.RiverRace);
        Assert.False(Connector(Game.ClashRoyale).Features.Wars);
    }

    [Fact]
    public void DefaultBaseUrls_PointToProxy()
    {
        var o = new SupercellOptions();
        Assert.Contains("cocproxy.royaleapi.dev", o.ClashOfClans.BaseUrl);
        Assert.Contains("proxy.royaleapi.dev", o.ClashRoyale.BaseUrl);
        Assert.Contains("bsproxy.royaleapi.dev", o.BrawlStars.BaseUrl);
    }
}