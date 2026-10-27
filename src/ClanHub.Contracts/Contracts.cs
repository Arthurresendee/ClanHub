namespace ClanHub.Contracts;

public static class GameIds
{
    public const string ClashOfClans = "ClashOfClans";
    public const string ClashRoyale = "ClashRoyale";
    public const string BrawlStars = "BrawlStars";

    public static string Label(string game) => game switch
    {
        ClashOfClans => "Clash of Clans",
        ClashRoyale => "Clash Royale",
        BrawlStars => "Brawl Stars",
        _ => game,
    };
}

public record Page<T>(IReadOnlyList<T> Items, string? NextCursor);

// ---- Conta ----
public record RegisterRequest(string Email, string Password, string DisplayName, bool AcceptTerms);

/// <summary>RequiresEmailConfirmation=false quando a plataforma não exige confirmação (a conta já pode entrar).</summary>
public record SignupResponse(bool RequiresEmailConfirmation);

public record LinkedPlayerDto(Guid Id, string Game, string PlayerTag, DateTime VerifiedAt);

public record WorkspaceSummaryDto(Guid Id, string Name, string Slug, string Role, IReadOnlyList<ClanSummaryDto> Clans);

public record MeResponse(
    Guid UserId, string Email, string DisplayName, bool IsPlatformAdmin,
    IReadOnlyList<WorkspaceSummaryDto> Workspaces, IReadOnlyList<LinkedPlayerDto> LinkedPlayers);

public record VerifyPlayerRequest(string Game, string PlayerTag, string Token);

public record NotificationDto(Guid Id, string Title, string? Body, string? Link, DateTime CreatedAt, bool Read);

// ---- Jogos ----
public record GameFeaturesDto(
    bool ClanSearch, bool PlayerTokenVerification, bool Wars, bool Cwl, bool CapitalRaids, bool RiverRace,
    bool BattleLog = false, bool Rankings = false, bool Tournaments = false, bool Calendar = false, bool UpcomingChests = false);

public record GameInfoDto(string Id, string Name, GameFeaturesDto Features);

public record ClanSearchItemDto(string Tag, string Name, string? BadgeUrl, int Members, int? Level);

public record ClanPreviewDto(string Game, string Tag, string Name, string? Description, string? BadgeUrl, int Members);

public record PlayerPreviewDto(string Game, string Tag, string Name, int ExpLevel, int Trophies, string? ClanTag, int? TownHall);

// ---- Workspaces ----
public record CreateWorkspaceRequest(string Name);
public record RenameWorkspaceRequest(string Name);

public record TeamMemberDto(Guid UserId, string DisplayName, string Email, string Role, DateTime JoinedAt);
public record UpdateMemberRoleRequest(string Role);

public record InviteDto(Guid Id, string? Email, string Role, DateTime ExpiresAt, DateTime? UsedAt);
public record CreateInviteRequest(string? Email, string Role);
public record CreateInviteResponse(Guid InviteId, string Token, string Url, DateTime ExpiresAt);
public record AcceptInviteResponse(Guid WorkspaceId, string WorkspaceName);

public record StartClaimRequest(string Game, string ClanTag, string Method);
public record StartClaimResponse(Guid ClaimId, string Method, string? Code, DateTime ExpiresAt, string Instructions);
public record VerifyClaimRequest(string? PlayerTag, string? PlayerToken);
public record VerifyClaimResponse(bool Verified, Guid? ClanId, string Message);

public record AuditEntryDto(long Id, Guid? UserId, string Action, string? Data, DateTime At);

// ---- Clãs ----
public record ClanSummaryDto(
    Guid Id, Guid WorkspaceId, string Game, string Tag, string Name, string? BadgeUrl, string Status,
    int MemberCount, DateTime? LastSyncAt);

public record ClanOverviewDto(
    ClanSummaryDto Clan, GameFeaturesDto Features, int TotalDonations, int TotalTrophies,
    WarBriefDto? CurrentWar, RiverRaceBriefDto? CurrentRiverRace, long LastEventId, IReadOnlyList<EventDto> RecentEvents);

