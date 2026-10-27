using System.Net;
using ClanHub.Application.Alerts;
using ClanHub.Domain;
using ClanHub.Infrastructure.Persistence;
using ClanHub.Sync.Handlers;
using ClanHub.UnitTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClanHub.UnitTests;

public class AlertsHandlerTests
{
    private sealed class CapturingHandler(HttpStatusCode status = HttpStatusCode.NoContent) : HttpMessageHandler
    {
        public List<(Uri Uri, string Body)> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add((request.RequestUri!, request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct)));
            return new HttpResponseMessage(status);
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static readonly DateTime Now = new(2026, 1, 5, 12, 0, 0, DateTimeKind.Utc); // segunda-feira

    private static (AppDbContext Db, TrackedClan Clan, Guid Owner, Guid Staff, Guid Viewer) Setup(Game game = Game.ClashOfClans)
    {
        var db = TestDb.Create();
        var clan = Ctx.NewClan(db, game);
        var owner = Guid.NewGuid(); var staff = Guid.NewGuid(); var viewer = Guid.NewGuid();
        db.WorkspaceMembers.AddRange(
            new WorkspaceMember { WorkspaceId = clan.WorkspaceId, UserId = owner, Role = WorkspaceRole.Owner },
            new WorkspaceMember { WorkspaceId = clan.WorkspaceId, UserId = staff, Role = WorkspaceRole.Staff },
            new WorkspaceMember { WorkspaceId = clan.WorkspaceId, UserId = viewer, Role = WorkspaceRole.Viewer });
        db.SaveChanges();
        return (db, clan, owner, staff, viewer);
    }

    private static AlertsHandler Handler(HttpMessageHandler? http = null) =>
        new(new Factory(http ?? new CapturingHandler()), NullLogger<AlertsHandler>.Instance);

    private static AlertRule Rule(TrackedClan clan, string type, AlertChannel channel = AlertChannel.Panel, AlertConfig? cfg = null) =>
        new() { TrackedClanId = clan.Id, Type = type, Channel = channel, ConfigJson = (cfg ?? new AlertConfig()).ToJson() };

    [Fact]
    public async Task MemberLeft_NotifiesStaffAndAbove_Once()
    {
        var (db, clan, owner, staff, viewer) = Setup();
        using var _ = db;
        db.ClanEvents.Add(new ClanEvent { TrackedClanId = clan.Id, Type = ClanEventType.MemberLeft, PlayerTag = "#QQQ2", PayloadJson = "{\"name\":\"Fulano\"}", OccurredAt = Now });
        db.AlertRules.Add(Rule(clan, AlertTypes.MemberLeft));
        await db.SaveChangesAsync();

        var h = Handler();
        await h.HandleAsync(Ctx.Make(db, clan, new FakeConnector(), Now), default);

        var users = db.Notifications.Select(n => n.UserId).ToList();
        Assert.Contains(owner, users);
        Assert.Contains(staff, users);
        Assert.DoesNotContain(viewer, users);
        Assert.Contains("Fulano", db.Notifications.First().Body);

        await h.HandleAsync(Ctx.Make(db, clan, new FakeConnector(), Now), default);
        Assert.Equal(2, db.Notifications.Count()); // sem repetir o mesmo evento
    }

    [Fact]
    public async Task DisabledRule_IsIgnored()
    {
        var (db, clan, _, _, _) = Setup();
        using var _ = db;
        db.ClanEvents.Add(new ClanEvent { TrackedClanId = clan.Id, Type = ClanEventType.MemberLeft, PlayerTag = "#QQQ2" });
        var rule = Rule(clan, AlertTypes.MemberLeft);
        rule.Enabled = false;
        db.AlertRules.Add(rule);
        await db.SaveChangesAsync();

        await Handler().HandleAsync(Ctx.Make(db, clan, new FakeConnector(), Now), default);
        Assert.Empty(db.Notifications);
    }

