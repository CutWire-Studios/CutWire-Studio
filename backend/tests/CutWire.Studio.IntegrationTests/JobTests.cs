using CutWire.Studio.Application.Abstractions;
using CutWire.Studio.Domain.Blobs;
using CutWire.Studio.Infrastructure.Jobs;
using CutWire.Studio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace CutWire.Studio.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class JobTests(PostgresFixture postgres) : IDisposable
{
    private readonly StudioFactory _factory = new(postgres.ConnectionString);
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task AdvisoryLock_AllowsOnlyOneConcurrentRun()
    {
        var runner = _factory.Services.GetRequiredService<JobRunner>();
        var job = new BlockingJob();

        var first = runner.TryRunAsync(job, Ct);
        await job.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        var others = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => runner.TryRunAsync(job, Ct)));
        others.ShouldAllBe(ran => !ran);

        job.Release.SetResult();
        (await first).ShouldBeTrue();
        job.Runs.ShouldBe(1);

        (await runner.TryRunAsync(job, Ct)).ShouldBeTrue();
        job.Runs.ShouldBe(2);
    }

    [Fact]
    public async Task AdvisoryLock_IsReleasedWhenJobThrows()
    {
        var runner = _factory.Services.GetRequiredService<JobRunner>();
        var job = new ThrowingJob();

        await Should.ThrowAsync<InvalidOperationException>(runner.TryRunAsync(job, Ct));
        await Should.ThrowAsync<InvalidOperationException>(runner.TryRunAsync(job, Ct));

        job.Runs.ShouldBe(2);
    }

    [Fact]
    public async Task BlobGc_DeletesOnlyExpiredUnreferencedBlobs()
    {
        var store = _factory.Services.GetRequiredService<IBlobStore>();
        var clock = _factory.Services.GetRequiredService<IClock>();
        var old = clock.UtcNow.AddDays(-8);

        async Task<Blob> Make(string content, DateTimeOffset createdAt, bool referenced)
        {
            var stored = (await store.PutAsync(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content)), 1024, Ct)).Value;
            var blob = new Blob(stored.Hash, stored.Size, "text/plain", createdAt);
            if (referenced) blob.AddRef();
            return blob;
        }

        var expired = await Make("gc-expired", old, referenced: false);
        var fresh = await Make("gc-fresh", clock.UtcNow, referenced: false);
        var referenced = await Make("gc-referenced", old, referenced: true);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StudioDbContext>();
            db.Blobs.AddRange(expired, fresh, referenced);
            await db.SaveChangesAsync(Ct);
        }

        await _factory.Services.GetRequiredService<IEnumerable<IRecurringJob>>().OfType<BlobGcJob>().Single().RunAsync(Ct);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StudioDbContext>();
            (await db.Blobs.AnyAsync(b => b.Id == expired.Id, Ct)).ShouldBeFalse();
            (await db.Blobs.AnyAsync(b => b.Id == fresh.Id, Ct)).ShouldBeTrue();
            (await db.Blobs.AnyAsync(b => b.Id == referenced.Id, Ct)).ShouldBeTrue();
        }

        (await store.ExistsAsync(expired.Id, Ct)).ShouldBeFalse();
        (await store.ExistsAsync(fresh.Id, Ct)).ShouldBeTrue();
        (await store.ExistsAsync(referenced.Id, Ct)).ShouldBeTrue();
    }

    private sealed class BlockingJob : IRecurringJob
    {
        private int _runs;
        public string Name => "test-blocking";
        public TimeSpan Interval => TimeSpan.FromHours(1);
        public int Runs => _runs;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task RunAsync(CancellationToken ct)
        {
            Interlocked.Increment(ref _runs);
            Started.TrySetResult();
            await Release.Task.WaitAsync(ct);
        }
    }

    private sealed class ThrowingJob : IRecurringJob
    {
        public string Name => "test-throwing";
        public TimeSpan Interval => TimeSpan.FromHours(1);
        public int Runs { get; private set; }

        public Task RunAsync(CancellationToken ct)
        {
            Runs++;
            throw new InvalidOperationException("boom");
        }
    }
}
