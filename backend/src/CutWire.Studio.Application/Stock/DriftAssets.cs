using System.Text.Json;
using System.Text.RegularExpressions;
using CutWire.Studio.Application.Abstractions;
using CutWire.Studio.Domain.Stock;
using Microsoft.EntityFrameworkCore;

namespace CutWire.Studio.Application.Stock;

public sealed class DriftAssetsOptions
{
    public const string Section = "Stock:DriftAssets";

    /// <summary>GitHub "owner/repo" of the asset pack, e.g. CutWire-Studios/Drift-Assets. Empty disables mirroring.</summary>
    public string Repo { get; set; } = "";

    /// <summary>"latest" follows new releases; set a tag (v1.2.0) to pin one. Ignored when <see cref="BaseUrl"/> is set.</summary>
    public string Release { get; set; } = "latest";

    /// <summary>Overrides the release download base (a URL holding index.json and drift-assets.tar.gz). For tests and mirrors.</summary>
    public string BaseUrl { get; set; } = "";

    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(6);

    public string ResolveBase() => !string.IsNullOrWhiteSpace(BaseUrl)
        ? BaseUrl.TrimEnd('/')
        : string.IsNullOrWhiteSpace(Repo)
            ? ""
            : Release is "" or "latest"
                ? $"https://github.com/{Repo}/releases/latest/download"
                : $"https://github.com/{Repo}/releases/download/{Release}";
}

public sealed class DriftIndex
{
    public int Schema { get; set; }
    public string Version { get; set; } = "";
    public List<DriftIndexCategory> Categories { get; set; } = [];
    public List<DriftIndexAsset> Assets { get; set; } = [];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly Regex SafeVersion = new("^[A-Za-z0-9._-]{1,64}$", RegexOptions.Compiled);
    private static readonly Regex SafeSha = new("^[0-9a-f]{64}$", RegexOptions.Compiled);
    private static readonly Regex SafeId = new("^[a-z0-9][a-z0-9-]{0,63}$", RegexOptions.Compiled);

    /// <summary>The most one indexed file may claim; gzip's compressed cap does not bound what it expands to.</summary>
    public const long MaxAssetBytes = 256L << 20;

    /// <summary>Decodes and validates an index. Every path it names is checked here, once, so extraction and serving can trust them.</summary>
    public static DriftIndex Parse(string json)
    {
        DriftIndex idx;
        try
        {
            idx = JsonSerializer.Deserialize<DriftIndex>(json, Json) ?? throw new FormatException("index: empty");
        }
        catch (JsonException ex)
        {
            throw new FormatException("index: " + ex.Message, ex);
        }

        if (idx.Schema != 1) throw new FormatException($"index: unsupported schema {idx.Schema}");
        if (!SafeVersion.IsMatch(idx.Version)) throw new FormatException($"index: bad version \"{idx.Version}\"");
        var cats = idx.Categories.Select(c => c.Id).ToHashSet();
        var seen = new HashSet<string>();
        foreach (var a in idx.Assets)
        {
            if (!SafeId.IsMatch(a.Id) || !seen.Add(a.Id)) throw new FormatException($"index: bad or duplicate asset id \"{a.Id}\"");
            if (!cats.Contains(a.Category)) throw new FormatException($"index: {a.Id} has unknown category \"{a.Category}\"");
            foreach (var role in new[] { "main", "thumbnail", "preview" })
                if (!a.Files.ContainsKey(role)) throw new FormatException($"index: {a.Id} has no {role} file");
            foreach (var (role, f) in a.Files) CheckFile(a, role, f);
            var variantIds = new HashSet<string>();
            foreach (var v in a.Variants)
            {
                if (!SafeId.IsMatch(v.Id) || !variantIds.Add(v.Id)) throw new FormatException($"index: {a.Id} has a bad variant id \"{v.Id}\"");
                foreach (var role in new[] { "main", "thumbnail", "preview" })
                    if (!v.Files.ContainsKey(role)) throw new FormatException($"index: {a.Id} variant {v.Id} has no {role} file");
                foreach (var (role, f) in v.Files) CheckFile(a, role, f);
            }
        }

        return idx;
    }

    private static void CheckFile(DriftIndexAsset a, string role, DriftIndexFile f)
    {
        var rel = a.RelPath(f);
        var cleaned = string.Join('/', rel.Split('/').Where(p => p != "."));
        if (!SafeSha.IsMatch(f.Sha256) || string.IsNullOrEmpty(f.Name) || f.Name.Contains('/') || f.Name.Contains('\\')
            || f.Size <= 0 || f.Size > MaxAssetBytes || rel != cleaned || rel.StartsWith('/') || rel.Split('/').Any(p => p == ".."))
            throw new FormatException($"index: {a.Id} has a bad {role} file");
    }

    /// <summary>Every file the release ships, keyed by its relative path inside the archive.</summary>
    public Dictionary<string, DriftIndexFile> Files()
    {
        var all = new Dictionary<string, DriftIndexFile>();
        foreach (var a in Assets)
        {
            foreach (var f in a.Files.Values) all[a.RelPath(f)] = f;
            foreach (var v in a.Variants)
                foreach (var f in v.Files.Values) all[a.RelPath(f)] = f;
        }

        return all;
    }
}

