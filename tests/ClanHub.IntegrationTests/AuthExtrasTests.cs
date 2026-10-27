using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using ClanHub.Contracts;
using ClanHub.Infrastructure.Persistence;
using ClanHub.IntegrationTests.Infrastructure;
using ClanHub.Server.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ClanHub.IntegrationTests;

[Collection("api")]
public class DiscordLoginTests(ApiFactory factory) : ApiTestBase(factory)
{
    // UseSetting (e não ConfigureAppConfiguration): o Program lê Discord:ClientId antes do Build().
    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> WithDiscord() => factory.WithWebHostBuilder(b =>
    {
        b.UseSetting("Discord:ClientId", "test-client-id");
        b.UseSetting("Discord:ClientSecret", "test-secret");
    });
    [DockerFact]
    public async Task WithoutConfiguration_NoProviders_AndNoDiscordRoute()
    {
        var client = factory.NewClient();
        Assert.Empty(await (await client.GetAsync("/api/auth/providers")).ReadAs<List<ProviderDto>>());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/auth/discord/login")).StatusCode);
    }

    [DockerFact]
    public async Task WithConfiguration_ListsDiscord_AndRedirectsToDiscordAuthorize()
    {
        using var configured = WithDiscord();
        var client = configured.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var providers = await (await client.GetAsync("/api/auth/providers")).ReadAs<List<ProviderDto>>();
        Assert.Equal("discord", Assert.Single(providers).Id);

        var res = await client.GetAsync("/api/auth/discord/login?returnUrl=/app/conta");
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        var location = res.Headers.Location!.ToString();
        Assert.StartsWith("https://discord.com/", location);
        Assert.Contains("oauth2/authorize", location);
        Assert.Contains("client_id=test-client-id", location);
        var scope = System.Web.HttpUtility.ParseQueryString(res.Headers.Location!.Query)["scope"]!;
        Assert.Contains("identify", scope);
        Assert.Contains("email", scope);
        Assert.Contains("redirect_uri=", location);
        Assert.Contains("%2Fapi%2Fauth%2Fdiscord%2Fsignin", location, StringComparison.OrdinalIgnoreCase);
    }

    [DockerFact]
    public async Task Callback_WithoutExternalLogin_GoesBackToLoginWithError()
    {
        using var configured = WithDiscord();
        var res = await configured.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false })
            .GetAsync("/api/auth/discord/callback?returnUrl=/app");
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        Assert.Equal("/entrar?error=discord", res.Headers.Location!.OriginalString);
    }

    // ---- regras de vínculo/criação de conta (ExternalLogins) ----
    private static ExternalLoginInfo Info(string key, string? email, bool verified, string name = "Discordante") =>
        new(new ClaimsPrincipal(new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.NameIdentifier, key), new Claim(ClaimTypes.Name, name),
                email is null ? null : new Claim(ClaimTypes.Email, email),
                new Claim(ExternalLogins.VerifiedClaim, verified ? "true" : "false"),
            }.OfType<Claim>(), "Discord")), "Discord", key, "Discord");

    private async Task<(ExternalLoginOutcome Outcome, ApplicationUser? User)> Run(ExternalLoginInfo info)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        return await scope.ServiceProvider.GetRequiredService<ExternalLogins>().SignInOrRegisterAsync(info);
    }

    [DockerFact]
    public async Task NewVerifiedDiscordUser_GetsAnAccount_ThenSignsInDirectlyNextTime()
    {
        var key = Guid.NewGuid().ToString("N");
        var email = $"discord-{key}@test.local";

        var (first, user) = await Run(Info(key, email, true));
        Assert.Equal(ExternalLoginOutcome.Created, first);
        Assert.True(user!.EmailConfirmed);
        Assert.Equal("Discordante", user.DisplayName);

        var (second, again) = await Run(Info(key, email, true));
        Assert.Equal(ExternalLoginOutcome.SignedIn, second);
        Assert.Equal(user.Id, again!.Id);
    }

    [DockerFact]
    public async Task ExistingAccount_WithSameVerifiedEmail_IsLinked_NotDuplicated()
    {
        var email = $"existing-{Guid.NewGuid():N}@test.local";
        await factory.RegisterAndLogin(email);

        var (outcome, user) = await Run(Info(Guid.NewGuid().ToString("N"), email, true));
        Assert.Equal(ExternalLoginOutcome.Linked, outcome);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.Users.CountAsync(u => u.Email == email));
        Assert.Equal(1, await db.UserLogins.CountAsync(l => l.UserId == user!.Id && l.LoginProvider == "Discord"));
    }

    [DockerFact]
    public async Task UnverifiedOrMissingEmail_NeverCreatesOrLinksAnAccount()
    {
        var email = $"victim-{Guid.NewGuid():N}@test.local";
        await factory.RegisterAndLogin(email);

        Assert.Equal(ExternalLoginOutcome.EmailNotVerified, (await Run(Info(Guid.NewGuid().ToString("N"), email, false))).Outcome);   // tomada de conta barrada
        Assert.Equal(ExternalLoginOutcome.NoEmail, (await Run(Info(Guid.NewGuid().ToString("N"), null, true))).Outcome);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var victimId = await db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
        Assert.Equal(0, await db.UserLogins.CountAsync(l => l.UserId == victimId));
    }

    [DockerFact]
    public async Task LockedUser_CannotSignInWithDiscord()
    {
        var key = Guid.NewGuid().ToString("N");
        var email = $"locked-{key}@test.local";
        var (_, user) = await Run(Info(key, email, true));

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var u = (await users.FindByIdAsync(user!.Id.ToString()))!;
            await users.SetLockoutEnabledAsync(u, true);
            await users.SetLockoutEndDateAsync(u, DateTimeOffset.UtcNow.AddHours(1));
        }
        Assert.Equal(ExternalLoginOutcome.LockedOut, (await Run(Info(key, email, true))).Outcome);
    }
}

