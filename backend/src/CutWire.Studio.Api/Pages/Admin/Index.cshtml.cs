using CutWire.Studio.Application.Abstractions;
using CutWire.Studio.Domain.Catalog;
using CutWire.Studio.Domain.Common;
using CutWire.Studio.Domain.Moderation;
using CutWire.Studio.Domain.Stock;
using Microsoft.EntityFrameworkCore;

namespace CutWire.Studio.Api.Pages.Admin;

public sealed class IndexModel(IAppDbContext db, IClock clock) : AdminPageModel
{
    public int OpenReports { get; private set; }
    public int OpenFlags { get; private set; }
    public int HiddenItems { get; private set; }
    public IReadOnlyList<(string Reason, int Count)> HiddenByReason { get; private set; } = [];
    public int ScheduledPurges { get; private set; }
    public int BannedUsers { get; private set; }
    public int JobsQueued { get; private set; }
    public int JobsProcessing { get; private set; }
    public int ItemDownloads { get; private set; }
    public int StockDownloads { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var since = now.AddHours(-24);
        OpenReports = await db.Reports.CountAsync(r => r.Status == ReportStatus.Open, ct);
        OpenFlags = await db.ItemFlags.CountAsync(f => f.ResolvedAt == null, ct);
        HiddenByReason = (await db.Items.Where(i => i.Status == ItemStatus.Hidden).GroupBy(i => i.VisibilityReason)
                .Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct))
            .Select(g => (g.Key ?? "unspecified", g.Count)).OrderByDescending(g => g.Item2).ToList();
        HiddenItems = HiddenByReason.Sum(h => h.Count);
        ScheduledPurges = await db.CleanupSchedules.CountAsync(ct);
        BannedUsers = await db.UserProfiles.CountAsync(p => p.DeletedAt == null && (p.IsPermanentlyBanned || p.BannedUntil > now), ct);
        JobsQueued = await db.DownloadJobs.CountAsync(j => j.Status == JobStatus.Queued, ct);
        JobsProcessing = await db.DownloadJobs.CountAsync(j => j.Status == JobStatus.Processing, ct);
        ItemDownloads = await db.DownloadEvents.CountAsync(d => d.At >= since, ct);
        StockDownloads = await db.DownloadJobs.CountAsync(j => j.Status == JobStatus.Ready && j.CreatedAt >= since, ct);
    }
}
