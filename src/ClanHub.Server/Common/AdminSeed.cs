using ClanHub.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;

namespace ClanHub.Server.Common;

/// <summary>Dá o papel Admin da plataforma ao e-mail configurado em Admin:Email (se já cadastrado).</summary>
public static class AdminSeed
{
    public static async Task RunAsync(WebApplication app)
    {
        var email = app.Configuration["Admin:Email"];
        if (string.IsNullOrWhiteSpace(email)) return;

        using var scope = app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        try
        {
            if (!await roles.RoleExistsAsync("Admin")) await roles.CreateAsync(new IdentityRole<Guid>("Admin"));
            var user = await users.FindByEmailAsync(email);
            if (user is not null && !await users.IsInRoleAsync(user, "Admin")) await users.AddToRoleAsync(user, "Admin");
        }
        catch (Exception ex)
        {
            app.Logger.LogWarning(ex, "Não foi possível aplicar Admin:Email (o banco já foi migrado?).");
        }
    }
}