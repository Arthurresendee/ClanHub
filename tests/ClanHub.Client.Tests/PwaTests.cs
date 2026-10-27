using System.Text.Json;
using System.Text.RegularExpressions;

namespace ClanHub.Client.Tests;

/// <summary>Garante que os arquivos da PWA existem e estão coerentes entre si (manifest, ícones, service worker, index.html).</summary>
public class PwaTests
{
    private static readonly string Wwwroot = FindWwwroot();

    private static string FindWwwroot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "ClanHub.Client", "wwwroot"))) dir = dir.Parent;
        return dir is null ? throw new DirectoryNotFoundException("wwwroot do Client não encontrado") : Path.Combine(dir.FullName, "src", "ClanHub.Client", "wwwroot");
    }

    private static string Read(string rel) => File.ReadAllText(Path.Combine(Wwwroot, rel));

    private static (int W, int H) PngSize(string rel)
    {
        var b = File.ReadAllBytes(Path.Combine(Wwwroot, rel));
        Assert.True(b.Length > 24 && b[0] == 0x89 && b[1] == 'P' && b[2] == 'N' && b[3] == 'G', $"{rel} não é um PNG");
        int Be(int o) => (b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3];
        return (Be(16), Be(20));
    }

    [Fact]
    public void Manifest_HasWhatBrowsersRequireToOfferInstallation()
    {
        using var doc = JsonDocument.Parse(Read("manifest.webmanifest"));
        var m = doc.RootElement;
        Assert.False(string.IsNullOrWhiteSpace(m.GetProperty("name").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(m.GetProperty("short_name").GetString()));
        Assert.True(m.GetProperty("short_name").GetString()!.Length <= 12);
        Assert.Equal("standalone", m.GetProperty("display").GetString());
        Assert.StartsWith("/", m.GetProperty("start_url").GetString());
        Assert.Equal("/", m.GetProperty("scope").GetString());
        Assert.Matches("^#[0-9a-fA-F]{6}$", m.GetProperty("theme_color").GetString()!);
        Assert.Matches("^#[0-9a-fA-F]{6}$", m.GetProperty("background_color").GetString()!);
        Assert.Equal("pt-BR", m.GetProperty("lang").GetString());
    }

    [Fact]
    public void Manifest_DoesNotUseSupercellBrands()
    {
        var text = Read("manifest.webmanifest") + Read("index.html");
        foreach (var brand in new[] { "Supercell", "Clash of Clans", "Clash Royale", "Brawl Stars" })
            Assert.DoesNotContain(brand, text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Manifest_Icons_ExistWithDeclaredSizes_AndIncludeAnyAndMaskable()
    {
        using var doc = JsonDocument.Parse(Read("manifest.webmanifest"));
        var icons = doc.RootElement.GetProperty("icons").EnumerateArray().ToList();
        Assert.Contains(icons, i => i.GetProperty("sizes").GetString() == "192x192" && i.GetProperty("purpose").GetString() == "any");
        Assert.Contains(icons, i => i.GetProperty("sizes").GetString() == "512x512" && i.GetProperty("purpose").GetString() == "any");
        Assert.Contains(icons, i => i.GetProperty("purpose").GetString() == "maskable");

        foreach (var icon in icons)
        {
            var size = icon.GetProperty("sizes").GetString()!.Split('x');
            var (w, h) = PngSize(icon.GetProperty("src").GetString()!);
            Assert.Equal((int.Parse(size[0]), int.Parse(size[1])), (w, h));
        }
        Assert.Equal((180, 180), PngSize("icons/apple-touch-icon.png"));
    }

    [Fact]
    public void Manifest_StartUrlAndShortcuts_StayInsideScope()
    {
        using var doc = JsonDocument.Parse(Read("manifest.webmanifest"));
        var scope = doc.RootElement.GetProperty("scope").GetString()!;
        Assert.StartsWith(scope, doc.RootElement.GetProperty("start_url").GetString()!);
        foreach (var s in doc.RootElement.GetProperty("shortcuts").EnumerateArray())
            Assert.StartsWith(scope, s.GetProperty("url").GetString()!);
    }

    [Fact]
    public void IndexHtml_LinksManifest_Icons_AndRegistersTheServiceWorker()
    {
        var html = Read("index.html");
        Assert.Contains("rel=\"manifest\" href=\"manifest.webmanifest\"", html);
        Assert.Contains("rel=\"apple-touch-icon\"", html);
        Assert.Contains("name=\"theme-color\"", html);
        Assert.Contains("serviceWorker.register(\"service-worker.js\"", html);
        Assert.Contains("js/app.js", html);
        Assert.Contains("js/passkeys.js", html);
        Assert.Contains("lang=\"pt-BR\"", html);
        foreach (Match href in Regex.Matches(html, "(?:href|src)=\"((?!https?:|#)[^\"]+)\""))
        {
            var path = href.Groups[1].Value.Split('?')[0];
            if (path.Contains('_') && (path.StartsWith("_framework") || path.StartsWith("_content"))) continue;   // gerados no build
            if (path is "." or "/" ) continue;
            Assert.True(File.Exists(Path.Combine(Wwwroot, path.Replace('/', Path.DirectorySeparatorChar))), $"index.html referencia arquivo inexistente: {path}");
        }
    }

    [Fact]
    public void PublishedServiceWorker_NeverCachesTheApi_AndOnlyHandlesGet()
    {
        var sw = Read("service-worker.published.js");
        foreach (var route in new[] { "api", "internal", "health", "openapi" })
            Assert.Contains($"^\\/{route}", sw);
        Assert.Contains("request.method !== 'GET'", sw);
        Assert.Contains("url.origin !== self.location.origin", sw);
        Assert.Contains("service-worker-assets.js", sw);
        Assert.Contains("assetsManifest.version", sw);                 // cache versionado: atualiza a cada deploy
        Assert.Contains("caches.delete", sw);                          // limpa versões antigas
        Assert.Contains("clients.claim", sw);
        Assert.Contains("'index.html'", sw);                           // navegação offline cai no casco do app
    }

    [Fact]
    public void DevServiceWorker_DoesNotCache() => Assert.DoesNotContain("caches.open", Read("service-worker.js"));

    [Fact]
    public void ClientCsproj_WiresTheServiceWorkerAssetsManifest()
    {
        var csproj = File.ReadAllText(Path.Combine(Wwwroot, "..", "ClanHub.Client.csproj"));
        Assert.Contains("ServiceWorkerAssetsManifest", csproj);
        Assert.Contains("service-worker.published.js", csproj);
    }

    [Fact]
    public void PasskeyScript_ExposesWhatTheClientCalls()
    {
        var js = Read("js/passkeys.js");
        foreach (var fn in new[] { "supported", "create", "get" }) Assert.Contains(fn, js);
        Assert.Contains("parseCreationOptionsFromJSON", js);
        Assert.Contains("parseRequestOptionsFromJSON", js);
    }

    [Fact]
    public void AppScript_ExposesPwaHooks_UsedByTheComponents()
    {
        var js = Read("js/app.js");
        foreach (var fn in new[] { "isVisible", "isOnline", "canInstall", "install", "isStandalone", "isIos", "onConnectivity", "onInstallAvailable", "copy" })
            Assert.Contains(fn, js);
        Assert.Contains("beforeinstallprompt", js);
    }
}