using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace ClanHub.E2E.Infrastructure;

/// <summary>Passos de interface reutilizáveis pelos testes (cadastro, login, criação de workspace e clã).</summary>
public static class Journeys
{
    public const string Password = "Str0ng!Passw0rd";

    public static string NewEmail() => $"e2e-{Guid.NewGuid():N}@test.local";

    public static async Task SignUpConfirmAndLoginAsync(this IPage page, AppFixture app, string email, string name = "Ana Teste")
    {
        await page.GotoAsync("/cadastro");
        await page.WaitForBlazorAsync();
        await page.GetByLabel("Nome", new() { Exact = true }).FillAsync(name);
        await page.GetByLabel("E-mail").FillAsync(email);
        await page.GetByLabel("Senha").FillAsync(Password);
        await page.GetByLabel("Li e aceito").CheckAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Cadastrar", Exact = true }).ClickAsync();
        await Assertions.Expect(page.GetByText("Conta criada!")).ToBeVisibleAsync();

        var link = await app.Smtp.WaitForLinkAsync("/confirmar-email", TimeSpan.FromSeconds(20));
        await page.GotoAsync(link);
        await Assertions.Expect(page.GetByText("E-mail confirmado!")).ToBeVisibleAsync();

        await page.LoginAsync(email);
    }

    public static async Task LoginAsync(this IPage page, string email)
    {
        await page.GotoAsync("/entrar");
        await page.WaitForBlazorAsync();
        await page.GetByLabel("E-mail").FillAsync(email);
        await page.GetByLabel("Senha").FillAsync(Password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Entrar", Exact = true }).ClickAsync();
        await page.WaitForURLAsync(new Regex("/app"), new() { Timeout = 20000 });
    }

    /// <summary>Cria o workspace pelo "Primeiros passos" e adiciona o clã #2PP pelo código na descrição.</summary>
    public static async Task<(Guid Ws, Guid Clan)> CreateWorkspaceAndClanAsync(this IPage page, AppFixture app, string workspaceName = "Família E2E")
    {
        await page.WaitForURLAsync(new Regex("/app/bem-vindo"), new() { Timeout = 20000 });
        await page.GetByLabel("Nome do workspace").FillAsync(workspaceName);
        await page.GetByRole(AriaRole.Button, new() { Name = "Criar e continuar" }).ClickAsync();

        await page.GetByLabel("Tag do clã").FillAsync("#2PP");
        await page.GetByRole(AriaRole.Button, new() { Name = "Ver prévia" }).ClickAsync();
        await Assertions.Expect(page.GetByText("Clã Teste").First).ToBeVisibleAsync();

        await page.GetByText("Código na descrição do clã").ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Iniciar verificação" }).ClickAsync();
        var codeText = await page.Locator("h4").Filter(new() { HasTextRegex = new Regex(@"^CH-[A-Z0-9]{5}$") }).InnerTextAsync();

        app.SetClanDescription($"Bem-vindos! {codeText}");
        await page.GetByRole(AriaRole.Button, new() { Name = "Verificar agora" }).ClickAsync();
        await page.WaitForURLAsync(new Regex(@"/app/[0-9a-f-]{36}/cla/[0-9a-f-]{36}"), new() { Timeout = 20000 });

        var m = Regex.Match(page.Url, @"/app/(?<ws>[0-9a-f-]{36})/cla/(?<clan>[0-9a-f-]{36})");
        return (Guid.Parse(m.Groups["ws"].Value), Guid.Parse(m.Groups["clan"].Value));
    }
}