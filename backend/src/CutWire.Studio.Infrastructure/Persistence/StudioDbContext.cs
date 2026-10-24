using CutWire.Studio.Application.Abstractions;
using CutWire.Studio.Domain.Blobs;
using Microsoft.EntityFrameworkCore;

namespace CutWire.Studio.Infrastructure.Persistence;

public sealed class StudioDbContext(DbContextOptions<StudioDbContext> options) : DbContext(options), IAppDbContext
{
    public DbSet<Blob> Blobs => Set<Blob>();

    public Task<bool> CanConnectAsync(CancellationToken ct) => Database.CanConnectAsync(ct);

    Task<int> IAppDbContext.SaveChangesAsync(CancellationToken ct) => SaveChangesAsync(ct);

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(StudioDbContext).Assembly);
}
