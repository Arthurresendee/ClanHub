using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClanHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase2And3 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPublic",
                schema: "app",
                table: "TrackedClans",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PublicSlug",
                schema: "app",
                table: "TrackedClans",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecruitmentText",
                schema: "app",
                table: "TrackedClans",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Message",
                schema: "app",
                table: "RecruitmentApplications",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactInfo",
                schema: "app",
                table: "RecruitmentApplications",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewComment",
                schema: "app",
                table: "RecruitmentApplications",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReviewedAt",
                schema: "app",
                table: "RecruitmentApplications",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "LastEventId",
                schema: "app",
                table: "AlertRules",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastFiredAt",
                schema: "app",
                table: "AlertRules",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastFiredKey",
                schema: "app",
                table: "AlertRules",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CapitalRaidSeasons",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TrackedClanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    State = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    TotalLoot = table.Column<long>(type: "bigint", nullable: false),
                    AttackCount = table.Column<int>(type: "int", nullable: false),
                    DistrictsDestroyed = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CapitalRaidSeasons", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ClanRankings",
                schema: "app",
                columns: table => new
                {
                    TrackedClanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Scope = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    LocationName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Rank = table.Column<int>(type: "int", nullable: true),
                    Score = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClanRankings", x => new { x.TrackedClanId, x.Date, x.Scope });
                });

            migrationBuilder.CreateTable(
                name: "CwlSeasons",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TrackedClanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Season = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    State = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ClansJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CwlSeasons", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CapitalRaidMembers",
                schema: "app",
                columns: table => new
                {
                    CapitalRaidSeasonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlayerTag = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Attacks = table.Column<int>(type: "int", nullable: false),
                    AttackLimit = table.Column<int>(type: "int", nullable: false),
                    BonusAttackLimit = table.Column<int>(type: "int", nullable: false),
                    Looted = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CapitalRaidMembers", x => new { x.CapitalRaidSeasonId, x.PlayerTag });
                    table.ForeignKey(
                        name: "FK_CapitalRaidMembers_CapitalRaidSeasons_CapitalRaidSeasonId",
                        column: x => x.CapitalRaidSeasonId,
                        principalSchema: "app",
                        principalTable: "CapitalRaidSeasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CwlPlan",
                schema: "app",
                columns: table => new
                {
                    CwlSeasonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoundIndex = table.Column<int>(type: "int", nullable: false),
                    PlayerTag = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Plays = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CwlPlan", x => new { x.CwlSeasonId, x.RoundIndex, x.PlayerTag });
                    table.ForeignKey(
                        name: "FK_CwlPlan_CwlSeasons_CwlSeasonId",
                        column: x => x.CwlSeasonId,
                        principalSchema: "app",
                        principalTable: "CwlSeasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CwlRounds",
                schema: "app",
                columns: table => new
                {
                    CwlSeasonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoundIndex = table.Column<int>(type: "int", nullable: false),
                    WarTagsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OurWarTag = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    WarId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CwlRounds", x => new { x.CwlSeasonId, x.RoundIndex });
                    table.ForeignKey(
                        name: "FK_CwlRounds_CwlSeasons_CwlSeasonId",
                        column: x => x.CwlSeasonId,
                        principalSchema: "app",
                        principalTable: "CwlSeasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TrackedClans_PublicSlug",
                schema: "app",
                table: "TrackedClans",
                column: "PublicSlug",
                unique: true,
                filter: "[PublicSlug] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RecruitmentApplications_TrackedClanId_Status",
                schema: "app",
                table: "RecruitmentApplications",
                columns: new[] { "TrackedClanId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AlertRules_TrackedClanId",
                schema: "app",
                table: "AlertRules",
                column: "TrackedClanId");

            migrationBuilder.CreateIndex(
                name: "IX_CapitalRaidSeasons_TrackedClanId_StartTime",
                schema: "app",
                table: "CapitalRaidSeasons",
                columns: new[] { "TrackedClanId", "StartTime" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CwlSeasons_TrackedClanId_Season",
                schema: "app",
                table: "CwlSeasons",
                columns: new[] { "TrackedClanId", "Season" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CapitalRaidMembers",
                schema: "app");

            migrationBuilder.DropTable(
                name: "ClanRankings",
                schema: "app");

            migrationBuilder.DropTable(
                name: "CwlPlan",
                schema: "app");

            migrationBuilder.DropTable(
                name: "CwlRounds",
                schema: "app");

            migrationBuilder.DropTable(
                name: "CapitalRaidSeasons",
                schema: "app");

            migrationBuilder.DropTable(
                name: "CwlSeasons",
                schema: "app");

            migrationBuilder.DropIndex(
                name: "IX_TrackedClans_PublicSlug",
                schema: "app",
                table: "TrackedClans");

            migrationBuilder.DropIndex(
                name: "IX_RecruitmentApplications_TrackedClanId_Status",
                schema: "app",
                table: "RecruitmentApplications");

            migrationBuilder.DropIndex(
                name: "IX_AlertRules_TrackedClanId",
                schema: "app",
                table: "AlertRules");

            migrationBuilder.DropColumn(
                name: "IsPublic",
                schema: "app",
                table: "TrackedClans");

            migrationBuilder.DropColumn(
                name: "PublicSlug",
                schema: "app",
                table: "TrackedClans");

            migrationBuilder.DropColumn(
                name: "RecruitmentText",
                schema: "app",
                table: "TrackedClans");

            migrationBuilder.DropColumn(
                name: "ContactInfo",
                schema: "app",
                table: "RecruitmentApplications");

            migrationBuilder.DropColumn(
                name: "ReviewComment",
                schema: "app",
                table: "RecruitmentApplications");

            migrationBuilder.DropColumn(
                name: "ReviewedAt",
                schema: "app",
                table: "RecruitmentApplications");

            migrationBuilder.DropColumn(
                name: "LastEventId",
                schema: "app",
                table: "AlertRules");

            migrationBuilder.DropColumn(
                name: "LastFiredAt",
                schema: "app",
                table: "AlertRules");

            migrationBuilder.DropColumn(
                name: "LastFiredKey",
                schema: "app",
                table: "AlertRules");

            migrationBuilder.AlterColumn<string>(
                name: "Message",
                schema: "app",
                table: "RecruitmentApplications",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(1000)",
                oldMaxLength: 1000,
                oldNullable: true);
        }
    }
}
