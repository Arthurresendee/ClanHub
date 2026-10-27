using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClanHub.Contracts;
using ClanHub.Domain;
using Microsoft.AspNetCore.WebUtilities;
using System.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClanHub.IntegrationTests.Infrastructure;

public static class TestClient
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public const string Password = "Str0ng!Passw0rd";

    public static HttpClient NewClient(this ApiFactory f) =>
        f.CreateClient(new() { HandleCookies = true, AllowAutoRedirect = false });

    public static async Task<HttpClient> RegisterAndLogin(this ApiFactory f, string? email = null)
    {
        email ??= $"user-{Guid.NewGuid():N}@test.local";
        var client = f.NewClient();

        var signup = await client.PostAsJsonAsync("/api/auth/signup", new RegisterRequest(email, Password, "Fulano de Tal", true));
        Assert.Equal(HttpStatusCode.Accepted, signup.StatusCode);

        var link = f.Emails.Confirmations.Last(c => c.Email == email).Link;
        var q = HttpUtility.ParseQueryString(new Uri(link).Query);
        var confirm = await client.GetAsync($"/api/auth/confirmEmail?userId={q["userId"]}&code={Uri.EscapeDataString(q["code"]!)}");
        Assert.True(confirm.IsSuccessStatusCode, await confirm.Content.ReadAsStringAsync());

        var login = await client.PostAsJsonAsync("/api/auth/login?useCookies=true", new { email, password = Password });
        Assert.True(login.IsSuccessStatusCode, await login.Content.ReadAsStringAsync());
        return client;
    }

    public static async Task<T> ReadAs<T>(this HttpResponseMessage r)
    {
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return (await r.Content.ReadFromJsonAsync<T>(Json))!;
    }

    public static async Task<WorkspaceSummaryDto> CreateWorkspace(this HttpClient c, string name = "Família Teste") =>
        await (await c.PostAsJsonAsync("/api/v1/workspaces", new CreateWorkspaceRequest(name))).ReadAs<WorkspaceSummaryDto>();

    /// <summary>Cria workspace e adiciona o clã #2PP pelo método do código na descrição.</summary>
    public static async Task<(WorkspaceSummaryDto Ws, Guid ClanId)> CreateWorkspaceWithClan(this ApiFactory f, HttpClient c, string tag = "#2PP")
    {
        var ws = await c.CreateWorkspace();
        var start = await (await c.PostAsJsonAsync($"/api/v1/workspaces/{ws.Id}/claims",
            new StartClaimRequest("ClashOfClans", tag, "DescriptionCode"))).ReadAs<StartClaimResponse>();
        f.Connector.Clan = f.Connector.Clan with { Description = $"Bem-vindos {start.Code}" };
        var verify = await (await c.PostAsJsonAsync($"/api/v1/claims/{start.ClaimId}/verify", new VerifyClaimRequest(null, null))).ReadAs<VerifyClaimResponse>();
        Assert.True(verify.Verified, verify.Message);
        return (ws, verify.ClanId!.Value);
    }

    public static async Task ForceDue(this ApiFactory f, Guid clanId, params ClanHub.Domain.SyncKind[] kinds)
    {
        await using var scope = f.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClanHub.Infrastructure.Persistence.AppDbContext>();
        await db.SyncTargets.Where(t => t.TrackedClanId == clanId && (kinds.Length == 0 || kinds.Contains(t.Kind)))
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.NextRunAt, DateTime.UtcNow.AddSeconds(-1))
                .SetProperty(t => t.LeaseUntil, (DateTime?)null).SetProperty(t => t.LastRunAt, (DateTime?)null));
    }

    public static async Task<ClanHub.Sync.TickResult> Tick(this ApiFactory f)
    {
        var res = await f.NewClient().SendAsync(Cron(HttpMethod.Post, "/internal/sync/tick"));
        return await res.ReadAs<ClanHub.Sync.TickResult>();
    }

    public static HttpRequestMessage Cron(HttpMethod method, string url, string? secret = ApiFactory.CronSecret)
    {
        var req = new HttpRequestMessage(method, url);
        if (secret is not null) req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {secret}");
        return req;
    }
}