using CutWire.Studio.Domain.Synth;
using CutWire.Studio.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CutWire.Studio.Infrastructure.Persistence;

internal sealed class SynthSongConfiguration : IEntityTypeConfiguration<SynthSong>
{
    public void Configure(EntityTypeBuilder<SynthSong> builder)
    {
        builder.ToTable("synth_songs");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(s => s.OwnerId).OnDelete(DeleteBehavior.Cascade);
        builder.Property(s => s.LocalId).HasMaxLength(SynthSong.MaxLocalIdLength).IsRequired();
        builder.Property(s => s.Name).HasMaxLength(SynthSong.MaxNameLength).IsRequired();
        builder.Property(s => s.DocJson).HasColumnName("doc").HasColumnType("jsonb").IsRequired();
        builder.HasIndex(s => new { s.OwnerId, s.LocalId }).IsUnique();
        builder.HasIndex(s => s.OwnerId);
    }
}