public record WarBriefDto(string State, string OpponentName, int TeamSize, int Stars, int OpponentStars, DateTime? EndTime, int PendingAttackers);
public record RiverRaceBriefDto(int Fame, int Rank, int ParticipantsPendingToday);

public record MemberDto(
    string Tag, string Name, string Role, int ExpLevel, int Trophies, int Donations, int DonationsReceived,
    int? TownHall, int ClanRank, bool IsCurrent, DateTime JoinedAt, DateTime? LeftAt, DateTime? LastActivityAt, int Strikes);

public record DailyPointDto(DateOnly Date, int Trophies, int Donations, int Received, int ExpLevel, int? TownHall);

public record MemberDetailDto(
    MemberDto Member, string? ProfileJson, IReadOnlyList<DailyPointDto> History, IReadOnlyList<WarSummaryDto> Wars);

public record EventDto(long Id, string Type, string? PlayerTag, string? PayloadJson, DateTime OccurredAt);
public record EventsResponse(IReadOnlyList<EventDto> Items, long LastId, string? NextCursor);

public record SyncStatusItemDto(string Kind, DateTime NextRunAt, DateTime? LastRunAt, DateTime? LastSuccessAt, int FailCount, string? LastError);
public record SyncStatusDto(string ClanStatus, IReadOnlyList<SyncStatusItemDto> Targets);

/// <summary>Campos nulos não são alterados.</summary>
public record UpdateClanRequest(
    string? CountryCode = null, int? StrikeExpiryDays = null, int? StrikesToWarn = null,
    bool? IsPublic = null, string? PublicSlug = null, string? RecruitmentText = null);

public record ClanSettingsDto(
    string? CountryCode, int StrikeExpiryDays, int StrikesToWarn, bool IsPublic, string? PublicSlug, string? RecruitmentText);

// ---- Guerras ----
public record WarParticipantDto(string Tag, string Name, int MapPosition, int TownHall, int AttacksUsed, int AttacksAllowed);
public record WarAttackDto(string AttackerTag, string DefenderTag, int Stars, double Destruction, int Order);

public record WarSummaryDto(
    Guid Id, string State, string? Result, string OpponentName, int TeamSize, int Stars, double Destruction,
    int OpponentStars, double OpponentDestruction, DateTime? EndTime);

public record WarDetailDto(
    WarSummaryDto Summary, DateTime? StartTime, IReadOnlyList<WarParticipantDto> Participants, IReadOnlyList<WarAttackDto> Attacks);

public record WarStatsRowDto(string Tag, string Name, int Wars, int AttacksUsed, int AttacksMissed, int TotalStars, double AverageDestruction);

public record RiverRaceParticipantDto(string Tag, string Name, int Fame, int DecksUsed, int DecksUsedToday);
public record RiverRaceDto(
    Guid Id, int SeasonId, int SectionIndex, int Fame, int Rank, bool IsCurrent, DateTime UpdatedAt,
    IReadOnlyList<RiverRaceParticipantDto> Participants);
public record RiverRaceStatsRowDto(string Tag, string Name, int Weeks, double AverageFame, int TotalDecks);

// ---- Admin ----
public record AdminSyncDto(
    int PendingTargets, int FailingTargets, int LeasedTargets, DateTime? OldestDue,
    IReadOnlyList<AdminSyncGameDto> Games, IReadOnlyList<AdminSyncFailureDto> Failures);
public record AdminSyncGameDto(string Game, int Clans, int Failing);
public record AdminSyncFailureDto(Guid ClanId, string ClanName, string Game, string Kind, int FailCount, string? LastError, DateTime NextRunAt);
public record SeasonStatRowDto(string Tag, string Name, int Donations, int Received, int WarStars, int AttacksUsed, int AttacksMissed);

public record NoteDto(Guid Id, string PlayerTag, Guid AuthorUserId, string AuthorName, string Kind, string Text, int Points, DateTime? ExpiresAt, DateTime CreatedAt);
public record CreateNoteRequest(string Kind, string Text, int Points, DateTime? ExpiresAt);
public record UpdateNoteRequest(string? Text, int? Points, DateTime? ExpiresAt);

public record RefreshResponse(int Queued, int RetryAfterSeconds);

