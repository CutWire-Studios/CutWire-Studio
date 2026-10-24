using System.Security.Cryptography;
using CutWire.Studio.Domain.Common;
using CutWire.Studio.Infrastructure.Blobs;
using Microsoft.Extensions.Options;
using Shouldly;

namespace CutWire.Studio.UnitTests;

public sealed class FileSystemBlobStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "studio-blobs-" + Guid.NewGuid().ToString("N"));
    private readonly FileSystemBlobStore _store;

    public FileSystemBlobStoreTests() =>
        _store = new FileSystemBlobStore(Options.Create(new BlobStoreOptions { BlobRoot = _root }));

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static MemoryStream Bytes(string s) => new(System.Text.Encoding.UTF8.GetBytes(s));

    private IEnumerable<string> StoredFiles() =>
        Directory.GetFiles(_root, "*", SearchOption.AllDirectories).Where(f => !f.Contains(".tmp"));

    [Fact]
    public async Task Put_StoresUnderShardedHashPath()
    {
        var result = await _store.PutAsync(Bytes("hello"), 1024, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var hash = result.Value.Hash.Value;
        hash.ShouldBe(Convert.ToHexStringLower(SHA256.HashData("hello"u8)));
        result.Value.Size.ShouldBe(5);
        File.Exists(Path.Combine(_root, hash[..2], hash[2..4], hash)).ShouldBeTrue();
    }

    [Fact]
    public async Task Put_SameContentTwice_Dedupes()
    {
        var ct = TestContext.Current.CancellationToken;
        var first = await _store.PutAsync(Bytes("same"), 1024, ct);
        var second = await _store.PutAsync(Bytes("same"), 1024, ct);

        first.Value.AlreadyExisted.ShouldBeFalse();
        second.Value.AlreadyExisted.ShouldBeTrue();
        second.Value.Hash.ShouldBe(first.Value.Hash);
        StoredFiles().Count().ShouldBe(1);
    }

    [Fact]
    public async Task Put_OverCap_FailsAndLeavesNothingBehind()
    {
        var result = await _store.PutAsync(Bytes("0123456789"), 5, TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Kind.ShouldBe(ErrorKind.TooLarge);
        Directory.GetFiles(_root, "*", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    [Fact]
    public async Task Put_ExactlyAtCap_Succeeds()
    {
        var result = await _store.PutAsync(Bytes("12345"), 5, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Put_FailingSource_LeavesNoPartialBlob()
    {
        var result = _store.PutAsync(new ThrowingStream(), 1024, TestContext.Current.CancellationToken);

        await Should.ThrowAsync<IOException>(result);
        Directory.GetFiles(_root, "*", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    [Fact]
    public async Task Open_WithOffset_ReturnsSeekableStreamAtOffset()
    {
        var ct = TestContext.Current.CancellationToken;
        var hash = (await _store.PutAsync(Bytes("0123456789"), 1024, ct)).Value.Hash;

        await using var stream = (await _store.OpenAsync(hash, 4, ct))!;
        using var reader = new StreamReader(stream);

        stream.CanSeek.ShouldBeTrue();
        (await reader.ReadToEndAsync(ct)).ShouldBe("456789");
    }

    [Fact]
    public async Task ExistsOpenDelete_ReflectState()
    {
        var ct = TestContext.Current.CancellationToken;
        var hash = (await _store.PutAsync(Bytes("x"), 1024, ct)).Value.Hash;

        (await _store.ExistsAsync(hash, ct)).ShouldBeTrue();
        (await _store.DeleteAsync(hash, ct)).ShouldBeTrue();
        (await _store.ExistsAsync(hash, ct)).ShouldBeFalse();
        (await _store.OpenAsync(hash, ct: ct)).ShouldBeNull();
        (await _store.DeleteAsync(hash, ct)).ShouldBeFalse();
    }

    private sealed class ThrowingStream : Stream
    {
        private bool _first = true;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!_first) throw new IOException("boom");
            _first = false;
            buffer.Span[0] = 1;
            return ValueTask.FromResult(1);
        }
    }
}
