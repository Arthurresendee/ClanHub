using ClanHub.Application.Games;
using ClanHub.Application.Workspaces;
using ClanHub.Contracts;
using ClanHub.Domain;
using ClanHub.Infrastructure.Persistence;
using ClanHub.Infrastructure.Security;
using ClanHub.Server.Common;
using ClanHub.Sync;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ClanHub.Server.Features;

public static class WorkspaceEndpoints
{
    public static void MapWorkspaces(this IEndpointRouteBuilder app)
    {
        var all = app.MapGroup("/api/v1/workspaces").RequireAuthorization();
        all.MapGet("/", ListWorkspaces);
        all.MapPost("/", CreateWorkspace);

        var ws = app.MapGroup("/api/v1/workspaces/{wsId:guid}");
        ws.MapGet("/", GetWorkspace).RequireWorkspaceRole(WorkspaceRole.Viewer);
        ws.MapPatch("/", Rename).RequireWorkspaceRole(WorkspaceRole.Admin);
        ws.MapDelete("/", DeleteWorkspace).RequireWorkspaceRole(WorkspaceRole.Owner);

        ws.MapGet("/members", ListMembers).RequireWorkspaceRole(WorkspaceRole.Viewer);
        ws.MapPatch("/members/{userId:guid}", UpdateMember).RequireWorkspaceRole(WorkspaceRole.Admin);
        ws.MapDelete("/members/{userId:guid}", RemoveMember).RequireWorkspaceRole(WorkspaceRole.Admin);

        ws.MapGet("/invites", ListInvites).RequireWorkspaceRole(WorkspaceRole.Admin);
        ws.MapPost("/invites", CreateInvite).RequireWorkspaceRole(WorkspaceRole.Admin);
        ws.MapDelete("/invites/{inviteId:guid}", CancelInvite).RequireWorkspaceRole(WorkspaceRole.Admin);

        ws.MapPost("/claims", StartClaim).RequireWorkspaceRole(WorkspaceRole.Admin);
        ws.MapGet("/audit", Audit).RequireWorkspaceRole(WorkspaceRole.Admin);

        app.MapPost("/api/v1/claims/{claimId:guid}/verify", VerifyClaim).RequireWorkspaceRole(WorkspaceRole.Admin);
        app.MapPost("/api/v1/invites/{token}/accept", AcceptInvite).RequireAuthorization();
    }

    // ---- Workspaces ----
    private static async Task<IResult> ListWorkspaces(HttpContext http, AppDbContext db)
    {
        var uid = http.User.UserId();
        var rows = await (from m in db.WorkspaceMembers join w in db.Workspaces on m.WorkspaceId equals w.Id
                          where m.UserId == uid orderby w.Name select new { w.Id, w.Name, w.Slug, m.Role }).ToListAsync();
        var ids = rows.Select(r => r.Id).ToList();
        var clans = await Queries.SummariesAsync(db, db.TrackedClans.Where(c => ids.Contains(c.WorkspaceId)));
        return Results.Ok(rows.Select(r => new WorkspaceSummaryDto(r.Id, r.Name, r.Slug, r.Role.ToString(),
            clans.Where(c => c.WorkspaceId == r.Id).ToList())).ToList());
    }

    private static async Task<IResult> CreateWorkspace(CreateWorkspaceRequest req, HttpContext http, AppDbContext db)
    {
        var name = req.Name?.Trim() ?? "";
        if (name.Length is < 2 or > 100) return Results2.BadRequest("O nome deve ter entre 2 e 100 caracteres.");

        var baseSlug = Slugs.From(name);
        var slug = baseSlug;
        for (var i = 2; await db.Workspaces.AnyAsync(w => w.Slug == slug); i++) slug = $"{baseSlug}-{i}";

        var uid = http.User.UserId();
        var workspace = new Workspace { Name = name, Slug = slug };
        workspace.Members.Add(new WorkspaceMember { UserId = uid, Role = WorkspaceRole.Owner });
        db.Workspaces.Add(workspace);
        Queries.Audit(db, workspace.Id, uid, "workspace.created", new { name });
        await db.SaveChangesAsync();
        return Results.Created($"/api/v1/workspaces/{workspace.Id}",
            new WorkspaceSummaryDto(workspace.Id, workspace.Name, workspace.Slug, nameof(WorkspaceRole.Owner), []));
    }

