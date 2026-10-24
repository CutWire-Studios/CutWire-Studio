using CutWire.Studio.Application.Abstractions;
using CutWire.Studio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CutWire.Studio.Infrastructure.Jobs;

public sealed class DownloadRollupOptions
{
    public const string Section = "DownloadRollup";

    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(10);
}

/// <summary>
/// Collapses <c>download_events</c> into <c>item_download_daily</c> (one count per distinct downloader per item per UTC day;
/// the downloader is the user, else the install id, else the IP hash) and refreshes <c>items.downloads_total</c>.
/// </summary>
public sealed class DownloadRollupJob(IServiceScopeFactory scopes, IOptions<DownloadRollupOptions> options, ILogger<DownloadRollupJob> logger)
    : IRecurringJob
{
    public string Name => "download-rollup";
    public TimeSpan Interval => options.Value.Interval;

    public async Task RunAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StudioDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var upTo = await db.Database.SqlQuery<long>($"select coalesce(max(id), 0) as \"Value\" from download_events where rolled_up_at is null").SingleAsync(ct);
        if (upTo == 0) return;

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlAsync($"""
            insert into item_download_daily (item_id, day, downloads)
            select e.item_id, (e.at at time zone 'utc')::date,
                   count(distinct coalesce('u:' || e.user_id::text, 'i:' || e.install_id, 'p:' || e.ip_hash))
            from download_events e
            where (e.item_id, (e.at at time zone 'utc')::date) in (
                select p.item_id, (p.at at time zone 'utc')::date from download_events p where p.rolled_up_at is null and p.id <= {upTo})
            group by e.item_id, (e.at at time zone 'utc')::date
            on conflict (item_id, day) do update set downloads = excluded.downloads
            """, ct);
        await db.Database.ExecuteSqlAsync($"""
            update items set downloads_total = coalesce((select sum(d.downloads) from item_download_daily d where d.item_id = items.id), 0)
            where id in (select item_id from download_events where rolled_up_at is null and id <= {upTo})
            """, ct);
        var rolled = await db.Database.ExecuteSqlAsync(
            $"update download_events set rolled_up_at = {clock.UtcNow} where rolled_up_at is null and id <= {upTo}", ct);
        await tx.CommitAsync(ct);
        logger.LogInformation("Rolled up {Count} download events", rolled);
    }
}
