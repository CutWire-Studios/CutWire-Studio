using CutWire.Studio.Domain.Accounts;
using CutWire.Studio.Domain.Blobs;
using CutWire.Studio.Domain.Catalog;
using CutWire.Studio.Domain.Moderation;
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

    Task<bool> CanConnectAsync(CancellationToken ct);
    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct);
    Task<int> SaveChangesAsync(CancellationToken ct);
}
