using System.Net;
using System.Net.Http.Json;
using ClanHub.Application.Games;
using ClanHub.Contracts;
using ClanHub.Domain;
using ClanHub.Infrastructure.Persistence;
using ClanHub.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClanHub.IntegrationTests;

[Collection("api")]
public class RecruitmentAndSettingsTests(ApiFactory factory) : ApiTestBase(factory)
{
    private void ResetClan() => factory.Connector.Clan = new ClanSnapshot("#2PP", "Clã Teste", "sem código", "https://img/b.png", 0, []);

    [DockerFact]
    public async Task Settings_Validation_AndSlugRules()
    {
        ResetClan();
        var owner = await factory.RegisterAndLogin();
        var (ws, clanId) = await factory.CreateWorkspaceWithClan(owner);
        var url = $"/api/v1/clans/{clanId}";

        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PatchAsJsonAsync(url, new UpdateClanRequest(IsPublic: true))).StatusCode);      // sem endereço
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PatchAsJsonAsync(url, new UpdateClanRequest(PublicSlug: "Slug Ruim!"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PatchAsJsonAsync(url, new UpdateClanRequest(StrikeExpiryDays: 0))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PatchAsJsonAsync(url, new UpdateClanRequest(StrikesToWarn: 99))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PatchAsJsonAsync(url, new UpdateClanRequest(CountryCode: "../etc"))).StatusCode);

        var slug = $"cla-{Guid.NewGuid():N}"[..20];
        var ok = await (await owner.PatchAsJsonAsync(url, new UpdateClanRequest("BR", 15, 4, true, slug, "Venha jogar!"))).ReadAs<ClanSettingsDto>();
        Assert.Equal(("br", 15, 4, true, slug, "Venha jogar!"), (ok.CountryCode, ok.StrikeExpiryDays, ok.StrikesToWarn, ok.IsPublic, ok.PublicSlug, ok.RecruitmentText));
        var read = await (await owner.GetAsync($"{url}/settings")).ReadAs<ClanSettingsDto>();
        Assert.Equal(ok, read);

        // o mesmo endereço não pode ser usado por outro clã
        var other = await factory.RegisterAndLogin();
        var (_, otherClan) = await factory.CreateWorkspaceWithClan(other);
        Assert.Equal(HttpStatusCode.Conflict, (await other.PatchAsJsonAsync($"/api/v1/clans/{otherClan}", new UpdateClanRequest(PublicSlug: slug))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.PatchAsJsonAsync(url, new UpdateClanRequest(StrikesToWarn: 2))).StatusCode);   // clã de outro workspace
    }

    [DockerFact]
    public async Task OnlyAdmins_CanChangeSettings()
    {
        ResetClan();
        var owner = await factory.RegisterAndLogin();
        var (ws, clanId) = await factory.CreateWorkspaceWithClan(owner);
        var invite = await (await owner.PostAsJsonAsync($"/api/v1/workspaces/{ws.Id}/invites", new CreateInviteRequest(null, "Staff"))).ReadAs<CreateInviteResponse>();
        var staff = await factory.RegisterAndLogin();
        await staff.PostAsync($"/api/v1/invites/{invite.Token}/accept", null);

        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync($"/api/v1/clans/{clanId}/settings")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PatchAsJsonAsync($"/api/v1/clans/{clanId}", new UpdateClanRequest(StrikesToWarn: 2))).StatusCode);
    }

    [DockerFact]
    public async Task PublicPage_And_Applications_FullFlow()
    {
        ResetClan();
        var owner = await factory.RegisterAndLogin();
        var (ws, clanId) = await factory.CreateWorkspaceWithClan(owner);
        var slug = $"pub-{Guid.NewGuid():N}"[..18];
        var anon = factory.NewClient();

        // desligada: não existe publicamente
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync($"/api/v1/public/clans/{slug}")).StatusCode);
        await owner.PatchAsJsonAsync($"/api/v1/clans/{clanId}", new UpdateClanRequest(PublicSlug: slug, RecruitmentText: "Venha!", IsPublic: false));
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync($"/api/v1/public/clans/{slug}")).StatusCode);   // ainda desligada
        Assert.Equal(HttpStatusCode.NotFound, (await anon.PostAsJsonAsync($"/api/v1/public/clans/{slug}/apply", new ApplyRequest("#PPP2", null, null))).StatusCode);

        await owner.PatchAsJsonAsync($"/api/v1/clans/{clanId}", new UpdateClanRequest(IsPublic: true));
        var page = await (await anon.GetAsync($"/api/v1/public/clans/{slug}")).ReadAs<PublicClanDto>();
        Assert.Equal(("Clã Teste", "Venha!", "ClashOfClans"), (page.Name, page.RecruitmentText, page.Game));

        // candidatura
        Assert.Equal(HttpStatusCode.BadRequest, (await anon.PostAsJsonAsync($"/api/v1/public/clans/{slug}/apply", new ApplyRequest("INVALIDA", null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await anon.PostAsJsonAsync($"/api/v1/public/clans/{slug}/apply", new ApplyRequest("#PPP2", new string('x', 1001), null))).StatusCode);
        var apply = await anon.PostAsJsonAsync($"/api/v1/public/clans/{slug}/apply", new ApplyRequest("ppp2", "Quero entrar", "discord: fulano"));
        Assert.Equal(HttpStatusCode.Accepted, apply.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await anon.PostAsJsonAsync($"/api/v1/public/clans/{slug}/apply", new ApplyRequest("#PPP2", null, null))).StatusCode);

        // equipe vê, avalia e é notificada
        var apps = await (await owner.GetAsync($"/api/v1/clans/{clanId}/applications?status=Pending")).ReadAs<List<ApplicationDto>>();
        var a = Assert.Single(apps);
        Assert.Equal(("#PPP2", "Líder", "discord: fulano"), (a.PlayerTag, a.PlayerName, a.Contact));
        Assert.Contains("expLevel", a.PlayerSnapshotJson);
        Assert.True((await (await owner.GetAsync("/api/v1/me/notifications/unread-count")).ReadAs<UnreadCountDto>()).Count >= 1);

        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PatchAsJsonAsync($"/api/v1/applications/{a.Id}", new UpdateApplicationRequest("Qualquer", null))).StatusCode);
        var reviewed = await (await owner.PatchAsJsonAsync($"/api/v1/applications/{a.Id}", new UpdateApplicationRequest("Approved", "Bem-vindo!"))).ReadAs<ApplicationDto>();
        Assert.Equal(("Approved", "Bem-vindo!"), (reviewed.Status, reviewed.ReviewComment));
        Assert.NotNull(reviewed.ReviewedAt);
        Assert.Empty(await (await owner.GetAsync($"/api/v1/clans/{clanId}/applications?status=Pending")).ReadAs<List<ApplicationDto>>());

        // depois de decidida, o jogador pode se candidatar de novo
        Assert.Equal(HttpStatusCode.Accepted, (await anon.PostAsJsonAsync($"/api/v1/public/clans/{slug}/apply", new ApplyRequest("#PPP2", null, null))).StatusCode);
    }

    [DockerFact]
    public async Task Applications_RequireStaff_AndBelongToTheClansWorkspace()
    {
        ResetClan();
        var owner = await factory.RegisterAndLogin();
        var (ws, clanId) = await factory.CreateWorkspaceWithClan(owner);
        var slug = $"app-{Guid.NewGuid():N}"[..18];
        await owner.PatchAsJsonAsync($"/api/v1/clans/{clanId}", new UpdateClanRequest(PublicSlug: slug, IsPublic: true));
        await factory.NewClient().PostAsJsonAsync($"/api/v1/public/clans/{slug}/apply", new ApplyRequest("#PPP2", null, null));
        var appId = (await (await owner.GetAsync($"/api/v1/clans/{clanId}/applications")).ReadAs<List<ApplicationDto>>()).Single().Id;

        var invite = await (await owner.PostAsJsonAsync($"/api/v1/workspaces/{ws.Id}/invites", new CreateInviteRequest(null, "Viewer"))).ReadAs<CreateInviteResponse>();
        var viewer = await factory.RegisterAndLogin();
        await viewer.PostAsync($"/api/v1/invites/{invite.Token}/accept", null);
        var stranger = await factory.RegisterAndLogin();

        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync($"/api/v1/clans/{clanId}/applications")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PatchAsJsonAsync($"/api/v1/applications/{appId}", new UpdateApplicationRequest("Approved", null))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.PatchAsJsonAsync($"/api/v1/applications/{appId}", new UpdateApplicationRequest("Approved", null))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.NewClient().GetAsync($"/api/v1/clans/{clanId}/applications")).StatusCode);
    }
}