public record AdminOverviewDto(int Users, int Workspaces, int Clans, IReadOnlyList<AdminSyncGameDto> Games);
public record AdminUserDto(Guid Id, string Email, string DisplayName, bool EmailConfirmed, bool LockedOut, DateTime CreatedAt);
public record LockUserRequest(bool Lock);
// ---- Fase 2: CWL ----
public record CwlClanDto(string Tag, string Name);
public record CwlRoundDto(int Index, string State, WarSummaryDto? War);
public record CwlSeasonDto(Guid Id, string Season, string State, IReadOnlyList<CwlClanDto> Clans, IReadOnlyList<CwlRoundDto> Rounds);
public record CwlMemberStatDto(string Tag, string Name, int TownHall, int RoundsPlayed, int Attacks, int Stars, double AverageDestruction);
public record CwlCurrentDto(CwlSeasonDto? Season, IReadOnlyList<CwlMemberStatDto> Members);

public record CwlPlanRowDto(string Tag, string Name, int TownHall, IReadOnlyList<bool?> Rounds);
public record CwlPlanDto(int RoundCount, IReadOnlyList<CwlPlanRowDto> Rows);
public record CwlPlanCellDto(int Round, string Tag, bool? Plays);
public record UpdateCwlPlanRequest(IReadOnlyList<CwlPlanCellDto> Cells);

public record CwlMedalRowDto(string Tag, string Name, int RoundsPlayed, int Attacks, int Stars, double AverageDestruction,
    double Score, double SharePercent, int SuggestedMedals);
public record CwlMedalsDto(int BonusPool, IReadOnlyList<CwlMedalRowDto> Rows);

// ---- Fase 2: Capital ----
public record CapitalRaidMemberDto(string Tag, string Name, int Attacks, int AttackLimit, int BonusAttackLimit, int Looted);
public record CapitalRaidDto(Guid Id, DateTime StartTime, DateTime EndTime, string State, long TotalLoot, int AttackCount,
    int DistrictsDestroyed, IReadOnlyList<CapitalRaidMemberDto> Members);

// ---- Fase 2: Recrutamento ----
public record PublicClanDto(string Game, string Tag, string Name, string? BadgeUrl, int MemberCount, string? RecruitmentText, string Slug);
public record ApplyRequest(string PlayerTag, string? Message, string? Contact);
public record ApplicationDto(Guid Id, string PlayerTag, string? PlayerName, string? Message, string? Contact, string Status,
    string? PlayerSnapshotJson, DateTime CreatedAt, string? ReviewComment, DateTime? ReviewedAt);
public record UpdateApplicationRequest(string Status, string? Comment);

// ---- Fase 2: Alertas ----
public record AlertDto(Guid Id, string Type, string Channel, int? Days, int? HoursBeforeEnd, int? HourUtc,
    string? WebhookHint, bool Enabled, DateTime? LastFiredAt);
public record CreateAlertRequest(string Type, string Channel, int? Days, int? HoursBeforeEnd, int? HourUtc, string? WebhookUrl, bool Enabled = true);
public record UpdateAlertRequest(bool? Enabled, int? Days, int? HoursBeforeEnd, int? HourUtc, string? WebhookUrl);

public record UnreadCountDto(int Count);
public record SeasonListDto(IReadOnlyList<string> Seasons);
public record ProviderDto(string Id, string Name);

// ---- Fase 3 ----
public record ClanRankingDto(DateOnly Date, string Scope, string? LocationName, int? Rank, int Score);
public record BattleCardDto(string Name, int Level, string? IconUrl);
public record BattleDto(DateTime? Time, string Mode, string Result, int? TrophyChange, string? Detail, IReadOnlyList<BattleCardDto> Deck);
public record ChestDto(int Index, string Name);
public record TournamentDto(string Tag, string Name, string Status, string? Type, int Players, int Capacity, string? Description,
    DateTime? StartedTime, DateTime? EndedTime);
public record CalendarEntryDto(string Kind, string Title, DateTime Start, DateTime End);

// ---- Passkeys ----
public record PasskeyDto(string Id, string Name, DateTime CreatedAt);