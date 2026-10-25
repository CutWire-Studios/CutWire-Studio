using System.Threading.Channels;
using CutWire.Studio.Application.Abstractions;
using CutWire.Studio.Application.Stock;
using CutWire.Studio.Infrastructure.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CutWire.Studio.Infrastructure.Stock;

public sealed class DownloadQueue(IOptions<StockJobOptions> options) : IDownloadQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateBounded<Guid>(new BoundedChannelOptions(Math.Max(1, options.Value.QueueCapacity))
    {
        FullMode = BoundedChannelFullMode.Wait,
    });

    public ChannelReader<Guid> Reader => _channel.Reader;

    public bool TryEnqueue(Guid jobId) => _channel.Writer.TryWrite(jobId);
}

/// <summary>Bounded channel plus N workers draining download jobs. Orphans from a restart are requeued once at startup.</summary>
public sealed class DownloadWorkerService(
    DownloadQueue queue,
    IServiceScopeFactory scopes,
    IOptions<StockJobOptions> options,
    ILogger<DownloadWorkerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Yield();
            await using (var scope = scopes.CreateAsyncScope())
                await scope.ServiceProvider.GetRequiredService<DownloadJobs>().RequeueOrphansAsync(stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not requeue interrupted download jobs");
        }

        await Task.WhenAll(Enumerable.Range(0, Math.Max(1, options.Value.Workers)).Select(_ => Worker(stoppingToken)));
    }

    private async Task Worker(CancellationToken ct)
    {
        try
        {
            await foreach (var id in queue.Reader.ReadAllAsync(ct))
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<DownloadRunner>().RunAsync(id, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Download job {Job} crashed the worker step", id);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }
}

public sealed class ResolveWorkerService(ResolveJobStore store, IOptions<StockJobOptions> options, ILogger<ResolveWorkerService> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(Enumerable.Range(0, Math.Max(1, options.Value.ResolveWorkers)).Select(_ => Worker(stoppingToken)));

    private async Task Worker(CancellationToken ct)
    {
        try
        {
            await foreach (var id in store.Reader.ReadAllAsync(ct))
            {
                try
                {
                    await store.RunAsync(id, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Resolve job {Job} failed unexpectedly", id);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }
}

public sealed class StockExpiryJobOptions
{
    public const string Section = "Stock:Expiry";

    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(10);
}

/// <summary>Expires download results (their blobs are released for the blob GC), the quota ledger and the provider cache.</summary>
public sealed class StockExpiryJob(IServiceScopeFactory scopes, IOptions<StockExpiryJobOptions> options, ILogger<StockExpiryJob> logger) : IRecurringJob
{
    public string Name => "stock-expiry";
    public TimeSpan Interval => options.Value.Interval;

    public async Task RunAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<StockExpiry>().SweepAsync(ct);
        if (result.Jobs + result.CacheRows + result.QuotaRows > 0)
            logger.LogInformation("Stock expiry removed {Jobs} jobs, {Cache} cache rows, {Quota} ledger rows", result.Jobs, result.CacheRows, result.QuotaRows);
    }
}
