using System.Net.Mail;
using System.Text;
using ClanHub.Contracts;
using ClanHub.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;

namespace ClanHub.Server.Features;

public static class AuthEndpoints
{
    public static void MapAuth(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/auth").RequireRateLimiting("auth");
        auth.MapIdentityApi<ApplicationUser>();

        auth.MapPost("/signup", Signup).AllowAnonymous();
        auth.MapPost("/logout", async (SignInManager<ApplicationUser> signIn) =>
        {
            await signIn.SignOutAsync();
            return Results.NoContent();
        }).RequireAuthorization();
    }

    private static async Task<IResult> Signup(
        RegisterRequest req, UserManager<ApplicationUser> users, IEmailSender<ApplicationUser> email,
        IConfiguration config, HttpContext http)
    {
        var errors = new Dictionary<string, string[]>();
        if (!MailAddress.TryCreate(req.Email, out _)) errors["email"] = ["E-mail inválido."];
        if (string.IsNullOrWhiteSpace(req.DisplayName) || req.DisplayName.Trim().Length is < 2 or > 100)
            errors["displayName"] = ["Informe um nome entre 2 e 100 caracteres."];
        if (!req.AcceptTerms) errors["acceptTerms"] = ["É preciso aceitar os termos de uso."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var requireConfirmation = config.GetValue("Auth:RequireConfirmedEmail", true);
        var user = new ApplicationUser
        {
            UserName = req.Email.Trim(), Email = req.Email.Trim(), DisplayName = req.DisplayName.Trim(),
            EmailConfirmed = !requireConfirmation,   // sem confirmação exigida, a conta já nasce confirmada
        };
        var result = await users.CreateAsync(user, req.Password);
        if (!result.Succeeded)
            return Results.ValidationProblem(result.Errors.GroupBy(e => e.Code)
                .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray()));

        if (!requireConfirmation) return Results.Accepted(value: new SignupResponse(false));

        var token = await users.GenerateEmailConfirmationTokenAsync(user);
        var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        var baseUrl = (config["App:PublicUrl"] ?? $"{http.Request.Scheme}://{http.Request.Host}").TrimEnd('/');
        var link = $"{baseUrl}/confirmar-email?userId={user.Id}&code={code}";
        await email.SendConfirmationLinkAsync(user, user.Email!, System.Net.WebUtility.HtmlEncode(link));
        return Results.Accepted(value: new SignupResponse(true));
    }
}