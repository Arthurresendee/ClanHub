namespace ClanHub.Domain;

public class SyncTarget
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TrackedClanId { get; set; }
    public SyncKind Kind { get; set; }
    public DateTime NextRunAt { get; set; } = DateTime.UtcNow;
    public DateTime? LeaseUntil { get; set; }
    public string? LeaseOwner { get; set; }
    public int FailCount { get; set; }
    public string? LastError { get; set; }
    public DateTime? LastRunAt { get; set; }
    public DateTime? LastSuccessAt { get; set; }
}

public class ClanEvent
{
    public long Id { get; set; }
    public Guid TrackedClanId { get; set; }
    public ClanEventType Type { get; set; }
    public string? PlayerTag { get; set; }
    public string? PayloadJson { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}