using CutWire.Studio.Application.Abstractions;
using CutWire.Studio.Application.Blobs;
using CutWire.Studio.Application.Catalog;
using CutWire.Studio.Domain.Blobs;
using CutWire.Studio.Domain.Common;
using CutWire.Studio.Domain.Stock;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CutWire.Studio.Application.Stock;

/// <summary>
/// Creates and reports download jobs. Quota is consumed when a job is created, not polled, and a failed job refunds it, so a source that
/// breaks does not silently eat a user's download for the day.
/// </summary>
public sealed class DownloadJobs(
    IAppDbContext db,
    StockSettings settings,
    StockQuota quota,
    IDownloadQueue queue,
    DownloadRunner runner,
    IMediaUrls urls,
    StockRegistry registry,
    IClock clock,
    IOptions<StockJobOptions> options)
{
    public async Task<Result<DownloadJobDto>> CreateAsync(CreateDownloadCommand command, CancellationToken ct)
    {
        if (!ItemIds.TryDecode(command.ItemId, out var r)) return StockErrors.ItemGone;
        var entry = await settings.EntryAsync(r.Provider, r.Type, command.Caller.Distribution, ct);
        if (entry is null || !entry.Provider.Descriptor.Capabilities.HasFlag(ProviderCapabilities.Fulfill)) return StockErrors.ItemGone;

        var active = await db.DownloadJobs.CountAsync(j => j.ClientId == command.Caller.ClientId
            && (j.Status == JobStatus.Queued || j.Status == JobStatus.Processing), ct);
        if (active >= options.Value.PerClientJobs)
            return Error.RateLimited("stock.too_many_jobs", "Another download is still running. Try again shortly.", 30);

        var now = clock.UtcNow;
        var subject = StockQuota.SubjectFor(entry.Setting, command.Caller);
        var job = new DownloadJob
        {
            ClientId = command.Caller.ClientId,
            UserId = command.Caller.UserId,
            Provider = r.Provider,
            Type = r.Type,
            Native = r.Native,
            VariantId = string.IsNullOrWhiteSpace(command.VariantId) ? null : command.VariantId.Trim(),
            Status = JobStatus.Queued,
            QuotaSubject = subject,
            QuotaWindow = entry.Setting.QuotaWindow,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.DownloadJobs.Add(job);
        await db.SaveChangesAsync(ct);

        var consumed = await quota.ConsumeAsync(subject, r.Provider, entry.Setting.QuotaWindow, entry.Setting.QuotaLimit, job.Id, ct);
        if (consumed.IsFailure)
        {
            // Nothing was fetched, so the job should not linger as a failure.
            db.DownloadJobs.Remove(job);
            await db.SaveChangesAsync(ct);
            return consumed.Error;
        }

        if (StockTypes.Find(r.Type)?.MediaKind == "image")
        {
            // Images finish well inside a request, so the contract lets them come back already ready.
            await runner.RunAsync(job.Id, CancellationToken.None);
            db.DownloadJobs.Entry(job).State = EntityState.Detached;
            return await GetAsync(command.Caller, job.Id.ToString(), ct);
        }

        if (!queue.TryEnqueue(job.Id))
        {
            await runner.FailAsync(job.Id, StockReason.SourceError, ct);
            return Error.RateLimited("stock.busy", "The stock library is busy. Try again shortly.", 60);
        }

        return ToDto(job, entry.Provider.Descriptor);
    }

    public async Task<Result<DownloadJobDto>> GetAsync(StockCaller caller, string id, CancellationToken ct)
    {
        if (!Guid.TryParse(id, out var jobId)) return StockErrors.ItemGone;
        var job = await db.DownloadJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == jobId, ct);
        var owned = job is not null && (job.ClientId == caller.ClientId || (caller.UserId is { } user && job.UserId == user));
        if (!owned) return StockErrors.ItemGone;
        return ToDto(job!, registry.Get(job!.Provider)?.Descriptor);
    }

    private DownloadJobDto ToDto(DownloadJob job, ProviderDescriptor? descriptor)
    {
        var license = job.LicenseName is { Length: > 0 }
            ? new StockLicenseDto(job.LicenseName, job.LicenseUrl, job.Attribution)
            : new StockLicenseDto($"{descriptor?.Label ?? job.Provider} terms", null, job.Attribution ?? descriptor?.Attribution);
        DownloadFileDto? file = null;
        StockJobErrorDto? error = null;
        if (job is { Status: JobStatus.Ready, BlobSha256: { } sha, ExpiresAt: { } expires } && expires > clock.UtcNow)
            file = new DownloadFileDto(job.Filename ?? "download", job.Mime ?? "application/octet-stream", job.Size,
                urls.FileUrl(sha.Value, job.Filename ?? "download", job.Mime ?? "application/octet-stream", expires), expires, job.FileSha256 ?? sha.Value);
        if (job.Status == JobStatus.Failed)
            error = new StockJobErrorDto(job.ErrorCode ?? "stock.download_error", job.ErrorDetail ?? StockReasons.Info(StockReason.DownloadError).Detail);
        return new DownloadJobDto(job.Id.ToString(), job.Status, job.Status == JobStatus.Ready ? 1 : job.Progress, file, error, license);
    }

    /// <summary>Picks up jobs left mid-flight by a restart.</summary>
    public async Task RequeueOrphansAsync(CancellationToken ct)
    {
        var orphans = await db.DownloadJobs.Where(j => j.Status == JobStatus.Queued || j.Status == JobStatus.Processing).Select(j => j.Id).ToListAsync(ct);
        foreach (var id in orphans)
            if (!queue.TryEnqueue(id))
                await runner.FailAsync(id, StockReason.SourceError, ct);
    }
}

