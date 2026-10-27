using ClanHub.Application.Games;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ClanHub.Supercell;

public static class DependencyInjection
{
    public static IServiceCollection AddSupercell(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SupercellOptions>(configuration.GetSection(SupercellOptions.Section));
        services.AddMemoryCache();

        AddClient<CocApiClient>(services, o => o.ClashOfClans);
        AddClient<CrApiClient>(services, o => o.ClashRoyale);
        AddClient<BsApiClient>(services, o => o.BrawlStars);

        services.AddTransient<IGameConnector, ClashOfClansConnector>();
        services.AddTransient<IGameConnector, ClashRoyaleConnector>();
        services.AddTransient<IGameConnector, BrawlStarsConnector>();
        services.AddTransient<IGameConnectorFactory, GameConnectorFactory>();
        return services;
    }

    private static void AddClient<T>(IServiceCollection services, Func<SupercellOptions, GameApiOptions> pick) where T : SupercellApiClient
    {
        services.AddHttpClient<T>((sp, http) =>
        {
            var o = pick(sp.GetRequiredService<IOptions<SupercellOptions>>().Value);
            http.BaseAddress = new Uri(o.BaseUrl.TrimEnd('/') + "/");
            http.DefaultRequestHeaders.Authorization = SupercellApiClient.Bearer(o.ApiKey);
            http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        }).AddStandardResilienceHandler(o =>
        {
            o.Retry.MaxRetryAttempts = 3;
            o.Retry.BackoffType = Polly.DelayBackoffType.Exponential;
            o.Retry.UseJitter = true;
            o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);
            o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(40);
            o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(60);
        });
    }
}