public sealed class DriftIndexCategory
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string Kind { get; set; } = "";
    public int Sort { get; set; }
}

public sealed class DriftIndexFile
{
    public string Name { get; set; } = "";
    public long Size { get; set; }
    public string Sha256 { get; set; } = "";
}

public sealed class DriftIndexAsset
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Category { get; set; } = "";
    public string Path { get; set; } = "";
    public Dictionary<string, DriftIndexFile> Files { get; set; } = [];
    public List<DriftIndexVariant> Variants { get; set; } = [];

    public string RelPath(DriftIndexFile f) => string.IsNullOrEmpty(Path) ? f.Name : Path.TrimEnd('/') + "/" + f.Name;
}

public sealed class DriftIndexVariant
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public Dictionary<string, DriftIndexFile> Files { get; set; } = [];
}

public sealed record DriftRawCategory(string Id, string Label, IReadOnlyList<DriftIndexAsset> Assets);

/// <summary>The mirrored Drift Assets pack, shaped for the Stock UI. Files are content-addressed blobs served by <c>/files/{sha256}</c>.</summary>
public sealed class DriftAssetsCatalog(IAppDbContext db)
{
    private static string Url(DriftIndexFile f) => $"/files/{f.Sha256}?name={Uri.EscapeDataString(f.Name)}";

    public static string KindFor(string kind) => kind switch
    {
        "face-prop" => "face-prop",
        "lottie" => "lottie",
        _ => "model3d",
    };

    public async Task<DriftAssetLibraryDto> GetAsync(CancellationToken ct)
    {
        var pack = await db.DriftAssetPacks.AsNoTracking().FirstOrDefaultAsync(p => p.IsCurrent, ct);
        if (pack is null) return new DriftAssetLibraryDto("", []);
        var index = DriftIndex.Parse(pack.IndexJson);
        var overrides = (await db.AssetCategories.AsNoTracking().ToListAsync(ct)).ToDictionary(c => c.Category);

        var categories = new List<(int Sort, DriftAssetCategoryDto Category)>();
        foreach (var c in index.Categories)
        {
            overrides.TryGetValue(c.Id, out var setting);
            if (setting is { Enabled: false }) continue;
            var assets = index.Assets.Where(a => a.Category == c.Id).Select(ToDto).ToList();
            if (assets.Count == 0) continue;
            categories.Add((setting?.Sort ?? c.Sort, new DriftAssetCategoryDto(c.Id, string.IsNullOrWhiteSpace(setting?.Label) ? c.Label : setting.Label, assets)));
        }

        return new DriftAssetLibraryDto(index.Version, categories.OrderBy(c => c.Sort).Select(c => c.Category).ToList());
    }

    /// <summary>The enabled categories in display order with their raw index assets, so callers can read every file and sha256.</summary>
    public async Task<IReadOnlyList<DriftRawCategory>> GetRawAsync(CancellationToken ct)
    {
        var pack = await db.DriftAssetPacks.AsNoTracking().FirstOrDefaultAsync(p => p.IsCurrent, ct);
        if (pack is null) return [];
        var index = DriftIndex.Parse(pack.IndexJson);
        var overrides = (await db.AssetCategories.AsNoTracking().ToListAsync(ct)).ToDictionary(c => c.Category);

        var categories = new List<(int Sort, DriftRawCategory Category)>();
        foreach (var c in index.Categories)
        {
            overrides.TryGetValue(c.Id, out var setting);
            if (setting is { Enabled: false }) continue;
            var assets = index.Assets.Where(a => a.Category == c.Id).ToList();
            if (assets.Count == 0) continue;
            categories.Add((setting?.Sort ?? c.Sort, new DriftRawCategory(c.Id, string.IsNullOrWhiteSpace(setting?.Label) ? c.Label : setting.Label, assets)));
        }

        return categories.OrderBy(c => c.Sort).Select(c => c.Category).ToList();
    }

    private static DriftAssetDto ToDto(DriftIndexAsset a)
    {
        var variants = a.Variants.Count > 0
            ? a.Variants.Select(v => new DriftAssetVariantDto(v.Id, string.IsNullOrWhiteSpace(v.Name) ? v.Id : v.Name, Url(v.Files["main"]), v.Files["main"].Size)).ToList()
            : [new DriftAssetVariantDto("default", "Default", Url(a.Files["main"]), a.Files["main"].Size)];
        return new DriftAssetDto(a.Id, a.Name, KindFor(a.Kind), Url(a.Files["thumbnail"]), a.Files.TryGetValue("preview", out var p) ? Url(p) : null, variants);
    }

    /// <summary>Adds a settings row for every category a new release introduces; existing rows are left alone so operator choices survive.</summary>
    public async Task SeedCategoriesAsync(DriftIndex index, CancellationToken ct)
    {
        var have = (await db.AssetCategories.Select(c => c.Category).ToListAsync(ct)).ToHashSet();
        foreach (var c in index.Categories.Where(c => !have.Contains(c.Id)))
            db.AssetCategories.Add(new AssetCategory { Category = c.Id, Enabled = true, Sort = c.Sort });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
        }
    }
}
