using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ClanHub.Application.Games;
using ClanHub.Domain;
using Microsoft.Extensions.Caching.Memory;

namespace ClanHub.Supercell;

/// <summary>Cliente HTTP base: tags codificadas, erros tipados e cache por Cache-Control.</summary>
public abstract class SupercellApiClient(HttpClient http, IMemoryCache cache)
{
    protected static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString,
    };

    protected abstract string Name { get; }

    protected static string Tag(string tag) => Tags.ToUrl(tag);

    public async Task<(T Value, string Raw)> GetWithRawAsync<T>(string path, CancellationToken ct)
    {
        var raw = await GetRawAsync(path, ct);
        var value = JsonSerializer.Deserialize<T>(raw, Json)
            ?? throw new SupercellException(SupercellErrorKind.Unknown, $"{Name}: resposta vazia em {path}.");
        return (value, raw);
    }

    public async Task<T> GetAsync<T>(string path, CancellationToken ct) => (await GetWithRawAsync<T>(path, ct)).Value;

    private async Task<string> GetRawAsync(string path, CancellationToken ct)
    {
        var key = $"{Name}:{path}";
        if (cache.TryGetValue(key, out string? cached) && cached is not null) return cached;

        HttpResponseMessage response;
        try
        {
            response = await http.GetAsync(path, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new SupercellException(SupercellErrorKind.Unknown, $"{Name}: falha de rede.", null, ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode) throw Map(response);
            var raw = await response.Content.ReadAsStringAsync(ct);
            var maxAge = response.Headers.CacheControl?.MaxAge;
            if (maxAge is { TotalSeconds: > 0 }) cache.Set(key, raw, maxAge.Value);
            return raw;
        }
    }

    protected async Task<HttpResponseMessage> PostAsync<TBody>(string path, TBody body, CancellationToken ct)
    {
        var response = await http.PostAsJsonAsync(path, body, Json, ct);
        if (!response.IsSuccessStatusCode) { var ex = Map(response); response.Dispose(); throw ex; }
        return response;
    }

    private SupercellException Map(HttpResponseMessage r)
    {
        var retry = r.Headers.RetryAfter?.Delta;
        var kind = r.StatusCode switch
        {
            HttpStatusCode.BadRequest => SupercellErrorKind.BadRequest,
            HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized => SupercellErrorKind.AccessDenied,
            HttpStatusCode.NotFound => SupercellErrorKind.NotFound,
            HttpStatusCode.TooManyRequests => SupercellErrorKind.Throttled,
            HttpStatusCode.ServiceUnavailable => SupercellErrorKind.Maintenance,
            _ => SupercellErrorKind.Unknown,
        };
        return new SupercellException(kind, $"{Name}: HTTP {(int)r.StatusCode} {r.ReasonPhrase}", retry);
    }

    protected static async Task<T> PrivateLogAware<T>(Func<Task<T>> call)
    {
        try { return await call(); }
        catch (SupercellException ex) when (ex.Kind == SupercellErrorKind.AccessDenied)
        {
            throw new SupercellException(SupercellErrorKind.PrivateWarLog, ex.Message, null, ex);
        }
    }

    internal static AuthenticationHeaderValue Bearer(string key) => new("Bearer", key);
}