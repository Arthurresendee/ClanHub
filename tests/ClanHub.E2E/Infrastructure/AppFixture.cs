using System.Diagnostics;
using System.Net.Sockets;
using Microsoft.Data.SqlClient;
using Microsoft.Playwright;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace ClanHub.E2E.Infrastructure;

/// <summary>
/// Sobe o app de verdade (processo do Server, SQL Server LocalDB, SMTP falso e Supercell simulada pelo WireMock)
/// e um Chromium do Playwright. Se não houver LocalDB ou Chromium, os testes são pulados.
/// </summary>
public class AppFixture : IAsyncLifetime
{
    /// <summary>Servidor SQL dos testes: CLANHUB_TEST_SQL (ex.: CI) ou LocalDB.</summary>
    private static readonly string Master = Environment.GetEnvironmentVariable("CLANHUB_TEST_SQL") is { Length: > 0 } sql
        ? sql
        : @"Server=(localdb)\MSSQLLocalDB;Database=master;Integrated Security=true;TrustServerCertificate=true;Connect Timeout=30";
    public const string CronSecret = "e2e-cron-secret";

    private Process? _server;
    private string? _db;
    private IPlaywright? _playwright;
    public IBrowser? Browser { get; private set; }
    public WireMockServer Supercell { get; } = WireMockServer.Start();
    public SmtpSink Smtp { get; } = new();
    public string BaseUrl { get; private set; } = "";
    public string? SkipReason { get; private set; }
    public List<string> ServerLog { get; } = [];

    public static string? Skip { get; protected set; }

