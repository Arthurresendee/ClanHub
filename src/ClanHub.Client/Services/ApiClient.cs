using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ClanHub.Client.Services;

public record ApiResult<T>(T? Value, string? Error, int Status)
{
    public bool Ok => Error is null;
}

/// <summary>Cliente da API do ClanHub: converte ProblemDetails em mensagens para a tela.</summary>
public class ApiClient(HttpClient http)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<ApiResult<T>> Get<T>(string url) => Send<T>(HttpMethod.Get, url, null);
    public Task<ApiResult<T>> Post<T>(string url, object? body = null) => Send<T>(HttpMethod.Post, url, body);
    public Task<ApiResult<T>> Patch<T>(string url, object? body = null) => Send<T>(HttpMethod.Patch, url, body);
    public Task<ApiResult<T>> Put<T>(string url, object? body = null) => Send<T>(HttpMethod.Put, url, body);
    public Task<ApiResult<bool>> Delete(string url) => Send<bool>(HttpMethod.Delete, url, null);
    public Task<ApiResult<bool>> PostOk(string url, object? body = null) => Send<bool>(HttpMethod.Post, url, body);

    public async Task<ApiResult<T>> Send<T>(HttpMethod method, string url, object? body)
    {
        try
        {
            using var req = new HttpRequestMessage(method, url);
            if (body is not null) req.Content = JsonContent.Create(body, body.GetType(), options: Json);
            using var res = await http.SendAsync(req);
            var status = (int)res.StatusCode;
            if (res.IsSuccessStatusCode)
            {
                if (typeof(T) == typeof(bool) || res.StatusCode == HttpStatusCode.NoContent || res.Content.Headers.ContentLength == 0)
                    return new ApiResult<T>(typeof(T) == typeof(bool) ? (T)(object)true : default, null, status);
                var text = await res.Content.ReadAsStringAsync();
                if (string.IsNullOrWhiteSpace(text)) return new ApiResult<T>(default, null, status);
                return new ApiResult<T>(JsonSerializer.Deserialize<T>(text, Json), null, status);
            }
            return new ApiResult<T>(default, await ReadError(res), status);
        }
        catch (HttpRequestException)
        {
            return new ApiResult<T>(default, "Sem conexão com o servidor.", 0);
        }
    }

    private static async Task<string> ReadError(HttpResponseMessage res)
    {
        try
        {
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
            var root = doc.RootElement;
            if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
            {
                var msgs = errors.EnumerateObject().SelectMany(p => p.Value.ValueKind == JsonValueKind.Array
                    ? p.Value.EnumerateArray().Select(v => v.GetString() ?? "") : [p.Value.GetString() ?? ""]);
                var joined = string.Join(" ", msgs.Where(m => m.Length > 0));
                if (joined.Length > 0) return joined;
            }
            var title = root.TryGetProperty("title", out var t) ? t.GetString() : null;
            var detail = root.TryGetProperty("detail", out var d) ? d.GetString() : null;
            return string.Join(" ", new[] { title, detail }.Where(s => !string.IsNullOrWhiteSpace(s)));
        }
        catch
        {
            return res.StatusCode switch
            {
                HttpStatusCode.Unauthorized => "Faça login para continuar.",
                HttpStatusCode.Forbidden => "Você não tem permissão para isso.",
                HttpStatusCode.TooManyRequests => "Muitas tentativas. Aguarde um instante.",
                _ => $"Erro {(int)res.StatusCode}.",
            };
        }
    }
}