[Collection("api")]
public class AlertsApiTests(ApiFactory factory) : ApiTestBase(factory)
{
    private static ClanMemberSnapshot M(string tag, string name) => new(tag, name, "member", 100, 4000, 0, 0, 15, 1);

    private void SetClan(params ClanMemberSnapshot[] members) =>
        factory.Connector.Clan = new ClanSnapshot("#2PP", "Clã Teste", "sem código", null, members.Length, members);

    [DockerFact]
    public async Task Create_Validates_AndNeverEchoesTheWebhook()
    {
        SetClan();
        var owner = await factory.RegisterAndLogin();
        var (_, clanId) = await factory.CreateWorkspaceWithClan(owner);
        var url = $"/api/v1/clans/{clanId}/alerts";

        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(url, new CreateAlertRequest("Inexistente", "Panel", null, null, null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(url, new CreateAlertRequest("MemberLeft", "Pombo", null, null, null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(url, new CreateAlertRequest("RiverRaceDecksPending", "Panel", null, null, null, null))).StatusCode); // só CR
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(url, new CreateAlertRequest("MemberInactive", "Panel", 0, null, null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(url, new CreateAlertRequest("MemberLeft", "DiscordWebhook", null, null, null, "https://evil.example.com/api/webhooks/1/a"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(url, new CreateAlertRequest("MemberLeft", "DiscordWebhook", null, null, null, null))).StatusCode);

        var secret = "https://discord.com/api/webhooks/123456/SUPERSECRETTOKEN";
        var res = await owner.PostAsJsonAsync(url, new CreateAlertRequest("MemberLeft", "DiscordWebhook", null, null, null, secret));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        Assert.DoesNotContain("SUPERSECRETTOKEN", body);
        Assert.DoesNotContain("123456", body);

        var list = await owner.GetStringAsync(url);
        Assert.DoesNotContain("SUPERSECRETTOKEN", list);
        Assert.Contains("discord…", list);
    }

    [DockerFact]
    public async Task Crud_AndPermissions()
    {
        SetClan();
        var owner = await factory.RegisterAndLogin();
        var (ws, clanId) = await factory.CreateWorkspaceWithClan(owner);
        var invite = await (await owner.PostAsJsonAsync($"/api/v1/workspaces/{ws.Id}/invites", new CreateInviteRequest(null, "Staff"))).ReadAs<CreateInviteResponse>();
        var staff = await factory.RegisterAndLogin();
        await staff.PostAsync($"/api/v1/invites/{invite.Token}/accept", null);

        var alert = await (await owner.PostAsJsonAsync($"/api/v1/clans/{clanId}/alerts", new CreateAlertRequest("PendingWarAttacks", "Panel", null, 3, null, null))).ReadAs<AlertDto>();
        Assert.Equal(3, alert.HoursBeforeEnd);

        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync($"/api/v1/clans/{clanId}/alerts")).StatusCode);              // Viewer+ lê
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsJsonAsync($"/api/v1/clans/{clanId}/alerts", new CreateAlertRequest("MemberLeft", "Panel", null, null, null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.DeleteAsync($"/api/v1/alerts/{alert.Id}")).StatusCode);

        var off = await (await owner.PatchAsJsonAsync($"/api/v1/alerts/{alert.Id}", new UpdateAlertRequest(false, null, 5, null, null))).ReadAs<AlertDto>();
        Assert.False(off.Enabled);
        Assert.Equal(5, off.HoursBeforeEnd);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PatchAsJsonAsync($"/api/v1/alerts/{alert.Id}", new UpdateAlertRequest(null, null, 99, null, null))).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/v1/alerts/{alert.Id}")).StatusCode);
        Assert.Empty(await (await owner.GetAsync($"/api/v1/clans/{clanId}/alerts")).ReadAs<List<AlertDto>>());
    }

    [DockerFact]
    public async Task MemberLeft_Alert_EndToEnd_ThroughTheSyncTick_ThenMarkAsRead()
    {
        SetClan(M("#PPP2", "Líder"), M("#QQQ2", "Fulano"));
        var owner = await factory.RegisterAndLogin();
        var (_, clanId) = await factory.CreateWorkspaceWithClan(owner);
        await factory.Tick();                                                   // linha de base dos membros

        await owner.PostAsJsonAsync($"/api/v1/clans/{clanId}/alerts", new CreateAlertRequest("MemberLeft", "Panel", null, null, null, null));
        Assert.Equal(0, (await (await owner.GetAsync("/api/v1/me/notifications/unread-count")).ReadAs<UnreadCountDto>()).Count);

        SetClan(M("#PPP2", "Líder"));                                           // Fulano sai
        await factory.ForceDue(clanId, SyncKind.Members);
        await factory.Tick();
        await factory.ForceDue(clanId, SyncKind.Alerts);
        await factory.Tick();

        Assert.Equal(1, (await (await owner.GetAsync("/api/v1/me/notifications/unread-count")).ReadAs<UnreadCountDto>()).Count);
        var notes = await (await owner.GetAsync("/api/v1/me/notifications")).ReadAs<Page<NotificationDto>>();
        var n = Assert.Single(notes.Items);
        Assert.Contains("Fulano", n.Body);
        Assert.StartsWith("/app/", n.Link);

        // rodar de novo não repete
        await factory.ForceDue(clanId, SyncKind.Alerts);
        await factory.Tick();
        Assert.Single((await (await owner.GetAsync("/api/v1/me/notifications")).ReadAs<Page<NotificationDto>>()).Items);

        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync("/api/v1/me/notifications/read", null)).StatusCode);
        Assert.Equal(0, (await (await owner.GetAsync("/api/v1/me/notifications/unread-count")).ReadAs<UnreadCountDto>()).Count);
    }

    [DockerFact]
    public async Task Notifications_AreIsolatedPerUser()
    {
        SetClan(M("#PPP2", "Líder"), M("#QQQ2", "Fulano"));
        var owner = await factory.RegisterAndLogin();
        var (_, clanId) = await factory.CreateWorkspaceWithClan(owner);
        await factory.Tick();
        await owner.PostAsJsonAsync($"/api/v1/clans/{clanId}/alerts", new CreateAlertRequest("MemberLeft", "Panel", null, null, null, null));
        SetClan(M("#PPP2", "Líder"));
        await factory.ForceDue(clanId, SyncKind.Members); await factory.Tick();
        await factory.ForceDue(clanId, SyncKind.Alerts); await factory.Tick();

        var outsider = await factory.RegisterAndLogin();
        Assert.Equal(0, (await (await outsider.GetAsync("/api/v1/me/notifications/unread-count")).ReadAs<UnreadCountDto>()).Count);
        Assert.Empty((await (await outsider.GetAsync("/api/v1/me/notifications")).ReadAs<Page<NotificationDto>>()).Items);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.NewClient().GetAsync("/api/v1/me/notifications/unread-count")).StatusCode);
    }
}

