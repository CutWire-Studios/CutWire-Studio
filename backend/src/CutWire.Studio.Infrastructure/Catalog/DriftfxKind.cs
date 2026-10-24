using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CutWire.Studio.Application.Catalog;
using CutWire.Studio.Domain.Blobs;
using CutWire.Studio.Domain.Catalog;
using CutWire.Studio.Domain.Common;

namespace CutWire.Studio.Infrastructure.Catalog;

public abstract partial class DriftfxKind(AssetKindId id, string providesKind, string manifestFile) : AssetKindBase(id)
{
    private static readonly HashSet<string> ParamTypes = new(StringComparer.Ordinal)
    {
        "float", "int", "integer", "bool", "boolean", "color", "colour", "point", "vec2", "choice", "enum",
        "curve", "lut", "seed", "region", "image", "gradient", "track", "trackMask", "audio",
    };

    private static readonly HashSet<string> GpuExtensions = new(StringComparer.Ordinal) { ".json", ".frag", ".png", ".jpg", ".webp" };

    protected abstract bool CarriesShaders { get; }

    protected sealed override Result<PackageInfo> Validate(byte[] bytes, Sha256Hash sha256)
    {
        var read = DriftfxReader.Read(bytes, MaxUnpackedBytes);
        if (read.IsFailure) return read.Error;
        var package = read.Value;
        var manifest = package.Manifest;

        var kind = manifest.GetProperty("provides")[0].GetProperty("kind").GetString();
        if (kind != providesKind)
            return Invalid("wrong_kind", $"Package provides '{kind}', but this upload is a {DisplayName.ToLowerInvariant()} ({providesKind}).");

        if (package.Root != providesKind) return Invalid("unsafe_layout", $"Package root must be '{providesKind}'.");
        string? folder = null;
        foreach (var entry in package.Entries)
        {
            var rest = entry.Path.StartsWith(providesKind + "/", StringComparison.Ordinal) ? entry.Path[(providesKind.Length + 1)..] : "";
            var slash = rest.IndexOf('/');
            if (slash <= 0) return Invalid("unsafe_layout", $"File outside the package folder: {entry.Path}");
            var first = rest[..slash];
            if (folder is not null && first != folder) return Invalid("unsafe_layout", "A package must contain a single folder.");
            folder = first;
        }

        var prefix = $"{providesKind}/{folder}/";
        var files = package.Files.ToDictionary(f => f.Key[prefix.Length..], f => f.Value);

        if (!files.TryGetValue(manifestFile, out var json)) return Invalid("missing_file", $"{manifestFile} is missing.");
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
        }
        catch (JsonException)
        {
            return Invalid("manifest_invalid", $"{manifestFile} is not valid JSON.");
        }

