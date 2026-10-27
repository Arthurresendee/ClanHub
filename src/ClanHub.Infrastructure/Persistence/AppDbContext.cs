using ClanHub.Domain;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ClanHub.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options), IDataProtectionKeyContext
{
    public const string AppSchema = "app";
    public const string SyncSchema = "sync";
    public const string IdentitySchema = "identity";

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    public DbSet<Workspace> Workspaces => Set<Workspace>();
    public DbSet<WorkspaceMember> WorkspaceMembers => Set<WorkspaceMember>();
    public DbSet<WorkspaceInvite> WorkspaceInvites => Set<WorkspaceInvite>();
    public DbSet<LinkedPlayer> LinkedPlayers => Set<LinkedPlayer>();
    public DbSet<ClanClaim> ClanClaims => Set<ClanClaim>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<TrackedClan> TrackedClans => Set<TrackedClan>();
    public DbSet<Player> Players => Set<Player>();
    public DbSet<ClanMembership> ClanMemberships => Set<ClanMembership>();
    public DbSet<MemberDailySnapshot> MemberDailySnapshots => Set<MemberDailySnapshot>();
    public DbSet<SeasonStats> SeasonStats => Set<SeasonStats>();

    public DbSet<War> Wars => Set<War>();
    public DbSet<WarParticipant> WarParticipants => Set<WarParticipant>();
    public DbSet<WarAttack> WarAttacks => Set<WarAttack>();
    public DbSet<RiverRace> RiverRaces => Set<RiverRace>();
    public DbSet<RiverRaceParticipant> RiverRaceParticipants => Set<RiverRaceParticipant>();

    public DbSet<CwlSeason> CwlSeasons => Set<CwlSeason>();
    public DbSet<CwlRound> CwlRounds => Set<CwlRound>();
    public DbSet<CwlPlanEntry> CwlPlan => Set<CwlPlanEntry>();
    public DbSet<CapitalRaidSeason> CapitalRaidSeasons => Set<CapitalRaidSeason>();
    public DbSet<CapitalRaidMember> CapitalRaidMembers => Set<CapitalRaidMember>();
    public DbSet<ClanRanking> ClanRankings => Set<ClanRanking>();

    public DbSet<MemberNote> MemberNotes => Set<MemberNote>();
    public DbSet<RecruitmentApplication> RecruitmentApplications => Set<RecruitmentApplication>();
    public DbSet<AlertRule> AlertRules => Set<AlertRule>();
    public DbSet<Notification> Notifications => Set<Notification>();

    public DbSet<SyncTarget> SyncTargets => Set<SyncTarget>();
    public DbSet<ClanEvent> ClanEvents => Set<ClanEvent>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configuration)
    {
        base.ConfigureConventions(configuration);
        // Datas sempre em UTC: o EF devolve DateTime com Kind=Utc, e a API serializa com "Z".
        configuration.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configuration.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.HasDefaultSchema(AppSchema);

        // Identity
        b.Entity<ApplicationUser>(e =>
        {
            e.ToTable("Users", IdentitySchema);
            e.Property(u => u.DisplayName).HasMaxLength(100);
            e.Property(u => u.Locale).HasMaxLength(10);
            e.Property(u => u.TimeZone).HasMaxLength(64);
        });
        b.Entity<IdentityRole<Guid>>().ToTable("Roles", IdentitySchema);
        b.Entity<IdentityUserRole<Guid>>().ToTable("UserRoles", IdentitySchema);
        b.Entity<IdentityUserClaim<Guid>>().ToTable("UserClaims", IdentitySchema);
        b.Entity<IdentityUserLogin<Guid>>().ToTable("UserLogins", IdentitySchema);
        b.Entity<IdentityUserToken<Guid>>().ToTable("UserTokens", IdentitySchema);
        b.Entity<IdentityRoleClaim<Guid>>().ToTable("RoleClaims", IdentitySchema);
        // A tabela de passkeys só existe no modelo quando IdentityOptions.Stores.SchemaVersion >= 3.
        if (b.Model.FindEntityType(typeof(IdentityUserPasskey<Guid>)) is not null)
            b.Entity<IdentityUserPasskey<Guid>>().ToTable("UserPasskeys", IdentitySchema);
        b.Entity<DataProtectionKey>().ToTable("DataProtectionKeys", AppSchema);

        // Workspaces
        b.Entity<Workspace>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.Slug).HasMaxLength(120);
            e.HasIndex(x => x.Slug);
            e.HasQueryFilter(x => !x.IsDeleted);
        });
        b.Entity<WorkspaceMember>(e =>
        {
            e.HasKey(x => new { x.WorkspaceId, x.UserId });
            e.HasOne(x => x.Workspace).WithMany(w => w.Members).HasForeignKey(x => x.WorkspaceId);
            e.HasIndex(x => x.UserId);
        });
        b.Entity<WorkspaceInvite>(e =>
        {
            e.Property(x => x.Email).HasMaxLength(256);
            e.Property(x => x.TokenHash).HasMaxLength(128);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => x.WorkspaceId);
        });
        b.Entity<LinkedPlayer>(e =>
        {
            e.Property(x => x.PlayerTag).HasMaxLength(16);
            e.HasIndex(x => new { x.UserId, x.Game, x.PlayerTag }).IsUnique();
        });
        b.Entity<ClanClaim>(e =>
        {
            e.Property(x => x.ClanTag).HasMaxLength(16);
            e.Property(x => x.Code).HasMaxLength(16);
            e.HasIndex(x => x.UserId);
        });
        b.Entity<AuditLog>(e =>
        {
            e.Property(x => x.Action).HasMaxLength(100);
            e.HasIndex(x => new { x.WorkspaceId, x.Id });
        });

        // Clans
        b.Entity<TrackedClan>(e =>
        {
            e.Property(x => x.Tag).HasMaxLength(16);
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.BadgeUrl).HasMaxLength(500);
            e.HasOne(x => x.Workspace).WithMany(w => w.Clans).HasForeignKey(x => x.WorkspaceId);
            e.HasIndex(x => new { x.WorkspaceId, x.Game, x.Tag }).IsUnique().HasFilter("[IsDeleted] = 0");
            e.Property(x => x.PublicSlug).HasMaxLength(80);
            e.Property(x => x.RecruitmentText).HasMaxLength(2000);
            e.HasIndex(x => x.PublicSlug).IsUnique().HasFilter("[PublicSlug] IS NOT NULL AND [IsDeleted] = 0");
            e.HasQueryFilter(x => !x.IsDeleted);
        });
        b.Entity<Player>(e =>
        {
            e.Property(x => x.Tag).HasMaxLength(16);
            e.Property(x => x.Name).HasMaxLength(100);
            e.HasIndex(x => new { x.Game, x.Tag }).IsUnique();
        });
        b.Entity<ClanMembership>(e =>
        {
            e.HasKey(x => new { x.TrackedClanId, x.PlayerId });
            e.Property(x => x.Role).HasMaxLength(32);
            e.Property(x => x.Name).HasMaxLength(100);
            e.HasOne(x => x.Player).WithMany().HasForeignKey(x => x.PlayerId);
            e.HasIndex(x => new { x.TrackedClanId, x.IsCurrent });
        });
        b.Entity<MemberDailySnapshot>(e =>
        {
            e.HasKey(x => new { x.TrackedClanId, x.PlayerId, x.Date });
            e.HasIndex(x => new { x.TrackedClanId, x.Date });
        });
        b.Entity<SeasonStats>(e =>
        {
            e.HasKey(x => new { x.TrackedClanId, x.PlayerId, x.Season });
            e.Property(x => x.Season).HasMaxLength(16);
        });

        // Wars
        b.Entity<War>(e =>
        {
            e.Property(x => x.OpponentTag).HasMaxLength(16);
            e.Property(x => x.OpponentName).HasMaxLength(100);
            e.Property(x => x.Result).HasMaxLength(16);
            e.Property(x => x.WarTag).HasMaxLength(16);
            e.HasMany(x => x.Participants).WithOne().HasForeignKey(x => x.WarId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Attacks).WithOne().HasForeignKey(x => x.WarId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.TrackedClanId, x.EndTime });
        });
        b.Entity<WarParticipant>(e =>
        {
            e.HasKey(x => new { x.WarId, x.PlayerTag });
            e.Property(x => x.PlayerTag).HasMaxLength(16);
            e.Property(x => x.Name).HasMaxLength(100);
        });
        b.Entity<WarAttack>(e =>
        {
            e.HasKey(x => new { x.WarId, x.Order });
            e.Property(x => x.AttackerTag).HasMaxLength(16);
            e.Property(x => x.DefenderTag).HasMaxLength(16);
        });
        b.Entity<RiverRace>(e =>
        {
            e.HasMany(x => x.Participants).WithOne().HasForeignKey(x => x.RiverRaceId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.TrackedClanId, x.SeasonId, x.SectionIndex });
        });
        b.Entity<RiverRaceParticipant>(e =>
        {
            e.HasKey(x => new { x.RiverRaceId, x.PlayerTag });
            e.Property(x => x.PlayerTag).HasMaxLength(16);
            e.Property(x => x.Name).HasMaxLength(100);
        });

        b.Entity<CwlSeason>(e =>
        {
            e.Property(x => x.Season).HasMaxLength(16);
            e.Property(x => x.State).HasMaxLength(32);
            e.HasMany(x => x.Rounds).WithOne().HasForeignKey(x => x.CwlSeasonId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Plan).WithOne().HasForeignKey(x => x.CwlSeasonId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.TrackedClanId, x.Season }).IsUnique();
        });
        b.Entity<CwlRound>(e =>
        {
            e.HasKey(x => new { x.CwlSeasonId, x.RoundIndex });
            e.Property(x => x.OurWarTag).HasMaxLength(16);
        });
        b.Entity<CwlPlanEntry>(e =>
        {
            e.HasKey(x => new { x.CwlSeasonId, x.RoundIndex, x.PlayerTag });
            e.Property(x => x.PlayerTag).HasMaxLength(16);
        });
        b.Entity<CapitalRaidSeason>(e =>
        {
            e.Property(x => x.State).HasMaxLength(32);
            e.HasMany(x => x.Members).WithOne().HasForeignKey(x => x.CapitalRaidSeasonId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.TrackedClanId, x.StartTime }).IsUnique();
        });
        b.Entity<CapitalRaidMember>(e =>
        {
            e.HasKey(x => new { x.CapitalRaidSeasonId, x.PlayerTag });
            e.Property(x => x.PlayerTag).HasMaxLength(16);
            e.Property(x => x.Name).HasMaxLength(100);
        });
        b.Entity<ClanRanking>(e =>
        {
            e.HasKey(x => new { x.TrackedClanId, x.Date, x.Scope });
            e.Property(x => x.Scope).HasMaxLength(32);
            e.Property(x => x.LocationName).HasMaxLength(100);
        });

        // Social
        b.Entity<MemberNote>(e =>
        {
            e.Property(x => x.PlayerTag).HasMaxLength(16);
            e.Property(x => x.Text).HasMaxLength(2000);
            e.HasIndex(x => new { x.TrackedClanId, x.PlayerTag });
        });
        b.Entity<RecruitmentApplication>(e =>
        {
            e.Property(x => x.PlayerTag).HasMaxLength(16);
            e.Property(x => x.Message).HasMaxLength(1000);
            e.Property(x => x.ContactInfo).HasMaxLength(200);
            e.Property(x => x.ReviewComment).HasMaxLength(1000);
            e.HasIndex(x => new { x.TrackedClanId, x.Status });
        });
        b.Entity<AlertRule>(e =>
        {
            e.Property(x => x.Type).HasMaxLength(64);
            e.Property(x => x.LastFiredKey).HasMaxLength(100);
            e.HasIndex(x => x.TrackedClanId);
        });
        b.Entity<Notification>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(200);
            e.HasIndex(x => new { x.UserId, x.CreatedAt });
        });

        // Sync
        b.Entity<SyncTarget>(e =>
        {
            e.ToTable("SyncTarget", SyncSchema);
            e.Property(x => x.LeaseOwner).HasMaxLength(64);
            e.Property(x => x.LastError).HasMaxLength(1000);
            e.HasIndex(x => new { x.TrackedClanId, x.Kind }).IsUnique();
            e.HasIndex(x => x.NextRunAt).IncludeProperties(x => x.LeaseUntil);
        });
        b.Entity<ClanEvent>(e =>
        {
            e.ToTable("ClanEvent", SyncSchema);
            e.Property(x => x.PlayerTag).HasMaxLength(16);
            e.HasIndex(x => new { x.TrackedClanId, x.Id }).IsDescending(false, true);
        });
    }
}
public class UtcDateTimeConverter : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>
{
    public UtcDateTimeConverter() : base(v => v.Kind == DateTimeKind.Local ? v.ToUniversalTime() : v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc)) { }
}

public class NullableUtcDateTimeConverter : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime?, DateTime?>
{
    public NullableUtcDateTimeConverter() : base(
        v => v.HasValue && v.Value.Kind == DateTimeKind.Local ? v.Value.ToUniversalTime() : v,
        v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v) { }
}