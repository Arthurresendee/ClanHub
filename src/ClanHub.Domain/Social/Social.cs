namespace ClanHub.Domain;

public class MemberNote
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TrackedClanId { get; set; }
    public string PlayerTag { get; set; } = "";
    public Guid AuthorUserId { get; set; }
    public NoteKind Kind { get; set; }
    public string Text { get; set; } = "";
    public int Points { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class RecruitmentApplication
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TrackedClanId { get; set; }
    public string PlayerTag { get; set; } = "";
    public string? PlayerSnapshotJson { get; set; }
    public string? Message { get; set; }
    public ApplicationStatus Status { get; set; }
    public Guid? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewComment { get; set; }
    public string? ContactInfo { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class AlertRule
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TrackedClanId { get; set; }
    public string Type { get; set; } = "";
    public AlertChannel Channel { get; set; }
    public string? ConfigJson { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTime? LastFiredAt { get; set; }
    public string? LastFiredKey { get; set; }
    public long LastEventId { get; set; }
}

public class Notification
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public string Title { get; set; } = "";
    public string? Body { get; set; }
    public string? Link { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReadAt { get; set; }
}