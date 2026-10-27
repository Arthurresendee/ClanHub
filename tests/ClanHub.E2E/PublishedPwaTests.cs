using ClanHub.E2E.Infrastructure;
using Microsoft.Playwright;

namespace ClanHub.E2E;

/// <summary>PWA de verdade: service worker de produção instalando o app e abrindo sem internet.</summary>
[Collection("e2e-published")]
public class PublishedPwaTests(PublishedAppFixture app)
{
    [SkippableFact]
    public async Task InstalledApp_OpensOffline_FromTheServiceWorkerCache_ButNeverCachesTheApi()
    {
        Skip.If(AppFixture.Skip is not null, AppFixture.Skip);
        await using var ctx = await app.NewContextAsync();
        var page = await ctx.NewPageAsync();
        await page.GotoAsync("/");
        await page.WaitForBlazorAsync();
        await Assertions.Expect(page.GetByText("Gerencie seu clã sem planilhas")).ToBeVisibleAsync();

        // o service worker de produção instala e preenche o cache do "casco" do app
        Assert.True(await page.EvaluateAsync<bool>("navigator.serviceWorker.ready.then(r => !!r.active)"));
        var cached = false;
        for (var i = 0; i < 90 && !cached; i++)
        {
            cached = await page.EvaluateAsync<bool>(@"(async () => {
                const keys = (await caches.keys()).filter(k => k.startsWith('clanhub-shell-'));
                if (keys.length === 0) return false;
                const c = await caches.open(keys[0]);
                const entries = await c.keys();
                return entries.some(r => r.url.endsWith('/index.html')) && entries.some(r => r.url.endsWith('.wasm'));
            })()");
            if (!cached) await page.WaitForTimeoutAsync(1000);
        }
        Assert.True(cached, "o cache do service worker não foi preenchido");

        // a API nunca entra no cache
        var apiCached = await page.EvaluateAsync<bool>(@"(async () => {
            for (const k of await caches.keys()) { const c = await caches.open(k); for (const r of await c.keys()) if (new URL(r.url).pathname.startsWith('/api/')) return true; }
            return false; })()");
        Assert.False(apiCached);

        // sem internet: a página recarrega pelo cache e avisa que está offline
        await page.ReloadAsync();                                   // garante que o SW controla a página
        await ctx.SetOfflineAsync(true);
        await page.ReloadAsync();
        await Assertions.Expect(page.GetByText("Gerencie seu clã sem planilhas")).ToBeVisibleAsync(new() { Timeout = 30000 });
        await Assertions.Expect(page.GetByText("Você está sem conexão")).ToBeVisibleAsync();

        // e as chamadas da API continuam falhando (nunca respondem com dado velho)
        var apiFailed = await page.EvaluateAsync<bool>("fetch('/api/v1/games').then(() => false, () => true)");
        Assert.True(apiFailed);

        await ctx.SetOfflineAsync(false);
        var online = await page.EvaluateAsync<int>("fetch('/api/v1/games').then(r => r.status)");
        Assert.Equal(200, online);
    }

    [SkippableFact]
    public async Task ProductionBuild_ServesFingerprintedFramework_AndNoCacheShell()
    {
        Skip.If(AppFixture.Skip is not null, AppFixture.Skip);
        using var http = new HttpClient { BaseAddress = new Uri(app.BaseUrl) };
        var sw = await http.GetAsync("/service-worker.js");
        Assert.Contains("no-cache", sw.Headers.CacheControl?.ToString());
        Assert.Contains("clanhub-shell-", await sw.Content.ReadAsStringAsync());          // é o service worker de produção
        var assets = await http.GetStringAsync("/service-worker-assets.js");
        Assert.Contains("\"version\"", assets);
        Assert.Contains("_framework/", assets);
        Assert.Equal(System.Net.HttpStatusCode.OK, (await http.GetAsync("/manifest.webmanifest")).StatusCode);
    }
}