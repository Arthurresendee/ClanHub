namespace ClanHub.Domain;

public class TrackedClan : ISoftDeletable
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid WorkspaceId { get; set; }
    public Game Game { get; set; }
    public string Tag { get; set; } = "";
    public string Name { get; set; } = "";
    public string? BadgeUrl { get; set; }
    public TrackedClanStatus Status { get; set; }
    public string? SettingsJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastAccessedAt { get; set; }
    public bool IsDeleted { get; set; }

    /// <summary>Página pública de recrutamento (/c/{slug}), opcional por clã.</summary>
    public string? PublicSlug { get; set; }
    public bool IsPublic { get; set; }
    public string? RecruitmentText { get; set; }
    public Workspace? Workspace { get; set; }
}

public class Player
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Game Game { get; set; }
    public string Tag { get; set; } = "";
    public string Name { get; set; } = "";
    public string? ProfileJson { get; set; }
    public DateTime? LastFetchedAt { get; set; }
}

public class ClanMembership
{
    public Guid TrackedClanId { get; set; }
    public Guid PlayerId { get; set; }
    public string Role { get; set; } = "member";
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LeftAt { get; set; }
    public bool IsCurrent { get; set; } = true;
    public DateTime? LastActivityAt { get; set; }
    public int ExpLevel { get; set; }
    public int Trophies { get; set; }
    public int Donations { get; set; }
    public int DonationsReceived { get; set; }
    public int? TownHall { get; set; }
    public int ClanRank { get; set; }
    public string Name { get; set; } = "";
    public Player? Player { get; set; }
}

public class MemberDailySnapshot
{
    public Guid TrackedClanId { get; set; }
    public Guid PlayerId { get; set; }
    public DateOnly Date { get; set; }
    public int Trophies { get; set; }
    public int Donations { get; set; }
    public int Received { get; set; }
    public int ExpLevel { get; set; }
    public int? TownHall { get; set; }
}

public class SeasonStats
{
    public Guid TrackedClanId { get; set; }
    public Guid PlayerId { get; set; }
    public string Season { get; set; } = "";
    public int Donations { get; set; }
    public int Received { get; set; }
    public int WarStars { get; set; }
    public int AttacksUsed { get; set; }
    public int AttacksMissed { get; set; }
}