using CutWire.Studio.Application.Abstractions;
using CutWire.Studio.Domain.Common;
using CutWire.Studio.Domain.Stock;
using Microsoft.EntityFrameworkCore;

namespace CutWire.Studio.Application.Stock;

public sealed record QuotaStatus(int Limit, int Used, int Remaining, string Window, DateTimeOffset ResetAt)
{
    public bool Exhausted => Limit > 0 && Remaining <= 0;
}

/// <summary>
/// Per-client (or per-account) download caps over an append-only ledger. A refund is a row, so a failed job cannot refund twice (unique
/// on job and kind) and the arithmetic never drifts.
/// </summary>
public sealed class StockQuota(IAppDbContext db, IClock clock)
{
    public static DateTimeOffset WindowStart(string window, DateTimeOffset now)
    {
        var utc = now.UtcDateTime;
        return window == "hour"
            ? new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, TimeSpan.Zero)
            : new DateTimeOffset(utc.Year, utc.Month, utc.Day, 0, 0, 0, TimeSpan.Zero);
    }

    public static DateTimeOffset WindowEnd(string window, DateTimeOffset now)
    {
        var start = WindowStart(window, now);
        return window == "hour" ? start.AddHours(1) : start.AddDays(1);
    }

    public static string SubjectFor(ProviderSetting setting, StockCaller caller) =>
        setting.QuotaScope == "account" && caller.UserId is { } user ? "u:" + user : caller.ClientId;

    public async Task<QuotaStatus> CheckAsync(string subject, string provider, string window, int limit, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var end = WindowEnd(window, now);
        if (limit <= 0) return new QuotaStatus(limit, 0, 0, window, end);
        var used = await UsedAsync(subject, provider, window, now, ct);
        return new QuotaStatus(limit, used, Math.Max(0, limit - used), window, end);
    }

    public async Task<StockQuotaDto?> DtoAsync(StockEntry entry, StockCaller caller, CancellationToken ct)
    {
        var s = entry.Setting;
        if (s.QuotaLimit <= 0) return null;
        var status = await CheckAsync(SubjectFor(s, caller), entry.Provider.Descriptor.Id, s.QuotaWindow, s.QuotaLimit, ct);
        return new StockQuotaDto(status.Limit, status.Window, status.Remaining);
    }

    private async Task<int> UsedAsync(string subject, string provider, string window, DateTimeOffset now, CancellationToken ct)
    {
        var start = WindowStart(window, now);
        var counts = await db.QuotaEvents.AsNoTracking()
            .Where(e => e.Subject == subject && e.Provider == provider && e.WindowStart == start)
            .GroupBy(e => e.Kind)
            .Select(g => new { Kind = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var consumed = counts.FirstOrDefault(c => c.Kind == QuotaKind.Consumed)?.Count ?? 0;
        var refunded = counts.FirstOrDefault(c => c.Kind == QuotaKind.Refunded)?.Count ?? 0;
        return Math.Max(0, consumed - refunded);
    }

    /// <summary>
    /// Records one use, or fails with a 429 <c>stock.quota_exceeded</c> carrying <c>retryAfter</c>. The check and the write share a
    /// transaction behind a per-subject advisory lock, so two concurrent downloads cannot both take the last slot.
    /// </summary>
    public async Task<Result> ConsumeAsync(string subject, string provider, string window, int limit, Guid jobId, CancellationToken ct)
    {
        if (limit <= 0) return Result.Success();
        var now = clock.UtcNow;
        await using var tx = await db.BeginTransactionAsync(ct);
        await db.LockForTransactionAsync("stock-quota:" + subject + ":" + provider, ct);
        var used = await UsedAsync(subject, provider, window, now, ct);
        if (used >= limit)
        {
            var seconds = (int)Math.Ceiling((WindowEnd(window, now) - now).TotalSeconds);
            return Error.RateLimited("stock.quota_exceeded", "Download limit reached for this source. Try again later.", seconds);
        }

        db.QuotaEvents.Add(new QuotaEvent
        {
            Subject = subject, Provider = provider, WindowStart = WindowStart(window, now), Kind = QuotaKind.Consumed, JobId = jobId, CreatedAt = now,
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Result.Success();
    }

    /// <summary>Idempotent: the unique index on (job, kind) settles any race between two callers.</summary>
    public async Task RefundAsync(DownloadJob job, CancellationToken ct)
    {
        var consumed = await db.QuotaEvents.AsNoTracking()
            .FirstOrDefaultAsync(e => e.JobId == job.Id && e.Kind == QuotaKind.Consumed, ct);
        if (consumed is null) return;
        if (await db.QuotaEvents.AnyAsync(e => e.JobId == job.Id && e.Kind == QuotaKind.Refunded, ct)) return;
        db.QuotaEvents.Add(new QuotaEvent
        {
            Subject = consumed.Subject, Provider = consumed.Provider, WindowStart = consumed.WindowStart, Kind = QuotaKind.Refunded, JobId = job.Id, CreatedAt = clock.UtcNow,
        });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Another caller refunded first, which is the outcome we wanted.
        }
    }

    public Task<int> PruneAsync(DateTimeOffset before, CancellationToken ct) =>
        db.QuotaEvents.Where(e => e.WindowStart < before).ExecuteDeleteAsync(ct);
}
