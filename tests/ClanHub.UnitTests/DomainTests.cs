using ClanHub.Application.Workspaces;
using ClanHub.Domain;
using ClanHub.Sync;

namespace ClanHub.UnitTests;

public class TagsTests
{
    [Theory]
    [InlineData("2pp", "#2PP")]
    [InlineData("#2pp", "#2PP")]
    [InlineData(" #2OO ", "#200")]   // letra O vira zero
    [InlineData("", "")]
    public void Normalize(string input, string expected) => Assert.Equal(expected, Tags.Normalize(input));

    [Theory]
    [InlineData("#2PP", true)]
    [InlineData("#PPP2", true)]
    [InlineData("#ABC", false)]      // A, B não são caracteres de tag
    [InlineData("#2", false)]
    [InlineData("", false)]
    public void IsValid(string tag, bool expected) => Assert.Equal(expected, Tags.IsValid(tag));

    [Fact]
    public void ToUrl_EncodesHash() => Assert.Equal("%232PP", Tags.ToUrl("2pp"));
}

public class PermissionsTests
{
    [Theory]
    [InlineData(WorkspaceRole.Viewer, true, false, false, false)]
    [InlineData(WorkspaceRole.Staff, true, true, false, false)]
    [InlineData(WorkspaceRole.Admin, true, true, true, false)]
    [InlineData(WorkspaceRole.Owner, true, true, true, true)]
    public void FollowsPlanTable(WorkspaceRole role, bool view, bool notes, bool clans, bool delete)
    {
        Assert.Equal(view, WorkspacePermissions.CanView(role));
        Assert.Equal(notes, WorkspacePermissions.CanManageNotes(role));
        Assert.Equal(clans, WorkspacePermissions.CanManageClans(role));
        Assert.Equal(clans, WorkspacePermissions.CanManageTeam(role));
        Assert.Equal(delete, WorkspacePermissions.CanDeleteWorkspace(role));
    }
}

public class ClaimCodeTests
{
    [Fact]
    public void Generate_HasExpectedShape()
    {
        for (var i = 0; i < 50; i++)
        {
            var code = ClaimCodes.Generate();
            Assert.Matches("^CH-[2-9A-HJ-NP-Z]{5}$", code);
        }
    }

    [Fact]
    public void DescriptionContains_IsCaseInsensitiveAndNullSafe()
    {
        Assert.True(ClaimCodes.DescriptionContains("Bem-vindos! ch-7f3k2 aqui", "CH-7F3K2"));
        Assert.False(ClaimCodes.DescriptionContains("sem código", "CH-7F3K2"));
        Assert.False(ClaimCodes.DescriptionContains(null, "CH-7F3K2"));
    }
}

public class SlugTests
{
    [Theory]
    [InlineData("Família Dragões", "familia-dragoes")]
    [InlineData("  Meu  Clã!! ", "meu-cla")]
    [InlineData("???", "workspace")]
    public void From(string name, string slug) => Assert.Equal(slug, Slugs.From(name));
}

public class BackoffTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(6, 32)]
    [InlineData(7, 60)]
    [InlineData(50, 60)]
    public void IsExponentialAndCapped(int fails, int minutes) =>
        Assert.Equal(TimeSpan.FromMinutes(minutes), Backoff.For(fails, 60));
}

public class SyncTargetsTests
{
    [Fact]
    public void KindsDependOnGame()
    {
        Assert.Contains(SyncKind.CurrentWar, SyncTargets.KindsFor(Game.ClashOfClans));
        Assert.DoesNotContain(SyncKind.RiverRace, SyncTargets.KindsFor(Game.ClashOfClans));
        Assert.Contains(SyncKind.RiverRace, SyncTargets.KindsFor(Game.ClashRoyale));
        Assert.Equal([SyncKind.Members, SyncKind.PlayerProfiles, SyncKind.Alerts, SyncKind.Rankings], SyncTargets.KindsFor(Game.BrawlStars));
        Assert.Contains(SyncKind.CwlGroup, SyncTargets.KindsFor(Game.ClashOfClans));
        Assert.Contains(SyncKind.CapitalRaids, SyncTargets.KindsFor(Game.ClashOfClans));
        Assert.DoesNotContain(SyncKind.CwlGroup, SyncTargets.KindsFor(Game.ClashRoyale));
    }
}