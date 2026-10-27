using ClanHub.Application.Games;
using ClanHub.Domain;
using ClanHub.Supercell;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace ClanHub.UnitTests;

public sealed class SupercellPhase2And3Tests : IDisposable
{
    private readonly WireMockServer _server = WireMockServer.Start();
    private readonly ServiceProvider _sp;

    public SupercellPhase2And3Tests()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Supercell:ClashOfClans:BaseUrl"] = _server.Url + "/coc/v1", ["Supercell:ClashOfClans:ApiKey"] = "k",
            ["Supercell:ClashRoyale:BaseUrl"] = _server.Url + "/cr/v1", ["Supercell:ClashRoyale:ApiKey"] = "k",
            ["Supercell:BrawlStars:BaseUrl"] = _server.Url + "/bs/v1", ["Supercell:BrawlStars:ApiKey"] = "k",
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSupercell(config);
        services.ConfigureAll<Microsoft.Extensions.Http.Resilience.HttpStandardResilienceOptions>(o =>
        {
            o.Retry.Delay = TimeSpan.FromMilliseconds(1);
            o.Retry.MaxRetryAttempts = 1;
            o.Retry.UseJitter = false;
        });
        _sp = services.BuildServiceProvider();
    }

    public void Dispose() { _server.Dispose(); _sp.Dispose(); }

    private static string Fixture(string path) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", path));

    private void Stub(string path, string body, int status = 200) =>
        _server.Given(Request.Create().WithPath(path.Replace("%23", "#")).UsingGet())
            .RespondWith(Response.Create().WithStatusCode(status).WithHeader("Content-Type", "application/json").WithBody(body));

    private IGameConnector C(Game g) => _sp.GetRequiredService<IGameConnectorFactory>().Get(g);

    // ---- CWL ----
    [Fact]
    public async Task Coc_CwlGroup_MapsRoundsAndNormalizesTags()
    {
        Stub("/coc/v1/clans/%232PP/currentwar/leaguegroup", Fixture("coc/leaguegroup.json"));
        var g = await C(Game.ClashOfClans).GetCwlGroupAsync("#2PP");
        Assert.NotNull(g);
        Assert.Equal("2026-01", g!.Season);
        Assert.Equal(4, g.Clans.Count);
        Assert.Equal(2, g.Rounds.Count);
        Assert.Equal(["#2WWW", "#2WWL", "#2WWP", "#2WWQ"], g.Rounds[0]);
        Assert.All(g.Rounds[1], t => Assert.Equal("#0", t));
    }

    [Fact]
    public async Task Coc_CwlGroup_NotInCwl_IsNull_OnNotFoundOrNotInWar()
    {
        Stub("/coc/v1/clans/%232PP/currentwar/leaguegroup", "{}", 404);
        Assert.Null(await C(Game.ClashOfClans).GetCwlGroupAsync("#2PP"));
        _server.Reset();
        Stub("/coc/v1/clans/%232PP/currentwar/leaguegroup", "{\"state\":\"notInWar\"}");
        Assert.Null(await C(Game.ClashOfClans).GetCwlGroupAsync("#2PP"));
    }

    [Fact]
    public async Task Coc_CwlWar_IsOrientedToOurClan_EvenWhenWeAreTheOpponent()
    {
        Stub("/coc/v1/clanwarleagues/wars/%232WWW", Fixture("coc/cwlwar.json")); // nosso clã vem em "opponent"
        var w = await C(Game.ClashOfClans).GetCwlWarAsync("#2WWW", "#2PP");
        Assert.NotNull(w);
        Assert.Equal(9, w!.Stars);                  // estrelas do nosso lado
        Assert.Equal(5, w.OpponentStars);
        Assert.Equal("#RRR2", w.OpponentTag);
        Assert.Equal("Rivais", w.OpponentName);
        Assert.Equal(1, w.AttacksPerMember);       // CWL: 1 ataque por jogador
        Assert.Single(w.Attacks);
        Assert.Equal(1, w.Members.Single(m => m.Tag == "#PPP2").AttacksUsed);
    }

    [Fact]
    public async Task Coc_CwlWar_WithoutOurClan_IsNull()
    {
        Stub("/coc/v1/clanwarleagues/wars/%232WWL", Fixture("coc/cwlwar-other.json"));
        Assert.Null(await C(Game.ClashOfClans).GetCwlWarAsync("#2WWL", "#2PP"));
    }

    // ---- Capital ----
    [Fact]
    public async Task Coc_CapitalRaids_Map()
    {
        Stub("/coc/v1/clans/%232PP/capitalraidseasons", Fixture("coc/capitalraids.json"));
        var s = Assert.Single(await C(Game.ClashOfClans).GetCapitalRaidSeasonsAsync("#2PP"));
        Assert.Equal(new DateTime(2025, 12, 26, 7, 0, 0, DateTimeKind.Utc), s.StartTime);
        Assert.Equal(120000, s.TotalLoot);
        Assert.Equal(2, s.Members.Count);
        var leader = s.Members.Single(m => m.Tag == "#PPP2");
        Assert.Equal((5, 5, 1, 9000), (leader.Attacks, leader.AttackLimit, leader.BonusAttackLimit, leader.Looted));
    }

    // ---- Rankings ----
    [Fact]
    public async Task Coc_Ranking_UsesClanLocation_AndFindsPosition()
    {
        Stub("/coc/v1/clans/%232PP", Fixture("coc/clan-with-location.json"));
        Stub("/coc/v1/locations/32000007/rankings/clans", Fixture("coc/rankings.json"));
        var r = await C(Game.ClashOfClans).GetClanRankingAsync("#2PP", null);
        Assert.Equal(("location:32000007", "Brazil", 2, 45000), (r!.Scope, r.LocationName, r.Rank, r.Score));
    }

    [Fact]
    public async Task Coc_Ranking_ClanOutsideTop200_HasNoRankButKeepsScore()
    {
        Stub("/coc/v1/clans/%232PP", Fixture("coc/clan-with-location.json"));
        Stub("/coc/v1/locations/32000007/rankings/clans", "{\"items\":[]}");
        var r = await C(Game.ClashOfClans).GetClanRankingAsync("#2PP", null);
        Assert.Null(r!.Rank);
        Assert.Equal(45000, r.Score);
    }

    [Fact]
    public async Task Coc_Ranking_ClanWithoutLocation_IsNull()
    {
        Stub("/coc/v1/clans/%232PP", Fixture("coc/clan.json"));
        Assert.Null(await C(Game.ClashOfClans).GetClanRankingAsync("#2PP", null));
    }

    [Fact]
    public async Task Cr_Ranking()
    {
        Stub("/cr/v1/clans/%23CR22", Fixture("cr/clan-with-location.json"));
        Stub("/cr/v1/locations/57000038/rankings/clans", Fixture("cr/rankings.json"));
        var r = await C(Game.ClashRoyale).GetClanRankingAsync("#CR22", null);
        Assert.Equal((5, 50000), (r!.Rank, r.Score));
    }

    [Fact]
    public async Task Bs_Ranking_UsesCountryOrGlobal()
    {
        Stub("/bs/v1/rankings/br/clubs", Fixture("bs/rankings.json"));
        Stub("/bs/v1/rankings/global/clubs", "{\"items\":[]}");
        Stub("/bs/v1/clubs/%23GG22", Fixture("bs/club.json"));
        var br = await C(Game.BrawlStars).GetClanRankingAsync("#GG22", "BR");
        Assert.Equal((7, "country:br", "BR"), (br!.Rank, br.Scope, br.LocationName));
        var global = await C(Game.BrawlStars).GetClanRankingAsync("#GG22", null);
        Assert.Equal("country:global", global!.Scope);
        Assert.Null(global.Rank);
    }

    // ---- Battle logs ----
    [Fact]
    public async Task Coc_BattleLog_Attacks_AndDefenses()
    {
        Stub("/coc/v1/players/%23PPP2/battlelog", Fixture("coc/battlelog.json"));
        var log = await C(Game.ClashOfClans).GetBattleLogAsync("#PPP2");
        Assert.Equal(["attack", "defense"], log.Select(b => b.Result));
        Assert.Contains("2 estrela", log[0].Detail);
    }

    [Fact]
    public async Task Cr_BattleLog_ResultsDecksAndTrophies()
    {
        Stub("/cr/v1/players/%23P22/battlelog", Fixture("cr/battlelog.json"));
        var log = await C(Game.ClashRoyale).GetBattleLogAsync("#P22");
        Assert.Equal(["win", "loss"], log.Select(b => b.Result));
        Assert.Equal(30, log[0].TrophyChange);
        Assert.Equal(-28, log[1].TrophyChange);
        Assert.Equal(new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc), log[0].Time);
        var card = Assert.Single(log[0].Deck);
        Assert.Equal(("Knight", 14, "https://img/knight.png"), (card.Name, card.Level, card.IconUrl));
        Assert.Contains("3-1", log[0].Detail);
    }

    [Fact]
    public async Task Bs_BattleLog_MapsModeResultAndBrawlers()
    {
        Stub("/bs/v1/players/%23LL22/battlelog", Fixture("bs/battlelog.json"));
        var b = Assert.Single(await C(Game.BrawlStars).GetBattleLogAsync("#LL22"));
        Assert.Equal(("gemGrab", "victory", 8), (b.Mode, b.Result, b.TrophyChange));
        Assert.Contains("SHELLY", b.Detail);
        Assert.Contains("Hard Rock Mine", b.Detail);
    }

    // ---- CR extras ----
    [Fact]
    public async Task Cr_UpcomingChests()
    {
        Stub("/cr/v1/players/%23P22/upcomingchests", Fixture("cr/upcomingchests.json"));
        var chests = await C(Game.ClashRoyale).GetUpcomingChestsAsync("#P22");
        Assert.Equal(["Silver Chest", "Golden Chest"], chests.Select(c => c.Name));
    }

    [Fact]
    public async Task Cr_Tournaments_SearchAndDetail()
    {
        Stub("/cr/v1/tournaments", Fixture("cr/tournaments.json"));
        Stub("/cr/v1/tournaments/%23TTT2", Fixture("cr/tournament.json"));
        var c = C(Game.ClashRoyale);

        var found = Assert.Single(await c.SearchTournamentsAsync("aberto"));
        Assert.Equal(("#TTT2", "inPreparation", 100), (found.Tag, found.Status, found.Capacity));

        var t = await c.GetTournamentAsync("#TTT2");
        Assert.Equal(2, t.Players);
        Assert.Equal("Vale tudo", t.Description);
        Assert.Equal(new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc), t.StartedTime);
    }

    // ---- Calendar ----
    [Fact]
    public async Task Coc_Calendar_GoldPass()
    {
        Stub("/coc/v1/goldpass/seasons/current", Fixture("coc/goldpass.json"));
        var e = Assert.Single(await C(Game.ClashOfClans).GetCalendarAsync());
        Assert.Equal("goldpass", e.Kind);
        Assert.Equal(new DateTime(2026, 2, 1, 8, 0, 0, DateTimeKind.Utc), e.End);
    }

    [Fact]
    public async Task Bs_Calendar_EventRotation()
    {
        Stub("/bs/v1/events/rotation", Fixture("bs/events.json"));
        var e = Assert.Single(await C(Game.BrawlStars).GetCalendarAsync());
        Assert.Contains("Hard Rock Mine", e.Title);
    }

    [Fact]
    public async Task UnsupportedFeatures_Throw()
    {
        await Assert.ThrowsAsync<GameFeatureNotSupportedException>(() => C(Game.ClashRoyale).GetCwlGroupAsync("#X"));
        await Assert.ThrowsAsync<GameFeatureNotSupportedException>(() => C(Game.BrawlStars).GetCapitalRaidSeasonsAsync("#X"));
        await Assert.ThrowsAsync<GameFeatureNotSupportedException>(() => C(Game.ClashOfClans).GetUpcomingChestsAsync("#X"));
        await Assert.ThrowsAsync<GameFeatureNotSupportedException>(() => C(Game.BrawlStars).SearchTournamentsAsync("x"));
    }

    [Fact]
    public void FeatureFlags_PerGame()
    {
        var coc = C(Game.ClashOfClans).Features; var cr = C(Game.ClashRoyale).Features; var bs = C(Game.BrawlStars).Features;
        Assert.True(coc.Cwl && coc.CapitalRaids && coc.BattleLog && coc.Rankings && coc.Calendar && !coc.Tournaments);
        Assert.True(cr.Tournaments && cr.UpcomingChests && cr.Rankings && !cr.Cwl && !cr.Calendar);
        Assert.True(bs.BattleLog && bs.Rankings && bs.Calendar && !bs.Wars);
    }
}