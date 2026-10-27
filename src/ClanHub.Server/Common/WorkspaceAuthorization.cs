using System.Security.Claims;
using ClanHub.Domain;
using ClanHub.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace ClanHub.Server.Common;

public record WorkspaceRoleRequirement(WorkspaceRole Minimum) : IAuthorizationRequirement;

/// <summary>
/// Policy RequireWorkspaceRole: descobre o workspace pela rota (wsId, clanId, warId, noteId...) e confere
/// o papel do usuário. O papel e o workspace ficam em HttpContext.Items para os endpoints.
/// </summary>
public class WorkspaceRoleHandler(AppDbContext db, IHttpContextAccessor accessor, TimeProvider clock)
    : AuthorizationHandler<WorkspaceRoleRequirement>
{
    public const string RoleKey = "ws.role";
    public const string WorkspaceKey = "ws.id";
    public const string ClanKey = "ws.clan";

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, WorkspaceRoleRequirement requirement)
    {
        var http = accessor.HttpContext;
        if (http is null || !Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return;

        var rv = http.Request.RouteValues;
        Guid? wsId = null, clanId = null;

        if (Get(rv, "wsId") is { } ws) wsId = ws;
        else if (Get(rv, "clanId") is { } c) { clanId = c; wsId = await ClanWorkspace(c); }
        else if (Get(rv, "warId") is { } w)
            wsId = await (from war in db.Wars join cl in db.TrackedClans on war.TrackedClanId equals cl.Id
                          where war.Id == w select (Guid?)cl.WorkspaceId).FirstOrDefaultAsync();
        else if (Get(rv, "noteId") is { } n)
            wsId = await (from note in db.MemberNotes join cl in db.TrackedClans on note.TrackedClanId equals cl.Id
                          where note.Id == n select (Guid?)cl.WorkspaceId).FirstOrDefaultAsync();
        else if (Get(rv, "applicationId") is { } appId)
            wsId = await (from a in db.RecruitmentApplications join cl in db.TrackedClans on a.TrackedClanId equals cl.Id
                          where a.Id == appId select (Guid?)cl.WorkspaceId).FirstOrDefaultAsync();
        else if (Get(rv, "alertId") is { } alertId)
            wsId = await (from a in db.AlertRules join cl in db.TrackedClans on a.TrackedClanId equals cl.Id
                          where a.Id == alertId select (Guid?)cl.WorkspaceId).FirstOrDefaultAsync();
        else if (Get(rv, "claimId") is { } cid)
            wsId = await db.ClanClaims.Where(x => x.Id == cid).Select(x => (Guid?)x.WorkspaceId).FirstOrDefaultAsync();

        if (wsId is null) return;

        var role = await (from m in db.WorkspaceMembers join w in db.Workspaces on m.WorkspaceId equals w.Id
                          where m.WorkspaceId == wsId && m.UserId == userId select (WorkspaceRole?)m.Role).FirstOrDefaultAsync();
        if (role is null || role < requirement.Minimum) return;

        http.Items[RoleKey] = role.Value;
        http.Items[WorkspaceKey] = wsId.Value;
        if (clanId is { } id)
        {
            http.Items[ClanKey] = id;
            var now = clock.GetUtcNow().UtcDateTime;
            var threshold = now.AddHours(-1);
            await db.TrackedClans.Where(x => x.Id == id && (x.LastAccessedAt == null || x.LastAccessedAt < threshold))
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastAccessedAt, now));
        }
        context.Succeed(requirement);
    }

    private async Task<Guid?> ClanWorkspace(Guid clanId) =>
        await db.TrackedClans.Where(c => c.Id == clanId).Select(c => (Guid?)c.WorkspaceId).FirstOrDefaultAsync();

    private static Guid? Get(RouteValueDictionary rv, string key) =>
        rv.TryGetValue(key, out var v) && Guid.TryParse(v?.ToString(), out var g) ? g : null;
}

public static class WorkspacePolicies
{
    public static string Name(WorkspaceRole r) => $"ws:{r}";

    public static void AddWorkspacePolicies(this AuthorizationOptions options)
    {
        foreach (var role in Enum.GetValues<WorkspaceRole>())
            options.AddPolicy(Name(role), p => p.RequireAuthenticatedUser().AddRequirements(new WorkspaceRoleRequirement(role)));
        options.AddPolicy("PlatformAdmin", p => p.RequireRole("Admin"));
    }

    public static TBuilder RequireWorkspaceRole<TBuilder>(this TBuilder builder, WorkspaceRole role)
        where TBuilder : IEndpointConventionBuilder => builder.RequireAuthorization(Name(role));

    public static WorkspaceRole CurrentRole(this HttpContext http) => (WorkspaceRole)http.Items[WorkspaceRoleHandler.RoleKey]!;
}