        using (document)
        {
            var problem = CheckPackageFields(manifest) ?? CheckFiles(files) ?? CheckDefinition(document.RootElement, folder!, files);
            if (problem is not null) return problem;
            return BuildInfo(package, document.RootElement, sha256);
        }
    }

    protected abstract Error? CheckKind(JsonElement definition, IReadOnlyDictionary<string, byte[]> files);

    private static Error? CheckPackageFields(JsonElement manifest)
    {
        if (Text(manifest, "id") is not { Length: > 0 }) return Invalid("manifest_invalid", "Manifest id is required.");
        if (Text(manifest, "name") is not { Length: > 0 }) return Invalid("manifest_invalid", "Manifest name is required.");
        if (!Semver.IsValid(Text(manifest, "version"))) return Invalid("manifest_invalid", "Manifest version must be semver.");
        if (!Semver.IsValid(Text(manifest, "minAppVersion"))) return Invalid("manifest_invalid", "Manifest minAppVersion must be semver.");
        return null;
    }

    private Error? CheckFiles(Dictionary<string, byte[]> files)
    {
        foreach (var path in files.Keys)
        {
            var extension = Path.GetExtension(path).ToLowerInvariant();
            var ok = CarriesShaders
                ? GpuExtensions.Contains(extension)
                : extension == ".json" && !path.Contains('/') || path.StartsWith("ir/", StringComparison.Ordinal) && extension == ".wav" && path.Count(c => c == '/') == 1;
            if (!ok) return Invalid("unexpected_file", $"File type not allowed in this package: {path}");
        }

        return null;
    }

    private Error? CheckDefinition(JsonElement def, string folder, Dictionary<string, byte[]> files)
    {
        if (def.ValueKind != JsonValueKind.Object) return Invalid("manifest_invalid", $"{manifestFile} must be an object.");
        if (Text(def, "id") != folder || !IdPattern().IsMatch(folder))
            return Invalid("manifest_invalid", $"{manifestFile} id must match the package folder '{folder}' (letters, digits, _ and -).");
        if (Text(def, "displayName") is not { Length: > 0 }) return Invalid("manifest_invalid", "displayName is required.");
        if (Text(def, "category") is not { Length: > 0 }) return Invalid("manifest_invalid", "category is required.");
        if (def.TryGetProperty("order", out var order) && order.ValueKind != JsonValueKind.Number) return Invalid("manifest_invalid", "order must be a number.");
        return CheckParameters(def) ?? CheckKind(def, files);
    }

    private static Error? CheckParameters(JsonElement def)
    {
        if (!def.TryGetProperty("parameters", out var parameters)) return null;
        if (parameters.ValueKind != JsonValueKind.Array) return Invalid("manifest_invalid", "parameters must be an array.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in parameters.EnumerateArray())
        {
            if (p.ValueKind != JsonValueKind.Object) return Invalid("param_invalid", "Each parameter must be an object.");
            var identifier = Text(p, "identifier");
            if (identifier is null || !ParamIdPattern().IsMatch(identifier)) return Invalid("param_invalid", "Parameter identifier is missing or invalid.");
            if (!seen.Add(identifier)) return Invalid("param_invalid", $"Parameter '{identifier}' is declared twice.");
            if (Text(p, "displayName") is not { Length: > 0 }) return Invalid("param_invalid", $"Parameter '{identifier}' needs a displayName.");
            var type = p.TryGetProperty("type", out var t) ? (t.ValueKind == JsonValueKind.String ? t.GetString()! : "") : "float";
            if (!ParamTypes.Contains(type)) return Invalid("param_type_not_allowed", $"Parameter '{identifier}' has unsupported type '{type}'.");
            if (type is "float" or "int" or "integer" && p.TryGetProperty("minValue", out var min) && p.TryGetProperty("maxValue", out var max))
                if (min.ValueKind != JsonValueKind.Number || max.ValueKind != JsonValueKind.Number || min.GetDouble() > max.GetDouble())
                    return Invalid("param_invalid", $"Parameter '{identifier}' has an invalid range.");
            if (type is "choice" or "enum" && (!p.TryGetProperty("options", out var options) || options.ValueKind != JsonValueKind.Array || options.GetArrayLength() < 2))
                return Invalid("param_invalid", $"Parameter '{identifier}' needs at least two options.");
            if (type is "color" or "colour" && p.TryGetProperty("defaultValue", out var color) && !(color.ValueKind == JsonValueKind.String && ColorPattern().IsMatch(color.GetString()!)))
                return Invalid("param_invalid", $"Parameter '{identifier}' default must be #rrggbb or #rrggbbaa.");
        }

        return null;
    }

    private PackageInfo BuildInfo(DriftfxContents package, JsonElement def, Sha256Hash sha256)
    {
        var manifest = package.Manifest;
        var text = new StringBuilder();
        foreach (var value in new[] { Text(manifest, "name"), Text(manifest, "description"), Text(def, "displayName"), Text(def, "category"), Text(def, "description") })
            if (!string.IsNullOrWhiteSpace(value)) text.Append(value).Append(' ');
        if (def.TryGetProperty("parameters", out var parameters))
            foreach (var p in parameters.EnumerateArray())
                if (Text(p, "displayName") is { Length: > 0 } name) text.Append(name).Append(' ');

        return new PackageInfo(PackageFormat.DriftFx, manifest, Text(manifest, "id")!, Text(manifest, "version")!, Text(manifest, "minAppVersion")!,
            providesKind, package.Entries, text.ToString().Trim(), sha256);
    }

    protected static string? Text(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9_-]*$")]
    private static partial Regex IdPattern();

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex ParamIdPattern();

    [GeneratedRegex(@"^#([0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$")]
    private static partial Regex ColorPattern();
}

public abstract class GpuDriftfxKind(AssetKindId id, string providesKind, string manifestFile) : DriftfxKind(id, providesKind, manifestFile)
{
    protected override bool CarriesShaders => true;

    protected virtual string? CheckSpecific(JsonElement definition) => null;

