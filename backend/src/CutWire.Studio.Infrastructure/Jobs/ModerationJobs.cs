using CutWire.Studio.Application.Abstractions;
using CutWire.Studio.Application.Moderation;
using CutWire.Studio.Domain.Catalog;
using CutWire.Studio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CutWire.Studio.Infrastructure.Jobs;

public sealed class ModerationJobsOptions
{
    public const string Section = "ModerationJobs";

    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(15);
    public TimeSpan HotDecayInterval { get; set; } = TimeSpan.FromHours(1);
    public TimeSpan HotDecayActiveWindow { get; set; } = TimeSpan.FromDays(90);
    public TimeSpan TrustInterval { get; set; } = TimeSpan.FromHours(24);
    public TimeSpan DownloadEventsPruneInterval { get; set; } = TimeSpan.FromHours(24);
    public TimeSpan DownloadEventsRetention { get; set; } = TimeSpan.FromDays(90);
}

public sealed class CleanupPurgeJob(IServiceScopeFactory scopes, IOptions<ModerationJobsOptions> options, ILogger<CleanupPurgeJob> logger) : IRecurringJob
{
    public string Name => "cleanup-purge";
    public TimeSpan Interval => options.Value.CleanupInterval;

    public async Task RunAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var (purged, restored) = await scope.ServiceProvider.GetRequiredService<ModerationMaintenance>().CleanupAsync(ct);
        if (purged + restored > 0) logger.LogInformation("Cleanup purged {Purged} items and restored {Restored} after expired bans", purged, restored);
    }
}

public sealed class TrustLevelJob(IServiceScopeFactory scopes, IOptions<ModerationJobsOptions> options, ILogger<TrustLevelJob> logger) : IRecurringJob
{
    public string Name => "trust-levels";
    public TimeSpan Interval => options.Value.TrustInterval;

    public async Task RunAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var (toT1, toT2) = await scope.ServiceProvider.GetRequiredService<ModerationMaintenance>().RecomputeTrustAsync(ct);
        if (toT1 + toT2 > 0) logger.LogInformation("Trust levels: {ToT1} promoted to T1, {ToT2} to T2", toT1, toT2);
    }
}

/// <summary>Re-applies the time decay to <c>hot_score</c> of published items from the active window, as <see cref="Ranking.Hot"/> does.</summary>
public sealed class HotScoreDecayJob(IServiceScopeFactory scopes, IOptions<ModerationJobsOptions> options, ILogger<HotScoreDecayJob> logger) : IRecurringJob
{
    public string Name => "hot-score-decay";
    public TimeSpan Interval => options.Value.HotDecayInterval;

    public async Task RunAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StudioDbContext>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;
        var since = now - options.Value.HotDecayActiveWindow;
        var halfLife = Ranking.HotHalfLife.TotalSeconds;
        var updated = await db.Database.ExecuteSqlAsync($"""
            update items set hot_score = wilson_score * power(0.5, greatest(extract(epoch from ({now} - published_at)), 0) / {halfLife})
            where status = 'published' and published_at >= {since}
            """, ct);
        logger.LogInformation("Hot score decay updated {Count} items", updated);
    }
}

public sealed class DownloadEventsPruneJob(IServiceScopeFactory scopes, IOptions<ModerationJobsOptions> options, ILogger<DownloadEventsPruneJob> logger) : IRecurringJob
{
    public string Name => "download-events-prune";
    public TimeSpan Interval => options.Value.DownloadEventsPruneInterval;

    public async Task RunAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var cutoff = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow - options.Value.DownloadEventsRetention;
        var deleted = await db.DownloadEvents.Where(e => e.RolledUpAt != null && e.At < cutoff).ExecuteDeleteAsync(ct);
        if (deleted > 0) logger.LogInformation("Pruned {Count} rolled-up download events", deleted);
    }
}
