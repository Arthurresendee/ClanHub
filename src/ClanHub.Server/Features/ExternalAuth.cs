using System.Text;
using ClanHub.Contracts;
using ClanHub.Infrastructure.Persistence;
using ClanHub.Server.Common;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;

namespace ClanHub.Server.Features;

/// <summary>Login com Discord (opcional, ligado por configuração) e passkeys (WebAuthn).</summary>
public static class ExternalAuthEndpoints
{
    public const string DiscordScheme = "Discord";
    public record PasskeyRegisterRequest(string CredentialJson, string? Name);
    public record PasskeyLoginRequest(string CredentialJson);

    private static string SafeReturn(string? url) =>
        !string.IsNullOrEmpty(url) && url.StartsWith('/') && !url.StartsWith("//") && !url.StartsWith("/\\") ? url : "/app";

    public static void MapExternalAuth(this IEndpointRouteBuilder app, bool discordEnabled)
    {
        var auth = app.MapGroup("/api/auth").RequireRateLimiting("auth");

        auth.MapGet("/providers", () =>
            Results.Ok(discordEnabled ? new[] { new ProviderDto("discord", "Discord") } : []));

        if (discordEnabled)
        {
            auth.MapGet("/discord/login", (string? returnUrl, HttpContext http) =>
            {
                var redirect = $"/api/auth/discord/callback?returnUrl={Uri.EscapeDataString(SafeReturn(returnUrl))}";
                return Results.Challenge(new AuthenticationProperties { RedirectUri = redirect }, [DiscordScheme]);
            });

            auth.MapGet("/discord/callback", async (string? returnUrl, SignInManager<ApplicationUser> signIn, ExternalLogins external, HttpContext http) =>
            {
                var info = await signIn.GetExternalLoginInfoAsync();
                if (info is null) return Results.Redirect("/entrar?error=discord");

                var (outcome, _) = await external.SignInOrRegisterAsync(info);
                await http.SignOutAsync(IdentityConstants.ExternalScheme);
                return outcome switch
                {
                    ExternalLoginOutcome.SignedIn or ExternalLoginOutcome.Created or ExternalLoginOutcome.Linked => Results.Redirect(SafeReturn(returnUrl)),
                    ExternalLoginOutcome.NoEmail or ExternalLoginOutcome.EmailNotVerified => Results.Redirect("/entrar?error=discord-email"),
                    ExternalLoginOutcome.LockedOut => Results.Redirect("/entrar?error=locked"),
                    _ => Results.Redirect("/entrar?error=discord"),
                };
            });
        }

        // ---- Passkeys ----
        auth.MapPost("/passkey/register/options", async (HttpContext http, UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn) =>
        {
            var user = await users.GetUserAsync(http.User);
            if (user is null) return Results.Unauthorized();
            var json = await signIn.MakePasskeyCreationOptionsAsync(new PasskeyUserEntity
            {
                Id = user.Id.ToString(), Name = user.Email ?? user.UserName ?? user.Id.ToString(),
                DisplayName = string.IsNullOrWhiteSpace(user.DisplayName) ? user.Email ?? "" : user.DisplayName,
            });
            return Results.Content(json, "application/json");
        }).RequireAuthorization();

        auth.MapPost("/passkey/register", async (PasskeyRegisterRequest req, HttpContext http, UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn) =>
        {
            var user = await users.GetUserAsync(http.User);
            if (user is null) return Results.Unauthorized();
            var attestation = await signIn.PerformPasskeyAttestationAsync(req.CredentialJson);
            if (!attestation.Succeeded) return Results2.BadRequest("Não foi possível validar a passkey.", attestation.Failure?.Message);

            var passkey = attestation.Passkey;
            passkey.Name = string.IsNullOrWhiteSpace(req.Name) ? "Minha passkey" : req.Name.Trim()[..Math.Min(req.Name.Trim().Length, 60)];
            var result = await users.AddOrUpdatePasskeyAsync(user, passkey);
            if (!result.Succeeded) return Results2.BadRequest("Não foi possível salvar a passkey.");
            return Results.Ok(new PasskeyDto(WebEncoders.Base64UrlEncode(passkey.CredentialId), passkey.Name ?? "", passkey.CreatedAt.UtcDateTime));
        }).RequireAuthorization();

        auth.MapPost("/passkey/login/options", async (SignInManager<ApplicationUser> signIn) =>
            Results.Content(await signIn.MakePasskeyRequestOptionsAsync(null!), "application/json"));

        auth.MapPost("/passkey/login", async (PasskeyLoginRequest req, SignInManager<ApplicationUser> signIn) =>
        {
            signIn.AuthenticationScheme = IdentityConstants.ApplicationScheme;
            Microsoft.AspNetCore.Identity.SignInResult result;
            try
            {
                result = await signIn.PasskeySignInAsync(req.CredentialJson);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.Text.Json.JsonException or FormatException or ArgumentException)
            {
                // resposta repetida, sem desafio pendente ou JSON malformado: é "não autenticado", não erro do servidor
                return Results2.Problem(401, "Passkey inválida.");
            }
            if (result.Succeeded) return Results.NoContent();
            return result.IsLockedOut ? Results2.Problem(401, "LockedOut") : Results2.Problem(401, "Passkey inválida.");
        });

        auth.MapGet("/passkeys", async (HttpContext http, UserManager<ApplicationUser> users) =>
        {
            var user = await users.GetUserAsync(http.User);
            if (user is null) return Results.Unauthorized();
            var list = await users.GetPasskeysAsync(user);
            return Results.Ok(list.Select(p => new PasskeyDto(WebEncoders.Base64UrlEncode(p.CredentialId), p.Name ?? "Passkey", p.CreatedAt.UtcDateTime)).ToList());
        }).RequireAuthorization();

        auth.MapDelete("/passkeys/{id}", async (string id, HttpContext http, UserManager<ApplicationUser> users) =>
        {
            var user = await users.GetUserAsync(http.User);
            if (user is null) return Results.Unauthorized();
            byte[] credentialId;
            try { credentialId = WebEncoders.Base64UrlDecode(id); }
            catch (FormatException) { return Results2.BadRequest("Identificador inválido."); }
            if (await users.GetPasskeyAsync(user, credentialId) is null) return Results2.NotFound("Passkey não encontrada.");
            var result = await users.RemovePasskeyAsync(user, credentialId);
            return result.Succeeded ? Results.NoContent() : Results2.NotFound("Passkey não encontrada.");
        }).RequireAuthorization();
    }
}