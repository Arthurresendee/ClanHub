using ClanHub.Application.Games;
using ClanHub.Domain;
using ClanHub.Infrastructure.Persistence;
using ClanHub.Sync;
using ClanHub.Sync.Handlers;
using Microsoft.EntityFrameworkCore;

namespace ClanHub.UnitTests.Support;

public static class TestDb
{
    public static AppDbContext Create() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}

public class FakeConnector(Game game = Game.ClashOfClans) : IGameConnector
{
    public Game Game => game;
    public GameFeatures Features { get; } = new(true, true, true, true, true, true, Rankings: true);

    public ClanSnapshot Clan { get; set; } = new("#2PP", "Clã", null, null, 0, []);
    public WarSnapshot? War { get; set; }
    public RiverRaceSnapshot? Race { get; set; }

    // Fase 2/3
    public CwlGroupSnapshot? CwlGroup { get; set; }
    public Dictionary<string, WarSnapshot> CwlWars { get; } = new();
    public List<string> CwlWarCalls { get; } = [];
    public IReadOnlyList<CapitalRaidSnapshot> Raids { get; set; } = [];
    public ClanRankingSnapshot? Ranking { get; set; }

    public Task<CwlGroupSnapshot?> GetCwlGroupAsync(string clanTag, CancellationToken ct = default) => Task.FromResult(CwlGroup);
    public Task<WarSnapshot?> GetCwlWarAsync(string warTag, string clanTag, CancellationToken ct = default)
    {
        CwlWarCalls.Add(warTag);
        return Task.FromResult(CwlWars.GetValueOrDefault(warTag));
    }
    public Task<IReadOnlyList<CapitalRaidSnapshot>> GetCapitalRaidSeasonsAsync(string clanTag, CancellationToken ct = default) => Task.FromResult(Raids);
    public Task<ClanRankingSnapshot?> GetClanRankingAsync(string clanTag, string? countryCode, CancellationToken ct = default) => Task.FromResult(Ranking);

    public Task<ClanSnapshot> GetClanAsync(string clanTag, CancellationToken ct = default) => Task.FromResult(Clan);
    public Task<IReadOnlyList<ClanMemberSnapshot>> GetMembersAsync(string clanTag, CancellationToken ct = default) => Task.FromResult(Clan.Members);
    public Task<PlayerSnapshot> GetPlayerAsync(string playerTag, CancellationToken ct = default) =>
        Task.FromResult(new PlayerSnapshot(playerTag, "P", 1, 1, null, null, 15, "{\"ok\":true}"));
    public Task<WarSnapshot?> GetCurrentWarAsync(string clanTag, CancellationToken ct = default) => Task.FromResult(War);
    public Task<RiverRaceSnapshot?> GetCurrentRiverRaceAsync(string clanTag, CancellationToken ct = default) => Task.FromResult(Race);
}

public static class Ctx
{
    public static TrackedClan NewClan(AppDbContext db, Game game = Game.ClashOfClans)
    {
        var clan = new TrackedClan { WorkspaceId = Guid.NewGuid(), Game = game, Tag = "#2PP", Name = "Clã" };
        db.TrackedClans.Add(clan);
        db.SaveChanges();
        return clan;
    }

    public static SyncContext Make(AppDbContext db, TrackedClan clan, IGameConnector c, DateTime? now = null) =>
        new(db, clan, c, now ?? new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc), new SyncOptions());
}