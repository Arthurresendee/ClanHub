using System.Text.Json;
using ClanHub.Application.Wars;
using ClanHub.Contracts;
using ClanHub.Domain;
using ClanHub.Infrastructure.Persistence;
using ClanHub.Server.Common;
using Microsoft.EntityFrameworkCore;

namespace ClanHub.Server.Features;

public static class CwlAndCapitalEndpoints
{
    public static void MapCwlAndCapital(this IEndpointRouteBuilder app)
    {
        var c = app.MapGroup("/api/v1/clans/{clanId:guid}");
        c.MapGet("/cwl/current", Current).RequireWorkspaceRole(WorkspaceRole.Viewer);
        c.MapGet("/cwl/seasons", Seasons).RequireWorkspaceRole(WorkspaceRole.Viewer);
        c.MapGet("/cwl/current/plan", GetPlan).RequireWorkspaceRole(WorkspaceRole.Viewer);
        c.MapPut("/cwl/current/plan", PutPlan).RequireWorkspaceRole(WorkspaceRole.Staff);
        c.MapGet("/cwl/current/medals", Medals).RequireWorkspaceRole(WorkspaceRole.Viewer);
        c.MapGet("/capital/raids", Raids).RequireWorkspaceRole(WorkspaceRole.Viewer);
    }

    private static async Task<CwlSeason?> LatestSeason(AppDbContext db, Guid clanId) =>
        await db.CwlSeasons.Include(s => s.Rounds).Include(s => s.Plan)
            .Where(s => s.TrackedClanId == clanId).OrderByDescending(s => s.Season).FirstOrDefaultAsync();

    private static async Task<CwlSeasonDto> ToDto(AppDbContext db, CwlSeason s)
    {
        var warIds = s.Rounds.Where(r => r.WarId != null).Select(r => r.WarId!.Value).ToList();
        var wars = await db.Wars.Where(w => warIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id);
        var clans = new List<CwlClanDto>();
        if (!string.IsNullOrEmpty(s.ClansJson))
            try
            {
                using var doc = JsonDocument.Parse(s.ClansJson);
                clans = doc.RootElement.EnumerateArray()
                    .Select(e => new CwlClanDto(e.GetProperty("tag").GetString() ?? "", e.GetProperty("name").GetString() ?? "")).ToList();
            }
            catch (JsonException) { }

        var rounds = s.Rounds.OrderBy(r => r.RoundIndex).Select(r =>
            r.WarId is { } id && wars.TryGetValue(id, out var w)
                ? new CwlRoundDto(r.RoundIndex + 1, w.State.ToString(), WarEndpoints.ToSummary(w))
                : new CwlRoundDto(r.RoundIndex + 1, "NotStarted", null)).ToList();
        return new CwlSeasonDto(s.Id, s.Season, s.State, clans, rounds);
    }

    private static async Task<List<CwlPlayerStats>> StatsFor(AppDbContext db, CwlSeason s)
    {
        var warIds = s.Rounds.Where(r => r.WarId != null).Select(r => r.WarId!.Value).ToList();
        var wars = await db.Wars.Include(w => w.Participants).Include(w => w.Attacks).Where(w => warIds.Contains(w.Id)).ToListAsync();
        // Na CWL cada jogador tem 1 ataque por rodada em que está escalado.
        return wars.SelectMany(w => w.Participants.Select(p => (w, p)))
            .GroupBy(x => x.p.PlayerTag)
            .Select(g =>
            {
                var attacks = g.SelectMany(x => x.w.Attacks.Where(a => a.AttackerTag == g.Key)).ToList();
                return new CwlPlayerStats(g.Key, g.First().p.Name, g.Count(), attacks.Count, attacks.Sum(a => a.Stars),
                    attacks.Count == 0 ? 0 : attacks.Average(a => a.Destruction));
            }).ToList();
    }

    private static async Task<IResult> Current(Guid clanId, AppDbContext db)
    {
        var s = await LatestSeason(db, clanId);
        if (s is null) return Results.Ok(new CwlCurrentDto(null, []));

        var stats = await StatsFor(db, s);
        var tags = stats.Select(x => x.Tag).ToList();
        var ths = await db.WarParticipants.Where(p => tags.Contains(p.PlayerTag) && db.CwlRounds.Any(r => r.CwlSeasonId == s.Id && r.WarId == p.WarId))
            .GroupBy(p => p.PlayerTag).Select(g => new { g.Key, Th = g.Max(p => p.TownHall) }).ToDictionaryAsync(x => x.Key, x => x.Th);
        return Results.Ok(new CwlCurrentDto(await ToDto(db, s),
            stats.OrderByDescending(x => x.Stars).ThenByDescending(x => x.AverageDestruction)
                .Select(x => new CwlMemberStatDto(x.Tag, x.Name, ths.GetValueOrDefault(x.Tag), x.RoundsPlayed, x.Attacks, x.Stars, Math.Round(x.AverageDestruction, 1))).ToList()));
    }

