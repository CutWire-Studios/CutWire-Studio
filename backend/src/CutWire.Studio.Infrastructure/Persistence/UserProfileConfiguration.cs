using CutWire.Studio.Domain.Accounts;
using CutWire.Studio.Domain.Blobs;
using CutWire.Studio.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CutWire.Studio.Infrastructure.Persistence;

internal sealed class UserProfileConfiguration : IEntityTypeConfiguration<UserProfile>
{
    public void Configure(EntityTypeBuilder<UserProfile> builder)
    {
        builder.ToTable("user_profiles", t => t.HasCheckConstraint("ck_user_profiles_handle_lower", "handle = lower(handle)"));
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("user_id").ValueGeneratedNever();
        builder.HasOne<ApplicationUser>().WithOne().HasForeignKey<UserProfile>(p => p.Id).OnDelete(DeleteBehavior.Cascade);
        builder.Property(p => p.Handle).HasMaxLength(UserHandle.MaxLength).IsRequired();
        builder.HasIndex(p => p.Handle).IsUnique();
        builder.Property(p => p.DisplayName).HasMaxLength(UserProfile.MaxDisplayNameLength).IsRequired();
        builder.Property(p => p.Bio).HasMaxLength(UserProfile.MaxBioLength).IsRequired();
        builder.Property(p => p.AvatarSha256)
            .HasColumnName("avatar_sha256")
            .HasMaxLength(Sha256Hash.HexLength)
            .IsFixedLength()
            .HasConversion(h => h!.Value.Value, s => (Sha256Hash?)new Sha256Hash(s));
        builder.Property(p => p.TrustLevel).HasConversion<string>().HasMaxLength(2);
    }
}
