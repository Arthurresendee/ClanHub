using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClanHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "app");

            migrationBuilder.EnsureSchema(
                name: "sync");

            migrationBuilder.EnsureSchema(
                name: "identity");

            migrationBuilder.CreateTable(
                name: "AlertRules",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TrackedClanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Channel = table.Column<int>(type: "int", nullable: false),
                    ConfigJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Enabled = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AlertRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Action = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DataJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    At = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ClanClaims",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Game = table.Column<int>(type: "int", nullable: false),
                    ClanTag = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Method = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClanClaims", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ClanEvent",
                schema: "sync",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TrackedClanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    PlayerTag = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClanEvent", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DataProtectionKeys",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FriendlyName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Xml = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataProtectionKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LinkedPlayers",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Game = table.Column<int>(type: "int", nullable: false),
                    PlayerTag = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Method = table.Column<int>(type: "int", nullable: false),
                    VerifiedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LinkedPlayers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MemberDailySnapshots",
                schema: "app",
                columns: table => new
                {
                    TrackedClanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Trophies = table.Column<int>(type: "int", nullable: false),
                    Donations = table.Column<int>(type: "int", nullable: false),
                    Received = table.Column<int>(type: "int", nullable: false),
                    ExpLevel = table.Column<int>(type: "int", nullable: false),
                    TownHall = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MemberDailySnapshots", x => new { x.TrackedClanId, x.PlayerId, x.Date });
                });

            migrationBuilder.CreateTable(
                name: "MemberNotes",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TrackedClanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlayerTag = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Points = table.Column<int>(type: "int", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MemberNotes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Notifications",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Link = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReadAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Players",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Game = table.Column<int>(type: "int", nullable: false),
                    Tag = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ProfileJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastFetchedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Players", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RecruitmentApplications",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TrackedClanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlayerTag = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    PlayerSnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Message = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ReviewedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecruitmentApplications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RiverRaces",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TrackedClanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SeasonId = table.Column<int>(type: "int", nullable: false),
                    SectionIndex = table.Column<int>(type: "int", nullable: false),
                    Fame = table.Column<int>(type: "int", nullable: false),
                    Rank = table.Column<int>(type: "int", nullable: false),
                    IsCurrent = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiverRaces", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Roles",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SeasonStats",
                schema: "app",
                columns: table => new
                {
                    TrackedClanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Season = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Donations = table.Column<int>(type: "int", nullable: false),
                    Received = table.Column<int>(type: "int", nullable: false),
                    WarStars = table.Column<int>(type: "int", nullable: false),
                    AttacksUsed = table.Column<int>(type: "int", nullable: false),
                    AttacksMissed = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeasonStats", x => new { x.TrackedClanId, x.PlayerId, x.Season });
                });

            migrationBuilder.CreateTable(
                name: "SyncTarget",
                schema: "sync",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TrackedClanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    NextRunAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LeaseUntil = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LeaseOwner = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    FailCount = table.Column<int>(type: "int", nullable: false),
                    LastError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    LastRunAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastSuccessAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncTarget", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Locale = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    TimeZone = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SecurityStamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhoneNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "bit", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "bit", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Wars",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TrackedClanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    WarTag = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    OpponentTag = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    OpponentName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TeamSize = table.Column<int>(type: "int", nullable: false),
                    AttacksPerMember = table.Column<int>(type: "int", nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    StartTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EndTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Result = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    Stars = table.Column<int>(type: "int", nullable: false),
                    Destruction = table.Column<double>(type: "float", nullable: false),
                    OpponentStars = table.Column<int>(type: "int", nullable: false),
                    OpponentDestruction = table.Column<double>(type: "float", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Wars", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkspaceInvites",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    TokenHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Role = table.Column<int>(type: "int", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UsedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkspaceInvites", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Workspaces",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Workspaces", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ClanMemberships",
                schema: "app",
                columns: table => new
                {
                    TrackedClanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    JoinedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LeftAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsCurrent = table.Column<bool>(type: "bit", nullable: false),
                    LastActivityAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExpLevel = table.Column<int>(type: "int", nullable: false),
                    Trophies = table.Column<int>(type: "int", nullable: false),
                    Donations = table.Column<int>(type: "int", nullable: false),
                    DonationsReceived = table.Column<int>(type: "int", nullable: false),
                    TownHall = table.Column<int>(type: "int", nullable: true),
                    ClanRank = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClanMemberships", x => new { x.TrackedClanId, x.PlayerId });
                    table.ForeignKey(
                        name: "FK_ClanMemberships_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalSchema: "app",
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RiverRaceParticipants",
                schema: "app",
                columns: table => new
                {
                    RiverRaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlayerTag = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Fame = table.Column<int>(type: "int", nullable: false),
                    DecksUsed = table.Column<int>(type: "int", nullable: false),
                    DecksUsedToday = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiverRaceParticipants", x => new { x.RiverRaceId, x.PlayerTag });
                    table.ForeignKey(
                        name: "FK_RiverRaceParticipants_RiverRaces_RiverRaceId",
                        column: x => x.RiverRaceId,
                        principalSchema: "app",
                        principalTable: "RiverRaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RoleClaims",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClaimValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RoleClaims_Roles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "identity",
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserClaims",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClaimValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserClaims_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserLogins",
                schema: "identity",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ProviderKey = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_UserLogins_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserRoles",
                schema: "identity",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_UserRoles_Roles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "identity",
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserRoles_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserTokens",
                schema: "identity",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LoginProvider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_UserTokens_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WarAttacks",
                schema: "app",
                columns: table => new
                {
                    WarId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    AttackerTag = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    DefenderTag = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Stars = table.Column<int>(type: "int", nullable: false),
                    Destruction = table.Column<double>(type: "float", nullable: false),
                    Duration = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WarAttacks", x => new { x.WarId, x.Order });
                    table.ForeignKey(
                        name: "FK_WarAttacks_Wars_WarId",
                        column: x => x.WarId,
                        principalSchema: "app",
                        principalTable: "Wars",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WarParticipants",
                schema: "app",
                columns: table => new
                {
                    WarId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlayerTag = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    MapPosition = table.Column<int>(type: "int", nullable: false),
                    TownHall = table.Column<int>(type: "int", nullable: false),
                    AttacksUsed = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WarParticipants", x => new { x.WarId, x.PlayerTag });
                    table.ForeignKey(
                        name: "FK_WarParticipants_Wars_WarId",
                        column: x => x.WarId,
                        principalSchema: "app",
                        principalTable: "Wars",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TrackedClans",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Game = table.Column<int>(type: "int", nullable: false),
                    Tag = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    BadgeUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    SettingsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastAccessedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackedClans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrackedClans_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalSchema: "app",
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkspaceMembers",
                schema: "app",
                columns: table => new
                {
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Role = table.Column<int>(type: "int", nullable: false),
                    JoinedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkspaceMembers", x => new { x.WorkspaceId, x.UserId });
                    table.ForeignKey(
                        name: "FK_WorkspaceMembers_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalSchema: "app",
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_WorkspaceId_Id",
                schema: "app",
                table: "AuditLogs",
                columns: new[] { "WorkspaceId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ClanClaims_UserId",
                schema: "app",
                table: "ClanClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ClanEvent_TrackedClanId_Id",
                schema: "sync",
                table: "ClanEvent",
                columns: new[] { "TrackedClanId", "Id" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_ClanMemberships_PlayerId",
                schema: "app",
                table: "ClanMemberships",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_ClanMemberships_TrackedClanId_IsCurrent",
                schema: "app",
                table: "ClanMemberships",
                columns: new[] { "TrackedClanId", "IsCurrent" });

            migrationBuilder.CreateIndex(
                name: "IX_LinkedPlayers_UserId_Game_PlayerTag",
                schema: "app",
                table: "LinkedPlayers",
                columns: new[] { "UserId", "Game", "PlayerTag" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MemberDailySnapshots_TrackedClanId_Date",
                schema: "app",
                table: "MemberDailySnapshots",
                columns: new[] { "TrackedClanId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_MemberNotes_TrackedClanId_PlayerTag",
                schema: "app",
                table: "MemberNotes",
                columns: new[] { "TrackedClanId", "PlayerTag" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId_CreatedAt",
                schema: "app",
                table: "Notifications",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Players_Game_Tag",
                schema: "app",
                table: "Players",
                columns: new[] { "Game", "Tag" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RiverRaces_TrackedClanId_SeasonId_SectionIndex",
                schema: "app",
                table: "RiverRaces",
                columns: new[] { "TrackedClanId", "SeasonId", "SectionIndex" });

            migrationBuilder.CreateIndex(
                name: "IX_RoleClaims_RoleId",
                schema: "identity",
                table: "RoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                schema: "identity",
                table: "Roles",
                column: "NormalizedName",
                unique: true,
                filter: "[NormalizedName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SyncTarget_NextRunAt",
                schema: "sync",
                table: "SyncTarget",
                column: "NextRunAt")
                .Annotation("SqlServer:Include", new[] { "LeaseUntil" });

            migrationBuilder.CreateIndex(
                name: "IX_SyncTarget_TrackedClanId_Kind",
                schema: "sync",
                table: "SyncTarget",
                columns: new[] { "TrackedClanId", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrackedClans_WorkspaceId_Game_Tag",
                schema: "app",
                table: "TrackedClans",
                columns: new[] { "WorkspaceId", "Game", "Tag" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_UserClaims_UserId",
                schema: "identity",
                table: "UserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserLogins_UserId",
                schema: "identity",
                table: "UserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserRoles_RoleId",
                schema: "identity",
                table: "UserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                schema: "identity",
                table: "Users",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                schema: "identity",
                table: "Users",
                column: "NormalizedUserName",
                unique: true,
                filter: "[NormalizedUserName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Wars_TrackedClanId_EndTime",
                schema: "app",
                table: "Wars",
                columns: new[] { "TrackedClanId", "EndTime" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceInvites_TokenHash",
                schema: "app",
                table: "WorkspaceInvites",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceInvites_WorkspaceId",
                schema: "app",
                table: "WorkspaceInvites",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceMembers_UserId",
                schema: "app",
                table: "WorkspaceMembers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Workspaces_Slug",
                schema: "app",
                table: "Workspaces",
                column: "Slug");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AlertRules",
                schema: "app");

            migrationBuilder.DropTable(
                name: "AuditLogs",
                schema: "app");

            migrationBuilder.DropTable(
                name: "ClanClaims",
                schema: "app");

            migrationBuilder.DropTable(
                name: "ClanEvent",
                schema: "sync");

            migrationBuilder.DropTable(
                name: "ClanMemberships",
                schema: "app");

            migrationBuilder.DropTable(
                name: "DataProtectionKeys",
                schema: "app");

            migrationBuilder.DropTable(
                name: "LinkedPlayers",
                schema: "app");

            migrationBuilder.DropTable(
                name: "MemberDailySnapshots",
                schema: "app");

            migrationBuilder.DropTable(
                name: "MemberNotes",
                schema: "app");

            migrationBuilder.DropTable(
                name: "Notifications",
                schema: "app");

            migrationBuilder.DropTable(
                name: "RecruitmentApplications",
                schema: "app");

            migrationBuilder.DropTable(
                name: "RiverRaceParticipants",
                schema: "app");

            migrationBuilder.DropTable(
                name: "RoleClaims",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "SeasonStats",
                schema: "app");

            migrationBuilder.DropTable(
                name: "SyncTarget",
                schema: "sync");

            migrationBuilder.DropTable(
                name: "TrackedClans",
                schema: "app");

            migrationBuilder.DropTable(
                name: "UserClaims",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "UserLogins",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "UserRoles",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "UserTokens",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "WarAttacks",
                schema: "app");

            migrationBuilder.DropTable(
                name: "WarParticipants",
                schema: "app");

            migrationBuilder.DropTable(
                name: "WorkspaceInvites",
                schema: "app");

            migrationBuilder.DropTable(
                name: "WorkspaceMembers",
                schema: "app");

            migrationBuilder.DropTable(
                name: "Players",
                schema: "app");

            migrationBuilder.DropTable(
                name: "RiverRaces",
                schema: "app");

            migrationBuilder.DropTable(
                name: "Roles",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "Users",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "Wars",
                schema: "app");

            migrationBuilder.DropTable(
                name: "Workspaces",
                schema: "app");
        }
    }
}
