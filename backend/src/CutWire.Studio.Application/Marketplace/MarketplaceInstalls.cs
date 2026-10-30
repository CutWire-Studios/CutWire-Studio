using CutWire.Studio.Application.Abstractions;
using CutWire.Studio.Application.Catalog;
using CutWire.Studio.Application.Stock;
using CutWire.Studio.Domain.Catalog;
using CutWire.Studio.Domain.Common;
using CutWire.Studio.Domain.Stock;
using Microsoft.EntityFrameworkCore;

namespace CutWire.Studio.Application.Marketplace;

public sealed record MarketInstallCommand(string? Source, string? ItemId, string? VariantId);

/// <summary>
/// Turns an item into the files a client needs. Drift Assets, SFX and community items are ready at once and are not stored; only stock
/// downloads are real jobs, and their ids carry <see cref="StockJobPrefix"/> so a poll can find them.
/// </summary>
public sealed class MarketplaceInstalls(
    MarketplaceManifest manifest,
    DriftAssetsCatalog driftAssets,
    SfxCatalog sfx,
    DownloadJobs jobs,
    ICommandHandler<RecordDownloadCommand, DownloadTarget> record,
    IAppDbContext db)
{
    public const string StockJobPrefix = "stock_";

    private static readonly StockLicenseDto DriftLicense = new("Drift Assets", null, null);
    private static readonly StockLicenseDto SfxLicense = new("Drift sound effects", null, null);

    public async Task<Result<InstallJobDto>> CreateAsync(MarketCaller c, MarketInstallCommand command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.Source) || string.IsNullOrWhiteSpace(command.ItemId))
            return Error.Validation("bad_request", "A source and an item are required.",
                new Dictionary<string, string[]> { ["source"] = ["Required."], ["itemId"] = ["Required."] });
        var source = command.Source;
        var (_, sources) = await manifest.TargetAsync(c.Distribution, c.Version, ct);
        if (!sources.TryGetValue(source, out var def)) return MarketErrors.UnknownSource;

        return source switch
        {
            "drift-assets" => await DriftAsync(c, command, ct),
            "sfx" => await SfxAsync(c, command, ct),
            _ when def.Kind == "stock" => await StockAsync(c, source["stock.".Length..], command, ct),
            _ => await CommunityAsync(c, source["community.".Length..], command, ct),
        };
    }

    public async Task<Result<InstallJobDto>> GetAsync(MarketCaller c, string id, CancellationToken ct)
    {
        if (!id.StartsWith(StockJobPrefix, StringComparison.Ordinal)) return Error.NotFound("install_not_found", "That install is unknown or has expired.");
        var raw = id[StockJobPrefix.Length..];
        var job = await jobs.GetAsync(c.Stock, raw, ct);
        if (job.IsFailure) return Error.NotFound("install_not_found", "That install is unknown or has expired.");
        var type = Guid.TryParse(raw, out var guid) ? await db.DownloadJobs.AsNoTracking().Where(j => j.Id == guid).Select(j => j.Type).FirstOrDefaultAsync(ct) : null;
        return StockJob(c, job.Value, "stock." + type);
    }

    private static InstallJobDto Ready(string source, IReadOnlyList<InstallFileDto> files, StockLicenseDto license, string? minAppVersion = null) =>
        new("ready_" + Guid.CreateVersion7().ToString("N"), source, JobStatus.Ready, 1, files, license, null, minAppVersion);

    private async Task<Result<InstallJobDto>> DriftAsync(MarketCaller c, MarketInstallCommand command, CancellationToken ct)
    {
        var asset = (await driftAssets.GetRawAsync(ct)).SelectMany(cat => cat.Assets).FirstOrDefault(a => a.Id == command.ItemId);
        if (asset is null) return MarketErrors.ItemNotFound;

        var files = asset.Files;
        if (asset.Variants.Count > 0)
        {
            var variant = string.IsNullOrWhiteSpace(command.VariantId) ? asset.Variants[0] : asset.Variants.FirstOrDefault(v => v.Id == command.VariantId);
            if (variant is null) return Error.NotFound("variant_not_found", "That variant is not available.");
            files = variant.Files;
        }
        else if (!string.IsNullOrWhiteSpace(command.VariantId) && command.VariantId != "default")
        {
            return Error.NotFound("variant_not_found", "That variant is not available.");
        }

        var ordered = files.OrderBy(f => f.Key == "main" ? 0 : 1).ThenBy(f => f.Key, StringComparer.Ordinal)
            .Select(f => new InstallFileDto(f.Key, f.Value.Name, c.Abs(MarketplaceBrowse.DriftUrl(f.Value)), f.Value.Size, f.Value.Sha256, MarketMime.For(f.Value.Name))).ToList();
        return Ready("drift-assets", ordered, DriftLicense);
    }

    private async Task<Result<InstallJobDto>> SfxAsync(MarketCaller c, MarketInstallCommand command, CancellationToken ct)
    {
        var sound = (await sfx.GetAsync(ct)).Categories.SelectMany(cat => cat.Sounds).FirstOrDefault(s => s.Id == command.ItemId);
        if (sound is null) return MarketErrors.ItemNotFound;
        if (!string.IsNullOrWhiteSpace(command.VariantId) && command.VariantId != "default") return Error.NotFound("variant_not_found", "That variant is not available.");
        var name = MarketplaceBrowse.SfxFileName(sound);
        return Ready("sfx", [new InstallFileDto("main", name, c.Abs(sound.DownloadUrl), sound.Size, null, MarketMime.For(name))], SfxLicense);
    }

    private async Task<Result<InstallJobDto>> StockAsync(MarketCaller c, string type, MarketInstallCommand command, CancellationToken ct)
    {
        if (!ItemIds.TryDecode(command.ItemId, out var item) || item.Type != type) return MarketErrors.ItemNotFound;
        var job = await jobs.CreateAsync(new CreateDownloadCommand(c.Stock, command.ItemId!, command.VariantId), ct);
        return job.IsFailure ? job.Error : StockJob(c, job.Value, "stock." + type);
    }

    private static InstallJobDto StockJob(MarketCaller c, DownloadJobDto job, string source) =>
        new(MarketplaceInstalls.StockJobPrefix + job.Id, source, job.Status, job.Progress,
            job.File is { } f ? [new InstallFileDto("main", f.Filename, c.Abs(f.Url), f.Size, f.Sha256, f.Mime)] : null,
            job.License, job.Error, null);

    private async Task<Result<InstallJobDto>> CommunityAsync(MarketCaller c, string kind, MarketInstallCommand command, CancellationToken ct)
    {
        if (!Guid.TryParse(command.ItemId, out var itemId)) return MarketErrors.ItemNotFound;
        var item = await db.Items.AsNoTracking().Include(i => i.Versions).FirstOrDefaultAsync(i => i.Id == itemId, ct);
        if (item is null || item.Status != ItemStatus.Published || item.Kind.Value != kind) return MarketErrors.ItemNotFound;
        var version = item.Versions.FirstOrDefault(v => v.Id == item.CurrentVersionId);
        if (version is null) return MarketErrors.ItemNotFound;

        if (c.Version is { } app && SemVersion.TryParse(version.MinAppVersion, out var min) && app.CompareTo(min) < 0)
            return Error.Conflict("app_too_old", $"This item needs Drift {version.MinAppVersion} or newer.").With("minAppVersion", version.MinAppVersion);

        var target = await record.Handle(new RecordDownloadCommand(itemId, null, null, c.InstallId, c.IpHash), ct);
        if (target.IsFailure) return target.Error;
        var file = new InstallFileDto("main", target.Value.FileName, $"{c.BaseUrl}/files/{target.Value.Sha256}?name={Uri.EscapeDataString(target.Value.FileName)}",
            version.Size, target.Value.Sha256, MarketMime.For(target.Value.FileName));
        return Ready("community." + kind, [file], new StockLicenseDto(item.License, null, null), version.MinAppVersion);
    }
}
