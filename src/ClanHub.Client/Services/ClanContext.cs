using ClanHub.Contracts;

namespace ClanHub.Client.Services;

/// <summary>Estado compartilhado das telas de um clã (carregado pelo ClanLayout).</summary>
public class ClanContext
{
    public Guid Ws { get; set; }
    public Guid ClanId { get; set; }
    public ClanOverviewDto? Overview { get; set; }
    public string? Error { get; set; }
    public Func<Task> Reload { get; set; } = () => Task.CompletedTask;

    public string Game => Overview?.Clan.Game ?? "";

    /// <summary>Papel do usuário no workspace do clã (Viewer, Staff, Admin, Owner).</summary>
    public string Role { get; set; } = "Viewer";
    public bool IsStaff => Role is "Staff" or "Admin" or "Owner";
    public bool IsAdmin => Role is "Admin" or "Owner";
}