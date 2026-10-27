using ClanHub.Infrastructure;
using ClanHub.Supercell;
using ClanHub.Sync.Handlers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ClanHub.Sync;

public static class DependencyInjection
{
    public static IServiceCollection AddSync(this IServiceCollection services, IConfiguration configuration,
        Action<DbContextOptionsBuilder>? configureDb = null)
    {
        services.AddInfrastructure(configuration, configureDb);
        services.AddSupercell(configuration);
        services.Configure<SyncOptions>(configuration.GetSection(SyncOptions.Section));

        services.AddScoped<ISyncHandler, MembersHandler>();
        services.AddScoped<ISyncHandler, CurrentWarHandler>();
        services.AddScoped<ISyncHandler, WarLogHandler>();
        services.AddScoped<ISyncHandler, RiverRaceHandler>();
        services.AddScoped<ISyncHandler, RiverRaceLogHandler>();
        services.AddScoped<ISyncHandler, PlayerProfilesHandler>();
        services.AddScoped<ISyncHandler, CwlHandler>();
        services.AddScoped<ISyncHandler, CapitalRaidsHandler>();
        services.AddScoped<ISyncHandler, RankingsHandler>();
        services.AddScoped<ISyncHandler, AlertsHandler>();
        services.AddHttpClient(AlertsHandler.HttpClientName, h => h.Timeout = TimeSpan.FromSeconds(10));

        services.AddScoped<SyncQueue>();
        services.AddScoped<MaintenanceRunner>();
        services.AddSingleton<SyncProcessor>();
        services.AddSingleton<TickRunner>();
        return services;
    }
}