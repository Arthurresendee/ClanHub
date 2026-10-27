using ClanHub.Application.Games;
using ClanHub.Domain;
using ClanHub.Infrastructure.Persistence;

namespace ClanHub.Sync.Handlers;

public record SyncContext(
    AppDbContext Db, TrackedClan Clan, IGameConnector Connector, DateTime Now, SyncOptions Options);

public interface ISyncHandler
{
    SyncKind Kind { get; }

    /// <summary>Executa a sincronização e devolve o intervalo até a próxima execução.</summary>
    Task<TimeSpan> HandleAsync(SyncContext ctx, CancellationToken ct);
}

internal static class EventExtensions
{
    private static readonly System.Text.Json.JsonSerializerOptions Json = new(System.Text.Json.JsonSerializerDefaults.Web);

    public static void AddEvent(this AppDbContext db, Guid clanId, ClanEventType type, string? tag, object? payload, DateTime now) =>
        db.ClanEvents.Add(new ClanEvent
        {
            TrackedClanId = clanId, Type = type, PlayerTag = tag, OccurredAt = now,
            PayloadJson = payload is null ? null : System.Text.Json.JsonSerializer.Serialize(payload, Json),
        });
}