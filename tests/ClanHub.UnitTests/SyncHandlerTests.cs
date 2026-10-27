using ClanHub.Application.Games;
using ClanHub.Domain;
using ClanHub.Sync.Handlers;
using ClanHub.UnitTests.Support;
using Microsoft.EntityFrameworkCore;

namespace ClanHub.UnitTests;

public class MembersHandlerTests
{
    private static ClanMemberSnapshot M(string tag, string name, string role = "member", int donations = 0, int trophies = 4000) =>
        new(tag, name, role, 100, trophies, donations, 0, 15, 1);

    private static ClanSnapshot Snap(params ClanMemberSnapshot[] members) => new("#2PP", "Clã", null, null, members.Length, members);

    [Fact]
    public async Task FirstSync_CreatesMembers_WithoutEvents()
    {
        using var db = TestDb.Create();
        var clan = Ctx.NewClan(db);
        var fake = new FakeConnector { Clan = Snap(M("#PPP2", "Líder", "leader"), M("#QQQ2", "Membro")) };

        var delay = await new MembersHandler().HandleAsync(Ctx.Make(db, clan, fake), default);

        Assert.Equal(TimeSpan.FromMinutes(5), delay);
        Assert.Equal(2, await db.ClanMemberships.CountAsync(m => m.IsCurrent));
        Assert.Equal(2, await db.Players.CountAsync());
        Assert.Empty(db.ClanEvents);
    }

    [Fact]
    public async Task SecondSync_GeneratesEvents_InSameSave()
    {
        using var db = TestDb.Create();
        var clan = Ctx.NewClan(db);
        var fake = new FakeConnector { Clan = Snap(M("#PPP2", "Líder", "leader"), M("#QQQ2", "Membro")) };
        var handler = new MembersHandler();
        await handler.HandleAsync(Ctx.Make(db, clan, fake), default);

        fake.Clan = Snap(M("#PPP2", "Líder", "leader", donations: 40), M("#RRR2", "Novo"));
        await handler.HandleAsync(Ctx.Make(db, clan, fake, new DateTime(2026, 1, 1, 12, 5, 0, DateTimeKind.Utc)), default);

        var types = db.ClanEvents.Select(e => e.Type).ToList();
        Assert.Contains(ClanEventType.MemberJoined, types);
        Assert.Contains(ClanEventType.MemberLeft, types);
        Assert.Contains(ClanEventType.DonationsChanged, types);

        var left = await db.ClanMemberships.Include(m => m.Player).SingleAsync(m => m.Player!.Tag == "#QQQ2");
        Assert.False(left.IsCurrent);
        Assert.NotNull(left.LeftAt);
        Assert.NotNull((await db.ClanMemberships.Include(m => m.Player).SingleAsync(m => m.Player!.Tag == "#PPP2")).LastActivityAt);
    }

    [Fact]
    public async Task ReturningMember_BecomesCurrentAgain()
    {
        using var db = TestDb.Create();
        var clan = Ctx.NewClan(db);
        var fake = new FakeConnector { Clan = Snap(M("#PPP2", "A"), M("#QQQ2", "B")) };
        var handler = new MembersHandler();
        await handler.HandleAsync(Ctx.Make(db, clan, fake), default);
        fake.Clan = Snap(M("#PPP2", "A"));
        await handler.HandleAsync(Ctx.Make(db, clan, fake), default);
        fake.Clan = Snap(M("#PPP2", "A"), M("#QQQ2", "B"));
        await handler.HandleAsync(Ctx.Make(db, clan, fake), default);

        var b = await db.ClanMemberships.Include(m => m.Player).SingleAsync(m => m.Player!.Tag == "#QQQ2");
        Assert.True(b.IsCurrent);
        Assert.Null(b.LeftAt);
        // a primeira sincronização é só linha de base; o retorno de #QQQ2 gera o único MemberJoined\n        Assert.Equal(1, db.ClanEvents.Count(e => e.Type == ClanEventType.MemberJoined));
    }

    [Fact]
    public async Task DonationReset_ClosesSeasonStats_ForClashOfClans()
    {
        using var db = TestDb.Create();
        var clan = Ctx.NewClan(db);
        var fake = new FakeConnector { Clan = Snap(M("#PPP2", "A", donations: 700)) };
        var handler = new MembersHandler();
        await handler.HandleAsync(Ctx.Make(db, clan, fake), default);

        fake.Clan = Snap(M("#PPP2", "A", donations: 0));
        await handler.HandleAsync(Ctx.Make(db, clan, fake, new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc)), default);

        var stats = await db.SeasonStats.SingleAsync();
        Assert.Equal("2026-01", stats.Season);
        Assert.Equal(700, stats.Donations);
        Assert.Empty(db.ClanEvents.Where(e => e.Type == ClanEventType.DonationsChanged));
    }
}

