using System.Text.Json;
using CutWire.Studio.Application.Abstractions;
using CutWire.Studio.Domain.Stock;
using Microsoft.EntityFrameworkCore;

namespace CutWire.Studio.Application.Stock;

public sealed class SfxOptions
{
    public const string Section = "Stock:Sfx";

    /// <summary>Directory holding sfx.json, library/ (FLAC originals) and previews/ (Opus). Empty disables the library.</summary>
    public string Path { get; set; } = "";

    /// <summary>JSON file persisting waveform peaks and hashes computed for sounds the index lacks them for. Empty keeps them in memory only.</summary>
    public string CachePath { get; set; } = "";
}

/// <summary><paramref name="Peaks"/> and <paramref name="Sha256"/> are the index's own values when it carries them.</summary>
public sealed record SfxSoundData(
    string Id, string Title, string Tag, int DurationMs, long Size, string PreviewPath, string LibraryPath,
    string Subcategory = "", string? Peaks = null, string? Sha256 = null);

public sealed record SfxSubcategoryData(string Id, string Label);

public sealed record SfxCategoryData(string Id, string Label, IReadOnlyList<SfxSoundData> Sounds, IReadOnlyList<SfxSubcategoryData>? Subcategories = null);

public sealed record SfxData(string Version, IReadOnlyList<SfxCategoryData> Categories)
{
    public static readonly SfxData Empty = new("", []);
}

/// <summary>Source of the SFX index: sfx.json on disk, reloaded when it changes.</summary>
public interface ISfxSource
{
    SfxData Load();
}

public static class SfxIndex
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record Raw(List<RawCategory>? Categories, List<RawSound>? Sounds);
    private sealed record RawCategory(string Id, string? Label, List<RawSub>? Subcategories);
    private sealed record RawSub(string Id, string? Label);
    private sealed record RawSound(
        string Id, string? Name, string? Category, string? Subcategory, double Duration, long Size, string? Path, string? Preview, string? Peaks, string? Sha256);

    /// <summary>Parses sfx.json as written by the library build script. Sounds with unsafe relative paths are dropped; empty categories are skipped.</summary>
    public static SfxData Parse(string json, string version)
    {
        var raw = JsonSerializer.Deserialize<Raw>(json, Json) ?? throw new FormatException("sfx.json is empty");
        var subLabels = new Dictionary<(string, string), string>();
        foreach (var c in raw.Categories ?? [])
            foreach (var s in c.Subcategories ?? [])
                subLabels[(c.Id, s.Id)] = s.Label ?? s.Id;

        var byCategory = (raw.Sounds ?? [])
            .Where(s => !string.IsNullOrEmpty(s.Id) && SafeRel(s.Path) && SafeRel(s.Preview))
            .GroupBy(s => s.Category ?? "")
            .ToDictionary(g => g.Key, g => g.ToList());

        var categories = new List<SfxCategoryData>();
        foreach (var c in raw.Categories ?? [])
        {
            if (!byCategory.TryGetValue(c.Id, out var sounds)) continue;
            var used = sounds.Select(s => s.Subcategory ?? "").ToHashSet();
            var subs = (c.Subcategories ?? []).Where(s => !string.IsNullOrEmpty(s.Id) && used.Contains(s.Id))
                .Select(s => new SfxSubcategoryData(s.Id, s.Label ?? s.Id)).ToList();
            categories.Add(new SfxCategoryData(c.Id, c.Label ?? c.Id, sounds.Select(s => new SfxSoundData(
                s.Id, s.Name ?? s.Id,
                subLabels.GetValueOrDefault((c.Id, s.Subcategory ?? ""), s.Subcategory ?? ""),
                (int)Math.Round(s.Duration * 1000), s.Size, s.Preview!, s.Path!,
                s.Subcategory ?? "", ValidPeaks(s.Peaks), ValidSha(s.Sha256))).ToList(), subs));
        }

        return new SfxData(version, categories);
    }

    private static string? ValidPeaks(string? peaks) =>
        !string.IsNullOrEmpty(peaks) && peaks.Length <= 512 && Convert.TryFromBase64String(peaks, new byte[384], out var n) && n > 0 ? peaks : null;

    private static string? ValidSha(string? sha) =>
        sha is { Length: 64 } && sha.All(char.IsAsciiHexDigit) ? sha.ToLowerInvariant() : null;

    public static bool SafeRel(string? rel) =>
        !string.IsNullOrEmpty(rel) && !rel.StartsWith('/') && !rel.Contains('\\') && !rel.Contains('\0')
        && rel.Split('/').All(p => p.Length > 0 && p is not ("." or ".."));
}

public sealed class SfxCatalog(ISfxSource source, IAppDbContext db)
{
    public const string FilesBase = "/api/v1/stock/sfx/files";

    public async Task<SfxLibraryDto> GetAsync(CancellationToken ct)
    {
        var data = source.Load();
        if (data.Categories.Count == 0) return new SfxLibraryDto(data.Version, []);
        await SeedAsync(data, ct);
        var overrides = (await db.SfxCategories.AsNoTracking().ToListAsync(ct)).ToDictionary(c => c.Category);

        var ordered = new List<(int Sort, SfxCategoryDto Category)>();
        var index = 0;
        foreach (var c in data.Categories)
        {
            overrides.TryGetValue(c.Id, out var setting);
            index++;
            if (setting is { Enabled: false }) continue;
            ordered.Add((setting?.Sort ?? index, new SfxCategoryDto(c.Id, string.IsNullOrWhiteSpace(setting?.Label) ? c.Label : setting.Label,
                c.Sounds.Select(s => new SfxSoundDto(s.Id, s.Title, s.DurationMs, string.IsNullOrEmpty(s.Tag) ? [] : [s.Tag],
                    $"{FilesBase}/previews/{EscapePath(s.PreviewPath)}", $"{FilesBase}/library/{EscapePath(s.LibraryPath)}", s.Size, s)).ToList(), c.Subcategories)));
        }

        return new SfxLibraryDto(data.Version, ordered.OrderBy(o => o.Sort).Select(o => o.Category).ToList());
    }

    private static string EscapePath(string path) => string.Join('/', path.Split('/').Select(Uri.EscapeDataString));

    private async Task SeedAsync(SfxData data, CancellationToken ct)
    {
        var have = (await db.SfxCategories.Select(c => c.Category).ToListAsync(ct)).ToHashSet();
        var order = 0;
        foreach (var c in data.Categories)
        {
            order++;
            if (!have.Add(c.Id)) continue;
            db.SfxCategories.Add(new SfxCategory { Category = c.Id, Enabled = true, Sort = order });
        }

        if (!db.SfxCategories.Local.Any()) return;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
        }
    }
}
