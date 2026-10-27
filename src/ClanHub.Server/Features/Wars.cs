using ClanHub.Contracts;
using ClanHub.Domain;
using ClanHub.Infrastructure.Persistence;
using ClanHub.Server.Common;
using Microsoft.EntityFrameworkCore;

namespace ClanHub.Server.Features;

public static class WarEndpoints
{
    public static void MapWars(this IEndpointRouteBuilder app)
    {
        var c = app.MapGroup("/api/v1/clans/{clanId:guid}");
        c.MapGet("/wars/current", Current).RequireWorkspaceRole(WorkspaceRole.Viewer);
        c.MapGet("/wars", List).RequireWorkspaceRole(WorkspaceRole.Viewer);
        c.MapGet("/wars/stats", Stats).RequireWorkspaceRole(WorkspaceRole.Viewer);
        app.MapGet("/api/v1/wars/{warId:guid}", Detail).RequireWorkspaceRole(WorkspaceRole.Viewer);

        c.MapGet("/river-race/current", RiverCurrent).RequireWorkspaceRole(WorkspaceRole.Viewer);
        c.MapGet("/river-race/history", RiverHistory).RequireWorkspaceRole(WorkspaceRole.Viewer);
        c.MapGet("/river-race/stats", RiverStats).RequireWorkspaceRole(WorkspaceRole.Viewer);
    }

    internal static WarSummaryDto ToSummary(War w) => new(w.Id, w.State.ToString(), w.Result, w.OpponentName, w.TeamSize,
        w.Stars, w.Destruction, w.OpponentStars, w.OpponentDestruction, w.EndTime);

    private static WarDetailDto ToDetail(War w) => new(ToSummary(w), w.StartTime,
        w.Participants.OrderBy(p => p.MapPosition)
            .Select(p => new WarParticipantDto(p.PlayerTag, p.Name, p.MapPosition, p.TownHall, p.AttacksUsed, w.AttacksPerMember)).ToList(),
        w.Attacks.OrderBy(a => a.Order).Select(a => new WarAttackDto(a.AttackerTag, a.DefenderTag, a.Stars, a.Destruction, a.Order)).ToList());

    private static async Task<IResult> Current(Guid clanId, AppDbContext db)
    {
        var since = DateTime.UtcNow.AddHours(-24);
        var war = await db.Wars.Include(w => w.Participants).Include(w => w.Attacks)
            .Where(w => w.TrackedClanId == clanId && w.Type == WarType.Normal
                && (w.State == WarState.InWar || w.State == WarState.Preparation || (w.EndTime != null && w.EndTime > since && w.Participants.Count > 0)))
            .OrderByDescending(w => w.StartTime).FirstOrDefaultAsync();
        return Results.Ok(war is null ? null : ToDetail(war));
    }

    private static async Task<IResult> List(Guid clanId, AppDbContext db, string? cursor, int? limit)
    {
        var q = db.Wars.Where(w => w.TrackedClanId == clanId && w.State == WarState.WarEnded);
        if (DateTime.TryParse(cursor, null, System.Globalization.DateTimeStyles.RoundtripKind, out var c)) q = q.Where(w => w.EndTime < c);
        var take = Math.Clamp(limit ?? 20, 1, 100);
        var rows = await q.OrderByDescending(w => w.EndTime).Take(take + 1).ToListAsync();
        var items = rows.Take(take).ToList();
        return Results.Ok(new Page<WarSummaryDto>(items.Select(ToSummary).ToList(),
            rows.Count > take && items[^1].EndTime is { } e ? e.ToString("O") : null));
    }

    private static async Task<IResult> Detail(Guid warId, AppDbContext db)
    {
        var war = await db.Wars.Include(w => w.Participants).Include(w => w.Attacks).FirstOrDefaultAsync(w => w.Id == warId);
        return war is null ? Results2.NotFound("Guerra não encontrada.") : Results.Ok(ToDetail(war));
    }

