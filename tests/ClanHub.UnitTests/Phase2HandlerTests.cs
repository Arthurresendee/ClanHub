using ClanHub.Application.Games;
using ClanHub.Domain;
using ClanHub.Sync.Handlers;
using ClanHub.UnitTests.Support;
using Microsoft.EntityFrameworkCore;

namespace ClanHub.UnitTests;

public class CwlHandlerTests
{
    private static readonly DateTime Start = new(2026, 1, 2, 10, 0, 0, DateTimeKind.Utc);

    private static WarSnapshot War(WarState state, int stars, int attackerStars = 0) => new(
        state, 15, 1, "#RRR2", "Rivais", Start, Start.AddDays(1), stars, 60, 4, 30,
        [new WarMemberSnapshot("#PPP2", "Líder", 1, 16, attackerStars > 0 ? 1 : 0), new WarMemberSnapshot("#QQQ2", "Membro", 2, 14, 0)],
        attackerStars > 0 ? [new WarAttackSnapshot("#PPP2", "#LLL2", attackerStars, 100, 1, 90)] : []);

    private static CwlGroupSnapshot Group() => new("inWar", "2026-01", [new("#2PP", "Clã"), new("#RRR2", "Rivais")],
        [["#2WW1", "#2WW2"], ["#0", "#0"]]);

    [Fact]
    public async Task NoGroup_ReturnsIdle_AndWritesNothing()
    {
        using var db = TestDb.Create();
        var clan = Ctx.NewClan(db);
        var delay = await new CwlHandler().HandleAsync(Ctx.Make(db, clan, new FakeConnector()), default);
        Assert.Equal(TimeSpan.FromMinutes(180), delay); // início do mês: confere de 3 em 3 horas
        Assert.Empty(db.CwlSeasons);
    }

    [Fact]
    public async Task FindsOurWarOnce_StoresCwlWar_AndEmitsEvents()
    {
        using var db = TestDb.Create();
        var clan = Ctx.NewClan(db);
        var fake = new FakeConnector { CwlGroup = Group() };
        fake.CwlWars["#2WW2"] = War(WarState.InWar, 3, attackerStars: 3);
        var handler = new CwlHandler();

        var delay = await handler.HandleAsync(Ctx.Make(db, clan, fake), default);

        Assert.Equal(TimeSpan.FromMinutes(3), delay);
        Assert.Equal(["#2WW1", "#2WW2"], fake.CwlWarCalls);         // procurou nas duas guerras da rodada 1
        var war = await db.Wars.Include(w => w.Attacks).Include(w => w.Participants).SingleAsync();
        Assert.Equal(WarType.Cwl, war.Type);
        Assert.Equal("#2WW2", war.WarTag);
        Assert.Single(war.Attacks);
        Assert.Equal(2, war.Participants.Count);
        Assert.Contains(db.ClanEvents, e => e.Type == ClanEventType.CwlRoundStarted);
        Assert.Contains(db.ClanEvents, e => e.Type == ClanEventType.WarAttackMade);

        var round = await db.CwlRounds.SingleAsync(r => r.RoundIndex == 0);
        Assert.Equal("#2WW2", round.OurWarTag);
        Assert.Equal(war.Id, round.WarId);

        // segunda rodada do handler: só reconsulta a guerra já descoberta (não procura de novo)
        fake.CwlWarCalls.Clear();
        await handler.HandleAsync(Ctx.Make(db, clan, fake), default);
        Assert.Equal(["#2WW2"], fake.CwlWarCalls);
        Assert.Single(db.Wars);
        Assert.Single(db.WarAttacks);
    }

    [Fact]
    public async Task EndedWar_GetsResult_AndIsNotQueriedAgain()
    {
        using var db = TestDb.Create();
        var clan = Ctx.NewClan(db);
        var fake = new FakeConnector { CwlGroup = Group() };
        fake.CwlWars["#2WW1"] = War(WarState.InWar, 3, attackerStars: 3);
        var handler = new CwlHandler();
        await handler.HandleAsync(Ctx.Make(db, clan, fake), default);

        fake.CwlWars["#2WW1"] = War(WarState.WarEnded, 8, attackerStars: 3);
        var delay = await handler.HandleAsync(Ctx.Make(db, clan, fake), default);

        var war = await db.Wars.SingleAsync();
        Assert.Equal(WarState.WarEnded, war.State);
        Assert.Equal("win", war.Result);
        Assert.Single(db.ClanEvents.Where(e => e.Type == ClanEventType.WarEnded));
        Assert.Equal(TimeSpan.FromMinutes(180), delay); // nada ativo => volta ao ritmo ocioso

        fake.CwlWarCalls.Clear();
        await handler.HandleAsync(Ctx.Make(db, clan, fake), default);
        Assert.Empty(fake.CwlWarCalls);
        Assert.Single(db.ClanEvents.Where(e => e.Type == ClanEventType.WarEnded)); // sem evento duplicado
    }

