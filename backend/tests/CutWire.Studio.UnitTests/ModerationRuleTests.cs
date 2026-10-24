using CutWire.Studio.Domain.Accounts;
using CutWire.Studio.Domain.Catalog;
using CutWire.Studio.Domain.Moderation;
using Shouldly;

namespace CutWire.Studio.UnitTests;

public class ModerationRuleTests
{
    private static readonly AutoHideThresholds Defaults = new();

    [Theory]
    [InlineData(0, 8, 0, "community_downvoted")]
    [InlineData(3, 8, 0, "community_downvoted")]
    [InlineData(3.5, 8.5, 0, "community_downvoted")]
    [InlineData(4, 8, 0, null)]
    [InlineData(0, 7.5, 0, null)]
    [InlineData(1, 7, 0, null)]
    [InlineData(0, 0, 2, null)]
    [InlineData(0, 0, 3, "community_reported")]
    [InlineData(20, 2, 5, "community_reported")]
    [InlineData(0, 8, 3, "community_downvoted")]
    [InlineData(0, 0, 0, null)]
    public void AutoHide_Rule(double up, double down, int reporters, string? expected) =>
        AutoHideRule.Evaluate(up, down, reporters, Defaults).ShouldBe(expected);

    [Fact]
    public void AutoHide_UsesConfiguredThresholds() =>
        AutoHideRule.Evaluate(0, 2, 1, new AutoHideThresholds(2, 0.5, 1)).ShouldBe("community_downvoted");

    [Fact]
    public void VoteWeight_HalfForYoungT0Only()
    {
        var now = DateTimeOffset.UtcNow;
        var three = TimeSpan.FromDays(3);
        Vote.WeightFor(TrustLevel.T0, now.AddDays(-1), now, three, 0.5).ShouldBe(0.5);
        Vote.WeightFor(TrustLevel.T0, now.AddDays(-3), now, three, 0.5).ShouldBe(1.0);
        Vote.WeightFor(TrustLevel.T1, now.AddHours(-1), now, three, 0.5).ShouldBe(1.0);
    }

    [Theory]
    [InlineData("spam", true)]
    [InlineData("low_quality", true)]
    [InlineData("other", true)]
    [InlineData("LowQuality", false)]
    [InlineData("", false)]
    public void ReportReason_Parses(string key, bool ok)
    {
        ReportReasonExtensions.TryParse(key, out var reason).ShouldBe(ok);
        if (ok) reason.ToKey().ShouldBe(key);
    }
}
