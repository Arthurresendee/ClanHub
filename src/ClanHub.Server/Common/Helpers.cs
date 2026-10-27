using System.Security.Claims;
using ClanHub.Application.Games;
using ClanHub.Contracts;
using ClanHub.Domain;

namespace ClanHub.Server.Common;

public static class UserExtensions
{
    public static Guid UserId(this ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new InvalidOperationException("Usuário não autenticado."));
}

public static class GameMap
{
    public static Game? Parse(string? value) =>
        Enum.TryParse<Game>(value, ignoreCase: true, out var g) && Enum.IsDefined(g) ? g : null;

    public static string Id(Game g) => g.ToString();

    public static GameFeaturesDto Dto(GameFeatures f) =>
        new(f.ClanSearch, f.PlayerTokenVerification, f.Wars, f.Cwl, f.CapitalRaids, f.RiverRace,
            f.BattleLog, f.Rankings, f.Tournaments, f.Calendar, f.UpcomingChests);
}

public static class RoleMap
{
    public static WorkspaceRole? Parse(string? value) =>
        Enum.TryParse<WorkspaceRole>(value, ignoreCase: true, out var r) && Enum.IsDefined(r) ? r : null;
}

public static class Results2
{
    public static IResult Problem(int status, string title, string? detail = null) =>
        Results.Problem(statusCode: status, title: title, detail: detail);

    public static IResult BadRequest(string title, string? detail = null) => Problem(400, title, detail);
    public static IResult NotFound(string title) => Problem(404, title);
    public static IResult Conflict(string title) => Problem(409, title);
}