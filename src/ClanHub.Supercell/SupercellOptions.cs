namespace ClanHub.Supercell;

public class GameApiOptions
{
    public string ApiKey { get; set; } = "";
    public string BaseUrl { get; set; } = "";
}

public class SupercellOptions
{
    public const string Section = "Supercell";

    public GameApiOptions ClashOfClans { get; set; } = new() { BaseUrl = "https://cocproxy.royaleapi.dev/v1" };
    public GameApiOptions ClashRoyale { get; set; } = new() { BaseUrl = "https://proxy.royaleapi.dev/v1" };
    public GameApiOptions BrawlStars { get; set; } = new() { BaseUrl = "https://bsproxy.royaleapi.dev/v1" };
}