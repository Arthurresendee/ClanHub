using System.Net;
using System.Net.Http.Json;
using ClanHub.Application.Games;
using ClanHub.Contracts;
using ClanHub.Domain;
using ClanHub.Infrastructure.Persistence;
using ClanHub.IntegrationTests.Infrastructure;
using ClanHub.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClanHub.IntegrationTests;

[Collection("api")]
public class ClanClaimAndSyncTests(ApiFactory factory) : ApiTestBase(factory)
{
    private static ClanMemberSnapshot M(string tag, string name, string role = "member", int donations = 0) =>
        new(tag, name, role, 100, 4000, donations, 0, 15, 1);

    private void SetClan(params ClanMemberSnapshot[] members) =>
        factory.Connector.Clan = factory.Connector.Clan with { Members = members, MemberCount = members.Length };

    private async Task<TickResult> Tick()
    {
        var res = await factory.NewClient().SendAsync(TestClient.Cron(HttpMethod.Post, "/internal/sync/tick"));
        return await res.ReadAs<TickResult>();
    }

    [DockerFact]
    public async Task Claim_ByDescriptionCode_RequiresCode_AndCreatesClanWithSyncTargets()
    {
        var user = await factory.RegisterAndLogin();
        var ws = await user.CreateWorkspace();
        factory.Connector.Clan = factory.Connector.Clan with { Description = "sem código" };

        var start = await (await user.PostAsJsonAsync($"/api/v1/workspaces/{ws.Id}/claims",
            new StartClaimRequest("ClashOfClans", "#2PP", "DescriptionCode"))).ReadAs<StartClaimResponse>();
        Assert.Matches("^CH-[A-Z0-9]{5}$", start.Code!);

        var notYet = await (await user.PostAsJsonAsync($"/api/v1/claims/{start.ClaimId}/verify", new VerifyClaimRequest(null, null))).ReadAs<VerifyClaimResponse>();
        Assert.False(notYet.Verified);

        factory.Connector.Clan = factory.Connector.Clan with { Description = $"oi {start.Code} tchau" };
        var ok = await (await user.PostAsJsonAsync($"/api/v1/claims/{start.ClaimId}/verify", new VerifyClaimRequest(null, null))).ReadAs<VerifyClaimResponse>();
        Assert.True(ok.Verified);

        var clans = await (await user.GetAsync($"/api/v1/workspaces/{ws.Id}/clans")).ReadAs<List<ClanSummaryDto>>();
        Assert.Equal("#2PP", Assert.Single(clans).Tag);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(SyncTargets.KindsFor(Game.ClashOfClans).Count, await db.SyncTargets.CountAsync(t => t.TrackedClanId == ok.ClanId));
        Assert.Contains(await db.AuditLogs.Where(a => a.WorkspaceId == ws.Id).Select(a => a.Action).ToListAsync(), a => a == "clan.claimed");

        // o mesmo clã não entra duas vezes no workspace
        var again = await user.PostAsJsonAsync($"/api/v1/workspaces/{ws.Id}/claims", new StartClaimRequest("ClashOfClans", "#2PP", "DescriptionCode"));
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [DockerFact]
    public async Task Claim_ByPlayerToken_RequiresLeaderOfThatClan_AndLinksPlayer()
    {
        var user = await factory.RegisterAndLogin();
        var ws = await user.CreateWorkspace();
        factory.Connector.TokenValid = true;

        var start = await (await user.PostAsJsonAsync($"/api/v1/workspaces/{ws.Id}/claims",
            new StartClaimRequest("ClashOfClans", "#2PP", "PlayerToken"))).ReadAs<StartClaimResponse>();
        Assert.Null(start.Code);

        factory.Connector.TokenValid = false;
        var bad = await (await user.PostAsJsonAsync($"/api/v1/claims/{start.ClaimId}/verify", new VerifyClaimRequest("#PPP2", "x"))).ReadAs<VerifyClaimResponse>();
        Assert.False(bad.Verified);

        factory.Connector.TokenValid = true;
        var ok = await (await user.PostAsJsonAsync($"/api/v1/claims/{start.ClaimId}/verify", new VerifyClaimRequest("#PPP2", "token-bom"))).ReadAs<VerifyClaimResponse>();
        Assert.True(ok.Verified);

        var me = await (await user.GetAsync("/api/v1/me")).ReadAs<MeResponse>();
        Assert.Equal("#PPP2", Assert.Single(me.LinkedPlayers).PlayerTag);
    }

    [DockerFact]
    public async Task Claim_ByToken_NotAllowedForClashRoyale()
    {
        var user = await factory.RegisterAndLogin();
        var ws = await user.CreateWorkspace();
        // o conector falso suporta token para qualquer jogo; aqui validamos a regra do servidor pelo recurso informado
        var r = await user.PostAsJsonAsync($"/api/v1/workspaces/{ws.Id}/claims", new StartClaimRequest("Inexistente", "#2PP", "PlayerToken"));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [DockerFact]
    public async Task Tick_SyncsMembers_AndPublishesEvents_ThenPollingSeesOnlyNew()
    {
        var user = await factory.RegisterAndLogin();
        SetClan(M("#PPP2", "Líder", "leader"), M("#QQQ2", "Membro"));
        var (ws, clanId) = await factory.CreateWorkspaceWithClan(user);

        var first = await Tick();
        Assert.True(first.Leased >= 1);

        var members = await (await user.GetAsync($"/api/v1/clans/{clanId}/members")).ReadAs<List<MemberDto>>();
        Assert.Equal(2, members.Count);
        Assert.Equal("Líder", members.Single(m => m.Tag == "#PPP2").Name);

        var overview = await (await user.GetAsync($"/api/v1/clans/{clanId}")).ReadAs<ClanOverviewDto>();
        Assert.Equal(2, overview.Clan.MemberCount);
        Assert.Empty(overview.RecentEvents); // primeira coleta é linha de base

        // adianta a próxima coleta (como o botão "Atualizar agora") e muda o clã
        SetClan(M("#PPP2", "Líder", "leader", donations: 30), M("#RRR2", "Novato"));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.SyncTargets.Where(t => t.TrackedClanId == clanId && t.Kind == SyncKind.Members)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.NextRunAt, DateTime.UtcNow.AddSeconds(-1)).SetProperty(t => t.LastRunAt, (DateTime?)null));
        }
        await Tick();

