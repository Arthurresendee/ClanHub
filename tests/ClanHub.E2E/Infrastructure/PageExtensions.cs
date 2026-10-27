using Microsoft.Playwright;

namespace ClanHub.E2E.Infrastructure;

public static class PageExtensions
{
    /// <summary>Coleta erros de JavaScript e mensagens de console de nível error durante o teste.</summary>
    public static List<string> CaptureErrors(this IPage page)
    {
        var errors = new List<string>();
        page.PageError += (_, e) => errors.Add("pageerror: " + e);
        page.Console += (_, m) =>
        {
            if (m.Type == "error" && !m.Text.Contains("favicon") && !m.Text.Contains("Failed to load resource")) errors.Add("console: " + m.Text);
        };
        return errors;
    }

    /// <summary>Espera o Blazor WebAssembly terminar de carregar (some a tela de boot).</summary>
    public static async Task WaitForBlazorAsync(this IPage page)
    {
        await page.WaitForSelectorAsync(".boot", new() { State = WaitForSelectorState.Detached, Timeout = 60000 });
    }
}