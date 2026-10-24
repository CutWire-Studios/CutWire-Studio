using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using CutWire.Studio.Application.Catalog;
using CutWire.Studio.Domain.Blobs;
using CutWire.Studio.Domain.Catalog;
using CutWire.Studio.Domain.Common;
using Json.Schema;

namespace CutWire.Studio.Infrastructure.Catalog;

public sealed class ThemeKind() : AssetKindBase(AssetKindId.Theme)
{
    public const int MaxEntryBytes = 2 * 1024 * 1024;
    public const int MaxTotalBytes = 8 * 1024 * 1024;
    public const int MaxEntries = 64;
    public const int SchemaVersion = 1;

    private static readonly string[] ReservedNames = ["dark", "light", "system", "graphite", "frost"];
    private static readonly string[] AllowedEntries = ["theme.json", "theme.dark.json", "theme.light.json"];
    private static readonly Lazy<JsonSchema> Schema = new(LoadSchema);

    protected override Result<PackageInfo> Validate(byte[] bytes, Sha256Hash sha256)
    {
        var isZip = bytes.Length >= 4 && bytes[0] == 'P' && bytes[1] == 'K' && bytes[2] == 3 && bytes[3] == 4;
        if (!isZip) return ValidateTheme(bytes, PackageFormat.Json, [new PackageFileEntry("theme.json", bytes.Length, sha256)], sha256);

        var read = ReadZip(bytes);
        if (read.IsFailure) return read.Error;
        var files = read.Value;
        foreach (var variant in new[] { "theme.dark.json", "theme.light.json" })
            if (files.TryGetValue(variant, out var data) && !IsJsonObject(data))
                return Invalid("theme_invalid", $"{variant} is not a valid JSON object.");

        var entries = files.Select(f => new PackageFileEntry(f.Key, f.Value.Length, new Sha256Hash(Convert.ToHexStringLower(SHA256.HashData(f.Value))))).ToList();
        return ValidateTheme(files["theme.json"], PackageFormat.Zip, entries, sha256);
    }