[Collection("api")]
public class PasskeyTests(ApiFactory factory) : ApiTestBase(factory)
{
    private static async Task<string> Json(HttpResponseMessage r)
    {
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return await r.Content.ReadAsStringAsync();
    }

    private async Task<(HttpClient Client, VirtualAuthenticator Auth, byte[] UserHandle, PasskeyDto Passkey)> Enroll(string? email = null, string name = "Meu celular")
    {
        var client = await factory.RegisterAndLogin(email);
        var auth = new VirtualAuthenticator();
        var options = await Json(await client.PostAsJsonAsync("/api/auth/passkey/register/options", new { }));
        var (credential, userHandle) = auth.Create(options);
        var res = await client.PostAsJsonAsync("/api/auth/passkey/register", new { credentialJson = credential, name });
        var passkey = await res.ReadAs<PasskeyDto>();
        return (client, auth, userHandle, passkey);
    }

    private async Task<HttpResponseMessage> Login(VirtualAuthenticator auth, byte[] userHandle, HttpClient anon, bool tamper = false)
    {
        var options = await Json(await anon.PostAsJsonAsync("/api/auth/passkey/login/options", new { }));
        return await anon.PostAsJsonAsync("/api/auth/passkey/login", new { credentialJson = auth.Get(options, userHandle, tamper) });
    }

    [DockerFact]
    public async Task RegisterThenLogin_WithAPasskey_NoPasswordNeeded()
    {
        var email = $"pk-{Guid.NewGuid():N}@test.local";
        var (_, auth, handle, passkey) = await Enroll(email);
        Assert.Equal("Meu celular", passkey.Name);

        var anon = factory.NewClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/v1/me")).StatusCode);
        var login = await Login(auth, handle, anon);
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);

