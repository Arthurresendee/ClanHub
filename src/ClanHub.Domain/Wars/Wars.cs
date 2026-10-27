namespace ClanHub.Domain;

public class War
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TrackedClanId { get; set; }
    public WarType Type { get; set; }
    public string? WarTag { get; set; }
    public string OpponentTag { get; set; } = "";
    public string OpponentName { get; set; } = "";
    public int TeamSize { get; set; }
    public int AttacksPerMember { get; set; } = 2;
    public WarState State { get; set; }
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public string? Result { get; set; }
    public int Stars { get; set; }
    public double Destruction { get; set; }
    public int OpponentStars { get; set; }
    public double OpponentDestruction { get; set; }
    public List<WarParticipant> Participants { get; set; } = [];
    public List<WarAttack> Attacks { get; set; } = [];
}

public class WarParticipant
{
    public Guid WarId { get; set; }
    public string PlayerTag { get; set; } = "";
    public string Name { get; set; } = "";
    public int MapPosition { get; set; }
    public int TownHall { get; set; }
    public int AttacksUsed { get; set; }
}

public class WarAttack
{
    public Guid WarId { get; set; }
    public string AttackerTag { get; set; } = "";
    public string DefenderTag { get; set; } = "";
    public int Stars { get; set; }
    public double Destruction { get; set; }
    public int Order { get; set; }
    public int Duration { get; set; }
}

public class RiverRace
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TrackedClanId { get; set; }
    public int SeasonId { get; set; }
    public int SectionIndex { get; set; }
    public int Fame { get; set; }
    public int Rank { get; set; }
    public bool IsCurrent { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public List<RiverRaceParticipant> Participants { get; set; } = [];
}

public class RiverRaceParticipant
{
    public Guid RiverRaceId { get; set; }
    public string PlayerTag { get; set; } = "";
    public string Name { get; set; } = "";
    public int Fame { get; set; }
    public int DecksUsed { get; set; }
    public int DecksUsedToday { get; set; }
}