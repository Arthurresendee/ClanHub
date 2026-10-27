namespace ClanHub.Sync;

public static class Backoff
{
    /// <summary>Espera exponencial: 1, 2, 4... minutos, limitada ao máximo configurado.</summary>
    public static TimeSpan For(int failCount, int maxMinutes)
    {
        var exp = Math.Min(Math.Max(failCount - 1, 0), 10);
        return TimeSpan.FromMinutes(Math.Min(Math.Pow(2, exp), maxMinutes));
    }
}