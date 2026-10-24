using CutWire.Studio.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CutWire.Studio.Infrastructure.Jobs;

public sealed class BlobGcOptions
{
    public const string Section = "BlobGc";

    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(7);
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(1);
    public int BatchSize { get; set; } = 500;
}

public sealed class BlobGcJob(
    IServiceScopeFactory scopes,
    IOptions<BlobGcOptions> options,
    ILogger<BlobGcJob> logger) : IRecurringJob
{
    public string Name => "blob-gc";
    public TimeSpan Interval => options.Value.Interval;

    public async Task RunAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var store = scope.ServiceProvider.GetRequiredService<IBlobStore>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var cutoff = clock.UtcNow - options.Value.Retention;
        var deleted = 0;

        while (!ct.IsCancellationRequested)
        {
            var batch = await db.Blobs
                .Where(b => b.RefCount == 0 && b.ZeroRefSince < cutoff)
                .OrderBy(b => b.ZeroRefSince)
                .Take(options.Value.BatchSize)
                .ToListAsync(ct);
            if (batch.Count == 0) break;

            foreach (var blob in batch)
            {
                db.Blobs.Remove(blob);
                await db.SaveChangesAsync(ct);
                await store.DeleteAsync(blob.Id, ct);
                deleted++;
            }
        }

        if (deleted > 0) logger.LogInformation("Blob GC removed {Count} blobs", deleted);
    }
}
