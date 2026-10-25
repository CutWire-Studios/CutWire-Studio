using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using CutWire.Studio.Application.Stock;
using Microsoft.Extensions.Caching.Memory;

namespace CutWire.Studio.Infrastructure.Stock.Providers;

/// <summary>
/// Shared yt-dlp front end. Every call is a process spawn measured in seconds, so metadata is cached and concurrency is bounded.
/// Singleton: the cache and the extraction slots are shared by all providers.
/// </summary>
public sealed class YtdlpRunner : IDisposable
{
    public static readonly TimeSpan CacheTtl = TimeSpan.FromHours(6);
    public static readonly TimeSpan MetaTimeout = TimeSpan.FromSeconds(45);
    public static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(10);

    public const int MaxConcurrent = 4;
    public const int MaxCacheEntries = 512;
    public const int MaxMetaBytes = 8 << 20;
    public const int MaxDownloadStdoutBytes = 4 << 20;
    public const int MaxStderrBytes = 64 << 10;
    public const int MaxTargetChars = 2048;
    public const int MaxQueryChars = 200;
    public const string ProgressPrefix = "cwprog:";

    private readonly string _bin;
    private readonly YtdlpOptions _options;
    private readonly IReadOnlyList<string> _pluginDirs;
    private readonly IProcessRunner _processes;
    private readonly TimeSpan _metaTimeout;
    private readonly TimeSpan _downloadTimeout;
    private readonly SemaphoreSlim _slots = new(MaxConcurrent);
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = MaxCacheEntries });

    public YtdlpRunner(YtdlpOptions options, string binaryPath, IProcessRunner processes, TimeSpan? metaTimeout = null, TimeSpan? downloadTimeout = null)
    {
        _options = options;
        _bin = binaryPath;
        _processes = processes;
        _metaTimeout = metaTimeout ?? MetaTimeout;
        _downloadTimeout = downloadTimeout ?? DownloadTimeout;
        _pluginDirs = (options.PluginDirs ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(Directory.Exists)
            .ToArray();
    }

    public void Dispose()
    {
        _cache.Dispose();
        _slots.Dispose();
    }

    /// <summary>Full metadata extraction for one target.</summary>
    public async Task<YtdlpInfo> ExtractAsync(string target, CancellationToken ct)
    {
        target = CleanTarget(target);
        var body = await RunAsync(true, _metaTimeout, MaxMetaBytes, ["-J", "--no-playlist", "--", target], null, ct);
        var info = Parse(body);
        if (string.IsNullOrEmpty(info.Id))
            throw new StockException(StockReason.ItemMissing, "yt-dlp returned no id for the target");
        return info;
    }

    /// <summary>Lists a search or playlist target without extracting each entry.</summary>
    public async Task<IReadOnlyList<YtdlpInfo>> FlatAsync(string target, int limit, CancellationToken ct)
    {
        target = CleanTarget(target);
        var body = await RunAsync(true, _metaTimeout, MaxMetaBytes,
            ["-J", "--flat-playlist", "--playlist-items", "1:" + limit.ToString(CultureInfo.InvariantCulture), "--", target], null, ct);
        return (Parse(body).Entries ?? []).Where(e => e is not null).Select(e => e!).Take(limit).ToArray();
    }

    /// <summary>Downloads the target into the workspace and returns the written file. Progress occupies 0.1..0.66 of the job.</summary>
    public async Task<string> DownloadAsync(string target, StockWorkspace workspace, string format, CancellationToken ct)
    {
        target = CleanTarget(target);
        var args = new List<string>();
        if (format.Contains('+')) args.AddRange(["--merge-output-format", "mp4"]);
        args.AddRange(
        [
            "--no-playlist",
            "-f", format,
            "-o", Path.Combine(workspace.Dir, "%(id)s.%(ext)s"),
            "--no-part",
            "--no-simulate",
            "--print", "after_move:filepath",
            "--progress", "--newline",
            "--progress-template", "download:" + ProgressPrefix + "%(progress.downloaded_bytes)s/%(progress.total_bytes)s/%(progress.total_bytes_estimate)s",
            "--", target,
        ]);

        string? last = null;
        var highest = 0.1;
        void OnLine(string line)
        {
            if (!line.StartsWith(ProgressPrefix, StringComparison.Ordinal))
            {
                if (line.Trim().Length > 0) last = line.Trim();
                return;
            }
            var parts = line[ProgressPrefix.Length..].Split('/');
            if (parts.Length != 3 || !double.TryParse(parts[0], CultureInfo.InvariantCulture, out var done)) return;
            var total = double.TryParse(parts[1], CultureInfo.InvariantCulture, out var t) && t > 0 ? t
                : double.TryParse(parts[2], CultureInfo.InvariantCulture, out var e) && e > 0 ? e : 0;
            if (total <= 0) return;
            var fraction = 0.1 + Math.Clamp(done / total, 0, 1) * 0.56;
            if (fraction <= highest) return;
            highest = fraction;
            workspace.Report(fraction);
        }

        await RunAsync(false, _downloadTimeout, MaxDownloadStdoutBytes, args, OnLine, ct, progress: true);

        return FileWithin(workspace.Dir, last);
    }

    /// <summary>Only an http(s) URL or a ytsearchN: query built by this process may reach yt-dlp as a positional argument.</summary>
    public static string CleanTarget(string? target)
    {
        target = target?.Trim() ?? "";
        if (target.Length == 0 || target.Length > MaxTargetChars || target.StartsWith('-') || target.IndexOfAny(['\r', '\n', '\0', '\\']) >= 0)
            throw Rejected();

        if (target.StartsWith("ytsearch", StringComparison.Ordinal))
        {
            var rest = target["ytsearch".Length..];
            var colon = rest.IndexOf(':');
            if (colon <= 0) throw Rejected();
            var n = rest[..colon];
            var query = rest[(colon + 1)..];
            if (n.Length > 3 || !n.All(char.IsAsciiDigit) || query.Length == 0 || query.Length > MaxQueryChars) throw Rejected();
            return target;
        }

        if (!ProviderSupport.TryParseHttpUrl(target, out _)) throw Rejected();
        return target;
    }

    private static StockException Rejected() => new(StockReason.LinkUnsupported, "yt-dlp: rejected target");

    /// <summary>yt-dlp prints the path it wrote; that string is not trusted, so it must be a regular, non-symlink file inside the workspace.</summary>
    public static string FileWithin(string dir, string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Contains('\0')) throw Escaped();
        var root = Path.GetFullPath(dir);
        var full = Path.GetFullPath(name);
        var rel = Path.GetRelativePath(root, full);
        if (rel == ".." || rel.StartsWith(".." + Path.DirectorySeparatorChar) || Path.IsPathRooted(rel)) throw Escaped();
        var info = new FileInfo(full);
        if (!info.Exists || info.LinkTarget is not null || (info.Attributes & FileAttributes.Directory) != 0) throw Escaped();
        return full;
    }

    private static StockException Escaped() => new(StockReason.DownloadError, "yt-dlp: output path escaped the workspace");

    private static YtdlpInfo Parse(byte[] body)
    {
        try
        {
            return JsonSerializer.Deserialize<YtdlpInfo>(body, YtdlpInfo.Json) ?? throw new StockException(StockReason.SourceError, "yt-dlp: empty output");
        }
        catch (JsonException ex)
        {
            throw new StockException(StockReason.SourceError, "yt-dlp: unreadable output", ex);
        }
    }

    private async Task<byte[]> RunAsync(bool cacheable, TimeSpan timeout, int stdoutMax, IReadOnlyList<string> args, Action<string>? onLine, CancellationToken ct, bool progress = false)
    {
        var key = string.Join('\0', args);
        if (cacheable && _cache.TryGetValue(key, out byte[]? hit) && hit is not null) return hit;

        var full = new List<string> { "--no-warnings", "--ignore-config" };
        if (!progress) full.Add("--no-progress");
        if (_options.ForceIPv6) full.Add("-6");
        if (!string.IsNullOrEmpty(_options.CookiesFile)) full.AddRange(["--cookies", _options.CookiesFile]);
        foreach (var dir in _pluginDirs) full.AddRange(["--plugin-dirs", dir]);
        if (!string.IsNullOrEmpty(_options.PotBaseUrl)) full.AddRange(["--extractor-args", "youtubepot-bgutilhttp:base_url=" + _options.PotBaseUrl]);
        if (!string.IsNullOrEmpty(_options.ExtractorArgs)) full.AddRange(["--extractor-args", _options.ExtractorArgs]);
        full.AddRange(args);

        var clock = Stopwatch.StartNew();
        using (var wait = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            wait.CancelAfter(timeout);
            try
            {
                await _slots.WaitAsync(wait.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new StockException(StockReason.SourceTimeout, "yt-dlp: timed out waiting for a free extraction slot");
            }
        }

        ProcessOutput result;
        try
        {
            var remaining = timeout - clock.Elapsed;
            if (remaining <= TimeSpan.Zero) throw new StockException(StockReason.SourceTimeout, "yt-dlp: timed out waiting for a free extraction slot");
            result = await _processes.RunAsync(new ProcessSpec(_bin, full, remaining, stdoutMax, MaxStderrBytes, onLine), ct);
        }
        catch (Win32Exception ex)
        {
            throw new StockException(StockReason.SourceError, "yt-dlp: could not start " + _bin, ex);
        }
        finally
        {
            _slots.Release();
        }

        if (result.StdoutTooLarge) throw new StockException(StockReason.SourceError, "yt-dlp: output exceeded the cap");
        if (result.TimedOut) throw new StockException(StockReason.SourceTimeout, "yt-dlp: timed out: " + Tail(result.Stderr));
        if (result.ExitCode != 0) throw YtdlpErrors.Classify(result.Stderr, "exit code " + result.ExitCode.ToString(CultureInfo.InvariantCulture));

        if (cacheable) _cache.Set(key, result.Stdout, new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = CacheTtl });
        return result.Stdout;
    }

    private static string Tail(string s) => s.Length > 400 ? s[..400] : s;
}
