using CutWire.Studio.Domain.Forge;
using CutWire.Studio.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CutWire.Studio.Infrastructure.Persistence;

internal sealed class ForgeDocumentConfiguration : IEntityTypeConfiguration<ForgeDocument>
{
    public void Configure(EntityTypeBuilder<ForgeDocument> builder)
    {
        builder.ToTable("forge_documents");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(d => d.OwnerId).OnDelete(DeleteBehavior.Cascade);
        builder.Property(d => d.LocalId).HasMaxLength(ForgeDocument.MaxLocalIdLength).IsRequired();
        builder.Property(d => d.Kind).HasMaxLength(16).IsRequired();
        builder.Property(d => d.Name).HasMaxLength(ForgeDocument.MaxNameLength).IsRequired();
        builder.Property(d => d.DocJson).IsRequired();
        builder.Property(d => d.ThumbSha256)
            .HasMaxLength(Domain.Blobs.Sha256Hash.HexLength)
            .IsFixedLength()
            .HasConversion(h => h!.Value.Value, s => (Domain.Blobs.Sha256Hash?)new Domain.Blobs.Sha256Hash(s));
        builder.HasIndex(d => new { d.OwnerId, d.LocalId }).IsUnique();
        builder.HasIndex(d => new { d.OwnerId, d.UpdatedAt });
        builder.HasOne<Domain.Blobs.Blob>().WithMany().HasForeignKey(d => d.ThumbSha256).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ForgeDocAssetConfiguration : IEntityTypeConfiguration<ForgeDocAsset>
{
    public void Configure(EntityTypeBuilder<ForgeDocAsset> builder)
    {
        builder.ToTable("forge_doc_assets");
        builder.HasKey(a => new { a.DocumentId, a.BlobSha256 });
        builder.Property(a => a.BlobSha256).AsSha256();
        builder.HasOne<ForgeDocument>().WithMany().HasForeignKey(a => a.DocumentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Domain.Blobs.Blob>().WithMany().HasForeignKey(a => a.BlobSha256).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(a => a.BlobSha256);
    }
}

internal sealed class AiUsageConfiguration : IEntityTypeConfiguration<AiUsage>
{
    public void Configure(EntityTypeBuilder<AiUsage> builder)
    {
        builder.ToTable("ai_usage");
        builder.HasKey(u => new { u.Day, u.Sub });
        builder.Property(u => u.Sub).HasMaxLength(40);
    }
}

internal sealed class ThemeDraftConfiguration : IEntityTypeConfiguration<ThemeDraft>
{
    public void Configure(EntityTypeBuilder<ThemeDraft> builder)
    {
        builder.ToTable("theme_drafts");
        builder.HasKey(d => new { d.OwnerId, d.Id });
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(d => d.OwnerId).OnDelete(DeleteBehavior.Cascade);
        builder.Property(d => d.Id).HasMaxLength(ThemeDraft.MaxIdLength);
        builder.Property(d => d.Name).HasMaxLength(ThemeDraft.MaxNameLength).IsRequired();
        builder.Property(d => d.DocJson).IsRequired();
    }
}
