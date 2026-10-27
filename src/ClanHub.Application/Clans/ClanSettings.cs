using System.Text.Json;

namespace ClanHub.Application.Clans;

/// <summary>Configurações do clã guardadas em TrackedClan.SettingsJson.</summary>
public record ClanSettings(
    string? CountryCode = null,
    int StrikeExpiryDays = 30,
    int StrikesToWarn = 3)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static ClanSettings Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new ClanSettings();
        try { return JsonSerializer.Deserialize<ClanSettings>(json, Json) ?? new ClanSettings(); }
        catch (JsonException) { return new ClanSettings(); }
    }

    public string ToJson() => JsonSerializer.Serialize(this, Json);
}