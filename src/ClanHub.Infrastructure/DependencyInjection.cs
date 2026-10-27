using ClanHub.Infrastructure.Email;
using ClanHub.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ClanHub.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration,
        Action<DbContextOptionsBuilder>? configureDb = null)
    {
        services.AddDbContext<AppDbContext>(o =>
        {
            // A navegação obrigatória Workspace tem filtro de exclusão lógica por design: consultas usam Join explícito.
            o.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning));
            if (configureDb is not null) configureDb(o);
            else o.UseSqlServer(configuration.GetConnectionString("Default"),
                sql => sql.EnableRetryOnFailure(3));
        });

        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.Section));
        services.AddTransient<IEmailSender<ApplicationUser>, MailKitEmailSender>();

        services.AddDataProtection()
            .SetApplicationName("ClanHub")
            .PersistKeysToDbContext<AppDbContext>();

        // Cache compartilhado opcional (L2 do HybridCache): defina Redis:ConnectionString (ex.: Redis do Marketplace da Vercel).
        if (configuration["Redis:ConnectionString"] is { Length: > 0 } redis)
            services.AddStackExchangeRedisCache(o => o.Configuration = redis);
        services.AddHybridCache();
        services.AddSingleton(TimeProvider.System);
        return services;
    }
}