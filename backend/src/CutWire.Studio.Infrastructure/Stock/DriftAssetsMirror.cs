using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using CutWire.Studio.Application.Abstractions;
using CutWire.Studio.Application.Blobs;
using CutWire.Studio.Application.Catalog;
using CutWire.Studio.Application.Stock;
using CutWire.Studio.Domain.Blobs;
using CutWire.Studio.Domain.Common;
using CutWire.Studio.Domain.Stock;
using CutWire.Studio.Infrastructure.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CutWire.Studio.Infrastructure.Stock;

/// <summary>
/// Mirrors the Drift Assets GitHub release (index.json + drift-assets.tar.gz) into the blob store. Every file is checked against the
/// index's sha256 before the version goes live, and the switch is one transaction, so clients never see a half pack.
/// </summary>
public sealed class DriftAssetsMirror(
    IServiceScopeFactory scopes,
    IStockHttpClients http,
    IOptions<DriftAssetsOptions> options,
    ILogger<DriftAssetsMirror> logger)
{
    private const long MaxIndexBytes = 8 << 20;
    private const long MaxArchiveBytes = 1L << 30;
    private const long MaxUncompressed = 4L << 30;

    public async Task SyncAsync(CancellationToken ct)
    {
        var baseUrl = options.Value.ResolveBase();
        if (baseUrl.Length == 0) return;

        var indexJson = await FetchStringAsync(baseUrl + "/index.json", ct);
        var index = DriftIndex.Parse(indexJson);

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var current = await db.DriftAssetPacks.FirstOrDefaultAsync(p => p.IsCurrent, ct);
        if (current?.Version == index.Version) return;

        var existing = await db.DriftAssetPacks.FirstOrDefaultAsync(p => p.Version == index.Version, ct);
        var tmp = Directory.CreateTempSubdirectory("cutwire-drift-assets-").FullName;
        try
        {
            if (existing is null)
            {
                var shas = await DownloadAsync(baseUrl + "/drift-assets.tar.gz", index, tmp, scope.ServiceProvider, ct);
                existing = new DriftAssetPack
                {
                    Version = index.Version,
                    IndexJson = indexJson,
                    FilesJson = JsonSerializer.Serialize(shas),
                    SyncedAt = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow,
                };
                db.DriftAssetPacks.Add(existing);
            }

            if (current is not null) current.IsCurrent = false;
            existing.IsCurrent = true;
            await db.SaveChangesAsync(ct);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }

        await scope.ServiceProvider.GetRequiredService<DriftAssetsCatalog>().SeedCategoriesAsync(index, ct);
        await PruneAsync(db, scope.ServiceProvider.GetRequiredService<BlobReferences>(), index.Version, current?.Version, ct);
        logger.LogInformation("Drift assets synced to {Version} ({Assets} assets)", index.Version, index.Assets.Count);
    }

    private async Task<string[]> DownloadAsync(string url, DriftIndex index, string dir, IServiceProvider sp, CancellationToken ct)
    {
        using var response = await http.Public.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        var want = index.Files();
        var seen = new HashSet<string>();
        await using (var body = await response.Content.ReadAsStreamAsync(ct))
        await using (var gzip = new GZipStream(new LimitedStream(body, MaxArchiveBytes), CompressionMode.Decompress))
        {
            using var tar = new TarReader(new LimitedStream(gzip, MaxUncompressed));
            while (await tar.GetNextEntryAsync(copyData: false, ct) is { } entry)
            {
                var name = string.Join('/', entry.Name.Split('/').Where(p => p is not ("" or ".")));
                if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile) || !want.TryGetValue(name, out var file) || entry.DataStream is null) continue;
                var target = Path.Combine(dir, Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(name))));
                long written;
                string sha;
                await using (var output = File.Create(target))
                using (var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
                {
                    var buffer = new byte[81920];
                    written = 0;
                    int read;
                    while ((read = await entry.DataStream.ReadAsync(buffer, ct)) > 0)
                    {
                        written += read;
                        if (written > file.Size) break;
                        hasher.AppendData(buffer, 0, read);
                        await output.WriteAsync(buffer.AsMemory(0, read), ct);
                    }

                    sha = Convert.ToHexStringLower(hasher.GetHashAndReset());
                }

                if (written != file.Size || sha != file.Sha256) throw new InvalidDataException($"archive: {name} does not match the index");
                seen.Add(name);
            }
        }

        var missing = want.Keys.FirstOrDefault(k => !seen.Contains(k));
        if (missing is not null) throw new InvalidDataException($"archive: {missing} is missing");

        var blobs = sp.GetRequiredService<ICommandHandler<StoreBlobCommand, StoreBlobResult>>();
        var refs = sp.GetRequiredService<BlobReferences>();
        var added = new HashSet<string>();
        foreach (var (name, file) in want)
        {
            if (!added.Add(file.Sha256)) continue;
            var path = Path.Combine(dir, Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(name))));
            await using var stream = File.OpenRead(path);
            var stored = await blobs.Handle(new StoreBlobCommand(stream, MimeFor(file.Name), DriftIndex.MaxAssetBytes), ct);
            if (stored.IsFailure) throw new InvalidDataException("blob: " + stored.Error.Code);
            await refs.AddAsync(stored.Value.Hash, ct);
        }

        return added.ToArray();
    }

    private static async Task PruneAsync(IAppDbContext db, BlobReferences refs, string keepCurrent, string? keepPrevious, CancellationToken ct)
    {
        // A client may still be fetching files it listed a moment ago, so the previous version stays one more cycle.
        var stale = await db.DriftAssetPacks.Where(p => p.Version != keepCurrent && p.Version != keepPrevious).ToListAsync(ct);
        foreach (var pack in stale)
        {
            foreach (var sha in JsonSerializer.Deserialize<string[]>(pack.FilesJson) ?? [])
                if (Sha256Hash.TryParse(sha, out var hash)) await refs.ReleaseAsync(hash, ct);
            db.DriftAssetPacks.Remove(pack);
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task<string> FetchStringAsync(string url, CancellationToken ct)
    {
        using var response = await http.Public.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await using var stream = new LimitedStream(await response.Content.ReadAsStreamAsync(ct), MaxIndexBytes);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(ct);
    }

    public static string MimeFor(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".json" or ".lottie" => "application/json",
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".svg" => "image/svg+xml",
        ".glb" => "model/gltf-binary",
        ".gltf" => "model/gltf+json",
        ".mp4" => "video/mp4",
        ".webm" => "video/webm",
        _ => "application/octet-stream",
    };

    private sealed class LimitedStream(Stream inner, long max) : Stream
    {
        private long _read;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            var n = inner.Read(buffer);
            Account(n);
            return n;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            var n = await inner.ReadAsync(buffer, ct);
            Account(n);
            return n;
        }

        private void Account(int n)
        {
            _read += n;
            if (_read > max) throw new InvalidDataException("archive: size exceeds the cap");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }
}

public sealed class DriftAssetsSyncJob(DriftAssetsMirror mirror, IOptions<DriftAssetsOptions> options, ILogger<DriftAssetsSyncJob> logger) : IRecurringJob
{
    public string Name => "drift-assets-sync";
    public TimeSpan Interval => options.Value.Interval;

    public async Task RunAsync(CancellationToken ct)
    {
        try
        {
            await mirror.SyncAsync(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or FormatException or IOException)
        {
            logger.LogError(ex, "Drift assets sync failed");
        }
    }
}
