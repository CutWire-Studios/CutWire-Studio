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
}

public sealed record SfxSoundData(string Id, string Title, string Tag, int DurationMs, long Size, string PreviewPath, string LibraryPath);

public sealed record SfxCategoryData(string Id, string Label, IReadOnlyList<SfxSoundData> Sounds);

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
    private sealed record RawSound(string Id, string? Name, string? Category, string? Subcategory, double Duration, long Size, string? Path, string? Preview);

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
            categories.Add(new SfxCategoryData(c.Id, c.Label ?? c.Id, sounds.Select(s => new SfxSoundData(
                s.Id, s.Name ?? s.Id,
                subLabels.GetValueOrDefault((c.Id, s.Subcategory ?? ""), s.Subcategory ?? ""),
                (int)Math.Round(s.Duration * 1000), s.Size, s.Preview!, s.Path!)).ToList()));
        }

        return new SfxData(version, categories);
    }

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
                    $"{FilesBase}/previews/{EscapePath(s.PreviewPath)}", $"{FilesBase}/library/{EscapePath(s.LibraryPath)}", s.Size)).ToList())));
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
