namespace ClanHub.Domain;

public enum Game { ClashOfClans = 1, ClashRoyale = 2, BrawlStars = 3 }

public enum WorkspaceRole { Viewer = 0, Staff = 1, Admin = 2, Owner = 3 }

public enum ClaimMethod { PlayerToken = 1, DescriptionCode = 2 }

public enum ClaimStatus { Pending = 0, Verified = 1, Expired = 2, Failed = 3 }

public enum TrackedClanStatus { Active = 0, NotFound = 1, PrivateWarLog = 2 }

public enum SyncKind
{
    Members = 1,
    CurrentWar = 2,
    WarLog = 3,
    RiverRace = 4,
    RiverRaceLog = 5,
    PlayerProfiles = 6,
    CwlGroup = 7,
    CapitalRaids = 8,
    Alerts = 9,
    Rankings = 10,
}

public enum ClanEventType
{
    MemberJoined = 1, MemberLeft, RoleChanged, NameChanged, TownHallUpgraded, DonationsChanged,
    WarStarted, WarAttackMade, WarEnded, CwlRoundStarted, RaidAttackMade,
    RiverRaceDecksUsed, RiverRaceEnded, ClanSettingsChanged,
}

public enum NoteKind { Note = 0, Warning = 1, Strike = 2 }

public enum WarType { Normal = 0, Cwl = 1 }

public enum WarState { NotInWar = 0, Preparation = 1, InWar = 2, WarEnded = 3 }

public enum ApplicationStatus { Pending = 0, Approved = 1, Rejected = 2 }

public enum AlertChannel { Panel = 0, DiscordWebhook = 1 }