    private static async Task<IResult> Stats(Guid clanId, AppDbContext db, DateTime? from, DateTime? to)
    {
        var lo = from ?? DateTime.UtcNow.AddDays(-90);
        var hi = to ?? DateTime.UtcNow;
        var wars = await db.Wars.Include(w => w.Participants).Include(w => w.Attacks)
            .Where(w => w.TrackedClanId == clanId && w.State == WarState.WarEnded && w.EndTime >= lo && w.EndTime <= hi
                && w.Participants.Count > 0)
            .OrderByDescending(w => w.EndTime).Take(100).ToListAsync();

        var rows = wars.SelectMany(w => w.Participants.Select(p => new
            {
                p.PlayerTag, p.Name, Used = p.AttacksUsed, Missed = Math.Max(0, w.AttacksPerMember - p.AttacksUsed),
                Attacks = w.Attacks.Where(a => a.AttackerTag == p.PlayerTag).ToList(),
            }))
            .GroupBy(x => x.PlayerTag)
            .Select(g =>
            {
                var attacks = g.SelectMany(x => x.Attacks).ToList();
                return new WarStatsRowDto(g.Key, g.First().Name, g.Count(), g.Sum(x => x.Used), g.Sum(x => x.Missed),
                    attacks.Sum(a => a.Stars), attacks.Count == 0 ? 0 : Math.Round(attacks.Average(a => a.Destruction), 1));
            })
            .OrderByDescending(r => r.TotalStars).ToList();
        return Results.Ok(rows);
    }

    private static RiverRaceDto ToDto(RiverRace r) => new(r.Id, r.SeasonId, r.SectionIndex, r.Fame, r.Rank, r.IsCurrent, r.UpdatedAt,
        r.Participants.OrderByDescending(p => p.Fame)
            .Select(p => new RiverRaceParticipantDto(p.PlayerTag, p.Name, p.Fame, p.DecksUsed, p.DecksUsedToday)).ToList());

    private static async Task<IResult> RiverCurrent(Guid clanId, AppDbContext db)
    {
        var race = await db.RiverRaces.Include(r => r.Participants).FirstOrDefaultAsync(r => r.TrackedClanId == clanId && r.IsCurrent);
        return Results.Ok(race is null ? null : ToDto(race));
    }

    private static async Task<IResult> RiverHistory(Guid clanId, AppDbContext db, int? cursor, int? limit)
    {
        var skip = Math.Max(0, cursor ?? 0);
        var take = Math.Clamp(limit ?? 10, 1, 50);
        var rows = await db.RiverRaces.Include(r => r.Participants).Where(r => r.TrackedClanId == clanId && !r.IsCurrent)
            .OrderByDescending(r => r.SeasonId).ThenByDescending(r => r.SectionIndex).ThenByDescending(r => r.UpdatedAt)
            .Skip(skip).Take(take + 1).ToListAsync();
        return Results.Ok(new Page<RiverRaceDto>(rows.Take(take).Select(ToDto).ToList(),
            rows.Count > take ? (skip + take).ToString() : null));
    }

    private static async Task<IResult> RiverStats(Guid clanId, AppDbContext db, int? weeks)
    {
        var races = await db.RiverRaces.Include(r => r.Participants).Where(r => r.TrackedClanId == clanId && !r.IsCurrent)
            .OrderByDescending(r => r.SeasonId).ThenByDescending(r => r.SectionIndex)
            .Take(Math.Clamp(weeks ?? 8, 1, 52)).ToListAsync();
        var rows = races.SelectMany(r => r.Participants).GroupBy(p => p.PlayerTag)
            .Select(g => new RiverRaceStatsRowDto(g.Key, g.First().Name, g.Count(), Math.Round(g.Average(p => p.Fame), 0), g.Sum(p => p.DecksUsed)))
            .OrderByDescending(r => r.AverageFame).ToList();
        return Results.Ok(rows);
    }
}