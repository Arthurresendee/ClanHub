using System.Diagnostics;
using ClanHub.Application.Games;
using ClanHub.Domain;
using ClanHub.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.MsSql;

namespace ClanHub.IntegrationTests.Infrastructure;

public static class DockerInfo
{
    public const string LocalDbMaster = @"Server=(localdb)\MSSQLLocalDB;Database=master;Integrated Security=true;TrustServerCertificate=true;Connect Timeout=30";

    /// <summary>Servidor SQL para os testes: variável CLANHUB_TEST_SQL, senão LocalDB; null se não houver.</summary>
    public static readonly Lazy<string?> ExternalSql = new(() =>
    {
        var env = Environment.GetEnvironmentVariable("CLANHUB_TEST_SQL");
        var candidates = string.IsNullOrWhiteSpace(env) ? new[] { LocalDbMaster } : new[] { env };
        foreach (var cs in candidates)
        {
            try
            {
                using var c = new Microsoft.Data.SqlClient.SqlConnection(cs);
                c.Open();
                return cs;
            }
            catch { }
        }
        return null;
    });

    public static bool SqlAvailable => ExternalSql.Value is not null || Available.Value;

    public static readonly Lazy<bool> Available = new(() =>
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("docker", "version --format {{.Server.Version}}")
            { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false })!;
            var output = p.StandardOutput.ReadToEnd().Trim();
            return p.WaitForExit(15000) && p.ExitCode == 0 && output.Length > 0;
        }
        catch { return false; }
    });
}

/// <summary>[Fact] que é pulado quando não há Docker (Testcontainers precisa dele).</summary>
public sealed class DockerFactAttribute : FactAttribute
{
    public DockerFactAttribute()
    {
        if (!DockerInfo.SqlAvailable) Skip = "Sem SQL Server (LocalDB, CLANHUB_TEST_SQL ou Docker): teste pulado.";
    }
}

public class FakeConnector : IGameConnector
{
    public Game Game { get; init; } = Game.ClashOfClans;
    public GameFeatures Features { get; } = new(true, true, true, true, true, true,
        BattleLog: true, Rankings: true, Tournaments: true, Calendar: true, UpcomingChests: true);
    public ClanSnapshot Clan { get; set; } = new("#2PP", "Clã Teste", "sem código", "https://img/b.png", 0, []);
    public bool TokenValid { get; set; } = true;
    public SupercellErrorKind? ClanError { get; set; }
    public SupercellErrorKind? CalendarError { get; set; }

    /// <summary>O conector é compartilhado pela coleção de testes: cada teste começa do zero.</summary>
    public void Reset()
    {
        Clan = new ClanSnapshot("#2PP", "Clã Teste", "sem código", "https://img/b.png", 0, []);
        TokenValid = true; ClanError = null; CalendarError = null;
        CwlGroup = null; CwlWars.Clear(); Raids = []; RaidsError = null; Ranking = null; Battles = []; Chests = []; Tournaments = []; Calendar = [];
        BattleCalls = 0;
    }

    // Fase 2/3
    public CwlGroupSnapshot? CwlGroup { get; set; }
    public Dictionary<string, WarSnapshot> CwlWars { get; } = new();
    public IReadOnlyList<CapitalRaidSnapshot> Raids { get; set; } = [];
    public SupercellErrorKind? RaidsError { get; set; }
    public ClanRankingSnapshot? Ranking { get; set; }
    public IReadOnlyList<BattleSnapshot> Battles { get; set; } = [];
    public int BattleCalls;
    public IReadOnlyList<ChestSnapshot> Chests { get; set; } = [];
    public IReadOnlyList<TournamentSnapshot> Tournaments { get; set; } = [];
    public IReadOnlyList<CalendarEntrySnapshot> Calendar { get; set; } = [];

    public Task<CwlGroupSnapshot?> GetCwlGroupAsync(string clanTag, CancellationToken ct = default) => Task.FromResult(CwlGroup);
    public Task<WarSnapshot?> GetCwlWarAsync(string warTag, string clanTag, CancellationToken ct = default) =>
        Task.FromResult(CwlWars.GetValueOrDefault(warTag));
    public Task<IReadOnlyList<CapitalRaidSnapshot>> GetCapitalRaidSeasonsAsync(string clanTag, CancellationToken ct = default) =>
        RaidsError is { } kind ? throw new SupercellException(kind, "simulado") : Task.FromResult(Raids);
    public Task<ClanRankingSnapshot?> GetClanRankingAsync(string clanTag, string? countryCode, CancellationToken ct = default) => Task.FromResult(Ranking);
    public Task<IReadOnlyList<BattleSnapshot>> GetBattleLogAsync(string playerTag, CancellationToken ct = default)
    {
        Interlocked.Increment(ref BattleCalls);
        return Task.FromResult(Battles);
    }
    public Task<IReadOnlyList<ChestSnapshot>> GetUpcomingChestsAsync(string playerTag, CancellationToken ct = default) => Task.FromResult(Chests);
    public Task<IReadOnlyList<TournamentSnapshot>> SearchTournamentsAsync(string name, CancellationToken ct = default) => Task.FromResult(Tournaments);
    public Task<TournamentSnapshot> GetTournamentAsync(string tag, CancellationToken ct = default) =>
        Task.FromResult(Tournaments.FirstOrDefault(t => t.Tag == Tags.Normalize(tag)) ?? throw new SupercellException(SupercellErrorKind.NotFound, "x"));
    public Task<IReadOnlyList<CalendarEntrySnapshot>> GetCalendarAsync(CancellationToken ct = default) =>
        CalendarError is { } kind ? throw new SupercellException(kind, "simulado") : Task.FromResult(Calendar);

