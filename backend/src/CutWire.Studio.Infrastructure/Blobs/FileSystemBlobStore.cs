using System.Security.Cryptography;
using CutWire.Studio.Application.Abstractions;
using CutWire.Studio.Domain.Blobs;
using CutWire.Studio.Domain.Common;
using Microsoft.Extensions.Options;

namespace CutWire.Studio.Infrastructure.Blobs;

public sealed class FileSystemBlobStore : IBlobStore
{
    private const string TempDir = ".tmp";
    private readonly string _root;

    public FileSystemBlobStore(IOptions<BlobStoreOptions> options)
    {
        _root = Path.GetFullPath(options.Value.BlobRoot);
        Directory.CreateDirectory(Path.Combine(_root, TempDir));
    }

    public async Task<Result<StoredBlob>> PutAsync(Stream content, long maxBytes, CancellationToken ct)
    {
        var tempPath = Path.Combine(_root, TempDir, Guid.NewGuid().ToString("N"));
        try
        {
            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long size = 0;
            await using (var temp = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             81920, FileOptions.Asynchronous))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await content.ReadAsync(buffer, ct)) > 0)
                {
                    size += read;
                    if (size > maxBytes)
                        return Error.TooLarge("blob.too_large", $"Content exceeds the limit of {maxBytes} bytes.");
                    hasher.AppendData(buffer, 0, read);
                    await temp.WriteAsync(buffer.AsMemory(0, read), ct);
                }

                await temp.FlushAsync(ct);
            }

            var hash = Sha256Hash.FromBytes(hasher.GetHashAndReset());
            var target = PathFor(hash);
            if (File.Exists(target)) return new StoredBlob(hash, size, true);

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Move(tempPath, target, overwrite: true);
            return new StoredBlob(hash, size, false);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    public Task<Stream?> OpenAsync(Sha256Hash hash, long offset = 0, CancellationToken ct = default)
    {
        try
        {
            var stream = new FileStream(PathFor(hash), FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete,
                81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (offset > 0) stream.Seek(offset, SeekOrigin.Begin);
            return Task.FromResult<Stream?>(stream);
        }
        catch (FileNotFoundException)
        {
            return Task.FromResult<Stream?>(null);
        }
        catch (DirectoryNotFoundException)
        {
            return Task.FromResult<Stream?>(null);
        }
    }

    public Task<bool> ExistsAsync(Sha256Hash hash, CancellationToken ct = default) =>
        Task.FromResult(File.Exists(PathFor(hash)));

    public Task<bool> DeleteAsync(Sha256Hash hash, CancellationToken ct = default)
    {
        var path = PathFor(hash);
        var existed = File.Exists(path);
        File.Delete(path);
        return Task.FromResult(existed);
    }

    private string PathFor(Sha256Hash hash)
    {
        var v = hash.Value;
        return Path.Combine(_root, v[..2], v[2..4], v);
    }
}