[Collection("api")]
public class CwlAndCapitalApiTests(ApiFactory factory) : ApiTestBase(factory)
{
    private static readonly DateTime Start = new(2026, 1, 2, 10, 0, 0, DateTimeKind.Utc);

    private static ClanMemberSnapshot M(string tag, string name) => new(tag, name, "member", 100, 4000, 0, 0, 15, 1);

    private static WarSnapshot CwlWar(WarState state, int stars) => new(state, 15, 1, "#RRR2", "Rivais", Start, Start.AddDays(1), stars, 70, 5, 40,
        [new WarMemberSnapshot("#PPP2", "Líder", 1, 16, 1), new WarMemberSnapshot("#QQQ2", "Membro", 2, 14, 0)],
        [new WarAttackSnapshot("#PPP2", "#LLL2", 3, 100, 1, 100)]);

    private void Arrange()
    {
        factory.Connector.Clan = new ClanSnapshot("#2PP", "Clã Teste", "x", null, 2, [M("#PPP2", "Líder"), M("#QQQ2", "Membro")]);
        factory.Connector.CwlWars.Clear();
        factory.Connector.CwlGroup = new CwlGroupSnapshot("inWar", "2026-01", [new("#2PP", "Clã Teste"), new("#RRR2", "Rivais")], [["#2WW1", "#2WW2"], ["#0", "#0"]]);
        factory.Connector.CwlWars["#2WW2"] = CwlWar(WarState.InWar, 9);
    }

