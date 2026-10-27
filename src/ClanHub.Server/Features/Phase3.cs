using ClanHub.Application.Games;
using ClanHub.Contracts;
using ClanHub.Domain;
using ClanHub.Infrastructure.Persistence;
using ClanHub.Server.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;

namespace ClanHub.Server.Features;

/// <summary>Fase 3: ranking, histórico de batalhas, baús, torneios e calendário.</summary>
public static class Phase3Endpoints
{
    private static readonly HybridCacheEntryOptions Short = new() { Expiration = TimeSpan.FromMinutes(2), LocalCacheExpiration = TimeSpan.FromMinutes(2) };
    private static readonly HybridCacheEntryOptions Medium = new() { Expiration = TimeSpan.FromMinutes(15), LocalCacheExpiration = TimeSpan.FromMinutes(5) };

    public static void MapPhase3(this IEndpointRouteBuilder app)
    {
        var c = app.MapGroup("/api/v1/clans/{clanId:guid}");
        c.MapGet("/ranking", Ranking).RequireWorkspaceRole(WorkspaceRole.Viewer);
        c.MapGet("/members/{playerTag}/battles", Battles).RequireWorkspaceRole(WorkspaceRole.Viewer);
        c.MapGet("/members/{playerTag}/chests", Chests).RequireWorkspaceRole(WorkspaceRole.Viewer);

        var g = app.MapGroup("/api/v1/games/{game}").RequireAuthorization();
        g.MapGet("/tournaments/search", SearchTournaments);
        g.MapGet("/tournaments/{tag}", GetTournament);
        g.MapGet("/calendar", Calendar);
    }

    private static async Task<IResult> Ranking(Guid clanId, AppDbContext db, int? days)
    {
        var from = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-Math.Clamp(days ?? 30, 1, 365)));
        var rows = await db.ClanRankings.Where(r => r.TrackedClanId == clanId && r.Date >= from)
            .OrderBy(r => r.Date).Select(r => new ClanRankingDto(r.Date, r.Scope, r.LocationName, r.Rank, r.Score)).ToListAsync();
        return Results.Ok(rows);
    }

    private static async Task<(TrackedClan Clan, string Tag)?> MemberOf(AppDbContext db, Guid clanId, string playerTag)
    {
        var tag = Tags.Normalize(playerTag);
        var ok = await (from m in db.ClanMemberships join p in db.Players on m.PlayerId equals p.Id
                        where m.TrackedClanId == clanId && p.Tag == tag select m.PlayerId).AnyAsync();
        if (!ok) return null;
        return (await db.TrackedClans.FirstAsync(c => c.Id == clanId), tag);
    }

    private static async Task<IResult> Battles(Guid clanId, string playerTag, AppDbContext db, IGameConnectorFactory games, HybridCache cache, CancellationToken ct)
    {
        if (await MemberOf(db, clanId, playerTag) is not var (clan, tag)) return Results2.NotFound("Membro não encontrado.");
        var connector = games.Get(clan.Game);
        if (!connector.Features.BattleLog) return Results2.BadRequest("Este jogo não tem histórico de batalhas.");

        var list = await cache.GetOrCreateAsync($"battles:{clan.Game}:{tag}", async token =>
            (await connector.GetBattleLogAsync(tag, token)).Select(b => new BattleDto(b.Time, b.Mode, b.Result, b.TrophyChange, b.Detail,
                b.Deck.Select(d => new BattleCardDto(d.Name, d.Level, d.IconUrl)).ToList())).ToList(), Short, cancellationToken: ct);
        return Results.Ok(list);
    }

    private static async Task<IResult> Chests(Guid clanId, string playerTag, AppDbContext db, IGameConnectorFactory games, HybridCache cache, CancellationToken ct)
    {
        if (await MemberOf(db, clanId, playerTag) is not var (clan, tag)) return Results2.NotFound("Membro não encontrado.");
        var connector = games.Get(clan.Game);
        if (!connector.Features.UpcomingChests) return Results2.BadRequest("Este jogo não mostra próximos baús.");
        var list = await cache.GetOrCreateAsync($"chests:{clan.Game}:{tag}", async token =>
            (await connector.GetUpcomingChestsAsync(tag, token)).Select(x => new ChestDto(x.Index, x.Name)).ToList(), Short, cancellationToken: ct);
        return Results.Ok(list);
    }

    private static async Task<IResult> SearchTournaments(string game, string? name, IGameConnectorFactory games, HybridCache cache, CancellationToken ct)
    {
        if (GameMap.Parse(game) is not { } gm) return Results2.BadRequest("Jogo inválido.");
        var connector = games.Get(gm);
        if (!connector.Features.Tournaments) return Results2.BadRequest("Este jogo não tem torneios.");
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length < 3) return Results2.BadRequest("Digite ao menos 3 letras.");
        var key = name.Trim().ToLowerInvariant();
        var list = await cache.GetOrCreateAsync($"tournaments:{gm}:{key}", async token =>
            (await connector.SearchTournamentsAsync(name.Trim(), token)).Select(ToDto).ToList(), Short, cancellationToken: ct);
        return Results.Ok(list);
    }

    private static async Task<IResult> GetTournament(string game, string tag, IGameConnectorFactory games, CancellationToken ct)
    {
        if (GameMap.Parse(game) is not { } gm) return Results2.BadRequest("Jogo inválido.");
        var connector = games.Get(gm);
        if (!connector.Features.Tournaments) return Results2.BadRequest("Este jogo não tem torneios.");
        if (!Tags.IsValid(tag)) return Results2.BadRequest("Tag inválida.");
        return Results.Ok(ToDto(await connector.GetTournamentAsync(tag, ct)));
    }

    private static TournamentDto ToDto(TournamentSnapshot t) =>
        new(t.Tag, t.Name, t.Status, t.Type, t.Players, t.Capacity, t.Description, t.StartedTime, t.EndedTime);

    private static async Task<IResult> Calendar(string game, IGameConnectorFactory games, HybridCache cache, CancellationToken ct)
    {
        if (GameMap.Parse(game) is not { } gm) return Results2.BadRequest("Jogo inválido.");
        var connector = games.Get(gm);
        if (!connector.Features.Calendar) return Results.Ok(Array.Empty<CalendarEntryDto>());
        // O calendário é um extra: se a Supercell falhar, a tela segue sem ele.
        try
        {
            var list = await cache.GetOrCreateAsync($"calendar:{gm}", async token =>
                (await connector.GetCalendarAsync(token)).Select(e => new CalendarEntryDto(e.Kind, e.Title, e.Start, e.End)).ToList(), Medium, cancellationToken: ct);
            return Results.Ok(list);
        }
        catch (SupercellException)
        {
            return Results.Ok(Array.Empty<CalendarEntryDto>());
        }
    }
}