    [Fact]
    public async Task PendingWarAttacks_FiresOnlyInsideTheWindow_AndOncePerWar()
    {
        var (db, clan, _, _, _) = Setup();
        using var _ = db;
        var war = new War
        {
            TrackedClanId = clan.Id, State = WarState.InWar, OpponentName = "Rivais", AttacksPerMember = 2,
            StartTime = Now.AddHours(-20), EndTime = Now.AddHours(10),
            Participants = [new WarParticipant { PlayerTag = "#PPP2", Name = "Líder", AttacksUsed = 2 }, new WarParticipant { PlayerTag = "#QQQ2", Name = "Preguiçoso", AttacksUsed = 0, MapPosition = 2 }],
        };
        db.Wars.Add(war);
        db.AlertRules.Add(Rule(clan, AlertTypes.PendingWarAttacks, cfg: new AlertConfig(HoursBeforeEnd: 4)));
        await db.SaveChangesAsync();
        var h = Handler();

        await h.HandleAsync(Ctx.Make(db, clan, new FakeConnector(), Now), default);       // faltam 10 h: fora da janela
        Assert.Empty(db.Notifications);

        var late = Now.AddHours(8);                                                         // faltam 2 h
        await h.HandleAsync(Ctx.Make(db, clan, new FakeConnector(), late), default);
        var n = Assert.Single(db.Notifications.Where(x => x.Title.Contains("pendentes")).ToList().Take(1));
        Assert.Contains("Preguiçoso", n.Body);
        Assert.DoesNotContain("Líder", n.Body);

        var before = db.Notifications.Count();
        await h.HandleAsync(Ctx.Make(db, clan, new FakeConnector(), late.AddMinutes(15)), default);
        Assert.Equal(before, db.Notifications.Count());                                     // mesma guerra, não repete
    }

    [Fact]
    public async Task PendingWarAttacks_Silent_WhenEveryoneAttacked()
    {
        var (db, clan, _, _, _) = Setup();
        using var _ = db;
        db.Wars.Add(new War
        {
            TrackedClanId = clan.Id, State = WarState.InWar, AttacksPerMember = 2, EndTime = Now.AddHours(1),
            Participants = [new WarParticipant { PlayerTag = "#PPP2", Name = "Líder", AttacksUsed = 2 }],
        });
        db.AlertRules.Add(Rule(clan, AlertTypes.PendingWarAttacks));
        await db.SaveChangesAsync();
        await Handler().HandleAsync(Ctx.Make(db, clan, new FakeConnector(), Now), default);
        Assert.Empty(db.Notifications);
    }

    [Fact]
    public async Task MemberInactive_IsAWeeklySummary()
    {
        var (db, clan, _, _, _) = Setup();
        using var _ = db;
        var p1 = new Player { Game = Game.ClashOfClans, Tag = "#PPP2", Name = "Sumido" };
        var p2 = new Player { Game = Game.ClashOfClans, Tag = "#QQQ2", Name = "Ativo" };
        db.Players.AddRange(p1, p2);
        db.ClanMemberships.AddRange(
            new ClanMembership { TrackedClanId = clan.Id, PlayerId = p1.Id, Name = "Sumido", IsCurrent = true, JoinedAt = Now.AddDays(-60), LastActivityAt = Now.AddDays(-20) },
            new ClanMembership { TrackedClanId = clan.Id, PlayerId = p2.Id, Name = "Ativo", IsCurrent = true, JoinedAt = Now.AddDays(-60), LastActivityAt = Now.AddHours(-2) });
        db.AlertRules.Add(Rule(clan, AlertTypes.MemberInactive, cfg: new AlertConfig(Days: 7)));
        await db.SaveChangesAsync();
        var h = Handler();

        await h.HandleAsync(Ctx.Make(db, clan, new FakeConnector(), Now), default);
        Assert.Contains("Sumido", db.Notifications.First().Body);
        Assert.DoesNotContain("Ativo", db.Notifications.First().Body);
        var count = db.Notifications.Count();

        await h.HandleAsync(Ctx.Make(db, clan, new FakeConnector(), Now.AddDays(1)), default);   // mesma semana
        Assert.Equal(count, db.Notifications.Count());
        await h.HandleAsync(Ctx.Make(db, clan, new FakeConnector(), Now.AddDays(7)), default);   // semana seguinte
        Assert.True(db.Notifications.Count() > count);
    }

