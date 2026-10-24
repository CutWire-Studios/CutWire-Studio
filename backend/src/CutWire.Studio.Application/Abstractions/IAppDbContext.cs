using CutWire.Studio.Domain.Blobs;
using Microsoft.EntityFrameworkCore;

namespace CutWire.Studio.Application.Abstractions;

public interface IAppDbContext
{
    DbSet<Blob> Blobs { get; }

    Task<bool> CanConnectAsync(CancellationToken ct);
    Task<int> SaveChangesAsync(CancellationToken ct);
}
