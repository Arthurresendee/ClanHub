using ClanHub.Application.Games;
using ClanHub.Application.Members;
using ClanHub.Domain;

namespace ClanHub.UnitTests;

public class MemberDifferTests
{
    private static ClanMemberSnapshot M(string tag, string name = "N", string role = "member", int th = 14, int donations = 0) =>
        new(tag, name, role, 100, 4000, donations, 0, th, 1);

    private static KnownMember K(string tag, string name = "N", string role = "member", int th = 14, int donations = 0) =>
        new(tag, name, role, th, donations);

    [Fact]
    public void FirstSync_IsBaseline_NoEvents() =>
        Assert.Empty(MemberDiffer.Diff([], [M("#A"), M("#B")], isFirstSync: true));

    [Fact]
    public void NoChange_NoEvents() =>
        Assert.Empty(MemberDiffer.Diff([K("#A")], [M("#A")], false));

    [Fact]
    public void Detects_Join_And_Leave()
    {
        var events = MemberDiffer.Diff([K("#A"), K("#B")], [M("#A"), M("#C")], false);
        Assert.Contains(events, e => e.Type == ClanEventType.MemberJoined && e.PlayerTag == "#C");
        Assert.Contains(events, e => e.Type == ClanEventType.MemberLeft && e.PlayerTag == "#B");
        Assert.Equal(2, events.Count);
    }

    [Fact]
    public void Detects_Role_Name_TownHall_Donations()
    {
        var events = MemberDiffer.Diff([K("#A", "Old", "member", 14, 10)], [M("#A", "New", "elder", 15, 25)], false);
        Assert.Equal(
            new[] { ClanEventType.NameChanged, ClanEventType.RoleChanged, ClanEventType.TownHallUpgraded, ClanEventType.DonationsChanged }.OrderBy(x => x),
            events.Select(e => e.Type).OrderBy(x => x));
    }

    [Fact]
    public void DonationDrop_IsSeasonReset_NotAnEvent()
    {
        var before = new[] { K("#A", donations: 900) };
        var after = new[] { M("#A", donations: 0) };
        Assert.Empty(MemberDiffer.Diff(before, after, false));
        Assert.True(MemberDiffer.DetectSeasonReset(before, after));
        Assert.False(MemberDiffer.DetectSeasonReset(before, new[] { M("#A", donations: 950) }));
    }
}