    [DockerFact]
    public async Task Cwl_EndToEnd_Season_Plan_Medals()
    {
        Arrange();
        factory.Connector.Raids = [];
        var owner = await factory.RegisterAndLogin();
        var (ws, clanId) = await factory.CreateWorkspaceWithClan(owner);

        Assert.Null((await (await owner.GetAsync($"/api/v1/clans/{clanId}/cwl/current")).ReadAs<CwlCurrentDto>()).Season);   // antes da primeira coleta
        await factory.Tick();

        var cwl = await (await owner.GetAsync($"/api/v1/clans/{clanId}/cwl/current")).ReadAs<CwlCurrentDto>();
        Assert.Equal("2026-01", cwl.Season!.Season);
        Assert.Equal(2, cwl.Season.Rounds.Count);
        Assert.Equal("InWar", cwl.Season.Rounds[0].State);
        Assert.Equal("Rivais", cwl.Season.Rounds[0].War!.OpponentName);
        Assert.Equal("NotStarted", cwl.Season.Rounds[1].State);
        Assert.Equal(["Clã Teste", "Rivais"], cwl.Season.Clans.Select(c => c.Name));
        var leader = cwl.Members.Single(m => m.Tag == "#PPP2");
        Assert.Equal((1, 1, 3, 16), (leader.RoundsPlayed, leader.Attacks, leader.Stars, leader.TownHall));

        // planejamento (Staff edita; todos veem)
        var plan = await (await owner.GetAsync($"/api/v1/clans/{clanId}/cwl/current/plan")).ReadAs<CwlPlanDto>();
        Assert.Equal(7, plan.RoundCount);
        Assert.Equal(2, plan.Rows.Count);
        Assert.All(plan.Rows, r => Assert.All(r.Rounds, x => Assert.Null(x)));
        var updated = await (await owner.PutAsJsonAsync($"/api/v1/clans/{clanId}/cwl/current/plan", new UpdateCwlPlanRequest(
            [new CwlPlanCellDto(1, "#PPP2", true), new CwlPlanCellDto(1, "#QQQ2", false), new CwlPlanCellDto(99, "#PPP2", true), new CwlPlanCellDto(2, "invalida", true)]))).ReadAs<CwlPlanDto>();
        Assert.True(updated.Rows.Single(r => r.Tag == "#PPP2").Rounds[0]);
        Assert.False(updated.Rows.Single(r => r.Tag == "#QQQ2").Rounds[0]);
        Assert.All(updated.Rows, r => Assert.Null(r.Rounds[1]));                                     // células inválidas ignoradas
        var cleared = await (await owner.PutAsJsonAsync($"/api/v1/clans/{clanId}/cwl/current/plan", new UpdateCwlPlanRequest([new CwlPlanCellDto(1, "#PPP2", null)]))).ReadAs<CwlPlanDto>();
        Assert.Null(cleared.Rows.Single(r => r.Tag == "#PPP2").Rounds[0]);

        var invite = await (await owner.PostAsJsonAsync($"/api/v1/workspaces/{ws.Id}/invites", new CreateInviteRequest(null, "Viewer"))).ReadAs<CreateInviteResponse>();
        var viewer = await factory.RegisterAndLogin();
        await viewer.PostAsync($"/api/v1/invites/{invite.Token}/accept", null);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync($"/api/v1/clans/{clanId}/cwl/current/plan")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PutAsJsonAsync($"/api/v1/clans/{clanId}/cwl/current/plan", new UpdateCwlPlanRequest([]))).StatusCode);

        // medalhas: só quem atacou, somando exatamente o total
        var medals = await (await viewer.GetAsync($"/api/v1/clans/{clanId}/cwl/current/medals?bonus=100")).ReadAs<CwlMedalsDto>();
        Assert.Equal("#PPP2", Assert.Single(medals.Rows).Tag);
        Assert.Equal(100, medals.Rows.Sum(r => r.SuggestedMedals));
        Assert.Equal(0, (await (await viewer.GetAsync($"/api/v1/clans/{clanId}/cwl/current/medals?bonus=-5")).ReadAs<CwlMedalsDto>()).BonusPool);

        var seasons = await (await viewer.GetAsync($"/api/v1/clans/{clanId}/cwl/seasons")).ReadAs<List<CwlSeasonDto>>();
        Assert.Equal("2026-01", Assert.Single(seasons).Season);

        // o feed ganhou os eventos da CWL
        var events = await (await viewer.GetAsync($"/api/v1/clans/{clanId}/events")).ReadAs<EventsResponse>();
        Assert.Contains(events.Items, e => e.Type == "CwlRoundStarted");
        Assert.Contains(events.Items, e => e.Type == "WarAttackMade");

        // CWL não aparece na guerra normal
        var currentWar = await (await viewer.GetAsync($"/api/v1/clans/{clanId}/wars/current")).Content.ReadAsStringAsync();
        Assert.True(currentWar is "" or "null", $"esperava nenhuma guerra normal, veio: {currentWar}");
    }

    [DockerFact]
    public async Task Cwl_RoundEnds_AndPlanRequiresAnActiveSeason()
    {
        Arrange();
        var owner = await factory.RegisterAndLogin();
        var (_, clanId) = await factory.CreateWorkspaceWithClan(owner);

        factory.Connector.CwlGroup = null;
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PutAsJsonAsync($"/api/v1/clans/{clanId}/cwl/current/plan", new UpdateCwlPlanRequest([new CwlPlanCellDto(1, "#PPP2", true)]))).StatusCode);

        Arrange();
        await factory.Tick();
        factory.Connector.CwlWars["#2WW2"] = CwlWar(WarState.WarEnded, 11);
        await factory.ForceDue(clanId, SyncKind.CwlGroup);
        await factory.Tick();

        var cwl = await (await owner.GetAsync($"/api/v1/clans/{clanId}/cwl/current")).ReadAs<CwlCurrentDto>();
        var round = cwl.Season!.Rounds[0];
        Assert.Equal(("WarEnded", "win"), (round.State, round.War!.Result));
    }

    [DockerFact]
    public async Task CapitalRaids_EndToEnd_AndPaging()
    {
        Arrange();
        factory.Connector.CwlGroup = null;
        factory.Connector.Raids = Enumerable.Range(0, 10).Select(i => new CapitalRaidSnapshot("ended",
            new DateTime(2025, 6, 6, 7, 0, 0, DateTimeKind.Utc).AddDays(7 * i), new DateTime(2025, 6, 9, 7, 0, 0, DateTimeKind.Utc).AddDays(7 * i),
            1000 + i, 10, 3, [new CapitalRaidMemberSnapshot("#PPP2", "Líder", 5, 5, 1, 500 + i), new CapitalRaidMemberSnapshot("#QQQ2", "Membro", 1, 5, 0, 10)])).ToList();
        var owner = await factory.RegisterAndLogin();
        var (_, clanId) = await factory.CreateWorkspaceWithClan(owner);
        await factory.Tick();

        var page1 = await (await owner.GetAsync($"/api/v1/clans/{clanId}/capital/raids?limit=4")).ReadAs<Page<CapitalRaidDto>>();
        Assert.Equal(4, page1.Items.Count);
        Assert.Equal("4", page1.NextCursor);
        Assert.True(page1.Items[0].StartTime > page1.Items[1].StartTime);                            // mais recente primeiro
        Assert.Equal("Líder", page1.Items[0].Members[0].Name);                                       // maior saque primeiro
        var page3 = await (await owner.GetAsync($"/api/v1/clans/{clanId}/capital/raids?limit=4&cursor=8")).ReadAs<Page<CapitalRaidDto>>();
        Assert.Equal(2, page3.Items.Count);
        Assert.Null(page3.NextCursor);
        Assert.DoesNotContain((await (await owner.GetAsync($"/api/v1/clans/{clanId}/events")).ReadAs<EventsResponse>()).Items, e => e.Type == "RaidAttackMade"); // importação inicial é silenciosa

        var stranger = await factory.RegisterAndLogin();
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.GetAsync($"/api/v1/clans/{clanId}/capital/raids")).StatusCode);
    }

    [DockerFact]
    public async Task Maintenance_CreatesMissingSyncTargets_ForExistingClans()
    {
        Arrange();
        var owner = await factory.RegisterAndLogin();
        var (_, clanId) = await factory.CreateWorkspaceWithClan(owner);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.SyncTargets.Where(t => t.TrackedClanId == clanId && (t.Kind == SyncKind.CwlGroup || t.Kind == SyncKind.Alerts)).ExecuteDeleteAsync();
        }
        var result = await (await factory.NewClient().SendAsync(TestClient.Cron(HttpMethod.Post, "/internal/maintenance/daily"))).ReadAs<ClanHub.Sync.MaintenanceResult>();
        Assert.True(result.TargetsCreated >= 2);

        await using var verify = factory.Services.CreateAsyncScope();
        var kinds = await verify.ServiceProvider.GetRequiredService<AppDbContext>().SyncTargets.Where(t => t.TrackedClanId == clanId).Select(t => t.Kind).ToListAsync();
        Assert.Contains(SyncKind.CwlGroup, kinds);
        Assert.Contains(SyncKind.Alerts, kinds);

        var again = await (await factory.NewClient().SendAsync(TestClient.Cron(HttpMethod.Post, "/internal/maintenance/daily"))).ReadAs<ClanHub.Sync.MaintenanceResult>();
        Assert.Equal(0, again.TargetsCreated);                                                        // idempotente
    }

    [DockerFact]
    public async Task SeasonList_AlwaysOffersCurrent_AndStrikesExpireByClanSettings()
    {
        Arrange();
        factory.Connector.CwlGroup = null;
        var owner = await factory.RegisterAndLogin();
        var (_, clanId) = await factory.CreateWorkspaceWithClan(owner);
        await factory.Tick();

        var seasons = await (await owner.GetAsync($"/api/v1/clans/{clanId}/seasons")).ReadAs<SeasonListDto>();
        Assert.Equal("current", seasons.Seasons[0]);
        var current = await (await owner.GetAsync($"/api/v1/clans/{clanId}/seasons/current/stats")).ReadAs<List<SeasonStatRowDto>>();
        Assert.Equal(2, current.Count);

        await owner.PatchAsJsonAsync($"/api/v1/clans/{clanId}", new UpdateClanRequest(StrikeExpiryDays: 10));
        var note = await (await owner.PostAsJsonAsync($"/api/v1/clans/{clanId}/members/%23PPP2/notes", new CreateNoteRequest("Strike", "x", 1, null))).ReadAs<NoteDto>();
        Assert.NotNull(note.ExpiresAt);
        Assert.InRange((note.ExpiresAt!.Value - DateTime.UtcNow).TotalDays, 9.9, 10.1);
        var plain = await (await owner.PostAsJsonAsync($"/api/v1/clans/{clanId}/members/%23PPP2/notes", new CreateNoteRequest("Note", "só uma nota", 0, null))).ReadAs<NoteDto>();
        Assert.Null(plain.ExpiresAt);
    }
}
[Collection("api")]
public class SyncResilienceTests(ApiFactory factory) : ApiTestBase(factory)
{
    private static ClanMemberSnapshot M(string tag, string name) => new(tag, name, "member", 100, 4000, 0, 0, 15, 1);

