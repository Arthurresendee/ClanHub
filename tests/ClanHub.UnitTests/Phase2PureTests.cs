using ClanHub.Application.Alerts;
using ClanHub.Application.Clans;
using ClanHub.Application.Wars;
using ClanHub.Sync;

namespace ClanHub.UnitTests;

public class CwlMedalsTests
{
    private static CwlPlayerStats P(string tag, int rounds, int attacks, int stars, double destr) => new(tag, tag, rounds, attacks, stars, destr);

    [Fact]
    public void DistributesTheWholePool_NoMedalLost()
    {
        var players = new[] { P("A", 7, 7, 18, 90), P("B", 7, 7, 14, 80), P("C", 4, 4, 6, 60), P("D", 1, 1, 1, 30) };
        foreach (var pool in new[] { 0, 1, 7, 45, 100, 333 })
            Assert.Equal(pool, CwlMedals.Suggest(players, pool).Sum(s => s.SuggestedMedals));
    }

    [Fact]
    public void BetterPerformance_GetsMore_AndOrderIsByScore()
    {
        var result = CwlMedals.Suggest([P("A", 7, 7, 18, 90), P("B", 7, 7, 10, 70), P("C", 3, 3, 3, 50)], 100);
        Assert.Equal(["A", "B", "C"], result.Select(r => r.Tag));
        Assert.True(result[0].SuggestedMedals > result[1].SuggestedMedals);
        Assert.True(result[1].SuggestedMedals > result[2].SuggestedMedals);
        Assert.Equal(100, Math.Round(result.Sum(r => r.SharePercent)), 0);
    }

    [Fact]
    public void PlayersWhoNeverAttacked_GetNothing_AndAreOmitted()
    {
        var result = CwlMedals.Suggest([P("A", 5, 5, 10, 80), P("B", 5, 0, 0, 0)], 50);
        Assert.Equal("A", Assert.Single(result).Tag);
        Assert.Equal(50, result[0].SuggestedMedals);
    }

    [Fact]
    public void EmptyInput_OrNegativePool_AreSafe()
    {
        Assert.Empty(CwlMedals.Suggest([], 50));
        Assert.All(CwlMedals.Suggest([P("A", 1, 1, 1, 1)], -10), r => Assert.Equal(0, r.SuggestedMedals));
    }
}

public class AlertConfigTests
{
    [Fact]
    public void RoundTrips_AndToleratesGarbage()
    {
        var cfg = new AlertConfig(Days: 3, HoursBeforeEnd: 2, HourUtc: 9, WebhookUrl: "https://discord.com/api/webhooks/1/a");
        Assert.Equal(cfg, AlertConfig.Parse(cfg.ToJson()));
        Assert.Equal(new AlertConfig(), AlertConfig.Parse("{not json"));
        Assert.Equal(new AlertConfig(), AlertConfig.Parse(null));
    }

    [Theory]
    [InlineData("https://discord.com/api/webhooks/1/a", true)]
    [InlineData("https://discordapp.com/api/webhooks/1/a", true)]
    [InlineData("https://canary.discord.com/api/webhooks/1/a", true)]
    [InlineData("https://ptb.discord.com/api/webhooks/1/a", true)]
    [InlineData("http://discord.com/api/webhooks/1/a", false)]
    [InlineData("https://discord.com:8443/api/webhooks/1/a", false)]
    [InlineData("https://localhost/api/webhooks/1/a", false)]
    [InlineData("https://169.254.169.254/api/webhooks/1/a", false)]
    [InlineData("not a url", false)]
    [InlineData(null, false)]
    public void WebhookValidation_BlocksAnythingButDiscord(string? url, bool valid) => Assert.Equal(valid, AlertWebhook.IsValid(url));

    [Fact]
    public void AllTypes_AreListed() =>
        Assert.Equal(5, AlertTypes.All.Count);
}

public class ClanSettingsTests
{
    [Fact]
    public void Defaults_WhenMissingOrInvalid()
    {
        Assert.Equal(new ClanSettings(), ClanSettings.Parse(null));
        Assert.Equal(new ClanSettings(), ClanSettings.Parse("lixo"));
        Assert.Equal(30, ClanSettings.Parse("{}").StrikeExpiryDays);
    }

    [Fact]
    public void RoundTrip()
    {
        var s = new ClanSettings("br", 15, 5);
        Assert.Equal(s, ClanSettings.Parse(s.ToJson()));
    }
}

public class RaidScheduleTests
{
    [Theory]
    [InlineData(2026, 1, 2, 6, false)]    // sexta antes das 07:00
    [InlineData(2026, 1, 2, 7, true)]     // sexta 07:00
    [InlineData(2026, 1, 3, 12, true)]    // sábado
    [InlineData(2026, 1, 4, 23, true)]    // domingo
    [InlineData(2026, 1, 5, 6, true)]     // segunda antes das 07:00
    [InlineData(2026, 1, 5, 7, false)]    // segunda 07:00
    [InlineData(2026, 1, 7, 12, false)]   // quarta
    public void IsRaidWeekend(int y, int m, int d, int h, bool expected) =>
        Assert.Equal(expected, RaidSchedule.IsRaidWeekend(new DateTime(y, m, d, h, 0, 0, DateTimeKind.Utc)));

    [Fact]
    public void NextStart_IsTheComingFriday_AtSeven()
    {
        Assert.Equal(new DateTime(2026, 1, 9, 7, 0, 0, DateTimeKind.Utc), RaidSchedule.NextStart(new DateTime(2026, 1, 5, 12, 0, 0, DateTimeKind.Utc)));
        // dentro do fim de semana => o da semana seguinte
        Assert.Equal(new DateTime(2026, 1, 9, 7, 0, 0, DateTimeKind.Utc), RaidSchedule.NextStart(new DateTime(2026, 1, 3, 12, 0, 0, DateTimeKind.Utc)));
        Assert.Equal(new DateTime(2026, 1, 16, 7, 0, 0, DateTimeKind.Utc), RaidSchedule.NextStart(new DateTime(2026, 1, 9, 7, 0, 0, DateTimeKind.Utc)));
    }
}