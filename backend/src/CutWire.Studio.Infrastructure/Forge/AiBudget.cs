using Microsoft.Extensions.DependencyInjection;
using CutWire.Studio.Application.Abstractions;
using CutWire.Studio.Application.Forge;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CutWire.Studio.Infrastructure.Forge;

/// <summary>
/// Daily neuron budgets in <c>ai_usage</c>, one row per UTC day and user plus the global row <c>*</c>. Every change is one atomic
/// upsert, so concurrent requests can't overspend by interleaving a check and a booking.
/// </summary>
public sealed class AiBudget(IAppDbContext db, IClock clock, IOptions<AiOptions> options) : IAiBudget
{
    private const string Global = "*";

    private DateOnly Today => DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

    public async Task<(double UserUsed, double GlobalUsed)> UsedAsync(Guid userId, CancellationToken ct)
    {
        var sub = userId.ToString();
        var day = Today;
        var rows = await db.AiUsage.AsNoTracking().Where(u => u.Day == day && (u.Sub == sub || u.Sub == Global)).ToListAsync(ct);
        return (rows.FirstOrDefault(r => r.Sub == sub)?.Neurons ?? 0, rows.FirstOrDefault(r => r.Sub == Global)?.Neurons ?? 0);
    }

    public async Task<bool> ReserveAsync(Guid userId, double neurons, CancellationToken ct)
    {
        var sub = userId.ToString();
        if (!await TryAddAsync(sub, neurons, options.Value.UserDailyNeurons, ct)) return false;
        if (await TryAddAsync(Global, neurons, options.Value.DailyNeurons, ct)) return true;
        await AddAsync(sub, -neurons, ct);
        return false;
    }

    public async Task SettleAsync(Guid userId, double delta, CancellationToken ct)
    {
        await AddAsync(userId.ToString(), delta, ct);
        await AddAsync(Global, delta, ct);
    }

    private async Task<bool> TryAddAsync(string sub, double neurons, double limit, CancellationToken ct)
    {
        if (neurons > limit) return false;
        var day = Today;
        return await db.Database.ExecuteSqlAsync($"""
            insert into ai_usage (day, sub, neurons) values ({day}, {sub}, {neurons})
            on conflict (day, sub) do update set neurons = ai_usage.neurons + {neurons}
            where ai_usage.neurons + {neurons} <= {limit}
            """, ct) == 1;
    }

    private async Task AddAsync(string sub, double delta, CancellationToken ct)
    {
        var day = Today;
        await db.Database.ExecuteSqlAsync($"""
            insert into ai_usage (day, sub, neurons) values ({day}, {sub}, greatest({delta}, 0))
            on conflict (day, sub) do update set neurons = greatest(ai_usage.neurons + {delta}, 0)
            """, ct);
    }
}

public sealed class AiUsagePruneJob(IServiceScopeFactory scopes, IClock clock) : Jobs.IRecurringJob
{
    public string Name => "ai-usage-prune";
    public TimeSpan Interval => TimeSpan.FromHours(6);

    public async Task RunAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var cutoff = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime).AddDays(-3);
        await db.AiUsage.Where(u => u.Day < cutoff).ExecuteDeleteAsync(ct);
    }
}
