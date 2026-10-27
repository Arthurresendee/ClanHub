using System.Text.Json;
using ClanHub.Application.Alerts;
using ClanHub.Application.Games;
using ClanHub.Contracts;
using ClanHub.Domain;
using ClanHub.Infrastructure.Persistence;
using ClanHub.Server.Common;
using Microsoft.EntityFrameworkCore;

namespace ClanHub.Server.Features;

/// <summary>Recrutamento (página pública + candidaturas) e alertas.</summary>
public static class EngagementEndpoints
{
    public static void MapEngagement(this IEndpointRouteBuilder app)
    {
        var pub = app.MapGroup("/api/v1/public/clans/{slug}").RequireRateLimiting("public");
        pub.MapGet("/", PublicClan);
        pub.MapPost("/apply", Apply);

        var c = app.MapGroup("/api/v1/clans/{clanId:guid}");
        c.MapGet("/applications", ListApplications).RequireWorkspaceRole(WorkspaceRole.Staff);
        app.MapPatch("/api/v1/applications/{applicationId:guid}", Review).RequireWorkspaceRole(WorkspaceRole.Staff);

        c.MapGet("/alerts", ListAlerts).RequireWorkspaceRole(WorkspaceRole.Viewer);
        c.MapPost("/alerts", CreateAlert).RequireWorkspaceRole(WorkspaceRole.Admin);
        app.MapPatch("/api/v1/alerts/{alertId:guid}", UpdateAlert).RequireWorkspaceRole(WorkspaceRole.Admin);
        app.MapDelete("/api/v1/alerts/{alertId:guid}", DeleteAlert).RequireWorkspaceRole(WorkspaceRole.Admin);
    }

    // ---- Página pública e candidaturas ----
    private static async Task<IResult> PublicClan(string slug, AppDbContext db)
    {
        var clan = await db.TrackedClans.Where(c => c.PublicSlug == slug && c.IsPublic).FirstOrDefaultAsync();
        if (clan is null) return Results2.NotFound("Clã não encontrado.");
        var members = await db.ClanMemberships.CountAsync(m => m.TrackedClanId == clan.Id && m.IsCurrent);
        return Results.Ok(new PublicClanDto(GameMap.Id(clan.Game), clan.Tag, clan.Name, clan.BadgeUrl, members, clan.RecruitmentText, slug));
    }

