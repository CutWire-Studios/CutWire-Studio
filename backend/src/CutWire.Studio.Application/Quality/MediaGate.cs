using CutWire.Studio.Application.Catalog;
using CutWire.Studio.Domain.Catalog;
using static CutWire.Studio.Application.Quality.TextRules;

namespace CutWire.Studio.Application.Quality;

public sealed class MediaGate(ContentRulesOptions rules, IAssetKindRegistry kinds, IImageAnalyzer images) : IContentGate
{
    public Task<IReadOnlyList<GateViolation>> CheckAsync(SubmissionDraft draft, CancellationToken ct)
    {
        var found = new List<GateViolation>();
        var kind = kinds.Get(draft.Kind);
        if (kind is null) return Task.FromResult<IReadOnlyList<GateViolation>>(found);

        var media = draft.Media ?? [];
        foreach (var spec in kind.MediaSpecs)
        {
            var field = $"media.{spec.Role.ToKey()}";
            var matches = media.Where(m => m.Role == spec.Role).ToList();
            if (matches.Count == 0)
            {
                if (spec.Required) found.Add(V(field, "media_missing", $"A {spec.Role.ToKey()} image is required.", ("width", spec.Width), ("height", spec.Height)));
                continue;
            }

            if (matches.Count > 1 && spec.Role != MediaRole.Gallery)
                found.Add(V(field, "media_duplicate_role", $"Only one {spec.Role.ToKey()} image is allowed."));
            foreach (var upload in matches) CheckImage(found, field, spec, upload.Data);
        }

        foreach (var role in media.Select(m => m.Role).Distinct().Where(r => kind.MediaSpecs.All(s => s.Role != r)))
            found.Add(V($"media.{role.ToKey()}", "media_role_not_allowed", $"This kind does not take a {role.ToKey()} image."));
        return Task.FromResult<IReadOnlyList<GateViolation>>(found);
    }

    public IReadOnlyList<GateViolation> CheckUpload(AssetKindId kindId, MediaUpload upload)
    {
        var found = new List<GateViolation>();
        var kind = kinds.Get(kindId);
        var field = $"media.{upload.Role.ToKey()}";
        var spec = kind?.MediaSpecs.FirstOrDefault(s => s.Role == upload.Role);
        if (spec is null)
            found.Add(V(field, "media_role_not_allowed", $"This kind does not take a {upload.Role.ToKey()} image."));
        else
            CheckImage(found, field, spec, upload.Data);
        return found;
    }

    private void CheckImage(List<GateViolation> found, string field, MediaSpec spec, byte[] data)
    {
        if (data.Length > spec.MaxBytes)
        {
            found.Add(V(field, "media_too_large", $"Image must be at most {spec.MaxBytes} bytes.", ("max", spec.MaxBytes), ("actual", data.Length)));
            return;
        }

        var probe = images.Probe(data);
        if (probe is null)
        {
            found.Add(V(field, "media_undecodable", "File is not a readable image."));
            return;
        }

        if (probe.Format != "webp")
        {
            found.Add(V(field, "media_not_webp", $"Image must be {MediaSpec.MimeType}.", ("actual", probe.Format)));
            return;
        }

        if (probe.Width != spec.Width || probe.Height != spec.Height)
            found.Add(V(field, "media_wrong_dimensions", $"Image must be exactly {spec.Width}x{spec.Height}.",
                ("width", spec.Width), ("height", spec.Height), ("actualWidth", probe.Width), ("actualHeight", probe.Height)));
        if (probe.FullyTransparent)
            found.Add(V(field, "media_transparent", "Image is fully transparent."));
        else if (probe.LumaStdDev < rules.MinLumaStdDev)
            found.Add(V(field, "media_blank", "Image is blank or a single flat colour.", ("stdDev", Math.Round(probe.LumaStdDev, 2)), ("min", rules.MinLumaStdDev)));
    }
}
