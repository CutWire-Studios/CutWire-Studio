using CutWire.Studio.Domain.Common;

namespace CutWire.Studio.Domain.Moderation;

public static class VisibilityReasons
{
    public const string CommunityDownvoted = "community_downvoted";
    public const string CommunityReported = "community_reported";
    public const string OwnerBanned = "owner_banned";
}

public sealed record AutoHideThresholds(double MinDownvotes = 8, double MinDownShare = 0.7, int MinReporters = 3);

public static class AutoHideRule
{
    /// <summary>
    /// Returns the visibility reason when an item must be hidden, else null. <paramref name="trustedReporters"/> is the number of
    /// distinct T1+ users with an open report.
    /// </summary>
    public static string? Evaluate(double weightedUp, double weightedDown, int trustedReporters, AutoHideThresholds t)
    {
        var total = weightedUp + weightedDown;
        if (weightedDown >= t.MinDownvotes && total > 0 && weightedDown / total >= t.MinDownShare)
            return VisibilityReasons.CommunityDownvoted;
        if (trustedReporters >= t.MinReporters) return VisibilityReasons.CommunityReported;
        return null;
    }
}

public enum ReportReason
{
    Spam,
    LowQuality,
    Broken,
    Offensive,
    Copyright,
    Malware,
    Other,
}

public static class ReportReasonExtensions
{
    public static string ToKey(this ReportReason reason) => reason switch
    {
        ReportReason.LowQuality => "low_quality",
        _ => reason.ToString().ToLowerInvariant(),
    };

    public static bool TryParse(string? key, out ReportReason reason)
    {
        foreach (var candidate in Enum.GetValues<ReportReason>())
            if (candidate.ToKey() == key)
            {
                reason = candidate;
                return true;
            }

        reason = default;
        return false;
    }
}

public enum ReportStatus
{
    Open,
    Upheld,
    Dismissed,
}

public sealed class Report : Entity<Guid>
{
    public const int MaxNoteLength = 500;

    private Report() { }

    public Report(Guid id, Guid itemId, Guid reporterId, ReportReason reason, string? note, DateTimeOffset now)
    {
        Id = id;
        ItemId = itemId;
        ReporterId = reporterId;
        Reason = reason;
        Note = note;
        Status = ReportStatus.Open;
        CreatedAt = now;
    }

    public Guid ItemId { get; private set; }
    public Guid ReporterId { get; private set; }
    public ReportReason Reason { get; private set; }
    public string? Note { get; private set; }
    public ReportStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }
    public Guid? ResolvedBy { get; private set; }

    public void Resolve(ReportStatus status, Guid? by, DateTimeOffset now)
    {
        Status = status;
        ResolvedAt = now;
        ResolvedBy = by;
    }
}

/// <summary>Audit row. <see cref="ActorId"/> is null for the system (<see cref="Auto"/> is then true).</summary>
public sealed class ModerationAction : Entity<Guid>
{
    private ModerationAction() { }

    public ModerationAction(Guid id, Guid? actorId, bool auto, string action, string targetType, Guid targetId, Guid? itemId,
        string? reason, System.Text.Json.JsonElement details, DateTimeOffset now)
    {
        Id = id;
        ActorId = actorId;
        Auto = auto;
        Action = action;
        TargetType = targetType;
        TargetId = targetId;
        ItemId = itemId;
        Reason = reason;
        Details = details.Clone();
        CreatedAt = now;
    }

    public Guid? ActorId { get; private set; }
    public bool Auto { get; private set; }
    public string Action { get; private set; } = null!;
    /// <summary>item, user, report or flag.</summary>
    public string TargetType { get; private set; } = null!;
    public Guid TargetId { get; private set; }
    public Guid? ItemId { get; private set; }
    public string? Reason { get; private set; }
    public System.Text.Json.JsonElement Details { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}

/// <summary>When a hidden item is purged unless a moderator restores it first.</summary>
public sealed class CleanupSchedule
{
    private CleanupSchedule() { }

    public CleanupSchedule(Guid itemId, DateTimeOffset purgeAt, DateTimeOffset now)
    {
        ItemId = itemId;
        PurgeAt = purgeAt;
        CreatedAt = now;
    }

    public Guid ItemId { get; private set; }
    public DateTimeOffset PurgeAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public void Reschedule(DateTimeOffset purgeAt) => PurgeAt = purgeAt;
}
