using CutWire.Studio.Application.Catalog;
using CutWire.Studio.Domain.Catalog;

namespace CutWire.Studio.Application.Quality;

public sealed record MediaUpload(MediaRole Role, byte[] Data, string DeclaredMime);

public sealed record SubmissionDraft(
    AssetKindId Kind,
    Guid OwnerId,
    string Title,
    string Summary,
    string Description,
    IReadOnlyList<string> Tags,
    string Category,
    string License,
    IReadOnlyList<MediaUpload> Media,
    PackageInfo? PackageInfo,
    Guid? ItemId = null);
