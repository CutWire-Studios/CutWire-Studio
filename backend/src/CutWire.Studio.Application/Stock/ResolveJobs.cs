using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Threading.Channels;
using CutWire.Studio.Application.Abstractions;
using CutWire.Studio.Domain.Common;
using CutWire.Studio.Domain.Stock;
using Microsoft.Extensions.Options;

namespace CutWire.Studio.Application.Stock;

public delegate Task<StockItemDto> ResolveWork(CancellationToken ct);

/// <summary>
/// Pasted-URL lookups off the request path (yt-dlp extraction can outlast an HTTP timeout). Jobs live in memory: a resolve has no file to
/// keep, and a restart just means the client posts the URL again.
/// </summary>
public sealed class ResolveJobStore(IClock clock, IOptions<StockJobOptions> options)
{
    private sealed class Job(string id, string clientId, DateTimeOffset expires, ResolveWork work)
    {
        public string Id { get; } = id;
        public string ClientId { get; } = clientId;
        public ResolveWork? Work { get; set; } = work;
        public string Status { get; set; } = JobStatus.Queued;
        public StockItemDto? Item { get; set; }
        public StockJobErrorDto? Error { get; set; }
        public DateTimeOffset Expires { get; set; } = expires;
    }

    private readonly ConcurrentDictionary<string, Job> _jobs = new();
    private readonly Channel<string> _queue = Channel.CreateBounded<string>(new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.DropWrite });

    public ChannelReader<string> Reader => _queue.Reader;

    public Result<ResolveJobDto> Enqueue(string clientId, ResolveWork work)
    {
        Evict();
        if (_jobs.Count >= options.Value.MaxResolveJobs) return Error.RateLimited("stock.busy", "Too many requests. Try again shortly.", 5);
        var id = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        var job = new Job(id, clientId, clock.UtcNow + options.Value.ResolveTtl, work);
        _jobs[id] = job;
        if (!_queue.Writer.TryWrite(id))
        {
            _jobs.TryRemove(id, out _);
            return Error.RateLimited("stock.busy", "Too many requests. Try again shortly.", 5);
        }

        return Dto(job);
    }

    public Result<ResolveJobDto> Get(string clientId, string id) =>
        _jobs.TryGetValue(id, out var job) && job.ClientId == clientId && job.Expires > clock.UtcNow ? Dto(job) : StockErrors.ItemGone;

    public async Task RunAsync(string id, CancellationToken stopping)
    {
        if (!_jobs.TryGetValue(id, out var job) || job.Work is not { } work) return;
        job.Work = null;
        job.Status = JobStatus.Processing;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        cts.CancelAfter(options.Value.ResolveTimeout);
        try
        {
            job.Item = await work(cts.Token);
            job.Status = JobStatus.Ready;
        }
        catch (StockException ex)
        {
            Fail(job, ex.Reason);
        }
        catch (OperationCanceledException) when (!stopping.IsCancellationRequested)
        {
            Fail(job, StockReason.SourceTimeout);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            Fail(job, StockReason.SourceError);
        }

        job.Expires = clock.UtcNow + options.Value.ResolveTtl;
    }

    private static void Fail(Job job, StockReason reason)
    {
        var info = StockReasons.Info(reason);
        job.Status = JobStatus.Failed;
        job.Error = new StockJobErrorDto(info.Code, info.Detail);
    }

    private void Evict()
    {
        var now = clock.UtcNow;
        foreach (var (id, job) in _jobs)
            if (job.Expires < now) _jobs.TryRemove(id, out _);
    }

    private static ResolveJobDto Dto(Job job) => new(job.Id, job.Status, job.Status == JobStatus.Ready ? job.Item : null, job.Status == JobStatus.Failed ? job.Error : null);
}

public sealed class StockResolve(StockRegistry registry, StockSettings settings, StockQuota quota, StockAssembler assembler, ResolveJobStore store)
{
    public async Task<Result<ResolveJobDto>> StartAsync(CreateResolveCommand command, CancellationToken ct)
    {
        if (!ProviderSupport.TryParseHttpUrl(command.Url, out var uri))
            return Error.Validation("stock.invalid_url", "That link could not be read.", new Dictionary<string, string[]> { ["url"] = ["Must be an http(s) URL."] });
        var url = uri.ToString();

        var provider = registry.ResolverFor(url);
        if (provider is null)
        {
            // A source this deployment cannot serve is not the same as a link we do not understand.
            return registry.UnavailableFor(url) is not null
                ? StockErrors.From(new StockException(StockReason.SourceDisabled))
                : StockErrors.From(new StockException(StockReason.LinkUnsupported));
        }

        var d = provider.Descriptor;
        StockEntry? entry = null;
        foreach (var service in d.Serves)
            if (await settings.EntryAsync(d.Id, service.Type, ct) is { } e)
            {
                entry = e;
                break;
            }

        if (entry is null) return StockErrors.From(new StockException(StockReason.SourceDisabled));

        var status = await quota.DtoAsync(entry, command.Caller, ct);
        var downloadable = status is null or { Remaining: > 0 } && d.Capabilities.HasFlag(ProviderCapabilities.Fulfill);
        var type = entry.Service.Type;
        return store.Enqueue(command.Caller.ClientId, async token =>
        {
            var item = await provider.ResolveAsync(url, token);
            return assembler.Item(provider, item with { Type = string.IsNullOrEmpty(item.Type) ? type : item.Type }, downloadable);
        });
    }

    public Result<ResolveJobDto> Get(StockCaller caller, string id) => store.Get(caller.ClientId, id);
}
