using CutWire.Studio.Domain.Blobs;
using CutWire.Studio.Domain.Stock;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CutWire.Studio.Infrastructure.Persistence;

internal sealed class ProviderSettingConfiguration : IEntityTypeConfiguration<ProviderSetting>
{
    public void Configure(EntityTypeBuilder<ProviderSetting> builder)
    {
        builder.ToTable("provider_settings");
        builder.HasKey(s => new { s.Provider, s.Type });
        builder.Property(s => s.Provider).HasMaxLength(64);
        builder.Property(s => s.Type).HasMaxLength(32);
        builder.Property(s => s.Label).HasMaxLength(128);
        builder.Property(s => s.QuotaWindow).HasMaxLength(8).IsRequired();
        builder.Property(s => s.QuotaScope).HasMaxLength(8).IsRequired();
        builder.Property(s => s.HiddenFor).HasColumnName("hidden_for").HasDefaultValueSql("'{}'");
    }
}

internal sealed class DownloadJobConfiguration : IEntityTypeConfiguration<DownloadJob>
{
    public void Configure(EntityTypeBuilder<DownloadJob> builder)
    {
        builder.ToTable("download_jobs");
        builder.HasKey(j => j.Id);
        builder.Property(j => j.Id).ValueGeneratedNever();
        builder.Property(j => j.ClientId).HasMaxLength(80).IsRequired();
        builder.Property(j => j.Provider).HasMaxLength(64).IsRequired();
        builder.Property(j => j.Type).HasMaxLength(32).IsRequired();
        builder.Property(j => j.Native).HasMaxLength(2048).IsRequired();
        builder.Property(j => j.VariantId).HasMaxLength(128);
        builder.Property(j => j.Status).HasMaxLength(16).IsRequired();
        builder.Property(j => j.QuotaSubject).HasMaxLength(80).IsRequired();
        builder.Property(j => j.QuotaWindow).HasMaxLength(8).IsRequired();
        builder.Property(j => j.BlobSha256).HasColumnName("blob")
            .HasMaxLength(Sha256Hash.HexLength).IsFixedLength()
            .HasConversion(h => h!.Value.Value, s => (Sha256Hash?)new Sha256Hash(s));
        builder.Property(j => j.Filename).HasMaxLength(255);
        builder.Property(j => j.Mime).HasMaxLength(127);
        builder.Property(j => j.FileSha256).HasMaxLength(Sha256Hash.HexLength);
        builder.Property(j => j.LicenseName).HasMaxLength(256);
        builder.Property(j => j.LicenseUrl).HasMaxLength(1024);
        builder.Property(j => j.Attribution).HasMaxLength(1024);
        builder.Property(j => j.ErrorCode).HasMaxLength(64);
        builder.Property(j => j.ErrorDetail).HasMaxLength(512);
        builder.HasOne<Blob>().WithMany().HasForeignKey(j => j.BlobSha256).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(j => new { j.ClientId, j.Status });
        builder.HasIndex(j => j.ExpiresAt).HasFilter("expires_at is not null");
    }
}

internal sealed class QuotaEventConfiguration : IEntityTypeConfiguration<QuotaEvent>
{
    public void Configure(EntityTypeBuilder<QuotaEvent> builder)
    {
        builder.ToTable("quota_events");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.Property(e => e.Subject).HasMaxLength(80).IsRequired();
        builder.Property(e => e.Provider).HasMaxLength(64).IsRequired();
        builder.Property(e => e.Kind).HasMaxLength(16).IsRequired();
        builder.HasIndex(e => new { e.Subject, e.Provider, e.WindowStart, e.Kind });
        builder.HasIndex(e => new { e.JobId, e.Kind }).IsUnique().HasFilter("job_id is not null");
        builder.HasIndex(e => e.WindowStart);
    }
}

internal sealed class AssetCategoryConfiguration : IEntityTypeConfiguration<AssetCategory>
{
    public void Configure(EntityTypeBuilder<AssetCategory> builder)
    {
        builder.ToTable("asset_categories");
        builder.HasKey(c => c.Category);
        builder.Property(c => c.Category).HasMaxLength(64);
        builder.Property(c => c.Label).HasMaxLength(128);
    }
}

internal sealed class SfxCategoryConfiguration : IEntityTypeConfiguration<SfxCategory>
{
    public void Configure(EntityTypeBuilder<SfxCategory> builder)
    {
        builder.ToTable("sfx_categories");
        builder.HasKey(c => c.Category);
        builder.Property(c => c.Category).HasMaxLength(64);
        builder.Property(c => c.Label).HasMaxLength(128);
    }
}

internal sealed class ProviderCacheEntryConfiguration : IEntityTypeConfiguration<ProviderCacheEntry>
{
    public void Configure(EntityTypeBuilder<ProviderCacheEntry> builder)
    {
        builder.ToTable("provider_cache");
        builder.HasKey(e => e.Key);
        builder.Property(e => e.Key).HasMaxLength(2200);
        builder.Property(e => e.Body).IsRequired();
        builder.HasIndex(e => e.ExpiresAt);
    }
}

internal sealed class DriftAssetPackConfiguration : IEntityTypeConfiguration<DriftAssetPack>
{
    public void Configure(EntityTypeBuilder<DriftAssetPack> builder)
    {
        builder.ToTable("drift_asset_packs");
        builder.HasKey(p => p.Version);
        builder.Property(p => p.Version).HasMaxLength(64);
        builder.Property(p => p.IndexJson).HasColumnType("jsonb").IsRequired();
        builder.Property(p => p.FilesJson).HasColumnType("jsonb").IsRequired();
    }
}