    private static async Task<IResult> Seasons(Guid clanId, AppDbContext db)
    {
        var list = await db.CwlSeasons.Include(s => s.Rounds).Where(s => s.TrackedClanId == clanId)
            .OrderByDescending(s => s.Season).Take(12).ToListAsync();
        var result = new List<CwlSeasonDto>();
        foreach (var s in list) result.Add(await ToDto(db, s));
        return Results.Ok(result);
    }

    private static async Task<IResult> GetPlan(Guid clanId, AppDbContext db)
    {
        var s = await LatestSeason(db, clanId);
        var rounds = Math.Max(7, s?.Rounds.Count ?? 7);
        var members = await (from m in db.ClanMemberships join p in db.Players on m.PlayerId equals p.Id
                             where m.TrackedClanId == clanId && m.IsCurrent orderby m.TownHall descending, m.Name
                             select new { p.Tag, m.Name, m.TownHall }).ToListAsync();
        var rows = members.Select(m => new CwlPlanRowDto(m.Tag, m.Name, m.TownHall ?? 0,
            Enumerable.Range(0, rounds).Select(i => s?.Plan.FirstOrDefault(e => e.PlayerTag == m.Tag && e.RoundIndex == i)?.Plays).ToList())).ToList();
        return Results.Ok(new CwlPlanDto(rounds, rows));
    }

    private static async Task<IResult> PutPlan(Guid clanId, UpdateCwlPlanRequest req, AppDbContext db)
    {
        var s = await LatestSeason(db, clanId);
        if (s is null) return Results2.Problem(409, "Não há CWL em andamento para planejar.");
        foreach (var cell in req.Cells.Where(c => c.Round is >= 1 and <= 7 && Tags.IsValid(c.Tag)))
        {
            var tag = Tags.Normalize(cell.Tag);
            var idx = cell.Round - 1;
            var existing = s.Plan.FirstOrDefault(e => e.PlayerTag == tag && e.RoundIndex == idx);
            if (cell.Plays is null) { if (existing is not null) db.CwlPlan.Remove(existing); continue; }
            if (existing is null) db.CwlPlan.Add(new CwlPlanEntry { CwlSeasonId = s.Id, RoundIndex = idx, PlayerTag = tag, Plays = cell.Plays.Value });
            else existing.Plays = cell.Plays.Value;
        }
        await db.SaveChangesAsync();
        return await GetPlan(clanId, db);
    }

    private static async Task<IResult> Medals(Guid clanId, AppDbContext db, int? bonus)
    {
        var s = await LatestSeason(db, clanId);
        var pool = Math.Clamp(bonus ?? 0, 0, 10_000);
        if (s is null) return Results.Ok(new CwlMedalsDto(pool, []));
        var rows = CwlMedals.Suggest(await StatsFor(db, s), pool)
            .Select(m => new CwlMedalRowDto(m.Tag, m.Name, m.RoundsPlayed, m.Attacks, m.Stars, m.AverageDestruction, m.Score, m.SharePercent, m.SuggestedMedals)).ToList();
        return Results.Ok(new CwlMedalsDto(pool, rows));
    }

    private static async Task<IResult> Raids(Guid clanId, AppDbContext db, int? cursor, int? limit)
    {
        var skip = Math.Max(0, cursor ?? 0);
        var take = Math.Clamp(limit ?? 8, 1, 30);
        var rows = await db.CapitalRaidSeasons.Include(r => r.Members).Where(r => r.TrackedClanId == clanId)
            .OrderByDescending(r => r.StartTime).Skip(skip).Take(take + 1).ToListAsync();
        var items = rows.Take(take).Select(r => new CapitalRaidDto(r.Id, r.StartTime, r.EndTime, r.State, r.TotalLoot, r.AttackCount, r.DistrictsDestroyed,
            r.Members.OrderByDescending(m => m.Looted).Select(m => new CapitalRaidMemberDto(m.PlayerTag, m.Name, m.Attacks, m.AttackLimit, m.BonusAttackLimit, m.Looted)).ToList())).ToList();
        return Results.Ok(new Page<CapitalRaidDto>(items, rows.Count > take ? (skip + take).ToString() : null));
    }
}