    protected override Error? CheckKind(JsonElement def, IReadOnlyDictionary<string, byte[]> files)
    {
        if (CheckSpecific(def) is { } specific) return Invalid("manifest_invalid", specific);
        if (def.TryGetProperty("backend", out var backend) && backend.GetString() != "gpu")
            return Invalid("manifest_invalid", "backend must be \"gpu\".");
        if (!def.TryGetProperty("pipeline", out var pipeline) || pipeline.ValueKind != JsonValueKind.Object)
            return Invalid("manifest_invalid", "pipeline is required.");
        if (!pipeline.TryGetProperty("passes", out var passes) || passes.ValueKind != JsonValueKind.Array || passes.GetArrayLength() == 0)
            return Invalid("manifest_invalid", "pipeline.passes must list at least one pass.");

        var buffers = new HashSet<string>();
        if (pipeline.TryGetProperty("intermediateBuffers", out var declared) && declared.ValueKind == JsonValueKind.Array)
            foreach (var b in declared.EnumerateArray())
                if (Text(b, "id") is { } bufferId) buffers.Add(bufferId);

        JsonElement last = default;
        foreach (var pass in passes.EnumerateArray())
        {
            last = pass;
            var shader = Text(pass, "fragmentShader");
            if (shader is null || !PathRules.IsSafe(shader) || !files.TryGetValue(shader, out var source))
                return Invalid("missing_file", $"Pass shader '{shader}' is missing from the package.");
            if (!Encoding.UTF8.GetString(source).Contains("#version 330 core", StringComparison.Ordinal))
                return Invalid("shader_version", $"{shader} must declare #version 330 core.");
            if (!pass.TryGetProperty("output", out var output) || Text(output, "type") is not ("canvas" or "buffer"))
                return Invalid("manifest_invalid", "Each pass needs an output of type canvas or buffer.");
            if (Text(output, "type") == "buffer" && (Text(output, "id") is not { } outId || !buffers.Contains(outId)))
                return Invalid("manifest_invalid", "A buffer output must name a declared intermediate buffer.");
        }

        if (!last.TryGetProperty("output", out var lastOutput) || Text(lastOutput, "type") != "canvas")
            return Invalid("manifest_invalid", "The last pass must output to the canvas.");

        if (pipeline.TryGetProperty("textures", out var textures))
        {
            if (textures.ValueKind != JsonValueKind.Array) return Invalid("manifest_invalid", "pipeline.textures must be an array.");
            foreach (var texture in textures.EnumerateArray())
            {
                var file = Text(texture, "file");
                if (Text(texture, "id") is not { Length: > 0 } || file is null || !PathRules.IsSafe(file) || !files.ContainsKey(file))
                    return Invalid("missing_file", $"Texture '{file}' is missing from the package.");
            }
        }

        return null;
    }
}

public sealed class EffectKind() : GpuDriftfxKind(AssetKindId.Effect, "effects", "effect.json");

public sealed class TransitionKind() : GpuDriftfxKind(AssetKindId.Transition, "transitions", "transition.json")
{
    protected override string? CheckSpecific(JsonElement definition) =>
        definition.TryGetProperty("audioCurve", out var curve) && curve.GetString() is not ("crossfade" or "dip" or "hold")
            ? "audioCurve must be crossfade, dip or hold."
            : null;
}

public sealed class AudioEffectKind() : DriftfxKind(AssetKindId.AudioEffect, "audio-effects", "audio-effect.json")
{
    protected override bool CarriesShaders => false;

    protected override Error? CheckKind(JsonElement def, IReadOnlyDictionary<string, byte[]> files)
    {
        if (Text(def, "backend") != "juce") return Invalid("manifest_invalid", "backend must be \"juce\".");
        var processor = Text(def, "processor");
        if (processor is not { Length: > 0 }) return Invalid("manifest_invalid", "processor is required.");
        if (def.TryGetProperty("prerollMs", out var preroll) && (preroll.ValueKind != JsonValueKind.Number || preroll.GetDouble() < 0))
            return Invalid("manifest_invalid", "prerollMs must be a non-negative number.");
        if (processor == "graph" && (!def.TryGetProperty("graph", out var graph) || graph.ValueKind != JsonValueKind.Object))
            return Invalid("manifest_invalid", "A graph processor needs a graph object.");
        return null;
    }
}
