using System.Text.RegularExpressions;
using ClanHub.E2E.Infrastructure;
using Microsoft.Playwright;

namespace ClanHub.E2E;

[Collection("e2e")]
public class JourneyTests(AppFixture app)
{
    [SkippableFact]
    public async Task Signup_Login_CreateWorkspace_ClaimClan_ThenSeeMembers()
    {
        Skip.If(AppFixture.Skip is not null, AppFixture.Skip);
        await using var ctx = await app.NewContextAsync();
        var page = await ctx.NewPageAsync();
        var errors = page.CaptureErrors();
        var email = Journeys.NewEmail();

        await page.SignUpConfirmAndLoginAsync(app, email);
        var (ws, clan) = await page.CreateWorkspaceAndClanAsync(app);

        // visão geral do clã recém-adicionado
        await Assertions.Expect(page.GetByText("Clã Teste").First).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByText("Atividade recente")).ToBeVisibleAsync();

        // a sincronização (cron) traz os membros
        await app.RunTickAsync();
        await page.GetByRole(AriaRole.Link, new() { Name = "Membros", Exact = true }).ClickAsync();
        await Assertions.Expect(page.GetByText("Líder", new() { Exact = true }).First).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByText("Membro", new() { Exact = true }).First).ToBeVisibleAsync();

        // o clã aparece na lista de workspaces
        await page.GotoAsync("/app");
        await page.WaitForBlazorAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Família E2E" })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Clã Teste" }).First).ToBeVisibleAsync();

        var real = errors.Where(e => !e.Contains("401")).ToList();
        Assert.True(real.Count == 0, "erros no navegador:\n" + string.Join("\n", real));
    }

    [SkippableFact]
    public async Task EveryScreen_RendersWithoutRuntimeErrors()
    {
        Skip.If(AppFixture.Skip is not null, AppFixture.Skip);
        await using var ctx = await app.NewContextAsync();
        var page = await ctx.NewPageAsync();
        var errors = page.CaptureErrors();
        await page.SignUpConfirmAndLoginAsync(app, Journeys.NewEmail());
        var (ws, clan) = await page.CreateWorkspaceAndClanAsync(app);
        await app.RunTickAsync();

        var screens = new (string Path, string Expect)[]
        {
            ($"/app/{ws}/cla/{clan}", "Atividade recente"),
            ($"/app/{ws}/cla/{clan}/membros", "Buscar por nome ou tag"),
            ($"/app/{ws}/cla/{clan}/atividade", "Atualiza sozinho a cada 20 segundos"),
            ($"/app/{ws}/cla/{clan}/guerra", "Nenhuma guerra em andamento"),
            ($"/app/{ws}/cla/{clan}/guerras", "Guerras"),
            ($"/app/{ws}/cla/{clan}/cwl", "não está em uma Liga"),
            ($"/app/{ws}/cla/{clan}/capital", "Ainda sem dados"),
            ($"/app/{ws}/cla/{clan}/temporada", "Temporada"),
            ($"/app/{ws}/cla/{clan}/ranking", "Ainda sem posição registrada"),
            ($"/app/{ws}/cla/{clan}/recrutamento", "Nenhuma candidatura"),
            ($"/app/{ws}/cla/{clan}/alertas", "Novo alerta"),
            ($"/app/{ws}/cla/{clan}/configuracoes", "Página pública de recrutamento"),
            ($"/app/{ws}/equipe", "Convidar"),
            ($"/app/{ws}/configuracoes", "Registro de ações"),
            ("/app", "Meus workspaces"),
            ("/app/conta", "Alterar senha"),
            ("/app/notificacoes", "Notificações"),
            ("/app/torneios", "Torneios do Clash Royale"),
        };
        foreach (var (path, expect) in screens)
        {
            await page.GotoAsync(path);
            await page.WaitForBlazorAsync();
            await Assertions.Expect(page.GetByText(expect).First).ToBeVisibleAsync(new() { Timeout = 15000 });
            Assert.False(await page.Locator("#blazor-error-ui").IsVisibleAsync(), $"a tela {path} disparou o aviso de erro do Blazor");
        }
        var real = errors.Where(e => !e.Contains("401") && !e.Contains("404")).ToList();
        Assert.True(real.Count == 0, "erros no navegador:\n" + string.Join("\n", real));
    }

    [SkippableFact]
    public async Task Recruitment_PublicPage_Application_AndReview()
    {
        Skip.If(AppFixture.Skip is not null, AppFixture.Skip);
        await using var owner = await app.NewContextAsync();
        var page = await owner.NewPageAsync();
        await page.SignUpConfirmAndLoginAsync(app, Journeys.NewEmail());
        var (ws, clan) = await page.CreateWorkspaceAndClanAsync(app);

        var slug = $"e2e-{Guid.NewGuid():N}"[..16];
        await page.GotoAsync($"/app/{ws}/cla/{clan}/configuracoes");
        await page.GetByLabel("Endereço").FillAsync(slug);
        await page.GetByLabel("Texto de recrutamento").FillAsync("Venha jogar com a gente!");
        await page.GetByLabel("Página pública ativa").CheckAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Salvar" }).ClickAsync();
        await Assertions.Expect(page.GetByText("Configurações salvas.")).ToBeVisibleAsync();

        // visitante anônimo se candidata
        await using var visitor = await app.NewContextAsync();
        var pub = await visitor.NewPageAsync();
        await pub.GotoAsync($"/c/{slug}");
        await pub.WaitForBlazorAsync();
        await Assertions.Expect(pub.GetByText("Venha jogar com a gente!")).ToBeVisibleAsync();
        await pub.GetByLabel("Sua tag de jogador").FillAsync("#PPP2");
        await pub.GetByLabel("Contato").FillAsync("discord: visitante");
        await pub.GetByRole(AriaRole.Button, new() { Name = "Enviar candidatura" }).ClickAsync();
        await Assertions.Expect(pub.GetByText("Candidatura enviada!")).ToBeVisibleAsync();

        // a liderança avalia
        await page.GotoAsync($"/app/{ws}/cla/{clan}/recrutamento");
        await Assertions.Expect(page.GetByText("discord: visitante")).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Aprovar" }).ClickAsync();
        await Assertions.Expect(page.GetByText("Nenhuma candidatura")).ToBeVisibleAsync();
    }

    [SkippableFact]
    public async Task Alerts_CanBeCreated_AndListed()
    {
        Skip.If(AppFixture.Skip is not null, AppFixture.Skip);
        await using var ctx = await app.NewContextAsync();
        var page = await ctx.NewPageAsync();
        await page.SignUpConfirmAndLoginAsync(app, Journeys.NewEmail());
        var (ws, clan) = await page.CreateWorkspaceAndClanAsync(app);

        await page.GotoAsync($"/app/{ws}/cla/{clan}/alertas");
        await Assertions.Expect(page.GetByText("Nenhum alerta configurado.")).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Criar alerta" }).ClickAsync();
        await Assertions.Expect(page.GetByText("Alerta criado.")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByText("Nenhum alerta configurado.")).ToHaveCountAsync(0);
        await Assertions.Expect(page.GetByText("Membro saiu do clã").First).ToBeVisibleAsync();
    }

    [SkippableFact]
    public async Task TeamInvite_LinkLetsAnotherUserJoin()
    {
        Skip.If(AppFixture.Skip is not null, AppFixture.Skip);
        await using var owner = await app.NewContextAsync();
        var page = await owner.NewPageAsync();
        await page.SignUpConfirmAndLoginAsync(app, Journeys.NewEmail());
        var (ws, _) = await page.CreateWorkspaceAndClanAsync(app);

        await page.GotoAsync($"/app/{ws}/equipe");
        await page.GetByRole(AriaRole.Button, new() { Name = "Gerar convite" }).ClickAsync();
        var inviteText = await page.Locator(".mud-alert").Filter(new() { HasText = "Envie este link" }).InnerTextAsync();
        var link = Regex.Match(inviteText, @"https?://\S+/convite/\S+").Value;
        Assert.False(string.IsNullOrEmpty(link), inviteText);

        await using var guest = await app.NewContextAsync();
        var gp = await guest.NewPageAsync();
        await gp.SignUpConfirmAndLoginAsync(app, Journeys.NewEmail(), "Convidada");
        await gp.GotoAsync(link);
        await Assertions.Expect(gp.GetByText("Você entrou no workspace")).ToBeVisibleAsync();

        await page.ReloadAsync();
        await page.WaitForBlazorAsync();
        await Assertions.Expect(page.GetByText("Convidada")).ToBeVisibleAsync();
    }

    [SkippableFact]
    public async Task DeleteAccount_RemovesAccess()
    {
        Skip.If(AppFixture.Skip is not null, AppFixture.Skip);
        await using var ctx = await app.NewContextAsync();
        var page = await ctx.NewPageAsync();
        var email = Journeys.NewEmail();
        await page.SignUpConfirmAndLoginAsync(app, email);

        await page.GotoAsync("/app/conta");
        await page.GetByRole(AriaRole.Button, new() { Name = "Excluir minha conta" }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Excluir", Exact = true }).ClickAsync();
        await page.WaitForURLAsync(new Regex(@"/$"));

        await page.GotoAsync("/entrar");
        await page.WaitForBlazorAsync();
        await page.GetByLabel("E-mail").FillAsync(email);
        await page.GetByLabel("Senha").FillAsync(Journeys.Password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Entrar", Exact = true }).ClickAsync();
        await Assertions.Expect(page.GetByText("E-mail ou senha incorretos.")).ToBeVisibleAsync();
    }

    [SkippableFact]
    public async Task Passkey_RegisterInTheBrowser_ThenLoginWithoutPassword()
    {
        Skip.If(AppFixture.Skip is not null, AppFixture.Skip);
        await using var ctx = await app.NewContextAsync();
        var page = await ctx.NewPageAsync();
        var email = Journeys.NewEmail();
        await page.SignUpConfirmAndLoginAsync(app, email);

        // autenticador virtual do Chrome (CDP)
        var cdp = await ctx.NewCDPSessionAsync(page);
        await cdp.SendAsync("WebAuthn.enable");
        await cdp.SendAsync("WebAuthn.addVirtualAuthenticator", new Dictionary<string, object>
        {
            ["options"] = new Dictionary<string, object>
            {
                ["protocol"] = "ctap2", ["transport"] = "internal", ["hasResidentKey"] = true,
                ["hasUserVerification"] = true, ["isUserVerified"] = true, ["automaticPresenceSimulation"] = true,
            },
        });

        await page.GotoAsync("/app/conta");
        await page.WaitForBlazorAsync();
        await page.GetByLabel("Nome (ex.: Meu celular)").FillAsync("Chrome de teste");
        await page.GetByRole(AriaRole.Button, new() { Name = "Adicionar passkey" }).ClickAsync();
        await Assertions.Expect(page.GetByText("Passkey adicionada.")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByText("Chrome de teste")).ToBeVisibleAsync();

        // sai e entra só com a passkey
        await page.GetByLabel("Menu da conta").ClickAsync();
        await page.GetByText("Sair", new() { Exact = true }).ClickAsync();
        await page.WaitForURLAsync(new Regex(@"/$"));

        await page.GotoAsync("/entrar");
        await page.WaitForBlazorAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Entrar com passkey" }).ClickAsync();
        await page.WaitForURLAsync(new Regex("/app"), new() { Timeout = 20000 });
        await Assertions.Expect(page.GetByText("Você ainda não tem nenhum workspace").Or(page.GetByText("Primeiros passos"))).ToBeVisibleAsync();
    }
}