        var me = await (await anon.GetAsync("/api/v1/me")).ReadAs<MeResponse>();
        Assert.Equal(email, me.Email);
    }

    [DockerFact]
    public async Task TamperedSignature_IsRejected()
    {
        var (_, auth, handle, _) = await Enroll();
        var anon = factory.NewClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(auth, handle, anon, tamper: true)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/v1/me")).StatusCode);
    }

    [DockerFact]
    public async Task UnknownAuthenticator_IsRejected()
    {
        var (_, _, handle, _) = await Enroll();
        var stranger = new VirtualAuthenticator();     // credencial que nunca foi cadastrada
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(stranger, handle, factory.NewClient())).StatusCode);
    }

    [DockerFact]
    public async Task ChallengeCannotBeReplayed()
    {
        var (_, auth, handle, _) = await Enroll();
        var anon = factory.NewClient();
        var options = await Json(await anon.PostAsJsonAsync("/api/auth/passkey/login/options", new { }));
        var assertion = auth.Get(options, handle);

        Assert.Equal(HttpStatusCode.NoContent, (await anon.PostAsJsonAsync("/api/auth/passkey/login", new { credentialJson = assertion })).StatusCode);
        var replay = factory.NewClient();               // outro navegador tentando reaproveitar a mesma resposta
        Assert.Equal(HttpStatusCode.Unauthorized, (await replay.PostAsJsonAsync("/api/auth/passkey/login", new { credentialJson = assertion })).StatusCode);
    }

    [DockerFact]
    public async Task ListAndRemove_Passkeys_OnlyOwnAndAuthenticated()
    {
        var (client, auth, handle, passkey) = await Enroll(name: "Notebook");
        var list = await (await client.GetAsync("/api/auth/passkeys")).ReadAs<List<PasskeyDto>>();
        Assert.Equal("Notebook", Assert.Single(list).Name);

        var other = await factory.RegisterAndLogin();
        Assert.Empty(await (await other.GetAsync("/api/auth/passkeys")).ReadAs<List<PasskeyDto>>());
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/auth/passkeys/{passkey.Id}")).StatusCode);   // não remove a de outra pessoa
        Assert.Single(await (await client.GetAsync("/api/auth/passkeys")).ReadAs<List<PasskeyDto>>());

        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.NewClient().GetAsync("/api/auth/passkeys")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.NewClient().PostAsJsonAsync("/api/auth/passkey/register/options", new { })).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/auth/passkeys/{passkey.Id}")).StatusCode);
        Assert.Empty(await (await client.GetAsync("/api/auth/passkeys")).ReadAs<List<PasskeyDto>>());
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(auth, handle, factory.NewClient())).StatusCode);               // sem a passkey, sem login
    }

    [DockerFact]
    public async Task MalformedCredential_IsABadRequest_NotAServerError()
    {
        var client = await factory.RegisterAndLogin();
        await client.PostAsJsonAsync("/api/auth/passkey/register/options", new { });
        var res = await client.PostAsJsonAsync("/api/auth/passkey/register", new { credentialJson = "{\"nada\":true}", name = "x" });
        Assert.True((int)res.StatusCode is >= 400 and < 500, $"esperava erro de cliente, veio {(int)res.StatusCode}");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.DeleteAsync("/api/auth/passkeys/***")).StatusCode);
    }
}
[Collection("api")]
public class EmailConfirmationSettingTests(ApiFactory factory) : ApiTestBase(factory)
{
    [DockerFact]
    public async Task WhenNotRequired_AccountIsUsableRightAway_AndNoEmailIsSent()
    {
        using var relaxed = factory.WithWebHostBuilder(b => b.UseSetting("Auth:RequireConfirmedEmail", "false"));
        var client = relaxed.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = true });
        var email = $"noconfirm-{Guid.NewGuid():N}@test.local";
        var emailsBefore = factory.Emails.Confirmations.Count;

        var signup = await client.PostAsJsonAsync("/api/auth/signup", new RegisterRequest(email, TestClient.Password, "Sem Confirmação", true));
        Assert.Equal(HttpStatusCode.Accepted, signup.StatusCode);
        Assert.False((await signup.Content.ReadFromJsonAsync<SignupResponse>(TestClient.Json))!.RequiresEmailConfirmation);
        Assert.Equal(emailsBefore, factory.Emails.Confirmations.Count);       // nenhum e-mail enviado

        var login = await client.PostAsJsonAsync("/api/auth/login?useCookies=true", new { email, password = TestClient.Password });
        Assert.True(login.IsSuccessStatusCode, await login.Content.ReadAsStringAsync());
        var me = await (await client.GetAsync("/api/v1/me")).ReadAs<MeResponse>();
        Assert.Equal(email, me.Email);
    }

    [DockerFact]
    public async Task ByDefault_ConfirmationIsRequired_AndTheResponseSaysSo()
    {
        var client = factory.NewClient();
        var email = $"confirm-{Guid.NewGuid():N}@test.local";
        var signup = await client.PostAsJsonAsync("/api/auth/signup", new RegisterRequest(email, TestClient.Password, "Com Confirmação", true));
        Assert.True((await signup.Content.ReadFromJsonAsync<SignupResponse>(TestClient.Json))!.RequiresEmailConfirmation);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new { email, password = TestClient.Password })).StatusCode);
    }
}