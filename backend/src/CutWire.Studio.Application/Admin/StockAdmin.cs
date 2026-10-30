using CutWire.Studio.Application.Abstractions;
using CutWire.Studio.Application.Moderation;
using CutWire.Studio.Application.Stock;
using CutWire.Studio.Domain.Common;
using CutWire.Studio.Domain.Stock;
using Microsoft.EntityFrameworkCore;

namespace CutWire.Studio.Application.Admin;

public sealed record ProviderRowDto(
    string Provider, string ProviderLabel, string Type, bool Enabled, string? Label, int Sort, int QuotaLimit, string QuotaWindow,
    string QuotaScope, IReadOnlyList<string> HiddenFor);

public sealed record ProviderEdit(bool Enabled, string? Label, int Sort, int QuotaLimit, string? QuotaWindow, string? QuotaScope);

public sealed record CategoryRowDto(string Category, bool Enabled, string? Label, int Sort);

public sealed record CategoryEdit(bool Enabled, string? Label, int Sort);

public static class CategoryKinds
{
    public const string Asset = "asset";
    public const string Sfx = "sfx";
}

/// <summary>Operator configuration edited from the admin dashboard: provider settings, distribution visibility and category overrides.</summary>
public sealed class StockAdmin(IAppDbContext db, StockSettings settings, StockRegistry registry, AuditLog audit)
{
    public const int MaxLabel = 80;

    private static Error Bad(string field, string code, string message) =>
        Error.Validation("bad_request", message, [new FieldViolation(field, code, message)]);

    public async Task<IReadOnlyList<ProviderRowDto>> ProvidersAsync(CancellationToken ct)
    {
        await settings.SeedAsync(ct);
        var rows = await db.ProviderSettings.AsNoTracking().ToListAsync(ct);
        return rows.OrderBy(r => r.Type, StringComparer.Ordinal).ThenBy(r => r.Sort).ThenBy(r => r.Provider, StringComparer.Ordinal)
            .Select(r => new ProviderRowDto(r.Provider, registry.Get(r.Provider)?.Descriptor.Label ?? r.Provider, r.Type, r.Enabled, r.Label,
                r.Sort, r.QuotaLimit, r.QuotaWindow, r.QuotaScope, r.HiddenFor))
            .ToList();
    }

    public async Task<Result> SaveProviderAsync(Guid actor, string provider, string type, ProviderEdit edit, CancellationToken ct)
    {
        var label = string.IsNullOrWhiteSpace(edit.Label) ? null : edit.Label.Trim();
        if (label is { Length: > MaxLabel }) return Bad("label", "too_long", $"Label must be at most {MaxLabel} characters.");
        if (edit.Sort < 0) return Bad("sort", "out_of_range", "Sort must not be negative.");
        if (edit.QuotaLimit < 0) return Bad("quotaLimit", "out_of_range", "Quota limit must not be negative.");
        if (edit.QuotaWindow is not ("day" or "hour")) return Bad("quotaWindow", "invalid_window", "Quota window must be day or hour.");
        if (edit.QuotaScope is not ("client" or "account")) return Bad("quotaScope", "invalid_scope", "Quota scope must be client or account.");

        var row = await db.ProviderSettings.FirstOrDefaultAsync(r => r.Provider == provider && r.Type == type, ct);
        if (row is null) return Error.NotFound("provider.not_found", "Provider setting not found.");

        var before = new { row.Enabled, row.Label, row.Sort, row.QuotaLimit, row.QuotaWindow, row.QuotaScope };
        row.Enabled = edit.Enabled;
        row.Label = label;
        row.Sort = edit.Sort;
        row.QuotaLimit = edit.QuotaLimit;
        row.QuotaWindow = edit.QuotaWindow;
        row.QuotaScope = edit.QuotaScope;
        audit.Record(actor, "update_provider", "provider_setting", Guid.Empty, details: new
        {
            provider, type, before, after = new { row.Enabled, row.Label, row.Sort, row.QuotaLimit, row.QuotaWindow, row.QuotaScope },
        });
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <param name="visible">Every ticked cell as (provider, type, distribution); anything not listed becomes hidden.</param>
    public async Task<Result> SaveDistributionsAsync(Guid actor, IReadOnlySet<(string Provider, string Type, string Distribution)> visible, CancellationToken ct)
    {
        var rows = await db.ProviderSettings.ToListAsync(ct);
        foreach (var cell in visible)
            if (!Distributions.All.Contains(cell.Distribution) || !rows.Any(r => r.Provider == cell.Provider && r.Type == cell.Type))
                return Bad("distribution", "invalid_cell", "Unknown provider or distribution.");

        var changes = new List<object>();
        foreach (var row in rows)
        {
            var hidden = Distributions.All.Where(d => !visible.Contains((row.Provider, row.Type, d))).Order(StringComparer.Ordinal).ToList();
            if (row.HiddenFor.Order(StringComparer.Ordinal).SequenceEqual(hidden)) continue;
            changes.Add(new { provider = row.Provider, type = row.Type, before = row.HiddenFor, after = hidden });
            row.HiddenFor = hidden;
        }

        if (changes.Count == 0) return Result.Success();
        audit.Record(actor, "update_distributions", "provider_setting", Guid.Empty, details: new { changes });
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<IReadOnlyList<CategoryRowDto>> CategoriesAsync(string kind, CancellationToken ct) =>
        kind == CategoryKinds.Sfx
            ? await db.SfxCategories.AsNoTracking().OrderBy(c => c.Sort).ThenBy(c => c.Category)
                .Select(c => new CategoryRowDto(c.Category, c.Enabled, c.Label, c.Sort)).ToListAsync(ct)
            : await db.AssetCategories.AsNoTracking().OrderBy(c => c.Sort).ThenBy(c => c.Category)
                .Select(c => new CategoryRowDto(c.Category, c.Enabled, c.Label, c.Sort)).ToListAsync(ct);

    public async Task<Result> SaveCategoryAsync(Guid actor, string kind, string category, CategoryEdit edit, CancellationToken ct)
    {
        var label = string.IsNullOrWhiteSpace(edit.Label) ? null : edit.Label.Trim();
        if (label is { Length: > MaxLabel }) return Bad("label", "too_long", $"Label must be at most {MaxLabel} characters.");
        if (edit.Sort < 0) return Bad("sort", "out_of_range", "Sort must not be negative.");

        object before, after;
        if (kind == CategoryKinds.Sfx)
        {
            var row = await db.SfxCategories.FirstOrDefaultAsync(c => c.Category == category, ct);
            if (row is null) return Error.NotFound("category.not_found", "Category not found.");
            before = new { row.Enabled, row.Label, row.Sort };
            (row.Enabled, row.Label, row.Sort) = (edit.Enabled, label, edit.Sort);
            after = new { row.Enabled, row.Label, row.Sort };
        }
        else
        {
            var row = await db.AssetCategories.FirstOrDefaultAsync(c => c.Category == category, ct);
            if (row is null) return Error.NotFound("category.not_found", "Category not found.");
            before = new { row.Enabled, row.Label, row.Sort };
            (row.Enabled, row.Label, row.Sort) = (edit.Enabled, label, edit.Sort);
            after = new { row.Enabled, row.Label, row.Sort };
        }

        audit.Record(actor, "update_category", kind == CategoryKinds.Sfx ? "sfx_category" : "asset_category", Guid.Empty,
            details: new { category, before, after });
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
