using System.Text.Json;
using CutWire.Studio.Domain.Catalog;

namespace CutWire.Studio.Infrastructure.Catalog;

internal sealed record MediaDefinition(string Role, bool Required, int Width, int Height, int MaxBytes);

internal sealed record AssetKindDefinition(
    string DisplayName,
    string[] Extensions,
    long MaxPackageBytes,
    long MaxUnpackedBytes,
    bool Enabled,
    string[] Categories,
    string[] Tags,
    MediaDefinition[] Media)
{
    private static readonly Lazy<IReadOnlyDictionary<string, AssetKindDefinition>> Loaded = new(Load);

    public static AssetKindDefinition For(AssetKindId id) =>
        Loaded.Value.TryGetValue(id.Value, out var definition)
            ? definition
            : throw new InvalidOperationException($"asset-kinds.json has no entry for '{id}'.");

    public IReadOnlyList<MediaSpec> ToMediaSpecs() =>
        Media.Select(m => MediaRoleExtensions.TryParse(m.Role, out var role)
            ? new MediaSpec(role, m.Required, m.Width, m.Height, m.MaxBytes)
            : throw new InvalidOperationException($"Unknown media role '{m.Role}'.")).ToList();

    private static IReadOnlyDictionary<string, AssetKindDefinition> Load()
    {
        using var stream = typeof(AssetKindDefinition).Assembly.GetManifestResourceStream("CutWire.Studio.Infrastructure.Catalog.asset-kinds.json")
            ?? throw new InvalidOperationException("Embedded asset-kinds.json is missing.");
        return JsonSerializer.Deserialize<Dictionary<string, AssetKindDefinition>>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }
}
