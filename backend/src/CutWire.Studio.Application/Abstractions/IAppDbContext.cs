using CutWire.Studio.Domain.Accounts;
using CutWire.Studio.Domain.Blobs;
using CutWire.Studio.Domain.Catalog;
using CutWire.Studio.Domain.Forge;
using CutWire.Studio.Domain.Marketplace;
using CutWire.Studio.Domain.Moderation;
using CutWire.Studio.Domain.Stock;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CutWire.Studio.Application.Abstractions;

public interface IAppDbContext
{
    DbSet<Blob> Blobs { get; }
    DbSet<UserProfile> UserProfiles { get; }
    DbSet<PersonalAccessToken> PersonalAccessTokens { get; }
    DbSet<Item> Items { get; }
    DbSet<ItemVersion> ItemVersions { get; }
    DbSet<ItemMedia> ItemMedia { get; }
    DbSet<Tag> Tags { get; }
    DbSet<ItemTag> ItemTags { get; }
    DbSet<ItemFlag> ItemFlags { get; }
    DbSet<DownloadEvent> DownloadEvents { get; }
    DbSet<ItemDownloadDaily> ItemDownloadDaily { get; }
    DbSet<Vote> Votes { get; }
    DbSet<Report> Reports { get; }
    DbSet<ModerationAction> ModerationActions { get; }
    DbSet<CleanupSchedule> CleanupSchedules { get; }
    DbSet<ForgeDocument> ForgeDocuments { get; }
    DbSet<ForgeDocAsset> ForgeDocAssets { get; }
    DbSet<AiUsage> AiUsage { get; }
    DbSet<ThemeDraft> ThemeDrafts { get; }
    DbSet<ProviderSetting> ProviderSettings { get; }
    DbSet<DownloadJob> DownloadJobs { get; }
    DbSet<QuotaEvent> QuotaEvents { get; }
    DbSet<AssetCategory> AssetCategories { get; }
    DbSet<SfxCategory> SfxCategories { get; }
    DbSet<ProviderCacheEntry> ProviderCache { get; }
    DbSet<DriftAssetPack> DriftAssetPacks { get; }
    DbSet<MarketplaceLayout> MarketplaceLayouts { get; }
    DbSet<FeatureFlag> FeatureFlags { get; }
    Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade Database { get; }

    Task<bool> CanConnectAsync(CancellationToken ct);
    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct);
    /// <summary>Takes a Postgres transaction-scoped advisory lock for <paramref name="key"/>; must be called inside a transaction.</summary>
    Task LockForTransactionAsync(string key, CancellationToken ct);
    Task<int> SaveChangesAsync(CancellationToken ct);
}