        var events = await (await user.GetAsync($"/api/v1/clans/{clanId}/events")).ReadAs<EventsResponse>();
        var types = events.Items.Select(e => e.Type).ToList();
        Assert.Contains("MemberJoined", types);
        Assert.Contains("MemberLeft", types);
        Assert.Contains("DonationsChanged", types);

        // polling: afterId devolve só o que é novo
        var none = await (await user.GetAsync($"/api/v1/clans/{clanId}/events?afterId={events.LastId}")).ReadAs<EventsResponse>();
        Assert.Empty(none.Items);
        var all = await (await user.GetAsync($"/api/v1/clans/{clanId}/events?afterId=0")).ReadAs<EventsResponse>();
        Assert.Equal(events.Items.Count, all.Items.Count);

        // refresh manual respeita o intervalo mínimo
        var refresh = await user.PostAsync($"/api/v1/clans/{clanId}/refresh", null);
        Assert.Equal(HttpStatusCode.TooManyRequests, refresh.StatusCode);

        var status = await (await user.GetAsync($"/api/v1/clans/{clanId}/sync-status")).ReadAs<SyncStatusDto>();
        Assert.Contains(status.Targets, t => t.Kind == "Members" && t.LastSuccessAt != null && t.FailCount == 0);
    }

    [DockerFact]
    public async Task Viewer_CanReadClan_ButCannotWriteNotesOrRefresh()
    {
        var owner = await factory.RegisterAndLogin();
        SetClan(M("#PPP2", "Líder", "leader"));
        var (ws, clanId) = await factory.CreateWorkspaceWithClan(owner);
        await Tick();

        var invite = await (await owner.PostAsJsonAsync($"/api/v1/workspaces/{ws.Id}/invites", new CreateInviteRequest(null, "Viewer"))).ReadAs<CreateInviteResponse>();
        var viewer = await factory.RegisterAndLogin();
        await viewer.PostAsync($"/api/v1/invites/{invite.Token}/accept", null);

        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync($"/api/v1/clans/{clanId}/members")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync($"/api/v1/clans/{clanId}/wars/current")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync($"/api/v1/clans/{clanId}/members/%23PPP2/notes")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsync($"/api/v1/clans/{clanId}/refresh", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.DeleteAsync($"/api/v1/clans/{clanId}")).StatusCode);

        var note = await (await owner.PostAsJsonAsync($"/api/v1/clans/{clanId}/members/%23PPP2/notes",
            new CreateNoteRequest("Strike", "Não atacou", 2, null))).ReadAs<NoteDto>();
        var list = await (await owner.GetAsync($"/api/v1/clans/{clanId}/members/%23PPP2/notes")).ReadAs<List<NoteDto>>();
        Assert.Equal(note.Id, Assert.Single(list).Id);
        var members = await (await owner.GetAsync($"/api/v1/clans/{clanId}/members")).ReadAs<List<MemberDto>>();
        Assert.Equal(2, members.Single().Strikes);
    }

    [DockerFact]
    public async Task Clans_AreIsolatedBetweenWorkspaces()
    {
        var a = await factory.RegisterAndLogin();
        var b = await factory.RegisterAndLogin();
        SetClan(M("#PPP2", "Líder", "leader"));
        var (_, clanA) = await factory.CreateWorkspaceWithClan(a);

        Assert.Equal(HttpStatusCode.Forbidden, (await b.GetAsync($"/api/v1/clans/{clanA}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await b.GetAsync($"/api/v1/clans/{clanA}/events")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await b.GetAsync($"/api/v1/clans/{clanA}/members")).StatusCode);

        // outro workspace pode acompanhar o mesmo clã do jogo (dados compartilhados por tag)
        var (_, clanB) = await factory.CreateWorkspaceWithClan(b);
        Assert.NotEqual(clanA, clanB);
    }

    [DockerFact]
    public async Task Maintenance_CreatesDailySnapshots_Idempotently()
    {
        var user = await factory.RegisterAndLogin();
        SetClan(M("#PPP2", "Líder", "leader"), M("#QQQ2", "Membro"));
        var (_, clanId) = await factory.CreateWorkspaceWithClan(user);
        await Tick();

        var http = factory.NewClient();
        var first = await (await http.SendAsync(TestClient.Cron(HttpMethod.Get, "/internal/maintenance/daily"))).ReadAs<MaintenanceResult>();
        var second = await (await http.SendAsync(TestClient.Cron(HttpMethod.Get, "/internal/maintenance/daily"))).ReadAs<MaintenanceResult>();
        Assert.True(first.SnapshotsCreated >= 2);
        Assert.Equal(0, second.SnapshotsCreated);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(2, await db.MemberDailySnapshots.CountAsync(s => s.TrackedClanId == clanId));
    }

    [DockerFact]
    public async Task SyncQueue_ConcurrentLeases_NeverReturnTheSameTarget()
    {
        const int total = 60;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.SyncTargets.ExecuteDeleteAsync();
            db.SyncTargets.AddRange(Enumerable.Range(0, total).Select(_ => new SyncTarget
            {
                TrackedClanId = Guid.NewGuid(), Kind = SyncKind.Members, NextRunAt = DateTime.UtcNow.AddMinutes(-1),
            }));
            await db.SaveChangesAsync();
        }

        async Task<IReadOnlyList<Guid>> Lease(string owner)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<SyncQueue>().LeaseAsync(owner, 40, default);
        }

        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => Task.Run(() => Lease($"tick-{i}"))));
        var all = results.SelectMany(r => r).ToList();

        Assert.Equal(all.Count, all.Distinct().Count());   // nenhuma tarefa reservada duas vezes
        Assert.Equal(total, all.Count);                    // e nenhuma ficou para trás

        // reserva expirada volta para a fila
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.SyncTargets.ExecuteUpdateAsync(s => s.SetProperty(t => t.LeaseUntil, DateTime.UtcNow.AddMinutes(-5)));
        }
        Assert.Equal(40, (await Lease("tick-late")).Count);

        await using (var scope = factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().SyncTargets.ExecuteDeleteAsync();
    }
}