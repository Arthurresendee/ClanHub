using System.Security.Claims;
using ClanHub.Contracts;
using Microsoft.AspNetCore.Components.Authorization;

namespace ClanHub.Client.Services;

/// <summary>Consulta /api/v1/me para saber quem está logado (cookie HttpOnly).</summary>
public class ApiAuthStateProvider(ApiClient api) : AuthenticationStateProvider
{
    private Task<AuthenticationState>? _state;

    public MeResponse? Me { get; private set; }

    public override Task<AuthenticationState> GetAuthenticationStateAsync() => _state ??= Load();

    /// <summary>Disparado quando os dados do usuário (workspaces, clãs, contas vinculadas) são recarregados.</summary>
    public event Action? MeChanged;

    /// <summary>
    /// Recarrega o usuário. O estado de autenticação só é notificado quando a identidade muda (login/logout):
    /// notificar sempre faria o AuthorizeRouteView recriar a página atual e apagar o que o usuário estava fazendo.
    /// </summary>
    public async Task RefreshAsync()
    {
        var before = (Me?.UserId, Me?.IsPlatformAdmin);
        var task = Load();
        await task;
        _state = task;
        if (before != (Me?.UserId, Me?.IsPlatformAdmin)) NotifyAuthenticationStateChanged(task);
        MeChanged?.Invoke();
    }

    private async Task<AuthenticationState> Load()
    {
        var r = await api.Get<MeResponse>("/api/v1/me");
        Me = r.Ok ? r.Value : null;
        if (Me is null) return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, Me.UserId.ToString()),
            new(ClaimTypes.Name, Me.DisplayName),
            new(ClaimTypes.Email, Me.Email),
        };
        if (Me.IsPlatformAdmin) claims.Add(new Claim(ClaimTypes.Role, "Admin"));
        return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(claims, "cookie")));
    }
}