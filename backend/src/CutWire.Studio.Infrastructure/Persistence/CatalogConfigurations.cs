using CutWire.Studio.Domain.Blobs;
using CutWire.Studio.Domain.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;

namespace CutWire.Studio.Infrastructure.Persistence;

internal static class ConfigurationExtensions
{
    public static PropertyBuilder<Sha256Hash> AsSha256(this PropertyBuilder<Sha256Hash> property) => property
        .HasMaxLength(Sha256Hash.HexLength)
        .IsFixedLength()
        .HasConversion(h => h.Value, s => new Sha256Hash(s));
}

internal sealed class ItemConfiguration : IEntityTypeConfiguration<Item>
{
    private const string SearchVectorSql =
        "setweight(to_tsvector('english', coalesce(title, '')), 'A') || " +
        "setweight(to_tsvector('english', coalesce(search_tags, '')), 'B') || " +
        "setweight(to_tsvector('english', coalesce(summary, '')), 'C') || " +
        "setweight(to_tsvector('english', coalesce(description, '') || ' ' || coalesce(search_extra, '')), 'D')";

    public void Configure(EntityTypeBuilder<Item> builder)
    {
        builder.ToTable("items", t => t.HasCheckConstraint("ck_items_slug_format", "slug ~ '^[a-z0-9]+(-[a-z0-9]+)*$'"));
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();
        builder.Property(i => i.Kind).HasMaxLength(40).HasConversion(k => k.Value, s => new AssetKindId(s));
        builder.Property(i => i.Slug).HasMaxLength(Item.MaxSlugLength).IsRequired();
        builder.HasIndex(i => new { i.Kind, i.Slug }).IsUnique();
        builder.HasOne(i => i.Owner).WithMany().HasForeignKey(i => i.OwnerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(i => new { i.OwnerId, i.Status });
        builder.Property(i => i.Title).HasMaxLength(200).IsRequired();
        builder.Property(i => i.Summary).HasMaxLength(500).IsRequired();
        builder.Property(i => i.Description).HasMaxLength(8000).IsRequired();
        builder.Property(i => i.License).HasMaxLength(40).IsRequired();
        builder.Property(i => i.Category).HasMaxLength(40).IsRequired();
        builder.Property(i => i.Status).HasMaxLength(16)
            .HasConversion(s => s.ToKey(), s => Enum.Parse<ItemStatus>(s, true));
        builder.Property(i => i.VisibilityReason).HasMaxLength(200);
        builder.Property(i => i.KindMeta).HasColumnType("jsonb");
        builder.Property(i => i.SearchTags).IsRequired();
        builder.Property(i => i.SearchExtra).IsRequired();
        builder.HasIndex(i => new { i.Status, i.PublishedAt });
        builder.HasIndex(i => new { i.Status, i.WilsonScore });
        builder.HasIndex(i => new { i.Status, i.HotScore });
        builder.HasIndex(i => new { i.Status, i.DownloadsTotal });

        builder.Property<NpgsqlTsVector>("SearchVector").HasComputedColumnSql(SearchVectorSql, stored: true);
        builder.HasIndex("SearchVector").HasMethod("gin");
        builder.HasIndex(i => i.Title).HasMethod("gin").HasOperators("gin_trgm_ops");

        builder.HasMany(i => i.Versions).WithOne().HasForeignKey(v => v.ItemId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(i => i.Media).WithOne().HasForeignKey(m => m.ItemId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(i => i.Tags).WithOne().HasForeignKey(t => t.ItemId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(i => i.Flags).WithOne().HasForeignKey(f => f.ItemId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ItemVersion>().WithMany().HasForeignKey(i => i.CurrentVersionId).OnDelete(DeleteBehavior.SetNull);
        foreach (var name in new[] { nameof(Item.Versions), nameof(Item.Media), nameof(Item.Tags), nameof(Item.Flags) })
            builder.Navigation(name).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class ItemVersionConfiguration : IEntityTypeConfiguration<ItemVersion>
{
    public void Configure(EntityTypeBuilder<ItemVersion> builder)
    {
        builder.ToTable("item_versions");
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).ValueGeneratedNever();
        builder.Property(v => v.Version).HasMaxLength(64).IsRequired();
        builder.HasIndex(v => new { v.ItemId, v.Version }).IsUnique();
        builder.Property(v => v.Changelog).HasMaxLength(2000).IsRequired();
        builder.Property(v => v.PackageSha256).HasColumnName("package_blob").AsSha256();
        builder.HasOne<Blob>().WithMany().HasForeignKey(v => v.PackageSha256).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(v => v.PackageSha256);
        builder.Property(v => v.Format).HasMaxLength(16).HasConversion<string>();
        builder.Property(v => v.MinAppVersion).HasMaxLength(64).IsRequired();
        builder.Property(v => v.Manifest).HasColumnType("jsonb");
        builder.Ignore(v => v.FileExtension);
    }
}

internal sealed class ItemMediaConfiguration : IEntityTypeConfiguration<ItemMedia>
{
    public void Configure(EntityTypeBuilder<ItemMedia> builder)
    {
        builder.ToTable("item_media");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.Role).HasMaxLength(24).HasConversion(r => r.ToKey(), s => ParseRole(s));
        builder.HasIndex(m => new { m.ItemId, m.Role }).IsUnique();
        builder.Property(m => m.BlobSha256).HasColumnName("blob").AsSha256();
        builder.HasOne<Blob>().WithMany().HasForeignKey(m => m.BlobSha256).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(m => m.BlobSha256);
        builder.Property(m => m.DHash).HasColumnName("dhash");
    }

    private static MediaRole ParseRole(string key) =>
        MediaRoleExtensions.TryParse(key, out var role) ? role : throw new InvalidOperationException($"Unknown media role '{key}'.");
}

internal sealed class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    public void Configure(EntityTypeBuilder<Tag> builder)
    {
        builder.ToTable("tags");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedOnAdd();
        builder.Property(t => t.Kind).HasMaxLength(40).HasConversion(k => k.Value, s => new AssetKindId(s));
        builder.Property(t => t.Slug).HasMaxLength(40).IsRequired();
        builder.HasIndex(t => new { t.Kind, t.Slug }).IsUnique();
    }
}

internal sealed class ItemTagConfiguration : IEntityTypeConfiguration<ItemTag>
{
    public void Configure(EntityTypeBuilder<ItemTag> builder)
    {
        builder.ToTable("item_tags");
        builder.HasKey(t => new { t.ItemId, t.TagId });
        builder.HasOne(t => t.Tag).WithMany().HasForeignKey(t => t.TagId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(t => t.TagId);
    }
}

internal sealed class ItemFlagConfiguration : IEntityTypeConfiguration<ItemFlag>
{
    public void Configure(EntityTypeBuilder<ItemFlag> builder)
    {
        builder.ToTable("item_flags");
        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).ValueGeneratedNever();
        builder.Property(f => f.Field).HasMaxLength(64).IsRequired();
        builder.Property(f => f.Code).HasMaxLength(64).IsRequired();
        builder.Property(f => f.Message).HasMaxLength(500).IsRequired();
        builder.Property(f => f.Params).HasColumnType("jsonb");
        builder.HasIndex(f => f.ResolvedAt).HasFilter("resolved_at is null");
    }
}

internal sealed class DownloadEventConfiguration : IEntityTypeConfiguration<DownloadEvent>
{
    public void Configure(EntityTypeBuilder<DownloadEvent> builder)
    {
        builder.ToTable("download_events");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.HasOne<Item>().WithMany().HasForeignKey(e => e.ItemId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ItemVersion>().WithMany().HasForeignKey(e => e.VersionId).OnDelete(DeleteBehavior.SetNull);
        builder.Property(e => e.InstallId).HasMaxLength(64);
        builder.Property(e => e.IpHash).HasMaxLength(64).IsRequired();
        builder.HasIndex(e => e.Id).HasFilter("rolled_up_at is null").HasDatabaseName("ix_download_events_pending");
        builder.HasIndex(e => new { e.ItemId, e.At });
    }
}

internal sealed class ItemDownloadDailyConfiguration : IEntityTypeConfiguration<ItemDownloadDaily>
{
    public void Configure(EntityTypeBuilder<ItemDownloadDaily> builder)
    {
        builder.ToTable("item_download_daily");
        builder.HasKey(d => new { d.ItemId, d.Day });
        builder.HasOne<Item>().WithMany().HasForeignKey(d => d.ItemId).OnDelete(DeleteBehavior.Cascade);
    }
}
