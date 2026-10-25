using CutWire.Studio.Application.Abstractions;
using CutWire.Studio.Domain.Stock;
using Microsoft.EntityFrameworkCore;

namespace CutWire.Studio.Application.Stock;

/// <summary>A provider serving a type, joined with its operator settings.</summary>
public sealed record StockEntry(IStockProvider Provider, StockService Service, ProviderSetting Setting)
{
    public string Label => string.IsNullOrWhiteSpace(Setting.Label) ? Provider.Descriptor.Label : Setting.Label;
}

/// <summary>
/// provider_settings: seeded from each provider's defaults the first time it is seen and never overwritten afterwards, so an operator's
/// tuning survives a deploy that changes a built-in default.
/// </summary>
public sealed class StockSettings(IAppDbContext db, StockRegistry registry)
{
    public async Task SeedAsync(CancellationToken ct)
    {
        var have = (await db.ProviderSettings.AsNoTracking().Select(s => new { s.Provider, s.Type }).ToListAsync(ct))
            .Select(s => (s.Provider, s.Type)).ToHashSet();
        var order = 0;
        foreach (var provider in registry.All)
        {
            var d = provider.Descriptor;
            foreach (var service in d.Serves)
            {
                if (have.Contains((d.Id, service.Type))) continue;
                db.ProviderSettings.Add(new ProviderSetting
                {
                    Provider = d.Id,
                    Type = service.Type,
                    Enabled = true,
                    Sort = order,
                    QuotaLimit = service.Quota?.Limit ?? 0,
                    QuotaWindow = service.Quota?.Window ?? "day",
                    QuotaScope = service.Quota?.Scope ?? "client",
                });
            }

            order++;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Another instance seeded the same rows first.
        }
    }

    /// <summary>Providers enabled for a type, in configured order. Settings are seeded lazily so a freshly registered provider shows up immediately.</summary>
    public async Task<IReadOnlyList<StockEntry>> EntriesForAsync(string type, CancellationToken ct)
    {
        var serving = registry.All.Where(p => p.Descriptor.Serving(type) is not null).ToList();
        if (serving.Count == 0) return [];
        var settings = await LoadAsync(ct);
        var entries = new List<StockEntry>();
        foreach (var provider in serving)
        {
            var d = provider.Descriptor;
            if (!settings.TryGetValue((d.Id, type), out var setting) || !setting.Enabled) continue;
            entries.Add(new StockEntry(provider, d.Serving(type)!, setting));
        }

        return entries.OrderBy(e => e.Setting.Sort).ToList();
    }

    public async Task<StockEntry?> EntryAsync(string providerId, string type, CancellationToken ct) =>
        (await EntriesForAsync(type, ct)).FirstOrDefault(e => e.Provider.Descriptor.Id == providerId);

    private async Task<Dictionary<(string, string), ProviderSetting>> LoadAsync(CancellationToken ct)
    {
        var rows = await db.ProviderSettings.AsNoTracking().ToListAsync(ct);
        if (registry.All.Any(p => p.Descriptor.Serves.Any(s => !rows.Any(r => r.Provider == p.Descriptor.Id && r.Type == s.Type))))
        {
            await SeedAsync(ct);
            rows = await db.ProviderSettings.AsNoTracking().ToListAsync(ct);
        }

        return rows.ToDictionary(r => (r.Provider, r.Type));
    }
}