    /// <summary>Como iniciar o Server: por padrão, a saída de build em modo Development.</summary>
    protected virtual (string Dll, string WorkingDirectory, string Environment) Launch(string repoRoot)
    {
        var cfg = AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}") ? "Release" : "Debug";
        var serverDir = Path.Combine(repoRoot, "src", "ClanHub.Server");
        return (Path.Combine(serverDir, "bin", cfg, "net10.0", "ClanHub.Server.dll"), serverDir, "Development");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ClanHub.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("raiz do repositório não encontrada");
    }

    private static int FreePort()
    {
        var l = new TcpListener(System.Net.IPAddress.Loopback, 0);
        l.Start();
        var port = ((System.Net.IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    public async Task InitializeAsync()
    {
        try
        {
            _db = $"ClanHubE2E_{Guid.NewGuid():N}";
            await using (var c = new SqlConnection(Master))
            {
                await c.OpenAsync();
                await using var cmd = c.CreateCommand();
                cmd.CommandText = $"CREATE DATABASE [{_db}]";
                await cmd.ExecuteNonQueryAsync();
            }

            StubSupercell();

            _playwright = await Playwright.CreateAsync();
            Browser = await _playwright.Chromium.LaunchAsync(new() { Headless = true });

            var root = RepoRoot();
            var (dll, serverDir, environment) = Launch(root);
            var port = FreePort();
            BaseUrl = $"http://localhost:{port}"; // "localhost": passkeys exigem um domínio (não aceitam IP)

            var psi = new ProcessStartInfo("dotnet", $"\"{dll}\"")
            {
                WorkingDirectory = serverDir, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            };
            var env = psi.Environment;
            env["ASPNETCORE_ENVIRONMENT"] = environment;
            env["ASPNETCORE_URLS"] = BaseUrl;
            env["ConnectionStrings__Default"] = new SqlConnectionStringBuilder(Master) { InitialCatalog = _db }.ConnectionString;
            env["Database__MigrateOnStartup"] = "true";
            env["Auth__RequireConfirmedEmail"] = "true";   // os testes E2E exercitam a confirmação por e-mail
            env["CRON_SECRET"] = CronSecret;
            env["App__PublicUrl"] = BaseUrl;
            env["Email__Host"] = "127.0.0.1";
            env["Email__Port"] = Smtp.Port.ToString();
            env["RateLimit__AuthPerMinute"] = "10000";
            env["Supercell__ClashOfClans__BaseUrl"] = Supercell.Url + "/coc/v1";
            env["Supercell__ClashOfClans__ApiKey"] = "e2e";
            env["Supercell__ClashRoyale__BaseUrl"] = Supercell.Url + "/cr/v1";
            env["Supercell__BrawlStars__BaseUrl"] = Supercell.Url + "/bs/v1";
            _server = Process.Start(psi)!;
            _server.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (ServerLog) ServerLog.Add(e.Data); };
            _server.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (ServerLog) ServerLog.Add(e.Data); };
            _server.BeginOutputReadLine();
            _server.BeginErrorReadLine();

            using var http = new HttpClient();
            for (var i = 0; i < 90; i++)
            {
                if (_server.HasExited) throw new InvalidOperationException("o Server encerrou ao iniciar:\n" + string.Join("\n", ServerLog.TakeLast(20)));
                try { if ((await http.GetAsync(BaseUrl + "/health")).IsSuccessStatusCode) { Skip = null; return; } } catch { }
                await Task.Delay(1000);
            }
            throw new TimeoutException("o Server não respondeu em /health");
        }
        catch (Exception ex) when (ex is SqlException or PlaywrightException or InvalidOperationException or TimeoutException or System.ComponentModel.Win32Exception)
        {
            SkipReason = Skip = $"E2E indisponível neste ambiente: {ex.Message.Split('\n')[0]}";
        }
    }

    /// <summary>Respostas fixas da "Supercell": um clã com 2 membros.</summary>
    private void StubSupercell()
    {
        string Fx(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", name));
        Supercell.Given(Request.Create().WithPath("/coc/v1/clans/#2PP").UsingGet()).AtPriority(5)   // prioridade menor = vence (ver SetClanDescription)
            .RespondWith(Response.Create().WithHeader("Content-Type", "application/json").WithBody(Fx("coc/clan.json")));
        Supercell.Given(Request.Create().WithPath("/coc/v1/clans/#2PP/members").UsingGet())
            .RespondWith(Response.Create().WithHeader("Content-Type", "application/json").WithBody(Fx("coc/members.json")));
        Supercell.Given(Request.Create().WithPath("/coc/v1/clans/#2PP/currentwar").UsingGet())
            .RespondWith(Response.Create().WithHeader("Content-Type", "application/json").WithBody("{\"state\":\"notInWar\"}"));
        Supercell.Given(Request.Create().WithPath("/coc/v1/players/*").UsingGet())
            .RespondWith(Response.Create().WithHeader("Content-Type", "application/json").WithBody(Fx("coc/player.json")));
        Supercell.Given(Request.Create().WithPath("/coc/v1/*").UsingGet())
            .AtPriority(100).RespondWith(Response.Create().WithStatusCode(404).WithBody("{}"));
    }

    /// <summary>Atualiza a descrição do clã devolvida pela "Supercell" (para a verificação por código).</summary>
    public void SetClanDescription(string description)
    {
        var clan = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "coc/clan.json"))
            .Replace("Clã de teste CH-7F3K2", description);
        Supercell.Given(Request.Create().WithPath("/coc/v1/clans/#2PP").UsingGet()).AtPriority(1)
            .RespondWith(Response.Create().WithHeader("Content-Type", "application/json").WithBody(clan));
    }

    public async Task<IBrowserContext> NewContextAsync(BrowserNewContextOptions? options = null)
    {
        var o = options ?? new BrowserNewContextOptions();
        o.BaseURL = BaseUrl;
        o.Locale = "pt-BR";
        return await Browser!.NewContextAsync(o);
    }

    public async Task<string> RunTickAsync()
    {
        using var http = new HttpClient { BaseAddress = new Uri(BaseUrl) };
        http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Bearer {CronSecret}");
        return await (await http.PostAsync("/internal/sync/tick", null)).Content.ReadAsStringAsync();
    }

    public async Task DisposeAsync()
    {
        try { File.WriteAllLines(Path.Combine(Path.GetTempPath(), "clanhub-e2e-server.log"), ServerLog); } catch (IOException) { }
        if (Browser is not null) await Browser.DisposeAsync();
        _playwright?.Dispose();
        if (_server is { HasExited: false }) { _server.Kill(entireProcessTree: true); await _server.WaitForExitAsync(); }
        Smtp.Dispose();
        Supercell.Dispose();
        if (_db is not null)
        {
            try
            {
                SqlConnection.ClearAllPools();
                await using var c = new SqlConnection(Master);
                await c.OpenAsync();
                await using var cmd = c.CreateCommand();
                cmd.CommandText = $"ALTER DATABASE [{_db}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_db}]";
                await cmd.ExecuteNonQueryAsync();
            }
            catch (SqlException) { }
        }
    }
}

[CollectionDefinition("e2e")]
public class E2ECollection : ICollectionFixture<AppFixture>;