public class CurrentWarHandlerTests
{
    private static WarSnapshot War(WarState state, int stars, params WarAttackSnapshot[] attacks) => new(
        state, 15, 2, "#RRR2", "Rivais", new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 2, 10, 0, 0, DateTimeKind.Utc),
        stars, 50, 8, 40, [new WarMemberSnapshot("#PPP2", "A", 1, 16, attacks.Length), new WarMemberSnapshot("#QQQ2", "B", 2, 14, 0)], attacks);

    [Fact]
    public async Task TracksWarLifecycle_AndEmitsEvents()
    {
        using var db = TestDb.Create();
        var clan = Ctx.NewClan(db);
        var fake = new FakeConnector();
        var handler = new CurrentWarHandler();

        fake.War = War(WarState.InWar, 0);
        var delay = await handler.HandleAsync(Ctx.Make(db, clan, fake), default);
        Assert.Equal(TimeSpan.FromMinutes(2), delay);
        Assert.Single(db.ClanEvents.Where(e => e.Type == ClanEventType.WarStarted));

        fake.War = War(WarState.InWar, 3, new WarAttackSnapshot("#PPP2", "#RRR2", 3, 100, 1, 90));
        await handler.HandleAsync(Ctx.Make(db, clan, fake), default);
        Assert.Single(db.ClanEvents.Where(e => e.Type == ClanEventType.WarAttackMade));

        // repetir a mesma resposta não duplica o ataque
        await handler.HandleAsync(Ctx.Make(db, clan, fake), default);
        Assert.Single(db.WarAttacks);
        Assert.Single(db.Wars);

        fake.War = War(WarState.WarEnded, 12, new WarAttackSnapshot("#PPP2", "#RRR2", 3, 100, 1, 90));
        await handler.HandleAsync(Ctx.Make(db, clan, fake), default);
        var war = await db.Wars.SingleAsync();
        Assert.Equal(WarState.WarEnded, war.State);
        Assert.Equal("win", war.Result);
        Assert.Single(db.ClanEvents.Where(e => e.Type == ClanEventType.WarEnded));
    }

    [Fact]
    public async Task PrivateWarLog_MarksClanAndBacksOff()
    {
        using var db = TestDb.Create();
        var clan = Ctx.NewClan(db);
        var handler = new CurrentWarHandler();
        var throwing = new ThrowingConnector();

        var delay = await handler.HandleAsync(Ctx.Make(db, clan, throwing), default);

        Assert.Equal(TrackedClanStatus.PrivateWarLog, (await db.TrackedClans.IgnoreQueryFilters().SingleAsync()).Status);
        Assert.Equal(TimeSpan.FromMinutes(60), delay);
    }

    [Theory]
    [InlineData(5, 3, 10, 5, "win")]
    [InlineData(3, 5, 80, 10, "lose")]
    [InlineData(4, 4, 70, 60, "win")]
    [InlineData(4, 4, 50, 60, "lose")]
    [InlineData(4, 4, 60, 60, "tie")]
    public void ResultOf(int stars, int oppStars, double destr, double oppDestr, string expected)
    {
        var s = new WarSnapshot(WarState.WarEnded, 5, 2, "#X", "X", null, null, stars, destr, oppStars, oppDestr, [], []);
        Assert.Equal(expected, CurrentWarHandler.ResultOf(s));
    }

    private sealed class ThrowingConnector : IGameConnector
    {
        public Game Game => Game.ClashOfClans;
        public GameFeatures Features { get; } = new(true, true, true, false, false, false);
        public Task<ClanSnapshot> GetClanAsync(string t, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ClanMemberSnapshot>> GetMembersAsync(string t, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PlayerSnapshot> GetPlayerAsync(string t, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WarSnapshot?> GetCurrentWarAsync(string clanTag, CancellationToken ct = default) =>
            throw new SupercellException(SupercellErrorKind.PrivateWarLog, "privado");
    }
}

public class RiverRaceHandlerTests
{
    private static RiverRaceSnapshot Race(int section, int decksToday) => new(0, section, 500, 1,
        [new RiverRaceParticipantSnapshot("#P22", "Rei", 300, 8, decksToday)]);

    [Fact]
    public async Task TracksDecks_AndClosesWeekOnSectionChange()
    {
        using var db = TestDb.Create();
        var clan = Ctx.NewClan(db, Game.ClashRoyale);
        var fake = new FakeConnector(Game.ClashRoyale);
        var handler = new RiverRaceHandler();

        fake.Race = Race(1, 0);
        await handler.HandleAsync(Ctx.Make(db, clan, fake), default);
        fake.Race = Race(1, 3);
        await handler.HandleAsync(Ctx.Make(db, clan, fake), default);
        Assert.Single(db.ClanEvents.Where(e => e.Type == ClanEventType.RiverRaceDecksUsed));

        fake.Race = Race(2, 0);
        await handler.HandleAsync(Ctx.Make(db, clan, fake), default);

        Assert.Single(db.ClanEvents.Where(e => e.Type == ClanEventType.RiverRaceEnded));
        Assert.Equal(2, db.RiverRaces.Count());
        Assert.Single(db.RiverRaces.Where(r => r.IsCurrent && r.SectionIndex == 2));
    }

    [Theory]
    [InlineData(DayOfWeek.Thursday, 12, true)]
    [InlineData(DayOfWeek.Sunday, 23, true)]
    [InlineData(DayOfWeek.Monday, 8, true)]
    [InlineData(DayOfWeek.Monday, 15, false)]
    [InlineData(DayOfWeek.Wednesday, 12, false)]
    public void IsBattleDay(DayOfWeek day, int hour, bool expected)
    {
        var date = new DateTime(2026, 1, 5, hour, 0, 0, DateTimeKind.Utc); // segunda
        date = date.AddDays(((int)day - (int)date.DayOfWeek + 7) % 7);
        Assert.Equal(expected, RiverRaceHandler.IsBattleDay(date));
    }
}