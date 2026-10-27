using ClanHub.Domain;

namespace ClanHub.Application.Games;

public enum SupercellErrorKind { BadRequest, AccessDenied, NotFound, PrivateWarLog, Throttled, Maintenance, Unknown }

public class SupercellException(SupercellErrorKind kind, string message, TimeSpan? retryAfter = null, Exception? inner = null)
    : Exception(message, inner)
{
    public SupercellErrorKind Kind { get; } = kind;
    public TimeSpan? RetryAfter { get; } = retryAfter;
}

public class GameFeatureNotSupportedException(Game game, string feature)
    : Exception($"{game} não suporta o recurso {feature}.");