    private static async Task<IResult> Apply(string slug, ApplyRequest req, AppDbContext db, IGameConnectorFactory games,
        TimeProvider clock, CancellationToken ct)
    {
        var clan = await db.TrackedClans.Where(c => c.PublicSlug == slug && c.IsPublic).FirstOrDefaultAsync(ct);
        if (clan is null) return Results2.NotFound("Clã não encontrado.");
        if (!Tags.IsValid(req.PlayerTag)) return Results2.BadRequest("Tag de jogador inválida.");
        if (req.Message is { Length: > 1000 } || req.Contact is { Length: > 200 }) return Results2.BadRequest("Texto muito longo.");

        var tag = Tags.Normalize(req.PlayerTag);
        if (await db.RecruitmentApplications.AnyAsync(a => a.TrackedClanId == clan.Id && a.PlayerTag == tag && a.Status == ApplicationStatus.Pending, ct))
            return Results2.Conflict("Já existe uma candidatura pendente para este jogador.");

        var player = await games.Get(clan.Game).GetPlayerAsync(tag, ct); // valida que a tag existe e guarda um retrato do perfil
        var snapshot = JsonSerializer.Serialize(new
        {
            name = player.Name, expLevel = player.ExpLevel, trophies = player.Trophies, townHall = player.TownHall,
            currentClanTag = player.ClanTag,
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var application = new RecruitmentApplication
        {
            TrackedClanId = clan.Id, PlayerTag = tag, PlayerSnapshotJson = snapshot,
            Message = req.Message?.Trim(), ContactInfo = req.Contact?.Trim(), CreatedAt = clock.GetUtcNow().UtcDateTime,
        };
        db.RecruitmentApplications.Add(application);

        var staff = await db.WorkspaceMembers.Where(m => m.WorkspaceId == clan.WorkspaceId && m.Role >= WorkspaceRole.Staff)
            .Select(m => m.UserId).ToListAsync(ct);
        foreach (var user in staff)
            db.Notifications.Add(new Notification
            {
                UserId = user, Title = $"{clan.Name}: nova candidatura", Body = $"{player.Name} ({tag}) quer entrar no clã.",
                Link = $"/app/{clan.WorkspaceId}/cla/{clan.Id}/recrutamento", CreatedAt = application.CreatedAt,
            });
        await db.SaveChangesAsync(ct);
        return Results.Accepted(value: new { id = application.Id });
    }

    private static ApplicationDto ToDto(RecruitmentApplication a)
    {
        string? name = null;
        try
        {
            if (a.PlayerSnapshotJson is not null)
                name = JsonDocument.Parse(a.PlayerSnapshotJson).RootElement.GetProperty("name").GetString();
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException) { }
        return new ApplicationDto(a.Id, a.PlayerTag, name, a.Message, a.ContactInfo, a.Status.ToString(), a.PlayerSnapshotJson,
            a.CreatedAt, a.ReviewComment, a.ReviewedAt);
    }

    private static async Task<IResult> ListApplications(Guid clanId, AppDbContext db, string? status)
    {
        var q = db.RecruitmentApplications.Where(a => a.TrackedClanId == clanId);
        if (Enum.TryParse<ApplicationStatus>(status, true, out var s)) q = q.Where(a => a.Status == s);
        return Results.Ok((await q.OrderByDescending(a => a.CreatedAt).Take(200).ToListAsync()).Select(ToDto).ToList());
    }

    private static async Task<IResult> Review(Guid applicationId, UpdateApplicationRequest req, HttpContext http, AppDbContext db, TimeProvider clock)
    {
        if (!Enum.TryParse<ApplicationStatus>(req.Status, true, out var status) || !Enum.IsDefined(status))
            return Results2.BadRequest("Situação inválida.");
        var a = await db.RecruitmentApplications.FirstAsync(x => x.Id == applicationId);
        a.Status = status;
        a.ReviewComment = req.Comment?.Trim() is { Length: > 0 } cm ? cm[..Math.Min(cm.Length, 1000)] : null;
        a.ReviewedBy = http.User.UserId();
        a.ReviewedAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync();
        return Results.Ok(ToDto(a));
    }

    // ---- Alertas ----
    private static AlertDto ToDto(AlertRule r)
    {
        var cfg = AlertConfig.Parse(r.ConfigJson);
        string? hint = null;
        if (r.Channel == AlertChannel.DiscordWebhook && cfg.WebhookUrl is { Length: > 8 } url)
            hint = "discord…" + url[^6..];
        return new AlertDto(r.Id, r.Type, r.Channel.ToString(), cfg.Days, cfg.HoursBeforeEnd, cfg.HourUtc, hint, r.Enabled, r.LastFiredAt);
    }

    private static async Task<IResult> ListAlerts(Guid clanId, AppDbContext db) =>
        Results.Ok((await db.AlertRules.Where(r => r.TrackedClanId == clanId).OrderBy(r => r.Type).ToListAsync()).Select(ToDto).ToList());

    private static string? Validate(string type, AlertChannel channel, AlertConfig cfg, Game game)
    {
        if (!AlertTypes.All.Contains(type)) return "Tipo de alerta desconhecido.";
        if (type == AlertTypes.PendingWarAttacks && game != Game.ClashOfClans) return "Este alerta só existe no Clash of Clans.";
        if (type == AlertTypes.RiverRaceDecksPending && game != Game.ClashRoyale) return "Este alerta só existe no Clash Royale.";
        if (channel == AlertChannel.DiscordWebhook && !AlertWebhook.IsValid(cfg.WebhookUrl)) return "Informe um webhook válido do Discord (https://discord.com/api/webhooks/...).";
        if (cfg.Days is < 1 or > 90) return "Dias deve ficar entre 1 e 90.";
        if (cfg.HoursBeforeEnd is < 1 or > 23) return "Horas antes do fim deve ficar entre 1 e 23.";
        if (cfg.HourUtc is < 0 or > 23) return "Hora (UTC) deve ficar entre 0 e 23.";
        return null;
    }

    private static async Task<IResult> CreateAlert(Guid clanId, CreateAlertRequest req, AppDbContext db)
    {
        if (!Enum.TryParse<AlertChannel>(req.Channel, true, out var channel) || !Enum.IsDefined(channel))
            return Results2.BadRequest("Canal inválido.");
        var clan = await db.TrackedClans.FirstAsync(c => c.Id == clanId);
        var cfg = new AlertConfig(req.Days, req.HoursBeforeEnd, req.HourUtc, req.WebhookUrl?.Trim());
        if (Validate(req.Type, channel, cfg, clan.Game) is { } error) return Results2.BadRequest(error);

        var lastEvent = await db.ClanEvents.Where(e => e.TrackedClanId == clanId).Select(e => (long?)e.Id).MaxAsync() ?? 0;
        var rule = new AlertRule
        {
            TrackedClanId = clanId, Type = req.Type, Channel = channel, ConfigJson = cfg.ToJson(),
            Enabled = req.Enabled, LastEventId = lastEvent, // só avisa de eventos novos
        };
        db.AlertRules.Add(rule);
        await db.SaveChangesAsync();
        return Results.Created($"/api/v1/alerts/{rule.Id}", ToDto(rule));
    }

    private static async Task<IResult> UpdateAlert(Guid alertId, UpdateAlertRequest req, AppDbContext db)
    {
        var rule = await db.AlertRules.FirstAsync(r => r.Id == alertId);
        var clan = await db.TrackedClans.FirstAsync(c => c.Id == rule.TrackedClanId);
        var cfg = AlertConfig.Parse(rule.ConfigJson);
        cfg = cfg with
        {
            Days = req.Days ?? cfg.Days, HoursBeforeEnd = req.HoursBeforeEnd ?? cfg.HoursBeforeEnd,
            HourUtc = req.HourUtc ?? cfg.HourUtc, WebhookUrl = req.WebhookUrl?.Trim() ?? cfg.WebhookUrl,
        };
        if (Validate(rule.Type, rule.Channel, cfg, clan.Game) is { } error) return Results2.BadRequest(error);
        rule.ConfigJson = cfg.ToJson();
        if (req.Enabled is { } en) rule.Enabled = en;
        await db.SaveChangesAsync();
        return Results.Ok(ToDto(rule));
    }

    private static async Task<IResult> DeleteAlert(Guid alertId, AppDbContext db)
    {
        await db.AlertRules.Where(r => r.Id == alertId).ExecuteDeleteAsync();
        return Results.NoContent();
    }
}