using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CutWire.Studio.Infrastructure.Jobs;

public sealed class RecurringJobHostedService(
    IEnumerable<IRecurringJob> jobs,
    JobRunner runner,
    ILogger<RecurringJobHostedService> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(jobs.Select(job => Loop(job, stoppingToken)));

    private async Task Loop(IRecurringJob job, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(job.Interval);
        try
        {
            do
            {
                try
                {
                    if (await runner.TryRunAsync(job, ct)) logger.LogInformation("Job {Job} completed", job.Name);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Job {Job} failed", job.Name);
                }
            } while (await timer.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }
}