    private static async Task<IResult> GetWorkspace(Guid wsId, HttpContext http, AppDbContext db)
    {
        var w = await db.Workspaces.FirstOrDefaultAsync(x => x.Id == wsId);
        if (w is null) return Results2.NotFound("Workspace não encontrado.");
        var clans = await Queries.SummariesAsync(db, db.TrackedClans.Where(c => c.WorkspaceId == wsId));
        return Results.Ok(new WorkspaceSummaryDto(w.Id, w.Name, w.Slug, http.CurrentRole().ToString(), clans));
    }

    private static async Task<IResult> Rename(Guid wsId, RenameWorkspaceRequest req, HttpContext http, AppDbContext db)
    {
        var name = req.Name?.Trim() ?? "";
        if (name.Length is < 2 or > 100) return Results2.BadRequest("O nome deve ter entre 2 e 100 caracteres.");
        var w = await db.Workspaces.FirstAsync(x => x.Id == wsId);
        w.Name = name;
        Queries.Audit(db, wsId, http.User.UserId(), "workspace.renamed", new { name });
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteWorkspace(Guid wsId, HttpContext http, AppDbContext db)
    {
        var clanIds = await db.TrackedClans.Where(c => c.WorkspaceId == wsId).Select(c => c.Id).ToListAsync();
        await db.SyncTargets.Where(t => clanIds.Contains(t.TrackedClanId)).ExecuteDeleteAsync();
        await db.TrackedClans.Where(c => c.WorkspaceId == wsId).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsDeleted, true));
        Queries.Audit(db, wsId, http.User.UserId(), "workspace.deleted");
        await db.Workspaces.Where(w => w.Id == wsId).ExecuteUpdateAsync(s => s.SetProperty(w => w.IsDeleted, true));
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    // ---- Equipe ----
    private static async Task<IResult> ListMembers(Guid wsId, AppDbContext db) =>
        Results.Ok(await (from m in db.WorkspaceMembers
                          join u in db.Users on m.UserId equals u.Id
                          where m.WorkspaceId == wsId
                          orderby m.Role descending, u.DisplayName
                          select new TeamMemberDto(u.Id, u.DisplayName, u.Email ?? "", m.Role.ToString(), m.JoinedAt)).ToListAsync());

