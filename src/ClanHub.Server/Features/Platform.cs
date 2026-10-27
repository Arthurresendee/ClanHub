using System.Text;
using ClanHub.Contracts;
using ClanHub.Domain;
using ClanHub.Infrastructure.Persistence;
using ClanHub.Server.Common;
using ClanHub.Sync;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ClanHub.Server.Features;

public static class PlatformEndpoints
{
    public static void MapPlatform(this IEndpointRouteBuilder app)
    {
        // Cron: aceita GET (Vercel Cron) e POST; idempotente; protegido por CRON_SECRET.
        var internalGroup = app.MapGroup("/internal").AddEndpointFilter<CronSecretFilter>();
        internalGroup.MapMethods("/sync/tick", ["GET", "POST"],
            async (TickRunner tick, CancellationToken ct) => Results.Ok(await tick.RunAsync(ct)));
        internalGroup.MapMethods("/maintenance/daily", ["GET", "POST"],
            async (MaintenanceRunner m, CancellationToken ct) => Results.Ok(await m.RunDailyAsync(ct)));

        var admin = app.MapGroup("/api/v1/admin").RequireAuthorization("PlatformAdmin");
        admin.MapGet("/overview", Overview);
        admin.MapGet("/sync", SyncOverview);
        admin.MapGet("/users", Users);
        admin.MapPost("/users/{id:guid}/lock", LockUser);

        app.MapGet("/api/v1/clans/{clanId:guid}/reports/{type}", Report).RequireWorkspaceRole(WorkspaceRole.Staff);
    }

    private static async Task<IResult> Overview(AppDbContext db)
    {
        var games = await GamesAsync(db);
        return Results.Ok(new AdminOverviewDto(await db.Users.CountAsync(), await db.Workspaces.CountAsync(),
            await db.TrackedClans.CountAsync(), games));
    }

    private static async Task<List<AdminSyncGameDto>> GamesAsync(AppDbContext db)
    {
        var clans = await db.TrackedClans.GroupBy(c => c.Game).Select(g => new { g.Key, N = g.Count() }).ToListAsync();
        var failing = await (from t in db.SyncTargets join c in db.TrackedClans on t.TrackedClanId equals c.Id
                             where t.FailCount > 0 select new { c.Game, c.Id }).Distinct().ToListAsync();
        return clans.Select(c => new AdminSyncGameDto(c.Key.ToString(), c.N, failing.Count(f => f.Game == c.Key))).ToList();
    }

    private static async Task<IResult> SyncOverview(AppDbContext db, TimeProvider clock)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var pending = await db.SyncTargets.CountAsync(t => t.NextRunAt <= now);
        var leased = await db.SyncTargets.CountAsync(t => t.LeaseUntil != null && t.LeaseUntil > now);
        var failingCount = await db.SyncTargets.CountAsync(t => t.FailCount > 0);
        var oldest = await db.SyncTargets.Where(t => t.NextRunAt <= now).MinAsync(t => (DateTime?)t.NextRunAt);

        var failures = await (from t in db.SyncTargets join c in db.TrackedClans on t.TrackedClanId equals c.Id
                              where t.FailCount > 0 orderby t.FailCount descending
                              select new AdminSyncFailureDto(c.Id, c.Name, c.Game.ToString(), t.Kind.ToString(), t.FailCount, t.LastError, t.NextRunAt))
            .Take(100).ToListAsync();

        return Results.Ok(new AdminSyncDto(pending, failingCount, leased, oldest, await GamesAsync(db), failures));
    }

    private static async Task<IResult> Users(AppDbContext db, string? q, int? limit)
    {
        var query = db.Users.AsQueryable();
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(u => u.Email!.Contains(q) || u.DisplayName.Contains(q));
        var now = DateTimeOffset.UtcNow;
        return Results.Ok(await query.OrderByDescending(u => u.CreatedAt).Take(Math.Clamp(limit ?? 50, 1, 200))
            .Select(u => new AdminUserDto(u.Id, u.Email ?? "", u.DisplayName, u.EmailConfirmed,
                u.LockoutEnd != null && u.LockoutEnd > now, u.CreatedAt)).ToListAsync());
    }

    private static async Task<IResult> LockUser(Guid id, LockUserRequest req, UserManager<ApplicationUser> users)
    {
        var user = await users.FindByIdAsync(id.ToString());
        if (user is null) return Results2.NotFound("Usuário não encontrado.");
        await users.SetLockoutEnabledAsync(user, true);
        await users.SetLockoutEndDateAsync(user, req.Lock ? DateTimeOffset.MaxValue : null);
        return Results.NoContent();
    }

    private static async Task<IResult> Report(Guid clanId, string type, string? format, AppDbContext db)
    {
        if (format is not null && !format.Equals("csv", StringComparison.OrdinalIgnoreCase))
            return Results2.BadRequest("Só o formato csv é suportado.");

        var sb = new StringBuilder();
        switch (type.ToLowerInvariant())
        {
            case "members":
                sb.AppendLine("tag,nome,cargo,nivel,trofeus,doacoes,recebidas,atual");
                foreach (var x in await (from m in db.ClanMemberships join p in db.Players on m.PlayerId equals p.Id
                                         where m.TrackedClanId == clanId orderby m.ClanRank select new { p.Tag, m }).ToListAsync())
                    sb.AppendLine(string.Join(',', Csv(x.Tag), Csv(x.m.Name), Csv(x.m.Role), x.m.ExpLevel, x.m.Trophies,
                        x.m.Donations, x.m.DonationsReceived, x.m.IsCurrent ? "sim" : "nao"));
                break;
            case "wars":
                sb.AppendLine("oponente,resultado,fim,estrelas,destruicao,estrelas_oponente");
                foreach (var w in await db.Wars.Where(w => w.TrackedClanId == clanId && w.State == WarState.WarEnded)
                             .OrderByDescending(w => w.EndTime).Take(500).ToListAsync())
                    sb.AppendLine(string.Join(',', Csv(w.OpponentName), Csv(w.Result), w.EndTime?.ToString("O"), w.Stars,
                        w.Destruction.ToString(System.Globalization.CultureInfo.InvariantCulture), w.OpponentStars));
                break;
            case "season":
                sb.AppendLine("temporada,tag,nome,doacoes,recebidas");
                foreach (var s in await (from s in db.SeasonStats join p in db.Players on s.PlayerId equals p.Id
                                         where s.TrackedClanId == clanId orderby s.Season descending, s.Donations descending
                                         select new { s.Season, p.Tag, p.Name, s.Donations, s.Received }).ToListAsync())
                    sb.AppendLine(string.Join(',', s.Season, Csv(s.Tag), Csv(s.Name), s.Donations, s.Received));
                break;
            default:
                return Results2.NotFound("Relatório desconhecido. Use members, wars ou season.");
        }
        return Results.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray(),
            "text/csv; charset=utf-8", $"{type}.csv");
    }

    private static string Csv(string? v)
    {
        v ??= "";
        if (v.Length > 0 && v[0] is '=' or '+' or '-' or '@') v = "'" + v; // evita injeção de fórmulas no Excel
        return v.Contains(',') || v.Contains('"') || v.Contains('\n') ? $"\"{v.Replace("\"", "\"\"")}\"" : v;
    }
}