public sealed class DownloadRunner(
    IAppDbContext db,
    StockSettings settings,
    StockQuota quota,
    IStockHttpClients http,
    IMediaNormalizer normalizer,
    ICommandHandler<StoreBlobCommand, StoreBlobResult> blobs,
    BlobReferences blobRefs,
    IClock clock,
    IOptions<StockJobOptions> options,
    ILogger<DownloadRunner> logger)
{
    public async Task RunAsync(Guid jobId, CancellationToken stopping)
    {
        var job = await db.DownloadJobs.FirstOrDefaultAsync(j => j.Id == jobId, CancellationToken.None);
        if (job is null || job.Status is JobStatus.Ready or JobStatus.Failed) return;

        var entry = await settings.EntryAsync(job.Provider, job.Type, null, CancellationToken.None);
        if (entry is null || !entry.Provider.Descriptor.Capabilities.HasFlag(ProviderCapabilities.Fulfill))
        {
            await FailAsync(job, StockReason.ItemMissing);
            return;
        }

        job.Status = JobStatus.Processing;
        job.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(CancellationToken.None);

        var dir = Directory.CreateTempSubdirectory("cutwire-stock-").FullName;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        timeout.CancelAfter(options.Value.JobTimeout);
        var progress = 0d;
        using var pumpStop = new CancellationTokenSource();
        var pump = ProgressPump(job.Id, () => Volatile.Read(ref progress), pumpStop.Token);
        try
        {
            var workspace = new StockWorkspace(dir, http.Public, options.Value.MaxDownloadBytes, f => Volatile.Write(ref progress, f));
            var asset = await entry.Provider.FetchAsync(new FulfillRequest(job.Type, job.Native, job.VariantId), workspace, timeout.Token);
            workspace.Report(0.7);
            var media = await normalizer.NormalizeAsync(asset, dir, timeout.Token);
            await StopAsync(pumpStop, pump);

            await using var stream = File.OpenRead(media.Path);
            var stored = await blobs.Handle(new StoreBlobCommand(stream, media.Mime, options.Value.MaxDownloadBytes), CancellationToken.None);
            if (stored.IsFailure) throw new StockException(StockReason.FileTooLarge, stored.Error.Code);
            await blobRefs.AddAsync(stored.Value.Hash, CancellationToken.None);

            job.Status = JobStatus.Ready;
            job.Progress = 1;
            job.BlobSha256 = stored.Value.Hash;
            job.Filename = media.Filename;
            job.Mime = media.Mime;
            job.Size = media.Size;
            job.FileSha256 = media.Sha256;
            job.ExpiresAt = clock.UtcNow + options.Value.FileTtl;
            job.LicenseName = asset.License?.Name ?? job.LicenseName;
            job.LicenseUrl = asset.License?.Url;
            job.Attribution = asset.Attribution ?? asset.License?.Attribution;
            job.UpdatedAt = clock.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (StockException ex)
        {
            await StopAsync(pumpStop, pump);
            logger.LogWarning("Download job {Job} ({Provider}) failed: {Reason} {Message}", job.Id, job.Provider, ex.Reason, ex.Message);
            await FailAsync(job, ex.Reason);
        }
        catch (OperationCanceledException) when (!stopping.IsCancellationRequested)
        {
            await StopAsync(pumpStop, pump);
            await FailAsync(job, StockReason.SourceTimeout);
        }
        catch (OperationCanceledException)
        {
            await StopAsync(pumpStop, pump);
            // Shutting down: leave the job queued so the next start requeues it.
            job.Status = JobStatus.Queued;
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            await StopAsync(pumpStop, pump);
            logger.LogError(ex, "Download job {Job} ({Provider}) crashed", job.Id, job.Provider);
            await FailAsync(job, StockReason.DownloadError);
        }
        finally
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static async Task StopAsync(CancellationTokenSource stop, Task pump)
    {
        await stop.CancelAsync();
        await pump;
    }

    private async Task ProgressPump(Guid jobId, Func<double> read, CancellationToken stop)
    {
        var last = 0d;
        try
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), stop);
                var now = read();
                if (now <= last) continue;
                last = now;
                await db.DownloadJobs.Where(j => j.Id == jobId).ExecuteUpdateAsync(s => s.SetProperty(j => j.Progress, now), CancellationToken.None);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public async Task FailAsync(Guid jobId, StockReason reason, CancellationToken ct)
    {
        var job = await db.DownloadJobs.FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job is not null) await FailAsync(job, reason);
    }

    private async Task FailAsync(DownloadJob job, StockReason reason)
    {
        var info = StockReasons.Info(reason);
        job.Status = JobStatus.Failed;
        job.ErrorCode = info.Code;
        job.ErrorDetail = info.Detail;
        job.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(CancellationToken.None);
        await quota.RefundAsync(job, CancellationToken.None);
    }
}