    private static async Task<IResult> UpdateMember(Guid wsId, Guid userId, UpdateMemberRoleRequest req, HttpContext http, AppDbContext db)
    {
        if (RoleMap.Parse(req.Role) is not { } role || role == WorkspaceRole.Owner)
            return Results2.BadRequest("Papel inválido.");
        var m = await db.WorkspaceMembers.FirstOrDefaultAsync(x => x.WorkspaceId == wsId && x.UserId == userId);
        if (m is null) return Results2.NotFound("Membro não encontrado.");
        if (m.Role == WorkspaceRole.Owner) return Results2.Problem(403, "O dono do workspace não pode ser alterado.");
        m.Role = role;
        Queries.Audit(db, wsId, http.User.UserId(), "team.role_changed", new { userId, role = role.ToString() });
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> RemoveMember(Guid wsId, Guid userId, HttpContext http, AppDbContext db)
    {
        var m = await db.WorkspaceMembers.FirstOrDefaultAsync(x => x.WorkspaceId == wsId && x.UserId == userId);
        if (m is null) return Results2.NotFound("Membro não encontrado.");
        if (m.Role == WorkspaceRole.Owner) return Results2.Problem(403, "O dono do workspace não pode ser removido.");
        db.WorkspaceMembers.Remove(m);
        Queries.Audit(db, wsId, http.User.UserId(), "team.removed", new { userId });
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    // ---- Convites ----
    private static async Task<IResult> ListInvites(Guid wsId, AppDbContext db) =>
        Results.Ok(await db.WorkspaceInvites.Where(i => i.WorkspaceId == wsId && i.UsedAt == null)
            .OrderByDescending(i => i.ExpiresAt)
            .Select(i => new InviteDto(i.Id, i.Email, i.Role.ToString(), i.ExpiresAt, i.UsedAt)).ToListAsync());

    private static async Task<IResult> CreateInvite(Guid wsId, CreateInviteRequest req, HttpContext http, AppDbContext db,
        TimeProvider clock, IConfiguration config)
    {
        if (RoleMap.Parse(req.Role) is not { } role || role == WorkspaceRole.Owner)
            return Results2.BadRequest("Papel inválido.");

        var token = TokenHasher.NewToken();
        var invite = new WorkspaceInvite
        {
            WorkspaceId = wsId, Role = role, TokenHash = TokenHasher.Hash(token),
            Email = string.IsNullOrWhiteSpace(req.Email) ? null : req.Email.Trim(),
            ExpiresAt = clock.GetUtcNow().UtcDateTime.AddDays(7),
        };
        db.WorkspaceInvites.Add(invite);
        Queries.Audit(db, wsId, http.User.UserId(), "team.invited", new { invite.Email, role = role.ToString() });
        await db.SaveChangesAsync();

        var baseUrl = (config["App:PublicUrl"] ?? $"{http.Request.Scheme}://{http.Request.Host}").TrimEnd('/');
        return Results.Created($"/api/v1/workspaces/{wsId}/invites/{invite.Id}",
            new CreateInviteResponse(invite.Id, token, $"{baseUrl}/convite/{token}", invite.ExpiresAt));
    }

    private static async Task<IResult> CancelInvite(Guid wsId, Guid inviteId, AppDbContext db)
    {
        var n = await db.WorkspaceInvites.Where(i => i.Id == inviteId && i.WorkspaceId == wsId).ExecuteDeleteAsync();
        return n == 0 ? Results2.NotFound("Convite não encontrado.") : Results.NoContent();
    }

    private static async Task<IResult> AcceptInvite(string token, HttpContext http, AppDbContext db,
        UserManager<ApplicationUser> users, TimeProvider clock)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var hash = TokenHasher.Hash(token);
        var invite = await db.WorkspaceInvites.FirstOrDefaultAsync(i => i.TokenHash == hash);
        if (invite is null || invite.UsedAt is not null || invite.ExpiresAt < now)
            return Results2.Problem(410, "Convite inválido ou expirado.");

        var user = await users.GetUserAsync(http.User);
        if (user is null) return Results.Unauthorized();
        if (invite.Email is not null && !string.Equals(invite.Email, user.Email, StringComparison.OrdinalIgnoreCase))
            return Results2.Problem(403, "Este convite foi enviado para outro e-mail.");

        var workspace = await db.Workspaces.FirstOrDefaultAsync(w => w.Id == invite.WorkspaceId);
        if (workspace is null) return Results2.Problem(410, "O workspace deste convite não existe mais.");

        if (!await db.WorkspaceMembers.AnyAsync(m => m.WorkspaceId == workspace.Id && m.UserId == user.Id))
        {
            db.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = workspace.Id, UserId = user.Id, Role = invite.Role });
            Queries.Audit(db, workspace.Id, user.Id, "team.joined", new { role = invite.Role.ToString() });
        }
        invite.UsedAt = now;
        await db.SaveChangesAsync();
        return Results.Ok(new AcceptInviteResponse(workspace.Id, workspace.Name));
    }

    // ---- Posse do clã ----
    private static async Task<IResult> StartClaim(Guid wsId, StartClaimRequest req, HttpContext http, AppDbContext db,
        IGameConnectorFactory games, TimeProvider clock, CancellationToken ct)
    {
        if (GameMap.Parse(req.Game) is not { } game) return Results2.BadRequest("Jogo inválido.");
        if (!Enum.TryParse<ClaimMethod>(req.Method, true, out var method) || !Enum.IsDefined(method))
            return Results2.BadRequest("Método inválido.");
        if (!Tags.IsValid(req.ClanTag)) return Results2.BadRequest("Tag do clã inválida.");

        var connector = games.Get(game);
        if (method == ClaimMethod.PlayerToken && !connector.Features.PlayerTokenVerification)
            return Results2.BadRequest("Este jogo não suporta verificação por token. Use o código na descrição.");

        var tag = Tags.Normalize(req.ClanTag);
        if (await db.TrackedClans.AnyAsync(c => c.WorkspaceId == wsId && c.Game == game && c.Tag == tag))
            return Results2.Conflict("Este clã já está no workspace.");

        await connector.GetClanAsync(tag, ct); // 404 se não existir

        var now = clock.GetUtcNow().UtcDateTime;
        var claim = new ClanClaim
        {
            UserId = http.User.UserId(), WorkspaceId = wsId, Game = game, ClanTag = tag, Method = method,
            Code = method == ClaimMethod.DescriptionCode ? ClaimCodes.Generate() : null,
            ExpiresAt = now + ClaimCodes.Validity, Status = ClaimStatus.Pending,
        };
        db.ClanClaims.Add(claim);
        await db.SaveChangesAsync(ct);

        var instructions = method == ClaimMethod.DescriptionCode
            ? $"Cole o código {claim.Code} na descrição do clã dentro do jogo e clique em verificar (válido por 30 minutos). Depois pode apagá-lo."
            : "Informe a tag do seu jogador e o token de API (Configurações do jogo > Mais configurações). Você precisa ser líder ou co-líder do clã.";
        return Results.Ok(new StartClaimResponse(claim.Id, method.ToString(), claim.Code, claim.ExpiresAt, instructions));
    }

    private static async Task<IResult> VerifyClaim(Guid claimId, VerifyClaimRequest req, HttpContext http, AppDbContext db,
        IGameConnectorFactory games, TimeProvider clock, CancellationToken ct)
    {
        var uid = http.User.UserId();
        var claim = await db.ClanClaims.FirstOrDefaultAsync(c => c.Id == claimId && c.UserId == uid, ct);
        if (claim is null) return Results2.NotFound("Pedido não encontrado.");
        if (claim.Status == ClaimStatus.Verified) return Results2.Conflict("Este pedido já foi aprovado.");

        var now = clock.GetUtcNow().UtcDateTime;
        if (claim.ExpiresAt < now)
        {
            claim.Status = ClaimStatus.Expired;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new VerifyClaimResponse(false, null, "O código expirou. Inicie uma nova verificação."));
        }

        var connector = games.Get(claim.Game);
        ClanSnapshot clan = await connector.GetClanAsync(claim.ClanTag, ct);

        if (claim.Method == ClaimMethod.DescriptionCode)
        {
            if (!ClaimCodes.DescriptionContains(clan.Description, claim.Code!))
                return Results.Ok(new VerifyClaimResponse(false, null, "Não encontramos o código na descrição do clã. Salve a descrição no jogo e tente de novo."));
        }
        else
        {
            if (!Tags.IsValid(req.PlayerTag) || string.IsNullOrWhiteSpace(req.PlayerToken))
                return Results2.BadRequest("Informe a tag do jogador e o token.");
            var playerTag = Tags.Normalize(req.PlayerTag);
            if (!await connector.VerifyPlayerTokenAsync(playerTag, req.PlayerToken.Trim(), ct))
                return Results.Ok(new VerifyClaimResponse(false, null, "Token inválido para este jogador."));

            var player = await connector.GetPlayerAsync(playerTag, ct);
            var isLeader = player.ClanTag == claim.ClanTag && player.ClanRole is { } r
                && (r.Equals("leader", StringComparison.OrdinalIgnoreCase) || r.Equals("coLeader", StringComparison.OrdinalIgnoreCase));
            if (!isLeader)
                return Results.Ok(new VerifyClaimResponse(false, null, "Este jogador não é líder nem co-líder do clã."));

            if (!await db.LinkedPlayers.AnyAsync(l => l.UserId == uid && l.Game == claim.Game && l.PlayerTag == playerTag, ct))
                db.LinkedPlayers.Add(new LinkedPlayer { UserId = uid, Game = claim.Game, PlayerTag = playerTag, Method = ClaimMethod.PlayerToken, VerifiedAt = now });
        }

        var tracked = await db.TrackedClans.FirstOrDefaultAsync(c => c.WorkspaceId == claim.WorkspaceId && c.Game == claim.Game && c.Tag == claim.ClanTag, ct);
        if (tracked is null)
        {
            tracked = new TrackedClan
            {
                WorkspaceId = claim.WorkspaceId, Game = claim.Game, Tag = claim.ClanTag, Name = clan.Name,
                BadgeUrl = clan.BadgeUrl, LastAccessedAt = now,
            };
            db.TrackedClans.Add(tracked);
            db.SyncTargets.AddRange(SyncTargets.CreateFor(tracked, now));
        }

        claim.Status = ClaimStatus.Verified;
        Queries.Audit(db, claim.WorkspaceId, uid, "clan.claimed", new { game = claim.Game.ToString(), claim.ClanTag, method = claim.Method.ToString() });
        await db.SaveChangesAsync(ct);
        return Results.Ok(new VerifyClaimResponse(true, tracked.Id, "Clã verificado e adicionado ao workspace."));
    }

    private static async Task<IResult> Audit(Guid wsId, AppDbContext db, long? cursor, int? limit)
    {
        var q = db.AuditLogs.Where(a => a.WorkspaceId == wsId);
        if (cursor is { } c) q = q.Where(a => a.Id < c);
        var take = Math.Clamp(limit ?? 50, 1, 200);
        var rows = await q.OrderByDescending(a => a.Id).Take(take + 1).ToListAsync();
        var items = rows.Take(take).Select(a => new AuditEntryDto(a.Id, a.UserId, a.Action, a.DataJson, a.At)).ToList();
        return Results.Ok(new Page<AuditEntryDto>(items, rows.Count > take ? items[^1].Id.ToString() : null));
    }
}