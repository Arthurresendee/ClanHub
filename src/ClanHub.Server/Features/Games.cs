using ClanHub.Application.Games;
using ClanHub.Contracts;
using ClanHub.Domain;
using ClanHub.Server.Common;

namespace ClanHub.Server.Features;

public static class GamesEndpoints
{
    public static void MapGames(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/games");

        g.MapGet("/", (IGameConnectorFactory games) => Results.Ok(
            games.All.OrderBy(c => c.Game).Select(c =>
                new GameInfoDto(GameMap.Id(c.Game), GameIds.Label(GameMap.Id(c.Game)), GameMap.Dto(c.Features))).ToList()));

        var auth = g.MapGroup("/{game}").RequireAuthorization();

        auth.MapGet("/clans/search", async (string game, string? name, IGameConnectorFactory games, CancellationToken ct) =>
        {
            if (GameMap.Parse(game) is not { } gm) return Results2.BadRequest("Jogo inválido.");
            var connector = games.Get(gm);
            if (!connector.Features.ClanSearch) return Results2.BadRequest("Este jogo não tem busca de clãs. Informe a tag.");
            if (string.IsNullOrWhiteSpace(name) || name.Trim().Length < 3) return Results2.BadRequest("Digite ao menos 3 letras.");
            var found = await connector.SearchClansAsync(name.Trim(), ct);
            return Results.Ok(found.Select(c => new ClanSearchItemDto(c.Tag, c.Name, c.BadgeUrl, c.Members, c.Level)).ToList());
        });

        auth.MapGet("/clans/{tag}/preview", async (string game, string tag, IGameConnectorFactory games, CancellationToken ct) =>
        {
            if (GameMap.Parse(game) is not { } gm) return Results2.BadRequest("Jogo inválido.");
            if (!Tags.IsValid(tag)) return Results2.BadRequest("Tag inválida.");
            var c = await games.Get(gm).GetClanAsync(tag, ct);
            return Results.Ok(new ClanPreviewDto(GameMap.Id(gm), c.Tag, c.Name, c.Description, c.BadgeUrl, c.MemberCount));
        });

        auth.MapGet("/players/{tag}/preview", async (string game, string tag, IGameConnectorFactory games, CancellationToken ct) =>
        {
            if (GameMap.Parse(game) is not { } gm) return Results2.BadRequest("Jogo inválido.");
            if (!Tags.IsValid(tag)) return Results2.BadRequest("Tag inválida.");
            var p = await games.Get(gm).GetPlayerAsync(tag, ct);
            return Results.Ok(new PlayerPreviewDto(GameMap.Id(gm), p.Tag, p.Name, p.ExpLevel, p.Trophies, p.ClanTag, p.TownHall));
        });
    }
}