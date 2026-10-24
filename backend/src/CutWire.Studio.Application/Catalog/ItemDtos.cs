using System.Text.Json;

namespace CutWire.Studio.Application.Catalog;

public sealed record OwnerDto(string Handle, string DisplayName, string? AvatarUrl);

public sealed record ItemCardDto(
    Guid Id,
    string Kind,
    string Slug,
    string Title,
    string Summary,
    OwnerDto Owner,
    string? ThumbnailUrl,
    double Score,
    long Downloads,
    IReadOnlyList<string> Tags,
    DateTimeOffset? PublishedAt,
    string Status,
    int? ViewerVote = null,
    string? PreviewStripUrl = null,
    string? VisibilityReason = null);

public sealed record ItemPageDto(IReadOnlyList<ItemCardDto> Items, string? NextCursor);

public sealed record ItemVersionDto(
    Guid Id,
    string Version,
    string Changelog,
    long Size,
    string MinAppVersion,
    string Sha256,
    string DownloadUrl,
    DateTimeOffset CreatedAt,
    bool IsCurrent);

public sealed record ItemMediaDto(string Role, string Url, int Width, int Height, int Bytes);

public sealed record ItemDetailDto(
    Guid Id,
    string Kind,
    string Slug,
    string Title,
    string Summary,
    string Description,
    string License,
    string Category,
    string Status,
    string? VisibilityReason,
    OwnerDto Owner,
    IReadOnlyList<string> Tags,
    string? ThumbnailUrl,
    double ScoreUp,
    double ScoreDown,
    double Score,
    long Downloads,
    DateTimeOffset? PublishedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? CurrentVersion,
    IReadOnlyList<ItemVersionDto> Versions,
    IReadOnlyList<ItemMediaDto> Media,
    JsonElement KindMeta,
    int? ViewerVote,
    DateTimeOffset? PurgeAt = null);

public sealed record UserProfileDto(
    string Handle,
    string DisplayName,
    string? AvatarUrl,
    string Bio,
    string TrustLevel,
    DateTimeOffset JoinedAt,
    int PublishedItems,
    long TotalDownloads,
    ItemPageDto Items);

public sealed record Viewer(Guid? UserId, bool IsModerator)
{
    public static readonly Viewer Anonymous = new(null, false);
}

public sealed record DownloadTarget(string Sha256, string FileName);
