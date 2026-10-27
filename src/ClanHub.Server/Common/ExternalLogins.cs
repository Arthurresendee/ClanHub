using System.Security.Claims;
using ClanHub.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;

namespace ClanHub.Server.Common;

public enum ExternalLoginOutcome { SignedIn, Created, Linked, NoEmail, EmailNotVerified, LockedOut, Failed }

/// <summary>Entrada com provedor externo (Discord): reaproveita conta existente ou cria uma nova, sempre exigindo e-mail verificado.</summary>
public class ExternalLogins(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn)
{
    public const string VerifiedClaim = "urn:discord:user:verified";

    public async Task<(ExternalLoginOutcome Outcome, ApplicationUser? User)> SignInOrRegisterAsync(ExternalLoginInfo info)
    {
        signIn.AuthenticationScheme = IdentityConstants.ApplicationScheme;

        var existing = await signIn.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, isPersistent: true, bypassTwoFactor: true);
        if (existing.Succeeded)
            return (ExternalLoginOutcome.SignedIn, await users.FindByLoginAsync(info.LoginProvider, info.ProviderKey));
        if (existing.IsLockedOut) return (ExternalLoginOutcome.LockedOut, null);

        var email = info.Principal.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrWhiteSpace(email)) return (ExternalLoginOutcome.NoEmail, null);
        // Sem e-mail verificado pelo provedor, não vinculamos nem criamos conta (evita tomada de conta).
        if (!string.Equals(info.Principal.FindFirstValue(VerifiedClaim), "true", StringComparison.OrdinalIgnoreCase))
            return (ExternalLoginOutcome.EmailNotVerified, null);

        var user = await users.FindByEmailAsync(email);
        var outcome = ExternalLoginOutcome.Linked;
        if (user is null)
        {
            var name = info.Principal.FindFirstValue(ClaimTypes.Name) ?? email.Split('@')[0];
            user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, DisplayName = name.Length > 100 ? name[..100] : name };
            var created = await users.CreateAsync(user);
            if (!created.Succeeded) return (ExternalLoginOutcome.Failed, null);
            outcome = ExternalLoginOutcome.Created;
        }
        else if (!user.EmailConfirmed)
        {
            user.EmailConfirmed = true; // o provedor confirmou que o e-mail é do mesmo dono
            await users.UpdateAsync(user);
        }

        if (!(await users.AddLoginAsync(user, info)).Succeeded) return (ExternalLoginOutcome.Failed, null);
        if (await users.IsLockedOutAsync(user)) return (ExternalLoginOutcome.LockedOut, null);
        await signIn.SignInAsync(user, isPersistent: true, info.LoginProvider);
        return (outcome, user);
    }
}