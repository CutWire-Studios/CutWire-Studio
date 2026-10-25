using CutWire.Studio.Application.Abstractions;
using CutWire.Studio.Application.Blobs;
using CutWire.Studio.Application.Catalog;
using CutWire.Studio.Domain.Blobs;
using CutWire.Studio.Domain.Common;
using CutWire.Studio.Domain.Stock;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CutWire.Studio.Application.Stock;

/// <summary>Serves finished download results behind a sealed, expiring token (the token is the credential; the URL is a plain GET).</summary>
public sealed class StockFiles(IMediaSealer sealer, IBlobStore store, IAppDbContext db)
{
    public async Task<Result<(BlobContent Content, string FileName)>> OpenAsync(string token, CancellationToken ct)
    {
        var claims = sealer.Open(token);
        if (claims?.BlobSha256 is null || !Sha256Hash.TryParse(claims.BlobSha256, out var hash)) return Error.NotFound("stock.file_not_found", "File not found.");
        var blob = await db.Blobs.AsNoTracking().FirstOrDefaultAsync(b => b.Id == hash, ct);
        var stream = blob is null ? null : await store.OpenAsync(hash, ct: ct);
        if (blob is null || stream is null) return Error.NotFound("stock.file_not_found", "File not found.");
        return (new BlobContent(hash, stream, claims.Mime is { Length: > 0 } m ? m : blob.Mime, blob.Size, blob.CreatedAt), claims.Filename ?? "download");
    }
}

public sealed record ExpiryResult(int Jobs, int CacheRows, int QuotaRows);

/// <summary>Expires file URLs (releasing the blob references the blob GC then collects) and prunes the quota ledger and provider cache.</summary>
public sealed class StockExpiry(IAppDbContext db, BlobReferences blobRefs, StockQuota quota, IClock clock, IOptions<StockJobOptions> options)
{
    public async Task<ExpiryResult> SweepAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var jobs = 0;
        while (true)
        {
            var expired = await db.DownloadJobs
                .Where(j => (j.ExpiresAt != null && j.ExpiresAt < now)
                            || (j.ExpiresAt == null && j.Status != JobStatus.Queued && j.Status != JobStatus.Processing && j.UpdatedAt < now.AddDays(-1)))
                .OrderBy(j => j.UpdatedAt).Take(200).ToListAsync(ct);
            if (expired.Count == 0) break;
            foreach (var job in expired)
            {
                if (job.BlobSha256 is { } hash) await blobRefs.ReleaseAsync(hash, ct);
                db.DownloadJobs.Remove(job);
            }

            await db.SaveChangesAsync(ct);
            jobs += expired.Count;
        }

        var cache = await db.ProviderCache.Where(c => c.ExpiresAt < now).ExecuteDeleteAsync(ct);
        var ledger = await quota.PruneAsync(now - options.Value.QuotaRetention, ct);
        return new ExpiryResult(jobs, cache, ledger);
    }
}
