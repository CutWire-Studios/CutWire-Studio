using System.Text.Json;
using System.Text.RegularExpressions;
using CutWire.Studio.Application.Abstractions;
using CutWire.Studio.Application.Marketplace;
using CutWire.Studio.Application.Moderation;
using CutWire.Studio.Domain.Catalog;
using CutWire.Studio.Domain.Common;
using CutWire.Studio.Domain.Marketplace;
using CutWire.Studio.Domain.Stock;
using Microsoft.EntityFrameworkCore;

namespace CutWire.Studio.Application.Admin;

public sealed record LayoutRowDto(Guid Id, string Name, int Priority, bool Enabled, IReadOnlyList<string> Distributions, string? MinAppVersion, string? MaxAppVersion, DateTimeOffset UpdatedAt);

public sealed record LayoutEdit(string? Name, int Priority, bool Enabled, IReadOnlyList<string> Distributions, string? MinAppVersion, string? MaxAppVersion, string? PagesJson);

public sealed record FlagRowDto(Guid Id, string Key, bool Enabled, string? ValueJson, IReadOnlyList<string> Distributions, string? MinAppVersion, string? MaxAppVersion, string? Note, DateTimeOffset UpdatedAt);

public sealed record FlagEdit(string? Key, bool Enabled, string? ValueJson, IReadOnlyList<string> Distributions, string? MinAppVersion, string? MaxAppVersion, string? Note);

public sealed record ManifestPreview(string Json, string Layout);

/// <summary>Operator-edited marketplace layouts and feature flags. Every change is audited and drops the manifest cache.</summary>
public sealed partial class MarketplaceAdmin(IAppDbContext db, AuditLog audit, IClock clock, ManifestCache cache, MarketplaceManifest manifest)
{
    public const string Invalid = "marketplace.invalid";
    public const int MaxName = 100;
    public const int MaxNote = 500;
    public const string BuiltIn = "built-in default";

    [GeneratedRegex("^[a-z0-9][a-z0-9.-]{0,63}$")]
    private static partial Regex KeyPattern();

    private static readonly JsonSerializerOptions Pretty = new(JsonSerializerOptions.Web) { WriteIndented = true };

    public async Task<IReadOnlyList<LayoutRowDto>> LayoutsAsync(CancellationToken ct) =>
        (await db.MarketplaceLayouts.AsNoTracking().ToListAsync(ct))
        .OrderByDescending(l => l.Priority).ThenBy(l => l.Name, StringComparer.Ordinal)
        .Select(l => new LayoutRowDto(l.Id, l.Name, l.Priority, l.Enabled, l.Distributions, l.MinAppVersion, l.MaxAppVersion, l.UpdatedAt)).ToList();

