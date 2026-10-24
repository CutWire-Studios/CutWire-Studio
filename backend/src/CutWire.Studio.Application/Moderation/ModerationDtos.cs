using CutWire.Studio.Application.Catalog;
using System.Text.Json;

namespace CutWire.Studio.Application.Moderation;

public sealed record ModItemResultDto(Guid Id, string Status, string? VisibilityReason);

public sealed record ModItemRefDto(Guid Id, string Kind, string Slug, string Title, string OwnerHandle);

public sealed record ModReportDto(Guid Id, string Reporter, string ReporterTrust, string Reason, string? Note, string Status, DateTimeOffset CreatedAt);

public sealed record ModFlagDto(Guid Id, string Code, string Detail, JsonElement Params, DateTimeOffset CreatedAt, ModItemRefDto? MatchedItem);

/// <summary>One queue entry: <c>Reports</c> (open) and <c>Flags</c> (unresolved) are always lists; <c>PurgeAt</c> is set for hidden items.</summary>
public sealed record ModQueueEntryDto(
    ItemCardDto Item,
    IReadOnlyList<ModReportDto> Reports,
    IReadOnlyList<ModFlagDto> Flags,
    string? VisibilityReason,
    DateTimeOffset? PurgeAt,
    IReadOnlyDictionary<string, int> ReasonCounts);

public sealed record ModQueuePageDto(IReadOnlyList<ModQueueEntryDto> Entries, string? NextCursor);

public sealed record ModUserDto(
    Guid Id, string Handle, string DisplayName, string? AvatarUrl, string TrustLevel, IReadOnlyList<string> Roles, bool EmailVerified,
    bool Banned, DateTimeOffset? BannedUntil, bool PermanentlyBanned, DateTimeOffset JoinedAt, int ItemCount);

public sealed record ModUserPageDto(IReadOnlyList<ModUserDto> Users, string? NextCursor);

public sealed record AuditEntryDto(
    Guid Id, DateTimeOffset At, string? Actor, string Action, string TargetType, Guid Target, Guid? ItemId, string? Reason, bool Auto,
    JsonElement Details);

public sealed record AuditPageDto(IReadOnlyList<AuditEntryDto> Entries, string? NextCursor);

public sealed record BulkFailureDto(Guid Id, string Code, string Message);

public sealed record BulkResultDto(IReadOnlyList<Guid> Succeeded, IReadOnlyList<BulkFailureDto> Failed);

public sealed record ModUserActionDto(Guid Id, string TrustLevel, bool Banned, int AffectedItems);