    [Fact]
    public async Task RiverRaceDecksPending_OncePerBattleDay_AfterConfiguredHour()
    {
        var (db, clan, _, _, _) = Setup(Game.ClashRoyale);
        using var _ = db;
        var race = new RiverRace
        {
            TrackedClanId = clan.Id, IsCurrent = true,
            Participants = [new RiverRaceParticipant { PlayerTag = "#P22", Name = "Rei", DecksUsedToday = 4 }, new RiverRaceParticipant { PlayerTag = "#P92", Name = "Peao", DecksUsedToday = 1 }],
        };
        db.RiverRaces.Add(race);
        db.AlertRules.Add(Rule(clan, AlertTypes.RiverRaceDecksPending, cfg: new AlertConfig(HourUtc: 8)));
        await db.SaveChangesAsync();
        var h = Handler();

        var thursdayEarly = new DateTime(2026, 1, 8, 6, 0, 0, DateTimeKind.Utc);
        await h.HandleAsync(Ctx.Make(db, clan, new FakeConnector(Game.ClashRoyale), thursdayEarly), default);
        Assert.Empty(db.Notifications);                                                       // ainda antes das 08h

        var thursday = new DateTime(2026, 1, 8, 9, 0, 0, DateTimeKind.Utc);
        await h.HandleAsync(Ctx.Make(db, clan, new FakeConnector(Game.ClashRoyale), thursday), default);
        Assert.Contains("Peao (1/4)", db.Notifications.First().Body);
        var count = db.Notifications.Count();

        await h.HandleAsync(Ctx.Make(db, clan, new FakeConnector(Game.ClashRoyale), thursday.AddHours(3)), default);
        Assert.Equal(count, db.Notifications.Count());

        var wednesday = new DateTime(2026, 1, 7, 12, 0, 0, DateTimeKind.Utc);               // não é dia de batalha
        var fresh = db.AlertRules.Single(); fresh.LastFiredKey = null; await db.SaveChangesAsync();
        await h.HandleAsync(Ctx.Make(db, clan, new FakeConnector(Game.ClashRoyale), wednesday), default);
        Assert.Equal(count, db.Notifications.Count());
    }

    [Fact]
    public async Task DiscordWebhook_PostsMessage_AndMarksFired()
    {
        var (db, clan, _, _, _) = Setup();
        using var _ = db;
        db.ClanEvents.Add(new ClanEvent { TrackedClanId = clan.Id, Type = ClanEventType.MemberJoined, PlayerTag = "#QQQ2", PayloadJson = "{\"name\":\"Novato\"}" });
        db.AlertRules.Add(Rule(clan, AlertTypes.MemberJoined, AlertChannel.DiscordWebhook,
            new AlertConfig(WebhookUrl: "https://discord.com/api/webhooks/123/abc")));
        await db.SaveChangesAsync();
        var http = new CapturingHandler();

        await Handler(http).HandleAsync(Ctx.Make(db, clan, new FakeConnector(), Now), default);

        var req = Assert.Single(http.Requests);
        Assert.Equal("https://discord.com/api/webhooks/123/abc", req.Uri.ToString());
        Assert.Contains("Novato", req.Body);
        Assert.Contains("allowed_mentions", req.Body);       // não menciona @everyone por acidente
        Assert.Empty(db.Notifications);
        Assert.NotNull(db.AlertRules.Single().LastFiredAt);
    }

    [Theory]
    [InlineData("http://discord.com/api/webhooks/1/a")]
    [InlineData("https://evil.example.com/api/webhooks/1/a")]
    [InlineData("https://discord.com.evil.com/api/webhooks/1/a")]
    [InlineData("https://discord.com/other/path")]
    [InlineData("https://user@discord.com/api/webhooks/1/a")]
    [InlineData("")]
    public async Task InvalidWebhook_IsNeverCalled(string url)
    {
        var (db, clan, _, _, _) = Setup();
        using var _ = db;
        db.ClanEvents.Add(new ClanEvent { TrackedClanId = clan.Id, Type = ClanEventType.MemberJoined, PlayerTag = "#QQQ2" });
        db.AlertRules.Add(Rule(clan, AlertTypes.MemberJoined, AlertChannel.DiscordWebhook, new AlertConfig(WebhookUrl: url)));
        await db.SaveChangesAsync();
        var http = new CapturingHandler();

        await Handler(http).HandleAsync(Ctx.Make(db, clan, new FakeConnector(), Now), default);

        Assert.Empty(http.Requests);
        Assert.Null(db.AlertRules.Single().LastFiredAt);
    }

    [Fact]
    public async Task WebhookFailure_IsRetriedNextRun()
    {
        var (db, clan, _, _, _) = Setup();
        using var _ = db;
        db.ClanEvents.Add(new ClanEvent { TrackedClanId = clan.Id, Type = ClanEventType.MemberJoined, PlayerTag = "#QQQ2" });
        db.AlertRules.Add(Rule(clan, AlertTypes.MemberJoined, AlertChannel.DiscordWebhook,
            new AlertConfig(WebhookUrl: "https://discord.com/api/webhooks/123/abc")));
        await db.SaveChangesAsync();

        var failing = new CapturingHandler(HttpStatusCode.InternalServerError);
        await Handler(failing).HandleAsync(Ctx.Make(db, clan, new FakeConnector(), Now), default);
        Assert.Null(db.AlertRules.Single().LastFiredAt);
        Assert.Equal(0, db.AlertRules.Single().LastEventId); // evento continua pendente

        var ok = new CapturingHandler();
        await Handler(ok).HandleAsync(Ctx.Make(db, clan, new FakeConnector(), Now), default);
        Assert.Single(ok.Requests);
    }
}