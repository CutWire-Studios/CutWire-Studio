namespace CutWire.Studio.Infrastructure.Jobs;

public interface IRecurringJob
{
    string Name { get; }
    TimeSpan Interval { get; }
    Task RunAsync(CancellationToken ct);
}
