using ClanHub.Application.Games;
using ClanHub.Contracts;
using ClanHub.Domain;
using ClanHub.Infrastructure.Persistence;
using ClanHub.Server.Common;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ClanHub.Server.Features;

public static class MeEndpoints
{
    public static void MapMe(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/me").RequireAuthorization();

        g.MapGet("/", GetMe);
        g.MapDelete("/", DeleteMe);
        g.MapGet("/players", async (HttpContext http, AppDbContext db) =>
            Results.Ok(await Players(db, http.User.UserId())));
        g.MapPost("/players/verify", VerifyPlayer);
        g.MapDelete("/players/{linkId:guid}", async (Guid linkId, HttpContext http, AppDbContext db) =>
        {
            var n = await db.LinkedPlayers.Where(l => l.Id == linkId && l.UserId == http.User.UserId()).ExecuteDeleteAsync();
            return n == 0 ? Results2.NotFound("Vínculo não encontrado.") : Results.NoContent();
        });
        g.MapGet("/notifications", async (HttpContext http, AppDbContext db, int? limit) =>
        {
            var uid = http.User.UserId();
            var items = await db.Notifications.Where(n => n.UserId == uid).OrderByDescending(n => n.CreatedAt)
                .Take(Math.Clamp(limit ?? 50, 1, 200))
                .Select(n => new NotificationDto(n.Id, n.Title, n.Body, n.Link, n.CreatedAt, n.ReadAt != null)).ToListAsync();
            return Results.Ok(new Page<NotificationDto>(items, null));
        });
        g.MapGet("/notifications/unread-count", async (HttpContext http, AppDbContext db) =>
        {
            var uid = http.User.UserId();
            return Results.Ok(new UnreadCountDto(await db.Notifications.CountAsync(n => n.UserId == uid && n.ReadAt == null)));
        });
        g.MapPost("/notifications/read", async (HttpContext http, AppDbContext db, TimeProvider clock) =>
        {
            var uid = http.User.UserId();
            var now = clock.GetUtcNow().UtcDateTime;
            await db.Notifications.Where(n => n.UserId == uid && n.ReadAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now));
            return Results.NoContent();
        });
    }

    private static async Task<List<LinkedPlayerDto>> Players(AppDbContext db, Guid uid) =>
        await db.LinkedPlayers.Where(l => l.UserId == uid)
            .Select(l => new LinkedPlayerDto(l.Id, l.Game.ToString(), l.PlayerTag, l.VerifiedAt)).ToListAsync();

    private static async Task<IResult> GetMe(HttpContext http, AppDbContext db, UserManager<ApplicationUser> users)
    {
        var user = await users.GetUserAsync(http.User);
        if (user is null) return Results.Unauthorized();

        var memberships = await (from m in db.WorkspaceMembers join w in db.Workspaces on m.WorkspaceId equals w.Id
                                 where m.UserId == user.Id orderby w.Name
                                 select new { w.Id, w.Name, w.Slug, m.Role }).ToListAsync();
        var wsIds = memberships.Select(m => m.Id).ToList();
        var clans = await Queries.SummariesAsync(db, db.TrackedClans.Where(c => wsIds.Contains(c.WorkspaceId)));

        var workspaces = memberships.Select(m => new WorkspaceSummaryDto(m.Id, m.Name, m.Slug, m.Role.ToString(),
            clans.Where(c => c.WorkspaceId == m.Id).ToList())).ToList();

        return Results.Ok(new MeResponse(user.Id, user.Email ?? "", user.DisplayName,
            http.User.IsInRole("Admin"), workspaces, await Players(db, user.Id)));
    }

    /// <summary>Exclusão de conta (LGPD): remove dados pessoais e encerra workspaces sem outros donos.</summary>
    private static async Task<IResult> DeleteMe(HttpContext http, AppDbContext db, UserManager<ApplicationUser> users,
        SignInManager<ApplicationUser> signIn)
    {
        var uid = http.User.UserId();
        var user = await users.FindByIdAsync(uid.ToString());
        if (user is null) return Results.NoContent();

        var owned = await db.WorkspaceMembers.Where(m => m.UserId == uid && m.Role == WorkspaceRole.Owner)
            .Select(m => m.WorkspaceId).ToListAsync();
        foreach (var wsId in owned)
        {
            var otherOwners = await db.WorkspaceMembers.AnyAsync(m => m.WorkspaceId == wsId && m.UserId != uid && m.Role == WorkspaceRole.Owner);
            if (otherOwners) continue;
            await db.Workspaces.Where(w => w.Id == wsId).ExecuteUpdateAsync(s => s.SetProperty(w => w.IsDeleted, true));
            var clanIds = await db.TrackedClans.Where(c => c.WorkspaceId == wsId).Select(c => c.Id).ToListAsync();
            await db.SyncTargets.Where(t => clanIds.Contains(t.TrackedClanId)).ExecuteDeleteAsync();
            await db.TrackedClans.Where(c => c.WorkspaceId == wsId).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsDeleted, true));
        }

        await db.WorkspaceMembers.Where(m => m.UserId == uid).ExecuteDeleteAsync();
        await db.LinkedPlayers.Where(l => l.UserId == uid).ExecuteDeleteAsync();
        await db.ClanClaims.Where(c => c.UserId == uid).ExecuteDeleteAsync();
        await db.Notifications.Where(n => n.UserId == uid).ExecuteDeleteAsync();
        await db.AuditLogs.Where(a => a.UserId == uid).ExecuteUpdateAsync(s => s.SetProperty(a => a.UserId, (Guid?)null));

        await signIn.SignOutAsync();
        var result = await users.DeleteAsync(user);
        return result.Succeeded ? Results.NoContent() : Results2.Problem(500, "Não foi possível excluir a conta.");
    }

    private static async Task<IResult> VerifyPlayer(VerifyPlayerRequest req, HttpContext http, AppDbContext db,
        IGameConnectorFactory games, TimeProvider clock)
    {
        if (GameMap.Parse(req.Game) is not { } game) return Results2.BadRequest("Jogo inválido.");
        var connector = games.Get(game);
        if (!connector.Features.PlayerTokenVerification)
            return Results2.BadRequest("Este jogo não suporta verificação por token.");
        if (!Tags.IsValid(req.PlayerTag)) return Results2.BadRequest("Tag inválida.");

        var tag = Tags.Normalize(req.PlayerTag);
        if (!await connector.VerifyPlayerTokenAsync(tag, req.Token.Trim(), http.RequestAborted))
            return Results2.Problem(422, "Token inválido.", "Confira a tag e o token de API nas configurações do jogo.");

        var uid = http.User.UserId();
        var link = await db.LinkedPlayers.FirstOrDefaultAsync(l => l.UserId == uid && l.Game == game && l.PlayerTag == tag);
        if (link is null)
        {
            link = new LinkedPlayer { UserId = uid, Game = game, PlayerTag = tag, Method = ClaimMethod.PlayerToken, VerifiedAt = clock.GetUtcNow().UtcDateTime };
            db.LinkedPlayers.Add(link);
            await db.SaveChangesAsync();
        }
        return Results.Ok(new LinkedPlayerDto(link.Id, game.ToString(), link.PlayerTag, link.VerifiedAt));
    }
}