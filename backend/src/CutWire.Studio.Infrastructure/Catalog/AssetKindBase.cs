using System.Security.Cryptography;
using CutWire.Studio.Application.Catalog;
using CutWire.Studio.Domain.Blobs;
using CutWire.Studio.Domain.Catalog;
using CutWire.Studio.Domain.Common;

namespace CutWire.Studio.Infrastructure.Catalog;

public abstract class AssetKindBase : IAssetKind
{
    private readonly AssetKindDefinition _definition;

    protected AssetKindBase(AssetKindId id)
    {
        Id = id;
        _definition = AssetKindDefinition.For(id);
        MediaSpecs = _definition.ToMediaSpecs();
    }

    public AssetKindId Id { get; }
    public string DisplayName => _definition.DisplayName;
    public IReadOnlyList<string> AcceptedExtensions => _definition.Extensions;
    public long MaxPackageBytes => _definition.MaxPackageBytes;
    public long MaxUnpackedBytes => _definition.MaxUnpackedBytes;
    public IReadOnlyList<MediaSpec> MediaSpecs { get; }
    public IReadOnlyList<string> Categories => _definition.Categories;
    public IReadOnlyList<string> TagVocabulary => _definition.Tags;
    public bool Enabled => _definition.Enabled;

    public async Task<Result<PackageInfo>> ValidateAsync(Stream package, CancellationToken ct)
    {
        var read = await ReadBoundedAsync(package, MaxPackageBytes, ct);
        if (read.IsFailure) return read.Error;
        return Validate(read.Value, new Sha256Hash(Convert.ToHexStringLower(SHA256.HashData(read.Value))));
    }

    protected abstract Result<PackageInfo> Validate(byte[] bytes, Sha256Hash sha256);

    protected static Error Invalid(string code, string message) =>
        Error.Validation(code, message, [new FieldViolation("package", code, message)]);

    private static async Task<Result<byte[]>> ReadBoundedAsync(Stream stream, long max, CancellationToken ct)
    {
        if (stream.CanSeek && stream.Length > max)
            return Error.TooLarge("package_too_large", $"Package is larger than {max} bytes.");
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > max) return Error.TooLarge("package_too_large", $"Package is larger than {max} bytes.");
        }

        return buffer.ToArray();
    }
}
