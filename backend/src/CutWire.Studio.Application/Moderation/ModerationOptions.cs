using CutWire.Studio.Domain.Moderation;

namespace CutWire.Studio.Application.Moderation;

/// <summary>Config section <c>Moderation</c>: auto-hide thresholds, vote weights and trust-level rules.</summary>
public sealed class ModerationOptions
{
    public const string Section = "Moderation";

    public double AutoHideMinDownvotes { get; set; } = 8;
    public double AutoHideMinDownShare { get; set; } = 0.7;
    public int AutoHideMinReports { get; set; } = 3;
    public TimeSpan PurgeDelay { get; set; } = TimeSpan.FromDays(14);

    public TimeSpan NewAccountVoteAge { get; set; } = TimeSpan.FromDays(3);
    public double NewAccountVoteWeight { get; set; } = 0.5;

    public TimeSpan TrustT1AccountAge { get; set; } = TimeSpan.FromDays(7);
    public TimeSpan TrustT1CleanPeriod { get; set; } = TimeSpan.FromDays(7);
    public int TrustT2MinItems { get; set; } = 3;

    public AutoHideThresholds Thresholds => new(AutoHideMinDownvotes, AutoHideMinDownShare, AutoHideMinReports);
}
