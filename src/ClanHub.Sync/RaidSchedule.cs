namespace ClanHub.Sync;

/// <summary>Fim de semana de ataque à Capital (CoC): de sexta 07:00 UTC a segunda 07:00 UTC.</summary>
public static class RaidSchedule
{
    public static bool IsRaidWeekend(DateTime utc) => utc.DayOfWeek switch
    {
        DayOfWeek.Friday => utc.Hour >= 7,
        DayOfWeek.Saturday or DayOfWeek.Sunday => true,
        DayOfWeek.Monday => utc.Hour < 7,
        _ => false,
    };

    public static DateTime NextStart(DateTime utc)
    {
        var daysUntilFriday = ((int)DayOfWeek.Friday - (int)utc.DayOfWeek + 7) % 7;
        var start = utc.Date.AddDays(daysUntilFriday).AddHours(7);
        return start <= utc ? start.AddDays(7) : start;
    }
}