using CutWire.Studio.Application.Catalog;
using static CutWire.Studio.Application.Quality.TextRules;

namespace CutWire.Studio.Application.Quality;

public sealed class TaxonomyGate(ContentRulesOptions rules, IAssetKindRegistry kinds) : IContentGate
{
    public Task<IReadOnlyList<GateViolation>> CheckAsync(SubmissionDraft draft, CancellationToken ct)
    {
        var found = new List<GateViolation>();
        var kind = kinds.Get(draft.Kind);
        if (kind is not { Enabled: true })
        {
            found.Add(V("kind", "unknown_kind", "Unknown or unavailable asset kind."));
            return Task.FromResult<IReadOnlyList<GateViolation>>(found);
        }

        if (string.IsNullOrWhiteSpace(draft.Category))
            found.Add(V("category", "required", "Category is required."));
        else if (!kind.Categories.Contains(draft.Category))
            found.Add(V("category", "invalid_category", "Category is not valid for this kind.", ("allowed", kind.Categories)));

        var tags = draft.Tags ?? [];
        if (tags.Count < rules.MinTags)
            found.Add(V("tags", "too_few_tags", $"Pick at least {rules.MinTags} tag.", ("min", rules.MinTags)));
        if (tags.Count > rules.MaxTags)
            found.Add(V("tags", "too_many_tags", $"Pick at most {rules.MaxTags} tags.", ("max", rules.MaxTags)));
        foreach (var tag in tags.Where(t => !kind.TagVocabulary.Contains(t)).Distinct())
            found.Add(V("tags", "unknown_tag", $"Tag \"{tag}\" is not in the vocabulary.", ("tag", tag)));
        foreach (var tag in tags.GroupBy(t => t, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            found.Add(V("tags", "duplicate_tag", $"Tag \"{tag.Key}\" is listed more than once.", ("tag", tag.Key)));

        if (string.IsNullOrWhiteSpace(draft.License))
            found.Add(V("license", "required", "License is required."));
        else if (!rules.Licenses.Contains(draft.License))
            found.Add(V("license", "invalid_license", "License is not one of the allowed SPDX identifiers.", ("allowed", rules.Licenses)));
        return Task.FromResult<IReadOnlyList<GateViolation>>(found);
    }
}
