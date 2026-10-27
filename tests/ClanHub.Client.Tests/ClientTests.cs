using Bunit;
using Bunit.TestDoubles;
using ClanHub.Client.Components;
using ClanHub.Client.Pages;
using ClanHub.Client.Services;
using ClanHub.Contracts;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;

namespace ClanHub.Client.Tests;

public class FormatTests
{
    [Fact]
    public void Describe_RoleChange_UsesPortugueseLabels()
    {
        var e = new EventDto(1, "RoleChanged", "#PPP2", "{\"from\":\"member\",\"to\":\"coLeader\"}", DateTime.UtcNow);
        Assert.Equal("#PPP2 mudou de cargo: Membro → Co-líder", Format.Describe(e));
    }

    [Fact]
    public void Describe_UnknownType_FallsBackToTypeName() =>
        Assert.Equal("Algo", Format.Describe(new EventDto(1, "Algo", null, null, DateTime.UtcNow)));

    [Fact]
    public void Describe_Phase2Events()
    {
        Assert.Equal("Rodada 3 da CWL começou contra Rivais",
            Format.Describe(new EventDto(1, "CwlRoundStarted", null, "{\"round\":3,\"opponentName\":\"Rivais\"}", DateTime.UtcNow)));
        Assert.Equal("Fulano atacou a Capital (4/6), saque de 900",
            Format.Describe(new EventDto(2, "RaidAttackMade", "#PPP2", "{\"name\":\"Fulano\",\"attacks\":4,\"limit\":6,\"looted\":900}", DateTime.UtcNow)));
    }

    [Theory]
    [InlineData("member", "Membro")]
    [InlineData("coLeader", "Co-líder")]
    [InlineData("president", "Líder")]
    [InlineData("senior", "Ancião")]
    [InlineData("estranho", "estranho")]
    public void Role_Labels(string raw, string expected) => Assert.Equal(expected, Format.Role(raw));

    [Fact]
    public void EventIcons_AreNeverEmpty()
    {
        foreach (var type in new[] { "MemberJoined", "MemberLeft", "RoleChanged", "WarStarted", "DonationsChanged", "RiverRaceEnded", "CwlRoundStarted", "RaidAttackMade", "Qualquer" })
            Assert.False(string.IsNullOrEmpty(Format.EventIcon(type)));
    }

    [Theory]
    [InlineData("win", "Vitória")]
    [InlineData("lose", "Derrota")]
    [InlineData(null, "—")]
    public void WarResult(string? raw, string expected) => Assert.Equal(expected, Format.WarResult(raw));

    [Fact]
    public void Ago_Handles_Null_And_Recent()
    {
        Assert.Equal("nunca", Format.Ago(null));
        Assert.Equal("agora há pouco", Format.Ago(DateTime.UtcNow));
        Assert.Equal("há 3 dias", Format.Ago(DateTime.UtcNow.AddDays(-3).AddMinutes(-1)));
    }
}

public class ComponentTests : BunitContext, IAsyncLifetime
{
    public Task InitializeAsync() => Task.CompletedTask;
    async Task IAsyncLifetime.DisposeAsync() => await base.DisposeAsync();

    public ComponentTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddLocalization();
    }

    [Fact]
    public void ClanBadge_RendersImage_OnlyWhenUrlExists()
    {
        Assert.Contains("<img", Render<ClanBadge>(p => p.Add(x => x.Url, "https://img/b.png")).Markup);
        Assert.DoesNotContain("<img", Render<ClanBadge>().Markup);
    }

    [Fact]
    public void PwaStatus_ShowsInstallButton_WhenBrowserOffersIt()
    {
        JSInterop.Setup<bool>("clanhub.isOnline").SetResult(true);
        JSInterop.Setup<bool>("clanhub.isStandalone").SetResult(false);
        JSInterop.Setup<bool>("clanhub.canInstall").SetResult(true);
        JSInterop.Setup<bool>("clanhub.isIos").SetResult(false);
        var cut = Render<PwaStatus>();
        cut.WaitForAssertion(() => Assert.Contains("Instalar app", cut.Markup));
        Assert.DoesNotContain("sem conexão", cut.Markup);
    }

    [Fact]
    public void PwaStatus_HidesInstallButton_WhenAlreadyInstalled()
    {
        JSInterop.Setup<bool>("clanhub.isOnline").SetResult(true);
        JSInterop.Setup<bool>("clanhub.isStandalone").SetResult(true);
        JSInterop.Setup<bool>("clanhub.canInstall").SetResult(true);
        JSInterop.Setup<bool>("clanhub.isIos").SetResult(false);
        var cut = Render<PwaStatus>();
        cut.WaitForState(() => JSInterop.Invocations.Count >= 2);
        Assert.DoesNotContain("Instalar app", cut.Markup);
    }

    [Fact]
    public void PwaStatus_ShowsOfflineBanner_WhenThereIsNoConnection()
    {
        JSInterop.Setup<bool>("clanhub.isOnline").SetResult(false);
        JSInterop.Setup<bool>("clanhub.isStandalone").SetResult(false);
        JSInterop.Setup<bool>("clanhub.canInstall").SetResult(false);
        JSInterop.Setup<bool>("clanhub.isIos").SetResult(false);
        var cut = Render<PwaStatus>();
        cut.WaitForAssertion(() => Assert.Contains("sem conexão", cut.Markup));
    }

    [Fact]
    public void Home_ShowsSignupCall_ForAnonymousVisitors()
    {
        AddAuthorization();
        Render<MudPopoverProvider>();
        var cut = Render<Home>();
        Assert.Contains("Criar conta grátis", cut.Markup);
        Assert.Contains("não é possível expulsar", cut.Markup);
    }
}