    [Fact]
    public async Task SecondSeason_IsStoredSeparately()
    {
        using var db = TestDb.Create();
        var clan = Ctx.NewClan(db);
        var fake = new FakeConnector { CwlGroup = Group() };
        fake.CwlWars["#2WW1"] = War(WarState.WarEnded, 8);
        await new CwlHandler().HandleAsync(Ctx.Make(db, clan, fake), default);

        fake.CwlGroup = new CwlGroupSnapshot("inWar", "2026-02", [new("#2PP", "Clã")], [["#3WW1"]]);
        fake.CwlWars["#3WW1"] = War(WarState.InWar, 1) with { StartTime = Start.AddMonths(1), EndTime = Start.AddMonths(1).AddDays(1) };
        await new CwlHandler().HandleAsync(Ctx.Make(db, clan, fake, new DateTime(2026, 2, 2, 12, 0, 0, DateTimeKind.Utc)), default);

        Assert.Equal(2, await db.CwlSeasons.CountAsync());
        Assert.Equal(2, await db.Wars.CountAsync(w => w.Type == WarType.Cwl));
    }
}

public class CapitalRaidsHandlerTests
{
    private static CapitalRaidSnapshot Raid(int attacks, string state = "ongoing") => new(state,
        new DateTime(2026, 1, 2, 7, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 5, 7, 0, 0, DateTimeKind.Utc),
        1000, attacks, 2, [new CapitalRaidMemberSnapshot("#PPP2", "Líder", attacks, 5, 1, attacks * 100)]);

    [Fact]
    public async Task FirstImport_IsSilent_ThenAttacksProduceEvents()
    {
        using var db = TestDb.Create();
        var clan = Ctx.NewClan(db);
        var fake = new FakeConnector { Raids = [Raid(2)] };
        var handler = new CapitalRaidsHandler();

        await handler.HandleAsync(Ctx.Make(db, clan, fake), default);
        Assert.Empty(db.ClanEvents);
        Assert.Equal(2, (await db.CapitalRaidMembers.SingleAsync()).Attacks);

        fake.Raids = [Raid(4)];
        await handler.HandleAsync(Ctx.Make(db, clan, fake), default);
        var ev = await db.ClanEvents.SingleAsync();
        Assert.Equal(ClanEventType.RaidAttackMade, ev.Type);
        Assert.Equal("#PPP2", ev.PlayerTag);
        Assert.Equal(4, (await db.CapitalRaidMembers.SingleAsync()).Attacks);
        Assert.Single(db.CapitalRaidSeasons);

        await handler.HandleAsync(Ctx.Make(db, clan, fake), default); // nada mudou
        Assert.Single(db.ClanEvents);
    }

    [Fact]
    public async Task Delay_IsShortOnRaidWeekend_AndNeverSkipsTheNextWeekendStart()
    {
        using var db = TestDb.Create();
        var clan = Ctx.NewClan(db);
        var fake = new FakeConnector { Raids = [Raid(1)] };
        var handler = new CapitalRaidsHandler();

        var saturday = new DateTime(2026, 1, 3, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(TimeSpan.FromMinutes(15), await handler.HandleAsync(Ctx.Make(db, clan, fake, saturday), default));

        var thursday = new DateTime(2026, 1, 8, 12, 0, 0, DateTimeKind.Utc);       // 19 h antes da sexta 07:00
        var idle = await handler.HandleAsync(Ctx.Make(db, clan, fake, thursday), default);
        Assert.InRange(idle.TotalHours, 18.9, 19.1);

        var monday = new DateTime(2026, 1, 5, 12, 0, 0, DateTimeKind.Utc);          // ainda longe: usa o ritmo diário
        Assert.Equal(TimeSpan.FromMinutes(1440), await handler.HandleAsync(Ctx.Make(db, clan, fake, monday), default));
    }
}

public class RankingsHandlerTests
{
    [Fact]
    public async Task StoresOneRowPerDay_AndUpdatesIt()
    {
        using var db = TestDb.Create();
        var clan = Ctx.NewClan(db);
        var fake = new FakeConnector { Ranking = new ClanRankingSnapshot("location:1", "Brazil", 7, 40000) };
        var handler = new RankingsHandler();

        await handler.HandleAsync(Ctx.Make(db, clan, fake), default);
        fake.Ranking = new ClanRankingSnapshot("location:1", "Brazil", 5, 41000);
        await handler.HandleAsync(Ctx.Make(db, clan, fake), default);
        await handler.HandleAsync(Ctx.Make(db, clan, fake, new DateTime(2026, 1, 2, 12, 0, 0, DateTimeKind.Utc)), default);

        var rows = await db.ClanRankings.OrderBy(r => r.Date).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(5, rows[0].Rank);
        Assert.Equal(41000, rows[0].Score);
    }

    [Fact]
    public async Task NoRankingAvailable_WritesNothing()
    {
        using var db = TestDb.Create();
        var clan = Ctx.NewClan(db);
        await new RankingsHandler().HandleAsync(Ctx.Make(db, clan, new FakeConnector { Ranking = null }), default);
        Assert.Empty(db.ClanRankings);
    }
}