    public Task<ClanSnapshot> GetClanAsync(string clanTag, CancellationToken ct = default) =>
        ClanError is { } kind ? throw new SupercellException(kind, "simulado") : Task.FromResult(Clan with { Tag = Tags.Normalize(clanTag) });
    public Task<IReadOnlyList<ClanMemberSnapshot>> GetMembersAsync(string clanTag, CancellationToken ct = default) => Task.FromResult(Clan.Members);
    public Task<PlayerSnapshot> GetPlayerAsync(string playerTag, CancellationToken ct = default) =>
        Task.FromResult(new PlayerSnapshot(Tags.Normalize(playerTag), "Líder", 200, 5000, Clan.Tag, "leader", 16, "{}"));
    public Task<bool> VerifyPlayerTokenAsync(string playerTag, string token, CancellationToken ct = default) => Task.FromResult(TokenValid);
    public Task<IReadOnlyList<ClanSearchResult>> SearchClansAsync(string name, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ClanSearchResult>>([new ClanSearchResult(Clan.Tag, Clan.Name, null, 2, 10)]);
    public Task<WarSnapshot?> GetCurrentWarAsync(string clanTag, CancellationToken ct = default) => Task.FromResult<WarSnapshot?>(null);
    public Task<IReadOnlyList<WarLogEntry>> GetWarLogAsync(string clanTag, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<WarLogEntry>>([]);
}

public class FakeConnectorFactory(FakeConnector connector) : IGameConnectorFactory
{
    public FakeConnector Connector { get; } = connector;
    public IEnumerable<IGameConnector> All => [Connector];
    public IGameConnector Get(Game game) => Connector;
}

public class CapturingEmailSender : IEmailSender<ApplicationUser>
{
    public List<(string Email, string Link)> Confirmations { get; } = [];
    public Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink)
    {
        lock (Confirmations) Confirmations.Add((email, System.Net.WebUtility.HtmlDecode(confirmationLink)));
        return Task.CompletedTask;
    }
    public Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink) => Task.CompletedTask;
    public Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode) => Task.CompletedTask;
}

public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string CronSecret = "test-cron-secret";

    private MsSqlContainer? _sql;
    private string? _connectionString;
    private string? _externalAdmin;
    private string? _dbName;
    public FakeConnector Connector { get; } = new();
    public CapturingEmailSender Emails { get; } = new();

    public async Task InitializeAsync()
    {
        if (!DockerInfo.SqlAvailable) return;
        if (DockerInfo.ExternalSql.Value is { } admin)
        {
            _externalAdmin = admin;
            _dbName = $"ClanHubTest_{Guid.NewGuid():N}";
            await using var c = new Microsoft.Data.SqlClient.SqlConnection(admin);
            await c.OpenAsync();
            await using var cmd = c.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE [{_dbName}]";
            await cmd.ExecuteNonQueryAsync();
            _connectionString = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(admin) { InitialCatalog = _dbName }.ConnectionString;
        }
        else
        {
            _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
            await _sql.StartAsync();
            _connectionString = _sql.GetConnectionString();
        }
        _ = Server; // sobe o host (migrations aplicadas na inicialização)
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = _connectionString,
            ["Database:MigrateOnStartup"] = "true",
            ["CRON_SECRET"] = CronSecret,
            ["App:PublicUrl"] = "https://test.local",
            ["RateLimit:AuthPerMinute"] = "10000",
            ["Sync:TickBudgetSeconds"] = "20",
        }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IGameConnectorFactory>();
            services.AddSingleton<IGameConnectorFactory>(new FakeConnectorFactory(Connector));
            // Passkeys: o autenticador virtual dos testes usa origem http://localhost.
            services.Configure<IdentityPasskeyOptions>(o => o.ValidateOrigin = _ => ValueTask.FromResult(true));
            services.RemoveAll<IEmailSender<ApplicationUser>>();
            services.AddSingleton<IEmailSender<ApplicationUser>>(Emails);
        });
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        if (_sql is not null) await _sql.DisposeAsync();
        if (_externalAdmin is not null && _dbName is not null)
        {
            Microsoft.Data.SqlClient.SqlConnection.ClearAllPools();
            await using var c = new Microsoft.Data.SqlClient.SqlConnection(_externalAdmin);
            await c.OpenAsync();
            await using var cmd = c.CreateCommand();
            cmd.CommandText = $"ALTER DATABASE [{_dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_dbName}]";
            await cmd.ExecuteNonQueryAsync();
        }
    }
}

/// <summary>Base dos testes de API: zera o conector falso antes de cada teste.</summary>
public abstract class ApiTestBase
{
    protected ApiTestBase(ApiFactory factory) => factory.Connector.Reset();
}

[CollectionDefinition("api")]
public class ApiCollection : ICollectionFixture<ApiFactory>;