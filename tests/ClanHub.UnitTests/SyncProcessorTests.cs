using ClanHub.Application.Games;
using ClanHub.Domain;
using ClanHub.Infrastructure.Persistence;
using ClanHub.Sync;
using ClanHub.Sync.Handlers;
using ClanHub.UnitTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ClanHub.UnitTests;

public class SyncProcessorTests
{
    private sealed class FixedClock(DateTime utc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utc, TimeSpan.Zero);
    }

    private sealed class ScriptedHandler(SyncKind kind, Func<Task<TimeSpan>> script) : ISyncHandler
    {
        public SyncKind Kind => kind;
        public Task<TimeSpan> HandleAsync(SyncContext ctx, CancellationToken ct) => script();
    }

    private static readonly DateTime Now = new(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc);

    private sealed class Harness : IDisposable
    {
        private readonly ServiceProvider _sp;
        public SyncProcessor Processor { get; }
        public string DbName { get; } = Guid.NewGuid().ToString();

        public Harness(SyncKind kind, Func<Task<TimeSpan>> script)
        {
            var services = new ServiceCollection();
            services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(DbName));
            services.AddSingleton<IGameConnectorFactory>(new Factory());
            services.AddScoped<ISyncHandler>(_ => new ScriptedHandler(kind, script));
            _sp = services.BuildServiceProvider();
            Processor = new SyncProcessor(_sp.GetRequiredService<IServiceScopeFactory>(), Options.Create(new SyncOptions()),
                new FixedClock(Now), NullLogger<SyncProcessor>.Instance);
        }

        public AppDbContext NewDb() => new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(DbName).Options);
        public void Dispose() => _sp.Dispose();
    }

    private sealed class Factory : IGameConnectorFactory
    {
        private readonly FakeConnector _c = new();
        public IEnumerable<IGameConnector> All => [_c];
        public IGameConnector Get(Game game) => _c;
    }

    private static (Guid TargetId, Guid ClanId) Seed(Harness h, SyncKind kind, DateTime? lastAccess = null, int failCount = 0)
    {
        using var db = h.NewDb();
        var clan = new TrackedClan { WorkspaceId = Guid.NewGuid(), Game = Game.ClashOfClans, Tag = "#2PP", Name = "Clã", LastAccessedAt = lastAccess ?? Now, CreatedAt = Now.AddDays(-100) };
        var target = new SyncTarget { TrackedClanId = clan.Id, Kind = kind, NextRunAt = Now.AddMinutes(-1), LeaseUntil = Now.AddMinutes(2), LeaseOwner = "tick", FailCount = failCount };
        db.TrackedClans.Add(clan);
        db.SyncTargets.Add(target);
        db.SaveChanges();
        return (target.Id, clan.Id);
    }

    [Fact]
    public async Task Success_Reschedules_ClearsLease_AndResetsFailures()
    {
        using var h = new Harness(SyncKind.Members, () => Task.FromResult(TimeSpan.FromMinutes(5)));
        var (id, _) = Seed(h, SyncKind.Members, failCount: 3);

        Assert.True(await h.Processor.ProcessAsync(id, default));

        using var db = h.NewDb();
        var t = await db.SyncTargets.SingleAsync();
        Assert.Equal(Now.AddMinutes(5), t.NextRunAt);
        Assert.Equal(0, t.FailCount);
        Assert.Null(t.LastError);
        Assert.Null(t.LeaseUntil);
        Assert.Null(t.LeaseOwner);
        Assert.Equal(Now, t.LastSuccessAt);
    }

    [Fact]
    public async Task ClanNotAccessedForMonths_IsSyncedSixTimesLessOften()
    {
        using var h = new Harness(SyncKind.Members, () => Task.FromResult(TimeSpan.FromMinutes(5)));
        var (id, _) = Seed(h, SyncKind.Members, lastAccess: Now.AddDays(-45));
        await h.Processor.ProcessAsync(id, default);
        using var db = h.NewDb();
        Assert.Equal(Now.AddMinutes(30), (await db.SyncTargets.SingleAsync()).NextRunAt);
    }

    [Theory]
    [InlineData(SyncKind.Members)]
    [InlineData(SyncKind.CurrentWar)]
    [InlineData(SyncKind.WarLog)]
    [InlineData(SyncKind.RiverRace)]
    [InlineData(SyncKind.RiverRaceLog)]
    public async Task NotFound_OnCoreClanRoutes_MarksTheClanAsNotFound(SyncKind kind)
    {
        using var h = new Harness(kind, () => throw new SupercellException(SupercellErrorKind.NotFound, "404"));
        var (id, _) = Seed(h, kind);

        Assert.False(await h.Processor.ProcessAsync(id, default));

        using var db = h.NewDb();
        Assert.Equal(TrackedClanStatus.NotFound, (await db.TrackedClans.SingleAsync()).Status);
        var t = await db.SyncTargets.SingleAsync();
        Assert.Equal(1, t.FailCount);
        Assert.Equal(Now.AddMinutes(1), t.NextRunAt);
    }

    [Theory]
    [InlineData(SyncKind.CapitalRaids)]
    [InlineData(SyncKind.Rankings)]
    [InlineData(SyncKind.PlayerProfiles)]
    [InlineData(SyncKind.CwlGroup)]
    public async Task NotFound_OnOptionalRoutes_NeverMarksTheWholeClan(SyncKind kind)
    {
        using var h = new Harness(kind, () => throw new SupercellException(SupercellErrorKind.NotFound, "404"));
        var (id, _) = Seed(h, kind);

        await h.Processor.ProcessAsync(id, default);

        using var db = h.NewDb();
        Assert.Equal(TrackedClanStatus.Active, (await db.TrackedClans.SingleAsync()).Status);
        Assert.Equal(1, (await db.SyncTargets.SingleAsync()).FailCount);   // continua com backoff
    }

    [Fact]
    public async Task Throttled_HonorsRetryAfter_WithoutCountingAsFailure()
    {
        using var h = new Harness(SyncKind.Members, () => throw new SupercellException(SupercellErrorKind.Throttled, "429", TimeSpan.FromSeconds(30)));
        var (id, _) = Seed(h, SyncKind.Members);
        await h.Processor.ProcessAsync(id, default);
        using var db = h.NewDb();
        var t = await db.SyncTargets.SingleAsync();
        Assert.Equal(Now.AddSeconds(30), t.NextRunAt);
        Assert.Equal(0, t.FailCount);
        Assert.Null(t.LeaseUntil);
    }

    [Fact]
    public async Task Throttled_WithoutRetryAfter_WaitsOneMinute()
    {
        using var h = new Harness(SyncKind.Members, () => throw new SupercellException(SupercellErrorKind.Throttled, "429"));
        var (id, _) = Seed(h, SyncKind.Members);
        await h.Processor.ProcessAsync(id, default);
        using var db = h.NewDb();
        Assert.Equal(Now.AddMinutes(1), (await db.SyncTargets.SingleAsync()).NextRunAt);
    }

    [Fact]
    public async Task PrivateWarLog_MarksClan_AndRetriesInAnHour_WithoutFailureCount()
    {
        using var h = new Harness(SyncKind.CurrentWar, () => throw new SupercellException(SupercellErrorKind.PrivateWarLog, "403"));
        var (id, _) = Seed(h, SyncKind.CurrentWar);
        await h.Processor.ProcessAsync(id, default);
        using var db = h.NewDb();
        Assert.Equal(TrackedClanStatus.PrivateWarLog, (await db.TrackedClans.SingleAsync()).Status);
        var t = await db.SyncTargets.SingleAsync();
        Assert.Equal(Now.AddMinutes(60), t.NextRunAt);
        Assert.Equal(0, t.FailCount);
    }

    [Fact]
    public async Task UnexpectedErrors_BackOffExponentially_AndKeepTheMessage()
    {
        using var h = new Harness(SyncKind.Members, () => throw new InvalidOperationException("quebrou"));
        var (id, _) = Seed(h, SyncKind.Members);

        Assert.False(await h.Processor.ProcessAsync(id, default));
        using (var db = h.NewDb())
        {
            var t = await db.SyncTargets.SingleAsync();
            Assert.Equal((1, "quebrou", Now.AddMinutes(1)), (t.FailCount, t.LastError, t.NextRunAt));
        }

        await h.Processor.ProcessAsync(id, default);
        using var db2 = h.NewDb();
        var t2 = await db2.SyncTargets.SingleAsync();
        Assert.Equal((2, Now.AddMinutes(2)), (t2.FailCount, t2.NextRunAt));
    }

    [Fact]
    public async Task SoftDeletedClan_DropsItsTarget()
    {
        using var h = new Harness(SyncKind.Members, () => Task.FromResult(TimeSpan.FromMinutes(5)));
        var (id, clanId) = Seed(h, SyncKind.Members);
        using (var db = h.NewDb())
        {
            (await db.TrackedClans.IgnoreQueryFilters().SingleAsync()).IsDeleted = true;
            await db.SaveChangesAsync();
        }

        Assert.True(await h.Processor.ProcessAsync(id, default));
        using var verify = h.NewDb();
        Assert.Empty(verify.SyncTargets);
    }

    [Fact]
    public async Task MissingTarget_IsANoOp()
    {
        using var h = new Harness(SyncKind.Members, () => Task.FromResult(TimeSpan.FromMinutes(5)));
        Assert.True(await h.Processor.ProcessAsync(Guid.NewGuid(), default));
    }
}