    public Task<MarketplaceLayout?> LayoutAsync(Guid id, CancellationToken ct) =>
        db.MarketplaceLayouts.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id, ct);

    public async Task<IReadOnlyList<FlagRowDto>> FlagsAsync(CancellationToken ct) =>
        (await db.FeatureFlags.AsNoTracking().ToListAsync(ct))
        .OrderBy(f => f.Key, StringComparer.Ordinal).ThenBy(f => f.UpdatedAt)
        .Select(f => new FlagRowDto(f.Id, f.Key, f.Enabled, f.ValueJson, f.Distributions, f.MinAppVersion, f.MaxAppVersion, f.Note, f.UpdatedAt)).ToList();

    public Task<FeatureFlag?> FlagAsync(Guid id, CancellationToken ct) =>
        db.FeatureFlags.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct);

    private static void Targets(IReadOnlyList<string> distributions, string? min, string? max, List<FieldViolation> v)
    {
        if (distributions.Any(d => !Distributions.All.Contains(d))) v.Add(new("distributions", "enum", "Unknown distribution."));
        if (min is not null && !SemVersion.TryParse(min, out _)) v.Add(new("minAppVersion", "invalid_version", "Minimum version is not a valid version."));
        if (max is not null && !SemVersion.TryParse(max, out _)) v.Add(new("maxAppVersion", "invalid_version", "Maximum version is not a valid version."));
        if (SemVersion.TryParse(min, out var lo) && SemVersion.TryParse(max, out var hi) && lo.CompareTo(hi) >= 0)
            v.Add(new("maxAppVersion", "range", "Maximum version must be above the minimum."));
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IReadOnlyList<string> Distinct(IReadOnlyList<string> values) => values.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

    public async Task<Result<Guid>> SaveLayoutAsync(Guid actor, Guid? id, LayoutEdit edit, CancellationToken ct)
    {
        var name = Blank(edit.Name);
        var min = Blank(edit.MinAppVersion);
        var max = Blank(edit.MaxAppVersion);
        var distributions = Distinct(edit.Distributions);
        var violations = new List<FieldViolation>();
        if (name is null) violations.Add(new("name", "required", "Name is required."));
        else if (name.Length > MaxName) violations.Add(new("name", "too_long", $"Name must be at most {MaxName} characters."));
        Targets(distributions, min, max, violations);

        if (LayoutValidator.Validate(edit.PagesJson) is { IsFailure: true } bad && bad.Error.Violations is { } found) violations.AddRange(found);
        if (violations.Count > 0) return Error.Validation(Invalid, "The layout is not valid.", violations);

        MarketplaceLayout? row = null;
        if (id is { } existing)
        {
            row = await db.MarketplaceLayouts.FirstOrDefaultAsync(l => l.Id == existing, ct);
            if (row is null) return Error.NotFound("marketplace.layout_not_found", "Layout not found.");
        }

        if (await db.MarketplaceLayouts.AnyAsync(l => l.Name == name && (row == null || l.Id != row.Id), ct))
            return Error.Validation(Invalid, "The layout is not valid.", [new FieldViolation("name", "duplicate", "Another layout already has this name.")]);

        var created = row is null;
        row ??= db.MarketplaceLayouts.Add(new MarketplaceLayout()).Entity;
        (row.Name, row.Priority, row.Enabled, row.Distributions, row.MinAppVersion, row.MaxAppVersion, row.PagesJson, row.UpdatedAt, row.UpdatedBy) =
            (name!, edit.Priority, edit.Enabled, distributions.ToList(), min, max, edit.PagesJson!.Trim(), clock.UtcNow, actor);
        audit.Record(actor, "marketplace.layout.save", "mkt_layout", row.Id,
            details: new { row.Name, created, row.Priority, row.Enabled, row.Distributions, row.MinAppVersion, row.MaxAppVersion });
        await db.SaveChangesAsync(ct);
        cache.Invalidate();
        return row.Id;
    }

    public async Task<Result> DeleteLayoutAsync(Guid actor, Guid id, CancellationToken ct)
    {
        var row = await db.MarketplaceLayouts.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (row is null) return Error.NotFound("marketplace.layout_not_found", "Layout not found.");
        db.MarketplaceLayouts.Remove(row);
        audit.Record(actor, "marketplace.layout.delete", "mkt_layout", row.Id, details: new { row.Name });
        await db.SaveChangesAsync(ct);
        cache.Invalidate();
        return Result.Success();
    }

    public async Task<Result<Guid>> SaveFlagAsync(Guid actor, Guid? id, FlagEdit edit, CancellationToken ct)
    {
        var key = Blank(edit.Key);
        var min = Blank(edit.MinAppVersion);
        var max = Blank(edit.MaxAppVersion);
        var note = Blank(edit.Note);
        var value = Blank(edit.ValueJson);
        var distributions = Distinct(edit.Distributions);
        var violations = new List<FieldViolation>();
        if (key is null || !KeyPattern().IsMatch(key))
            violations.Add(new("key", "pattern", "Key uses lowercase letters, digits, dots and dashes, 1 to 64 characters."));
        if (note is { Length: > MaxNote }) violations.Add(new("note", "too_long", $"Note must be at most {MaxNote} characters."));
        if (value is not null)
        {
            try
            {
                using var _ = JsonDocument.Parse(value);
            }
            catch (JsonException)
            {
                violations.Add(new("valueJson", "invalid_json", "Value is not valid JSON."));
            }
        }

        Targets(distributions, min, max, violations);
        if (violations.Count > 0) return Error.Validation(Invalid, "The flag is not valid.", violations);

        FeatureFlag? row = null;
        if (id is { } existing)
        {
            row = await db.FeatureFlags.FirstOrDefaultAsync(f => f.Id == existing, ct);
            if (row is null) return Error.NotFound("marketplace.flag_not_found", "Flag not found.");
        }

        var created = row is null;
        row ??= db.FeatureFlags.Add(new FeatureFlag()).Entity;
        (row.Key, row.Enabled, row.ValueJson, row.Distributions, row.MinAppVersion, row.MaxAppVersion, row.Note, row.UpdatedAt) =
            (key!, edit.Enabled, value, distributions.ToList(), min, max, note, clock.UtcNow);
        audit.Record(actor, "marketplace.flag.save", "feature_flag", row.Id,
            details: new { row.Key, created, row.Enabled, row.ValueJson, row.Distributions, row.MinAppVersion, row.MaxAppVersion });
        await db.SaveChangesAsync(ct);
        cache.Invalidate();
        return row.Id;
    }

    public async Task<Result> DeleteFlagAsync(Guid actor, Guid id, CancellationToken ct)
    {
        var row = await db.FeatureFlags.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (row is null) return Error.NotFound("marketplace.flag_not_found", "Flag not found.");
        db.FeatureFlags.Remove(row);
        audit.Record(actor, "marketplace.flag.delete", "feature_flag", row.Id, details: new { row.Key });
        await db.SaveChangesAsync(ct);
        cache.Invalidate();
        return Result.Success();
    }

    /// <summary>The manifest a client of <paramref name="distribution"/> and <paramref name="version"/> would receive, and the layout it came from.</summary>
    public async Task<Result<ManifestPreview>> PreviewAsync(string? distribution, string? version, CancellationToken ct)
    {
        var normalized = MarketplaceManifest.Normalize(distribution ?? "");
        SemVersion? parsed = null;
        if (Blank(version) is { } text && !SemVersion.TryParse(text, out parsed))
            return Error.Validation(Invalid, "The version is not a valid version.");

        var built = await manifest.BuildAsync(normalized, parsed, ct);
        var chosen = (await db.MarketplaceLayouts.AsNoTracking().Where(l => l.Enabled).ToListAsync(ct))
            .Where(l => l.Matches(normalized, parsed))
            .OrderByDescending(l => l.Priority).ThenByDescending(l => l.UpdatedAt)
            .FirstOrDefault(l => LayoutValidator.Validate(l.PagesJson).IsSuccess);
        return new ManifestPreview(JsonSerializer.Serialize(built, Pretty), chosen?.Name ?? BuiltIn);
    }
}
