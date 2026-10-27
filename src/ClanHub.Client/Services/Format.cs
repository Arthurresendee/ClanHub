using System.Text.Json;
using ClanHub.Contracts;

namespace ClanHub.Client.Services;

public static class Format
{
    public static string Dt(DateTime? utc) =>
        utc is null ? "—" : DateTime.SpecifyKind(utc.Value, DateTimeKind.Utc).ToLocalTime().ToString("dd/MM/yyyy HH:mm");

    public static string Ago(DateTime? utc)
    {
        if (utc is null) return "nunca";
        var span = DateTime.UtcNow - DateTime.SpecifyKind(utc.Value, DateTimeKind.Utc);
        if (span.TotalSeconds < 60) return "agora há pouco";
        if (span.TotalMinutes < 60) return $"há {(int)span.TotalMinutes} min";
        if (span.TotalHours < 48) return $"há {(int)span.TotalHours} h";
        return $"há {(int)span.TotalDays} dias";
    }

    public static string Role(string role) => role.ToLowerInvariant() switch
    {
        "leader" or "president" => "Líder",
        "coleader" or "vicepresident" => "Co-líder",
        "admin" or "elder" or "senior" => "Ancião",
        "member" => "Membro",
        "owner" => "Dono",
        "staff" => "Staff",
        "viewer" => "Leitor",
        _ => role,
    };

    public static string WarState(string state) => state switch
    {
        "Preparation" => "Preparação",
        "InWar" => "Em guerra",
        "WarEnded" => "Encerrada",
        _ => "Sem guerra",
    };

    public static string WarResult(string? r) => r?.ToLowerInvariant() switch
    {
        "win" => "Vitória", "lose" => "Derrota", "tie" => "Empate", _ => "—",
    };

    public static string ClanStatus(string s) => s switch
    {
        "NotFound" => "Clã não encontrado no jogo",
        "PrivateWarLog" => "Log de guerra privado",
        _ => "Ativo",
    };

    public static string Describe(EventDto e)
    {
        JsonElement p = default;
        var has = false;
        if (!string.IsNullOrEmpty(e.PayloadJson))
        {
            try { p = JsonDocument.Parse(e.PayloadJson).RootElement; has = true; } catch { }
        }
        string S(string k) => has && p.TryGetProperty(k, out var v) ? v.ToString() : "";
        var who = string.IsNullOrEmpty(S("name")) ? e.PlayerTag ?? "Alguém" : S("name");

        return e.Type switch
        {
            "MemberJoined" => $"{who} entrou no clã",
            "MemberLeft" => $"{who} saiu do clã",
            "RoleChanged" => $"{e.PlayerTag} mudou de cargo: {Role(S("from"))} → {Role(S("to"))}",
            "NameChanged" => $"{S("from")} agora se chama {S("to")}",
            "TownHallUpgraded" => $"{e.PlayerTag} subiu o Centro de Vila: {S("from")} → {S("to")}",
            "DonationsChanged" => $"{e.PlayerTag} doou tropas ({S("from")} → {S("to")})",
            "WarStarted" => $"Guerra contra {S("opponentName")} começou",
            "WarAttackMade" => $"{e.PlayerTag} atacou: {S("stars")} estrela(s), {S("destruction")}%",
            "WarEnded" => $"Guerra contra {S("opponentName")} terminou ({WarResult(S("result"))})",
            "CwlRoundStarted" => $"Rodada {S("round")} da CWL começou contra {S("opponentName")}",
            "RaidAttackMade" => $"{who} atacou a Capital ({S("attacks")}/{S("limit")}), saque de {S("looted")}",
            "RiverRaceDecksUsed" => $"{e.PlayerTag} usou {S("decks")} deck(s) hoje",
            "RiverRaceEnded" => "A semana da Guerra de Clãs terminou",
            "ClanSettingsChanged" => $"O clã foi renomeado: {S("from")} → {S("to")}",
            _ => e.Type,
        };
    }

    public static string EventIcon(string type) => type switch
    {
        "MemberJoined" => MudBlazor.Icons.Material.Filled.PersonAdd,
        "MemberLeft" => MudBlazor.Icons.Material.Filled.PersonRemove,
        "RoleChanged" => MudBlazor.Icons.Material.Filled.Badge,
        "WarStarted" or "WarAttackMade" or "WarEnded" => MudBlazor.Icons.Material.Filled.Shield,
        "DonationsChanged" => MudBlazor.Icons.Material.Filled.VolunteerActivism,
        "RiverRaceDecksUsed" or "RiverRaceEnded" => MudBlazor.Icons.Material.Filled.Sailing,
        "CwlRoundStarted" => MudBlazor.Icons.Material.Filled.EmojiEvents,
        "RaidAttackMade" => MudBlazor.Icons.Material.Filled.LocationCity,
        _ => MudBlazor.Icons.Material.Filled.Info,
    };
}