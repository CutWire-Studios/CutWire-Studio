using CutWire.Studio.Application.Abstractions;
using CutWire.Studio.Domain.Accounts;
using CutWire.Studio.Domain.Blobs;
using CutWire.Studio.Domain.Catalog;
using CutWire.Studio.Domain.Forge;
using CutWire.Studio.Domain.Moderation;
using CutWire.Studio.Domain.Stock;
using CutWire.Studio.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using OpenIddict.EntityFrameworkCore.Models;

namespace CutWire.Studio.Infrastructure.Persistence;

public sealed class StudioDbContext(DbContextOptions<StudioDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options), IAppDbContext
{
    public DbSet<Blob> Blobs => Set<Blob>();
    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();
    public DbSet<PersonalAccessToken> PersonalAccessTokens => Set<PersonalAccessToken>();
    public DbSet<Item> Items => Set<Item>();
    public DbSet<ItemVersion> ItemVersions => Set<ItemVersion>();
    public DbSet<ItemMedia> ItemMedia => Set<ItemMedia>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<ItemTag> ItemTags => Set<ItemTag>();
    public DbSet<ItemFlag> ItemFlags => Set<ItemFlag>();
    public DbSet<DownloadEvent> DownloadEvents => Set<DownloadEvent>();
    public DbSet<ItemDownloadDaily> ItemDownloadDaily => Set<ItemDownloadDaily>();

    public DbSet<Vote> Votes => Set<Vote>();
    public DbSet<Report> Reports => Set<Report>();
    public DbSet<ModerationAction> ModerationActions => Set<ModerationAction>();
    public DbSet<CleanupSchedule> CleanupSchedules => Set<CleanupSchedule>();
    public DbSet<ForgeDocument> ForgeDocuments => Set<ForgeDocument>();
    public DbSet<ForgeDocAsset> ForgeDocAssets => Set<ForgeDocAsset>();
    public DbSet<AiUsage> AiUsage => Set<AiUsage>();
    public DbSet<ThemeDraft> ThemeDrafts => Set<ThemeDraft>();
    public DbSet<ProviderSetting> ProviderSettings => Set<ProviderSetting>();
    public DbSet<DownloadJob> DownloadJobs => Set<DownloadJob>();
    public DbSet<QuotaEvent> QuotaEvents => Set<QuotaEvent>();
    public DbSet<AssetCategory> AssetCategories => Set<AssetCategory>();
    public DbSet<SfxCategory> SfxCategories => Set<SfxCategory>();
    public DbSet<ProviderCacheEntry> ProviderCache => Set<ProviderCacheEntry>();
    public DbSet<DriftAssetPack> DriftAssetPacks => Set<DriftAssetPack>();

    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct) => Database.BeginTransactionAsync(ct);

    public Task<bool> CanConnectAsync(CancellationToken ct) => Database.CanConnectAsync(ct);

    public Task LockForTransactionAsync(string key, CancellationToken ct) =>
        Database.ExecuteSqlAsync($"select pg_advisory_xact_lock(hashtextextended({key}, 0))", ct);

    Task<int> IAppDbContext.SaveChangesAsync(CancellationToken ct) => SaveChangesAsync(ct);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.UseOpenIddict<Guid>();
        modelBuilder.HasPostgresExtension("pg_trgm");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(StudioDbContext).Assembly);

        modelBuilder.Entity<ApplicationUser>().ToTable("users");
        modelBuilder.Entity<IdentityRole<Guid>>().ToTable("roles");
        modelBuilder.Entity<IdentityUserRole<Guid>>().ToTable("user_roles");
        modelBuilder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        modelBuilder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        modelBuilder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");
        modelBuilder.Entity<IdentityRoleClaim<Guid>>().ToTable("role_claims");
        modelBuilder.Entity<IdentityUserPasskey<Guid>>().ToTable("user_passkeys");
        modelBuilder.Entity<OpenIddictEntityFrameworkCoreApplication<Guid>>().ToTable("openiddict_applications");
        modelBuilder.Entity<OpenIddictEntityFrameworkCoreAuthorization<Guid>>().ToTable("openiddict_authorizations");
        modelBuilder.Entity<OpenIddictEntityFrameworkCoreScope<Guid>>().ToTable("openiddict_scopes");
        modelBuilder.Entity<OpenIddictEntityFrameworkCoreToken<Guid>>().ToTable("openiddict_tokens");
    }
}