    private void SetClan(params ClanMemberSnapshot[] members) =>
        factory.Connector.Clan = new ClanSnapshot("#2PP", "Clã Teste", "x", null, members.Length, members);

    [DockerFact]
    public async Task OptionalRoute404_DoesNotMarkTheClanAsNotFound()
    {
        SetClan(M("#PPP2", "Líder"));
        factory.Connector.RaidsError = SupercellErrorKind.NotFound;           // o clã não tem Capital ainda
        var owner = await factory.RegisterAndLogin();
        var (_, clanId) = await factory.CreateWorkspaceWithClan(owner);

        await factory.Tick();

        var overview = await (await owner.GetAsync($"/api/v1/clans/{clanId}")).ReadAs<ClanOverviewDto>();
        Assert.Equal("Active", overview.Clan.Status);
        Assert.Equal(1, overview.Clan.MemberCount);                           // a coleta de membros funcionou normalmente
        var status = await (await owner.GetAsync($"/api/v1/clans/{clanId}/sync-status")).ReadAs<SyncStatusDto>();
        Assert.Contains(status.Targets, t => t.Kind == "CapitalRaids" && t.FailCount == 1);
        Assert.Contains(status.Targets, t => t.Kind == "Members" && t.FailCount == 0);
    }

    [DockerFact]
    public async Task CoreClan404_MarksNotFound_AndRecoversWhenTheClanComesBack()
    {
        SetClan(M("#PPP2", "Líder"));
        var owner = await factory.RegisterAndLogin();
        var (_, clanId) = await factory.CreateWorkspaceWithClan(owner);
        await factory.Tick();

        factory.Connector.ClanError = SupercellErrorKind.NotFound;
        await factory.ForceDue(clanId, SyncKind.Members);
        await factory.Tick();
        Assert.Equal("NotFound", (await (await owner.GetAsync($"/api/v1/clans/{clanId}")).ReadAs<ClanOverviewDto>()).Clan.Status);

        factory.Connector.ClanError = null;
        await factory.ForceDue(clanId, SyncKind.Members);
        await using (var scope = factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().SyncTargets
                .Where(t => t.TrackedClanId == clanId).ExecuteUpdateAsync(s => s.SetProperty(t => t.NextRunAt, DateTime.UtcNow.AddSeconds(-1)));
        await factory.Tick();
        Assert.Equal("Active", (await (await owner.GetAsync($"/api/v1/clans/{clanId}")).ReadAs<ClanOverviewDto>()).Clan.Status);
    }

    [DockerFact]
    public async Task ClansSharingPlayers_SyncedInTheSameTick_BothSucceed()
    {
        SetClan(M("#PPP2", "Líder"), M("#QQQ2", "Membro"));
        var a = await factory.RegisterAndLogin();
        var b = await factory.RegisterAndLogin();
        var (_, clanA) = await factory.CreateWorkspaceWithClan(a);
        var (_, clanB) = await factory.CreateWorkspaceWithClan(b);   // outro workspace, mesmo clã do jogo => mesmos jogadores

        await factory.Tick();                                          // as duas coletas inserem os mesmos jogadores em paralelo

        foreach (var (client, clanId) in new[] { (a, clanA), (b, clanB) })
        {
            var members = await (await client.GetAsync($"/api/v1/clans/{clanId}/members")).ReadAs<List<MemberDto>>();
            Assert.Equal(2, members.Count);
            var status = await (await client.GetAsync($"/api/v1/clans/{clanId}/sync-status")).ReadAs<SyncStatusDto>();
            Assert.Contains(status.Targets, t => t.Kind == "Members" && t.FailCount == 0 && t.LastSuccessAt != null);
        }
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.Players.CountAsync(p => p.Tag == "#PPP2"));   // um registro por jogador, compartilhado
    }

    [DockerFact]
    public async Task ClientDisconnect_IsNotAServerError()
    {
        SetClan(M("#PPP2", "Líder"));
        var owner = await factory.RegisterAndLogin();
        var (_, clanId) = await factory.CreateWorkspaceWithClan(owner);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => owner.GetAsync($"/api/v1/clans/{clanId}", cts.Token));
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/v1/clans/{clanId}")).StatusCode);       // o servidor segue saudável
    }

    [DockerFact]
    public async Task OptionalCalendar_FailureDoesNotBreakTheOverview()
    {
        SetClan(M("#PPP2", "Líder"));
        var owner = await factory.RegisterAndLogin();
        var user = owner;
        factory.Connector.CalendarError = SupercellErrorKind.Unknown;
        // BrawlStars: chave de cache que nenhum outro teste usa (o HybridCache vive por toda a coleção)
        Assert.Empty(await (await user.GetAsync("/api/v1/games/BrawlStars/calendar")).ReadAs<List<CalendarEntryDto>>());
    }
}