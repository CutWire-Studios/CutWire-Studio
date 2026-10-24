using CutWire.Studio.Domain.Blobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CutWire.Studio.Infrastructure.Persistence;

internal sealed class BlobConfiguration : IEntityTypeConfiguration<Blob>
{
    public void Configure(EntityTypeBuilder<Blob> builder)
    {
        builder.ToTable("blobs");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id)
            .HasColumnName("sha256")
            .HasMaxLength(Sha256Hash.HexLength)
            .IsFixedLength()
            .HasConversion(h => h.Value, s => new Sha256Hash(s))
            .ValueGeneratedNever();
        builder.Property(b => b.Mime).HasMaxLength(127).IsRequired();
        builder.HasIndex(b => b.ZeroRefSince).HasFilter("ref_count = 0");
    }
}
