namespace ClanHub.Domain;

public class Workspace : ISoftDeletable
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }
    public List<WorkspaceMember> Members { get; set; } = [];
    public List<TrackedClan> Clans { get; set; } = [];
}

public class WorkspaceMember
{
    public Guid WorkspaceId { get; set; }
    public Guid UserId { get; set; }
    public WorkspaceRole Role { get; set; }
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
    public Workspace? Workspace { get; set; }
}

public class WorkspaceInvite
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid WorkspaceId { get; set; }
    public string? Email { get; set; }
    public string TokenHash { get; set; } = "";
    public WorkspaceRole Role { get; set; } = WorkspaceRole.Staff;
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
}

public class LinkedPlayer
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public Game Game { get; set; }
    public string PlayerTag { get; set; } = "";
    public ClaimMethod Method { get; set; }
    public DateTime VerifiedAt { get; set; } = DateTime.UtcNow;
}

public class ClanClaim
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public Guid WorkspaceId { get; set; }
    public Game Game { get; set; }
    public string ClanTag { get; set; } = "";
    public ClaimMethod Method { get; set; }
    public string? Code { get; set; }
    public DateTime ExpiresAt { get; set; }
    public ClaimStatus Status { get; set; }
}

public class AuditLog
{
    public long Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid? UserId { get; set; }
    public string Action { get; set; } = "";
    public string? DataJson { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;
}