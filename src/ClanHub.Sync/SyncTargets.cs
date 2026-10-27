using ClanHub.Domain;

namespace ClanHub.Sync;

public static class SyncTargets
{
    public static IReadOnlyList<SyncKind> KindsFor(Game game) => game switch
    {
        Game.ClashOfClans =>
        [
            SyncKind.Members, SyncKind.CurrentWar, SyncKind.WarLog, SyncKind.PlayerProfiles,
            SyncKind.CwlGroup, SyncKind.CapitalRaids, SyncKind.Alerts, SyncKind.Rankings,
        ],
        Game.ClashRoyale =>
        [
            SyncKind.Members, SyncKind.RiverRace, SyncKind.RiverRaceLog, SyncKind.PlayerProfiles,
            SyncKind.Alerts, SyncKind.Rankings,
        ],
        _ => [SyncKind.Members, SyncKind.PlayerProfiles, SyncKind.Alerts, SyncKind.Rankings],
    };

    public static IEnumerable<SyncTarget> CreateFor(TrackedClan clan, DateTime now) =>
        KindsFor(clan.Game).Select(k => new SyncTarget { TrackedClanId = clan.Id, Kind = k, NextRunAt = now });
}