using System.Security.Cryptography;
using CutWire.Studio.Application.Stock;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CutWire.Studio.Infrastructure.Stock;

/// <summary>sfx.json read from <c>Stock:Sfx:Path</c>; re-read when the file's timestamp changes, so a re-sync needs no restart. A missing or broken index lists nothing.</summary>
public sealed class FileSfxSource(IOptions<SfxOptions> options, ILogger<FileSfxSource> logger) : ISfxSource
{
    public const string IndexFile = "sfx.json";
    private readonly object _gate = new();
    private DateTime _stamp;
    private SfxData _data = SfxData.Empty;

    public string Root => options.Value.Path;

    public SfxData Load()
    {
        if (string.IsNullOrWhiteSpace(Root)) return SfxData.Empty;
        lock (_gate)
        {
            var path = Path.Combine(Root, IndexFile);
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                _stamp = default;
                return _data = SfxData.Empty;
            }

            if (info.LastWriteTimeUtc == _stamp) return _data;
            try
            {
                var bytes = File.ReadAllBytes(path);
                var version = Convert.ToHexStringLower(SHA256.HashData(bytes))[..12];
                _data = SfxIndex.Parse(System.Text.Encoding.UTF8.GetString(bytes), version);
                _stamp = info.LastWriteTimeUtc;
                logger.LogInformation("SFX index loaded: {Sounds} sounds", _data.Categories.Sum(c => c.Sounds.Count));
            }
            catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or FormatException)
            {
                logger.LogError(ex, "SFX index {Path} is unreadable", path);
            }

            return _data;
        }
    }

    /// <summary>Resolves previews/ or library/ plus a relative path to a file, or null if it escapes the root or is missing.</summary>
    public string? Resolve(string area, string relative)
    {
        if (string.IsNullOrWhiteSpace(Root) || area is not ("previews" or "library") || !SfxIndex.SafeRel(relative)) return null;
        var areaRoot = Path.GetFullPath(Path.Combine(Root, area)) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(areaRoot, relative));
        if (!full.StartsWith(areaRoot, StringComparison.Ordinal)) return null;
        var info = new FileInfo(full);
        return info.Exists && info.LinkTarget is null ? full : null;
    }
}
