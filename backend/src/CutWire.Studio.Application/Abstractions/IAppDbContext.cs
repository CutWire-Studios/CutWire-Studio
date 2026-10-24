using CutWire.Studio.Domain.Accounts;
using CutWire.Studio.Domain.Blobs;
using CutWire.Studio.Domain.Catalog;
using Microsoft.EntityFrameworkCore;

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

    Task<bool> CanConnectAsync(CancellationToken ct);
    Task<int> SaveChangesAsync(CancellationToken ct);
}
