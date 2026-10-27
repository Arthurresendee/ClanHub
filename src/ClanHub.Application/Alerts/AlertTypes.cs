using System.Text.Json;

namespace ClanHub.Application.Alerts;

public static class AlertTypes
{
    public const string MemberLeft = "MemberLeft";
    public const string MemberJoined = "MemberJoined";
    public const string PendingWarAttacks = "PendingWarAttacks";
    public const string MemberInactive = "MemberInactive";
    public const string RiverRaceDecksPending = "RiverRaceDecksPending";

    public static readonly IReadOnlyList<string> All =
        [MemberLeft, MemberJoined, PendingWarAttacks, MemberInactive, RiverRaceDecksPending];
}

/// <summary>Parâmetros de uma regra (AlertRule.ConfigJson).</summary>
public record AlertConfig(int? Days = null, int? HoursBeforeEnd = null, int? HourUtc = null, string? WebhookUrl = null)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static AlertConfig Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new AlertConfig();
        try { return JsonSerializer.Deserialize<AlertConfig>(json, Json) ?? new AlertConfig(); }
        catch (JsonException) { return new AlertConfig(); }
    }

    public string ToJson() => JsonSerializer.Serialize(this, Json);
}

public static class AlertWebhook
{
    /// <summary>Só aceitamos webhooks do Discord (evita usar o servidor para chamar endereços arbitrários — SSRF).</summary>
    public static bool IsValid(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo)) return false;
        var host = uri.Host.ToLowerInvariant();
        var okHost = host is "discord.com" or "discordapp.com" or "ptb.discord.com" or "canary.discord.com";
        return okHost && uri.AbsolutePath.StartsWith("/api/webhooks/", StringComparison.Ordinal);
    }
}