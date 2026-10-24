using CutWire.Studio.Domain.Catalog;
using static CutWire.Studio.Application.Quality.TextRules;

namespace CutWire.Studio.Application.Quality;

public sealed class DuplicateGate(ContentRulesOptions rules, IDuplicateLookup lookup, IImageAnalyzer images) : IContentGate
{
    public async Task<IReadOnlyList<GateViolation>> CheckAsync(SubmissionDraft draft, CancellationToken ct)
    {
        var found = new List<GateViolation>();

        if (draft.PackageInfo is { } package && await lookup.PackageExistsAsync(package.PackageSha256, draft.ItemId, ct))
            found.Add(V("package", "duplicate_package", "This exact package has already been uploaded."));

        if (!string.IsNullOrWhiteSpace(draft.Title))
        {
            var normalized = NormalizeTitle(draft.Title);
            var titles = await lookup.OwnerTitlesAsync(draft.OwnerId, draft.ItemId, ct);
            if (normalized.Length > 0 && titles.Any(t => NormalizeTitle(t) == normalized))
                found.Add(V("title", "duplicate_title", "You already have an item with this title."));
        }

        var thumbnail = draft.Media?.FirstOrDefault(m => m.Role == MediaRole.Thumbnail);
        if (thumbnail is not null && images.Probe(thumbnail.Data) is { } probe
            && await lookup.SimilarThumbnailExistsAsync(probe.DHash, rules.SimilarThumbnailMaxDistance, draft.ItemId, ct))
            found.Add(new GateViolation("media.thumbnail", "similar_thumbnail", "The thumbnail looks very similar to an existing item.",
                new Dictionary<string, object> { ["maxDistance"] = rules.SimilarThumbnailMaxDistance }, GateSeverity.Flag));
        return found;
    }
}
