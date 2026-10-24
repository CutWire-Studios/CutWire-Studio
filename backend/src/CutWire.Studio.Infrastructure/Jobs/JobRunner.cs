using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace CutWire.Studio.Infrastructure.Jobs;

public sealed class JobRunner(NpgsqlDataSource dataSource, ILogger<JobRunner> logger)
{
    public async Task<bool> TryRunAsync(IRecurringJob job, CancellationToken ct)
    {
        var key = LockKey(job.Name);
        await using var connection = await dataSource.OpenConnectionAsync(ct);

        await using (var acquire = new NpgsqlCommand("select pg_try_advisory_lock(@key)", connection))
        {
            acquire.Parameters.AddWithValue("key", key);
            if (!(bool)(await acquire.ExecuteScalarAsync(ct))!)
            {
                logger.LogDebug("Job {Job} is held by another instance", job.Name);
                return false;
            }
        }

        try
        {
            await job.RunAsync(ct);
            return true;
        }
        finally
        {
            await using var release = new NpgsqlCommand("select pg_advisory_unlock(@key)", connection);
            release.Parameters.AddWithValue("key", key);
            await release.ExecuteScalarAsync(CancellationToken.None);
        }
    }

    internal static long LockKey(string name) =>
        BitConverter.ToInt64(SHA256.HashData(Encoding.UTF8.GetBytes("cutwire.studio.job:" + name)));
}
