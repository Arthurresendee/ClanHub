using System.Threading.RateLimiting;
using ClanHub.Infrastructure;
using ClanHub.Infrastructure.Persistence;
using ClanHub.Server.Common;
using ClanHub.Server.Features;
using ClanHub.Sync;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using AspNet.Security.OAuth.Discord;
using Scalar.AspNetCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Vercel: a porta vem da variável PORT (padrão 8080). Em desenvolvimento/testes valem as configurações do host.
if (!builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing")
    && string.IsNullOrEmpty(builder.Configuration["ASPNETCORE_URLS"]))
    builder.WebHost.UseUrls($"http://+:{builder.Configuration["PORT"] ?? "8080"}");

// Logs só no console (a Vercel não guarda arquivos).
builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

builder.AddServiceDefaults();
builder.Services.AddSync(builder.Configuration);
builder.Services.AddHttpContextAccessor();

builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>("database");

// Identity: cookie na web (HttpOnly, Secure, SameSite=Strict) e bearer token para o app futuro.
builder.Services.AddIdentityApiEndpoints<ApplicationUser>(o =>
    {
        // Confirmação de e-mail: ligada por padrão. Em desenvolvimento fica desligada (Auth:RequireConfirmedEmail=false)
        // até o envio de e-mails (n8n) ser implementado.
        o.SignIn.RequireConfirmedEmail = builder.Configuration.GetValue("Auth:RequireConfirmedEmail", true);
        o.User.RequireUniqueEmail = true;
        o.Lockout.MaxFailedAccessAttempts = 5;
        o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        o.Lockout.AllowedForNewUsers = true;
        o.Password.RequiredLength = 8;
        o.Stores.SchemaVersion = IdentitySchemaVersions.Version3; // habilita a tabela de passkeys
        o.Stores.MaxLengthForKeys = 450;                           // mantém as colunas de chave como já estavam (não altera PKs)
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<AppDbContext>();

// Login com Discord: só liga se Discord:ClientId estiver configurado.
var discordEnabled = !string.IsNullOrWhiteSpace(builder.Configuration["Discord:ClientId"]);
if (discordEnabled)
{
    builder.Services.AddAuthentication().AddDiscord(ExternalAuthEndpoints.DiscordScheme, o =>
    {
        o.ClientId = builder.Configuration["Discord:ClientId"]!;
        o.ClientSecret = builder.Configuration["Discord:ClientSecret"] ?? "";
        o.SignInScheme = IdentityConstants.ExternalScheme;
        o.CallbackPath = "/api/auth/discord/signin";
        o.Scope.Add("identify");
        o.Scope.Add("email");
    });
}
builder.Services.AddScoped<ExternalLogins>();

builder.Services.ConfigureApplicationCookie(o =>
{
    o.Cookie.Name = "clanhub.auth";
    o.Cookie.HttpOnly = true;
    o.Cookie.SecurePolicy = builder.Environment.IsEnvironment("Testing") ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.ExpireTimeSpan = TimeSpan.FromDays(14);
    o.SlidingExpiration = true;
    o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
});

builder.Services.AddAuthorization(o => o.AddWorkspacePolicies());
builder.Services.AddScoped<IAuthorizationHandler, WorkspaceRoleHandler>();

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("public", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = builder.Configuration.GetValue("RateLimit:PublicPerMinute", 20), Window = TimeSpan.FromMinutes(1) }));
    o.AddPolicy("auth", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = builder.Configuration.GetValue("RateLimit:AuthPerMinute", 20), Window = TimeSpan.FromMinutes(1) }));
});

builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    o.KnownIPNetworks.Clear(); // a Vercel fica na frente do contêiner
    o.KnownProxies.Clear();
});

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<SupercellExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

var app = builder.Build();

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseSerilogRequestLogging();

if (app.Configuration.GetValue("Database:MigrateOnStartup", false))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}
await AdminSeed.RunAsync(app);

if (!app.Environment.IsDevelopment()) app.UseHsts();

app.UseBlazorFrameworkFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        // O service worker e o manifest precisam ser revalidados sempre, senão atualizações do app não chegam.
        var name = ctx.File.Name;
        if (name is "service-worker.js" or "service-worker-assets.js" or "manifest.webmanifest" or "index.html")
            ctx.Context.Response.Headers.CacheControl = "no-cache";
    },
});
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapDefaultEndpoints();
app.MapAuth();
app.MapExternalAuth(discordEnabled);
app.MapMe();
app.MapGames();
app.MapWorkspaces();
app.MapClans();
app.MapWars();
app.MapCwlAndCapital();
app.MapEngagement();
app.MapPhase3();
app.MapPlatform();

// Documentação da API: aberta em desenvolvimento, só Admin da plataforma em produção.
var docs = app.MapOpenApi();
var scalar = app.MapScalarApiReference();
if (!app.Environment.IsDevelopment())
{
    docs.RequireAuthorization("PlatformAdmin");
    scalar.RequireAuthorization("PlatformAdmin");
}

app.MapFallback("/api/{**rest}", () => Results.Problem(statusCode: 404, title: "Rota não encontrada."));
app.MapFallback("/internal/{**rest}", () => Results.NotFound());
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;