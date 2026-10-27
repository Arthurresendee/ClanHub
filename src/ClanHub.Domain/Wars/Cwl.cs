namespace ClanHub.Domain;

public class CwlSeason
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TrackedClanId { get; set; }
    public string Season { get; set; } = "";
    public string State { get; set; } = "";
    public string? ClansJson { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public List<CwlRound> Rounds { get; set; } = [];
    public List<CwlPlanEntry> Plan { get; set; } = [];
}

public class CwlRound
{
    public Guid CwlSeasonId { get; set; }
    public int RoundIndex { get; set; }
    public string? WarTagsJson { get; set; }
    public string? OurWarTag { get; set; }
    public Guid? WarId { get; set; }
}

/// <summary>Planejamento interno de escalação: quem joga (ou fica de reserva) em cada rodada.</summary>
public class CwlPlanEntry
{
    public Guid CwlSeasonId { get; set; }
    public int RoundIndex { get; set; }
    public string PlayerTag { get; set; } = "";
    public bool Plays { get; set; }
}

public class CapitalRaidSeason
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TrackedClanId { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public string State { get; set; } = "";
    public long TotalLoot { get; set; }
    public int AttackCount { get; set; }
    public int DistrictsDestroyed { get; set; }
    public List<CapitalRaidMember> Members { get; set; } = [];
}

public class CapitalRaidMember
{
    public Guid CapitalRaidSeasonId { get; set; }
    public string PlayerTag { get; set; } = "";
    public string Name { get; set; } = "";
    public int Attacks { get; set; }
    public int AttackLimit { get; set; }
    public int BonusAttackLimit { get; set; }
    public int Looted { get; set; }
}

public class ClanRanking
{
    public Guid TrackedClanId { get; set; }
    public DateOnly Date { get; set; }
    public string Scope { get; set; } = "";
    public string? LocationName { get; set; }
    public int? Rank { get; set; }
    public int Score { get; set; }
}