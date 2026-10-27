using System.Security.Cryptography;
using System.Text;

namespace ClanHub.Server.Common;

/// <summary>Protege /internal/*: exige Authorization: Bearer com o valor de CRON_SECRET.</summary>
public class CronSecretFilter(IConfiguration configuration) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var secret = configuration["CRON_SECRET"];
        if (string.IsNullOrEmpty(secret))
            return Results.Problem(statusCode: 503, title: "CRON_SECRET não configurado.");

        var header = context.HttpContext.Request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        var provided = header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? header[prefix.Length..].Trim() : "";

        var ok = CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(provided)), SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
        return ok ? await next(context) : Results.Unauthorized();
    }
}