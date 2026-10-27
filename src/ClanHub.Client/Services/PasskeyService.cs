using System.Net.Http.Json;
using Microsoft.JSInterop;

namespace ClanHub.Client.Services;

/// <summary>Orquestra o fluxo WebAuthn entre o navegador (JS) e a API.</summary>
public class PasskeyService(ApiClient api, IJSRuntime js)
{
    public async Task<bool> IsSupportedAsync()
    {
        try { return await js.InvokeAsync<bool>("clanhubPasskeys.supported"); } catch (JSException) { return false; }
    }

    private static string? Unquote(string? raw) => raw;

    /// <summary>Cria uma passkey para o usuário logado.</summary>
    public async Task<string?> RegisterAsync(string name)
    {
        var options = await api.Send<System.Text.Json.JsonElement>(HttpMethod.Post, "/api/auth/passkey/register/options", new { });
        if (!options.Ok) return options.Error ?? "Não foi possível iniciar o cadastro da passkey.";
        string credential;
        try { credential = await js.InvokeAsync<string>("clanhubPasskeys.create", options.Value.GetRawText()); }
        catch (JSException) { return "O cadastro da passkey foi cancelado."; }
        var r = await api.PostOk("/api/auth/passkey/register", new { credentialJson = credential, name });
        return r.Ok ? null : (r.Error ?? "A passkey foi recusada.");
    }

    /// <summary>Entra com uma passkey (sem digitar e-mail ou senha).</summary>
    public async Task<string?> LoginAsync()
    {
        var options = await api.Send<System.Text.Json.JsonElement>(HttpMethod.Post, "/api/auth/passkey/login/options", new { });
        if (!options.Ok) return options.Error ?? "Não foi possível iniciar o login com passkey.";
        string credential;
        try { credential = await js.InvokeAsync<string>("clanhubPasskeys.get", options.Value.GetRawText()); }
        catch (JSException) { return "O login com passkey foi cancelado."; }
        var r = await api.PostOk("/api/auth/passkey/login", new { credentialJson = credential });
        return r.Ok ? null : "Passkey não reconhecida.";
    }
}