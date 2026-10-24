using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CutWire.Studio.Application.Catalog;
using CutWire.Studio.Domain.Blobs;
using CutWire.Studio.Domain.Catalog;
using CutWire.Studio.Domain.Common;

namespace CutWire.Studio.Infrastructure.Catalog;

public sealed partial class EffectTemplateKind() : AssetKindBase(AssetKindId.EffectTemplate)
{
    private const int MaxLayers = 16;
    private static readonly string[] SyncModes = ["beat", "bar", "onset", "clip"];

    protected override Result<PackageInfo> Validate(byte[] bytes, Sha256Hash sha256)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
        }
        catch (JsonException)
        {
            return Invalid("template_invalid", "template.json is not valid JSON.");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Invalid("template_invalid", "template.json must be an object.");

            var id = Text(root, "id");
            if (id is null || !IdPattern().IsMatch(id)) return Invalid("template_invalid", "id is required (lowercase letters, digits, _ and -).");
            var name = Text(root, "displayName");
            if (name is not { Length: > 0 and <= 60 }) return Invalid("template_invalid", "displayName is required (1-60 characters).");
            if (Text(root, "category") is not { Length: > 0 }) return Invalid("template_invalid", "category is required.");
            if (!SyncModes.Contains(Text(root, "sync"))) return Invalid("template_invalid", $"sync must be one of: {string.Join(", ", SyncModes)}.");
            if (root.TryGetProperty("order", out var order) && order.ValueKind != JsonValueKind.Number) return Invalid("template_invalid", "order must be a number.");
            if (root.TryGetProperty("speedPulse", out var speed) && CheckEnvelope(speed) is { } speedProblem) return Invalid("template_invalid", $"speedPulse: {speedProblem}");

            var effects = new List<string>();
            var layerGroups = new List<JsonElement>();
            if (root.TryGetProperty("layers", out var layers)) layerGroups.Add(layers);
            if (root.TryGetProperty("tracks", out var tracks))
            {
                if (tracks.ValueKind != JsonValueKind.Array) return Invalid("template_invalid", "tracks must be an array.");
                foreach (var track in tracks.EnumerateArray())
                {
                    if (track.ValueKind != JsonValueKind.Object || Text(track, "role") is not { Length: > 0 } || !track.TryGetProperty("layers", out var trackLayers))
                        return Invalid("template_invalid", "Each track needs a role and layers.");
                    layerGroups.Add(trackLayers);
                }
            }

            if (layerGroups.Count == 0) return Invalid("template_invalid", "A template needs layers or tracks.");
            foreach (var group in layerGroups)
            {
                if (group.ValueKind != JsonValueKind.Array || group.GetArrayLength() == 0) return Invalid("template_invalid", "layers must be a non-empty array.");
                foreach (var layer in group.EnumerateArray())
                {
                    var problem = CheckLayer(layer, effects);
                    if (problem is not null) return Invalid("template_invalid", problem);
                }
            }

            if (effects.Count > MaxLayers) return Invalid("template_invalid", $"A template may have at most {MaxLayers} layers.");

            var version = Text(root, "version");
            var minApp = Text(root, "minAppVersion");
            var search = $"{name} {Text(root, "category")} {string.Join(' ', effects.Distinct())}";
            return new PackageInfo(PackageFormat.Json, root.Clone(), id, Semver.IsValid(version) ? version! : "1.0.0",
                Semver.IsValid(minApp) ? minApp! : "0.1.0", "effect-templates", [new PackageFileEntry("template.json", bytes.Length, sha256)], search, sha256);
        }
    }

    private static string? CheckLayer(JsonElement layer, List<string> effects)
    {
        if (layer.ValueKind != JsonValueKind.Object) return "Each layer must be an object.";
        var effectId = Text(layer, "effectId");
        if (effectId is null || !EffectIdPattern().IsMatch(effectId)) return "Each layer needs an effectId.";
        effects.Add(effectId);
        if (layer.TryGetProperty("params", out var p))
        {
            if (p.ValueKind != JsonValueKind.Object) return $"{effectId}: params must be an object.";
            foreach (var value in p.EnumerateObject())
                if (value.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Null)
                    return $"{effectId}: param '{value.Name}' must be a number, string, boolean or array.";
        }

        if (layer.TryGetProperty("pulse", out var pulse))
        {
            if (pulse.ValueKind != JsonValueKind.Object || Text(pulse, "param") is not { Length: > 0 }) return $"{effectId}: pulse needs a param.";
            if (CheckEnvelope(pulse) is { } problem) return $"{effectId}: pulse {problem}";
        }

        return null;
    }

    private static string? CheckEnvelope(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object) return "must be an object.";
        foreach (var field in new[] { "rest", "peak", "decayMs" })
            if (!e.TryGetProperty(field, out var v) || v.ValueKind != JsonValueKind.Number) return $"{field} must be a number.";
        return e.GetProperty("decayMs").GetDouble() < 0 ? "decayMs must not be negative." : null;
    }

    private static string? Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    [GeneratedRegex("^[a-z][a-z0-9_-]{1,63}$")]
    private static partial Regex IdPattern();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9_.-]{0,63}$")]
    private static partial Regex EffectIdPattern();
}
