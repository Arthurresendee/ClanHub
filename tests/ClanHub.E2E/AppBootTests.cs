using ClanHub.E2E.Infrastructure;
using Microsoft.Playwright;

namespace ClanHub.E2E;

[Collection("e2e")]
public class AppBootTests(AppFixture app)
{
    private const string Notice = "This material is unofficial and is not endorsed by Supercell";

    [SkippableFact]
    public async Task Home_Boots_ShowsFooterNotice_WithoutJsErrors()
    {
        Skip.If(AppFixture.Skip is not null, AppFixture.Skip);
        await using var ctx = await app.NewContextAsync();
        var page = await ctx.NewPageAsync();
        var errors = page.CaptureErrors();

        await page.GotoAsync("/");
        await page.WaitForBlazorAsync();

        await Assertions.Expect(page.GetByText("Gerencie seu clã sem planilhas")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByText(Notice)).ToBeVisibleAsync();
        await Assertions.Expect(page).ToHaveTitleAsync("ClanHub — gestão de clãs");
        Assert.True(errors.Count == 0, "erros no navegador:\n" + string.Join("\n", errors));
    }

    [SkippableTheory]
    [InlineData("/")]
    [InlineData("/entrar")]
    [InlineData("/cadastro")]
    [InlineData("/termos")]
    [InlineData("/privacidade")]
    [InlineData("/recuperar-senha")]
    [InlineData("/pagina-que-nao-existe")]
    public async Task SupercellNotice_IsOnEveryPublicPage(string path)
    {
        Skip.If(AppFixture.Skip is not null, AppFixture.Skip);
        await using var ctx = await app.NewContextAsync();
        var page = await ctx.NewPageAsync();
        await page.GotoAsync(path);
        await page.WaitForBlazorAsync();
        await Assertions.Expect(page.Locator("footer").GetByText(Notice)).ToBeVisibleAsync();
    }

    [SkippableFact]
    public async Task PrivatePages_RedirectAnonymousVisitorsToLogin()
    {
        Skip.If(AppFixture.Skip is not null, AppFixture.Skip);
        await using var ctx = await app.NewContextAsync();
        var page = await ctx.NewPageAsync();
        await page.GotoAsync("/app");
        await page.WaitForBlazorAsync();
        await page.WaitForURLAsync(new System.Text.RegularExpressions.Regex("/entrar"));
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Entrar" })).ToBeVisibleAsync();
        Assert.Contains("returnUrl", page.Url);
    }

    [SkippableFact]
    public async Task Pwa_ManifestIsInstallable_AndServiceWorkerRegisters()
    {
        Skip.If(AppFixture.Skip is not null, AppFixture.Skip);
        await using var ctx = await app.NewContextAsync();
        var page = await ctx.NewPageAsync();
        await page.GotoAsync("/");
        await page.WaitForBlazorAsync();

        var manifestHref = await page.EvaluateAsync<string>("document.querySelector('link[rel=manifest]').href");
        Assert.EndsWith("/manifest.webmanifest", manifestHref);
        var manifest = await page.EvaluateAsync<System.Text.Json.JsonElement>("fetch('/manifest.webmanifest').then(r => r.json())");
        Assert.Equal("standalone", manifest.GetProperty("display").GetString());
        foreach (var icon in manifest.GetProperty("icons").EnumerateArray())
        {
            var status = await page.EvaluateAsync<int>($"fetch('/{icon.GetProperty("src").GetString()}').then(r => r.status)");
            Assert.Equal(200, status);
        }

        var registered = await page.EvaluateAsync<bool>(@"navigator.serviceWorker.ready.then(r => !!r.active)");
        Assert.True(registered);
        var theme = await page.EvaluateAsync<string>("document.querySelector('meta[name=theme-color]').content");
        Assert.Matches("^#[0-9a-f]{6}$", theme);
    }

    [SkippableFact]
    public async Task OfflineBanner_AppearsWhenConnectionDrops_AndDisappearsWhenItReturns()
    {
        Skip.If(AppFixture.Skip is not null, AppFixture.Skip);
        await using var ctx = await app.NewContextAsync();
        var page = await ctx.NewPageAsync();
        await page.GotoAsync("/");
        await page.WaitForBlazorAsync();
        await Assertions.Expect(page.GetByText("Você está sem conexão")).ToHaveCountAsync(0);

        await ctx.SetOfflineAsync(true);
        await Assertions.Expect(page.GetByText("Você está sem conexão")).ToBeVisibleAsync();
        await ctx.SetOfflineAsync(false);
        await Assertions.Expect(page.GetByText("Você está sem conexão")).ToHaveCountAsync(0);
    }

    [SkippableFact]
    public async Task MobileViewport_HasNoHorizontalScroll_OnPublicPages()
    {
        Skip.If(AppFixture.Skip is not null, AppFixture.Skip);
        await using var ctx = await app.NewContextAsync(new() { ViewportSize = new() { Width = 375, Height = 812 }, IsMobile = true, HasTouch = true });
        var page = await ctx.NewPageAsync();
        foreach (var path in new[] { "/", "/entrar", "/cadastro", "/termos" })
        {
            await page.GotoAsync(path);
            await page.WaitForBlazorAsync();
            var overflow = await page.EvaluateAsync<int>("document.documentElement.scrollWidth - window.innerWidth");
            Assert.True(overflow <= 1, $"{path} rolou {overflow}px na horizontal em 375px");
        }
    }

    [SkippableFact]
    public async Task DarkModeToggle_ChangesTheTheme_AndSurvivesReload()
    {
        Skip.If(AppFixture.Skip is not null, AppFixture.Skip);
        await using var ctx = await app.NewContextAsync(new() { ColorScheme = ColorScheme.Light });
        var page = await ctx.NewPageAsync();
        await page.GotoAsync("/");
        await page.WaitForBlazorAsync();
        var lightBg = await page.EvaluateAsync<string>("getComputedStyle(document.body).backgroundColor");

        await page.GetByLabel("Alternar tema").ClickAsync();
        await page.WaitForTimeoutAsync(400);
        var darkBg = await page.EvaluateAsync<string>("getComputedStyle(document.querySelector('.mud-layout') ?? document.body).backgroundColor");
        Assert.NotEqual(lightBg, darkBg);

        await page.ReloadAsync();
        await page.WaitForBlazorAsync();
        Assert.Equal("dark", await page.EvaluateAsync<string>("localStorage.getItem('clanhub.theme')"));
    }

    [SkippableFact]
    public async Task ApiAndSpaRoutes_AreServedByTheSameOrigin()
    {
        Skip.If(AppFixture.Skip is not null, AppFixture.Skip);
        using var http = new HttpClient { BaseAddress = new Uri(app.BaseUrl) };
        Assert.Equal(System.Net.HttpStatusCode.OK, (await http.GetAsync("/api/v1/games")).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await http.GetAsync("/api/nao-existe")).StatusCode);          // API não cai no index.html
        var spa = await http.GetStringAsync("/app/algum/caminho");
        Assert.Contains("<div id=\"app\">", spa);                                                                       // rota do SPA cai no index.html
    }
}