    private Result<PackageInfo> ValidateTheme(byte[] json, PackageFormat format, IReadOnlyList<PackageFileEntry> entries, Sha256Hash sha256)
    {
        if (json.Length > MaxEntryBytes) return Invalid("theme_too_large", "theme.json is larger than 2 MB.");
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
        }
        catch (JsonException)
        {
            return Invalid("theme_invalid", "theme.json is not valid JSON.");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Invalid("theme_invalid", "A theme must be a JSON object.");

            var results = Schema.Value.Evaluate(root, new EvaluationOptions { OutputFormat = OutputFormat.List });
            if (!results.IsValid)
            {
                var messages = Collect(results).Distinct().Take(20).ToArray();
                return Error.Validation("theme_schema_invalid", "Theme does not match the Drift theme schema.", new Dictionary<string, string[]> { ["package"] = messages });
            }

            if (!root.TryGetProperty("name", out var nameElement) || nameElement.ValueKind != JsonValueKind.String)
                return Invalid("theme_name_required", "Theme name is required.");
            var name = nameElement.GetString()!;
            if (NameProblem(name) is { } problem) return Invalid("theme_name_invalid", problem);

            if (root.TryGetProperty("schemaVersion", out var version) && (!version.TryGetInt32(out var v) || v is < 1 or > SchemaVersion))
                return Invalid("theme_schema_version", $"schemaVersion must be {SchemaVersion}.");

            var search = name;
            if (root.TryGetProperty("extends", out var extends) && extends.ValueKind == JsonValueKind.String) search += " " + extends.GetString();
            return new PackageInfo(format, root.Clone(), name, "1.0.0", "0.1.0", "themes", entries, search, sha256);
        }
    }

    private static Result<Dictionary<string, byte[]>> ReadZip(byte[] bytes)
    {
        try
        {
            using var zip = new ZipArchive(new MemoryStream(bytes, writable: false), ZipArchiveMode.Read);
            if (zip.Entries.Count > MaxEntries) return Invalid("zip_too_many_entries", $"Theme archive has more than {MaxEntries} entries.");

            var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            long total = 0;
            foreach (var entry in zip.Entries)
            {
                var name = entry.FullName;
                if (name.Length == 0) return Invalid("zip_unsafe_path", "Theme archive contains an empty path.");
                if (name.StartsWith('/') || name.StartsWith('\\') || name.Contains("..") || name.Contains(':') || name.Contains('\\'))
                    return Invalid("zip_unsafe_path", $"Theme archive path is not allowed: {name}");
                if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                    return Invalid("zip_symlink", $"Theme archive contains a symlink: {name}");
                if (name.EndsWith('/') || IsJunk(name)) continue;

                if (name.StartsWith("qml/", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".qml", StringComparison.OrdinalIgnoreCase))
                    return Invalid("qml_not_allowed", "Themes with QML overrides are not accepted because QML is code.");
                if (!AllowedEntries.Contains(name))
                    return Invalid("zip_unexpected_entry", $"Unexpected file in theme archive: {name}");
                if (entry.Length > MaxEntryBytes) return Invalid("zip_entry_too_large", $"Theme archive entry is larger than 2 MB: {name}");

                using var stream = entry.Open();
                using var buffer = new MemoryStream();
                var chunk = new byte[16384];
                int n;
                while ((n = stream.Read(chunk, 0, chunk.Length)) > 0)
                {
                    buffer.Write(chunk, 0, n);
                    total += n;
                    if (buffer.Length > MaxEntryBytes) return Invalid("zip_entry_too_large", $"Theme archive entry is larger than 2 MB: {name}");
                    if (total > MaxTotalBytes) return Invalid("zip_too_large", "Theme archive unpacks to more than 8 MB.");
                }

                files[name] = buffer.ToArray();
            }

            return files.ContainsKey("theme.json") ? files : Invalid("missing_file", "theme.json is missing from the archive.");
        }
        catch (InvalidDataException)
        {
            return Invalid("zip_invalid", "Not a valid ZIP archive.");
        }
    }

    private static bool IsJunk(string path)
    {
        var name = path[(path.LastIndexOf('/') + 1)..];
        return path.StartsWith("__MACOSX/", StringComparison.Ordinal) || name == ".DS_Store" || name.StartsWith("._", StringComparison.Ordinal);
    }

    private static bool IsJsonObject(byte[] data)
    {
        try
        {
            using var document = JsonDocument.Parse(data);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static string? NameProblem(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Theme name is required.";
        if (ReservedNames.Contains(name, StringComparer.OrdinalIgnoreCase)) return $"\"{name}\" is a reserved theme name.";
        if (name.StartsWith('.')) return "Theme name cannot start with a dot.";
        if (name.Contains('/') || name.Contains('\\') || name.Contains("..")) return "Theme name cannot contain \"/\", \"\\\" or \"..\".";
        if (name.Any(char.IsControl)) return "Theme name cannot contain control characters.";
        return null;
    }

    private static IEnumerable<string> Collect(EvaluationResults results)
    {
        if (results.Errors is { } errors)
            foreach (var (keyword, message) in errors)
            {
                var path = results.InstanceLocation.ToString().TrimStart('/').Replace('/', '.');
                yield return path.Length == 0 ? $"{keyword}: {message}" : $"{path}: {message}";
            }

        foreach (var detail in results.Details ?? [])
            if (!detail.IsValid)
                foreach (var message in Collect(detail))
                    yield return message;
    }

    private static JsonSchema LoadSchema()
    {
        using var stream = typeof(ThemeKind).Assembly.GetManifestResourceStream("CutWire.Studio.Infrastructure.Catalog.theme.schema.json")
            ?? throw new InvalidOperationException("Embedded theme.schema.json is missing.");
        using var reader = new StreamReader(stream);
        return JsonSchema.FromText(reader.ReadToEnd());
    }
}
