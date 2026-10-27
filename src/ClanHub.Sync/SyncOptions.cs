namespace ClanHub.Sync;

public class SyncOptions
{
    public const string Section = "Sync";

    public int TickBudgetSeconds { get; set; } = 40;
    public int BatchSize { get; set; } = 20;
    public int MaxParallelism { get; set; } = 4;
    public int LeaseMinutes { get; set; } = 2;

    public int MembersMinutes { get; set; } = 5;
    public int WarIdleMinutes { get; set; } = 15;
    public int WarPrepMinutes { get; set; } = 10;
    public int WarActiveMinutes { get; set; } = 2;
    public int WarLogMinutes { get; set; } = 1440;
    public int RiverRaceIdleMinutes { get; set; } = 30;
    public int RiverRaceActiveMinutes { get; set; } = 5;
    public int RiverRaceLogMinutes { get; set; } = 1440;
    public int PlayerProfileHours { get; set; } = 6;
    public int CwlActiveMinutes { get; set; } = 3;
    public int CwlIdleMinutesEarlyMonth { get; set; } = 180;
    public int CwlIdleMinutes { get; set; } = 1440;
    public int CapitalActiveMinutes { get; set; } = 15;
    public int CapitalIdleMinutes { get; set; } = 1440;
    public int AlertsMinutes { get; set; } = 15;
    public int RankingsMinutes { get; set; } = 1440;
    public int PlayersPerRun { get; set; } = 5;

    public int InactiveDays { get; set; } = 30;
    public int InactiveMultiplier { get; set; } = 6;
    public int MaxBackoffMinutes { get; set; } = 60;
    public int MaintenancePauseMinutes { get; set; } = 10;
    public int PrivateWarLogRetryMinutes { get; set; } = 60;
    public int ManualRefreshMinIntervalSeconds { get; set; } = 60;

    public int EventRetentionDays { get; set; } = 180;
    public int SnapshotRetentionDays { get; set; } = 730;
}