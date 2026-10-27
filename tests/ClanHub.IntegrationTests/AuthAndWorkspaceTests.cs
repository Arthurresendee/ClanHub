using System.Net;
using System.Net.Http.Json;
using ClanHub.Contracts;
using ClanHub.IntegrationTests.Infrastructure;

namespace ClanHub.IntegrationTests;

[Collection("api")]
public class AuthAndWorkspaceTests(ApiFactory factory) : ApiTestBase(factory)
{
    [DockerFact]
    public async Task Health_IsHealthy_WithDatabase()
    {
        var r = await factory.NewClient().GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [DockerFact]
    public async Task Me_RequiresLogin() =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.NewClient().GetAsync("/api/v1/me")).StatusCode);

    [DockerFact]
    public async Task Signup_Confirm_Login_Flow()
    {
        var email = $"flow-{Guid.NewGuid():N}@test.local";
        var client = factory.NewClient();

        Assert.Equal(HttpStatusCode.Accepted,
            (await client.PostAsJsonAsync("/api/auth/signup", new RegisterRequest(email, TestClient.Password, "Ana", true))).StatusCode);

        // e-mail não confirmado: login recusado
        var early = await client.PostAsJsonAsync("/api/auth/login?useCookies=true", new { email, password = TestClient.Password });
        Assert.Equal(HttpStatusCode.Unauthorized, early.StatusCode);

        var link = factory.Emails.Confirmations.Last(c => c.Email == email).Link;
        Assert.StartsWith("https://test.local/confirmar-email?userId=", link);

        var logged = await factory.RegisterAndLogin();
        var me = await (await logged.GetAsync("/api/v1/me")).ReadAs<MeResponse>();
        Assert.Equal("Fulano de Tal", me.DisplayName);
        Assert.Empty(me.Workspaces);
    }

    [DockerFact]
    public async Task Signup_RejectsMissingTerms_AndWeakPassword()
    {
        var client = factory.NewClient();
        var noTerms = await client.PostAsJsonAsync("/api/auth/signup", new RegisterRequest("a@test.local", TestClient.Password, "Ana", false));
        Assert.Equal(HttpStatusCode.BadRequest, noTerms.StatusCode);
        var weak = await client.PostAsJsonAsync("/api/auth/signup", new RegisterRequest($"w{Guid.NewGuid():N}@test.local", "123", "Ana", true));
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
    }

    [DockerFact]
    public async Task Lockout_AfterFiveWrongPasswords()
    {
        var email = $"lock-{Guid.NewGuid():N}@test.local";
        await factory.RegisterAndLogin(email);
        var client = factory.NewClient();
        for (var i = 0; i < 5; i++)
            await client.PostAsJsonAsync("/api/auth/login", new { email, password = "errada" });
        var r = await client.PostAsJsonAsync("/api/auth/login", new { email, password = TestClient.Password });
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        Assert.Contains("LockedOut", await r.Content.ReadAsStringAsync());
    }

    [DockerFact]
    public async Task Workspace_Roles_FollowPermissionTable()
    {
        var owner = await factory.RegisterAndLogin();
        var ws = await owner.CreateWorkspace();

        // convite como Viewer
        var invite = await (await owner.PostAsJsonAsync($"/api/v1/workspaces/{ws.Id}/invites", new CreateInviteRequest(null, "Viewer"))).ReadAs<CreateInviteResponse>();
        var viewer = await factory.RegisterAndLogin();
        var outsider = await factory.RegisterAndLogin();
        Assert.Equal(HttpStatusCode.Gone, (await outsider.PostAsync("/api/v1/invites/token-invalido/accept", null)).StatusCode);
        var accepted = await (await viewer.PostAsync($"/api/v1/invites/{invite.Token}/accept", null)).ReadAs<AcceptInviteResponse>();
        Assert.Equal(ws.Id, accepted.WorkspaceId);

        // convite é de uso único
        Assert.Equal(HttpStatusCode.Gone, (await outsider.PostAsync($"/api/v1/invites/{invite.Token}/accept", null)).StatusCode);

        // Viewer vê, mas não gerencia
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync($"/api/v1/workspaces/{ws.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync($"/api/v1/workspaces/{ws.Id}/members")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync($"/api/v1/workspaces/{ws.Id}/invites", new CreateInviteRequest(null, "Staff"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync($"/api/v1/workspaces/{ws.Id}/claims", new StartClaimRequest("ClashOfClans", "#2PP", "DescriptionCode"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.DeleteAsync($"/api/v1/workspaces/{ws.Id}")).StatusCode);

        // quem não é membro não enxerga nada
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync($"/api/v1/workspaces/{ws.Id}")).StatusCode);

        // promover a Admin libera convites, mas não excluir o workspace
        var members = await (await owner.GetAsync($"/api/v1/workspaces/{ws.Id}/members")).ReadAs<List<TeamMemberDto>>();
        var viewerId = members.Single(m => m.Role == "Viewer").UserId;
        Assert.Equal(HttpStatusCode.NoContent,
            (await owner.PatchAsJsonAsync($"/api/v1/workspaces/{ws.Id}/members/{viewerId}", new UpdateMemberRoleRequest("Admin"))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await viewer.PostAsJsonAsync($"/api/v1/workspaces/{ws.Id}/invites", new CreateInviteRequest(null, "Staff"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.DeleteAsync($"/api/v1/workspaces/{ws.Id}")).StatusCode);

        // o dono não pode ser rebaixado nem removido
        var ownerId = members.Single(m => m.Role == "Owner").UserId;
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.DeleteAsync($"/api/v1/workspaces/{ws.Id}/members/{ownerId}")).StatusCode);

        // só o Owner exclui (exclusão lógica)
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/v1/workspaces/{ws.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync($"/api/v1/workspaces/{ws.Id}")).StatusCode);
    }

    [DockerFact]
    public async Task DeleteAccount_RemovesUser_AndSoleOwnedWorkspaces()
    {
        var email = $"del-{Guid.NewGuid():N}@test.local";
        var client = await factory.RegisterAndLogin(email);
        await client.CreateWorkspace();

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/v1/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/me")).StatusCode);

        var again = await factory.NewClient().PostAsJsonAsync("/api/auth/login", new { email, password = TestClient.Password });
        Assert.Equal(HttpStatusCode.Unauthorized, again.StatusCode);
    }

    [DockerFact]
    public async Task Games_ListIsPublic_AndPreviewNeedsLogin()
    {
        var anon = factory.NewClient();
        var games = await (await anon.GetAsync("/api/v1/games")).ReadAs<List<GameInfoDto>>();
        Assert.NotNull(games);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/v1/games/ClashOfClans/clans/%232PP/preview")).StatusCode);

        var user = await factory.RegisterAndLogin();
        var preview = await (await user.GetAsync("/api/v1/games/ClashOfClans/clans/%232PP/preview")).ReadAs<ClanPreviewDto>();
        Assert.Equal("#2PP", preview.Tag);
        Assert.Equal(HttpStatusCode.BadRequest, (await user.GetAsync("/api/v1/games/ClashOfClans/clans/INVALIDA/preview")).StatusCode);
    }

    [DockerFact]
    public async Task InternalEndpoints_RequireCronSecret()
    {
        var c = factory.NewClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.SendAsync(TestClient.Cron(HttpMethod.Get, "/internal/sync/tick", null))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.SendAsync(TestClient.Cron(HttpMethod.Post, "/internal/sync/tick", "errado"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.SendAsync(TestClient.Cron(HttpMethod.Get, "/internal/sync/tick"))).StatusCode);   // Vercel Cron usa GET
        Assert.Equal(HttpStatusCode.OK, (await c.SendAsync(TestClient.Cron(HttpMethod.Post, "/internal/maintenance/daily"))).StatusCode);
    }
}