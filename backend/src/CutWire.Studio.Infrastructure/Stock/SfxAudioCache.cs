using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Channels;
using CutWire.Studio.Application.Stock;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CutWire.Studio.Infrastructure.Stock;

public sealed class FfmpegPcmDecoder(IOptions<FfmpegOptions> options) : ISfxPcmDecoder
{
    private const int MaxSeconds = 300;

    public async Task<short[]> DecodeAsync(string path, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(options.Value.Path) { RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, UseShellExecute = false };
        foreach (var a in new[]
        {
            "-nostdin", "-v", "error", "-protocol_whitelist", "file,crypto", "-t", MaxSeconds.ToString(), "-i", path,
            "-vn", "-ac", "1", "-ar", SfxPeaks.SampleRate.ToString(), "-f", "s16le", "-",
        }) psi.ArgumentList.Add(a);

        using var process = Process.Start(psi) ?? throw new IOException("could not start ffmpeg");
        process.StandardInput.Close();
        var stderr = process.StandardError.ReadToEndAsync(ct);
        using var pcm = new MemoryStream();
        try
        {
            await process.StandardOutput.BaseStream.CopyToAsync(pcm, ct);
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw;
        }

        if (process.ExitCode != 0) throw new IOException($"ffmpeg exited {process.ExitCode}: {(await stderr).Trim()}");
        var bytes = pcm.GetBuffer().AsSpan(0, (int)pcm.Length);
        var samples = new short[bytes.Length / 2];
        for (var i = 0; i < samples.Length; i++) samples[i] = System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(bytes[(i * 2)..]);
        return samples;
    }
}

/// <summary>
/// Peaks and sha256 for library files whose index entry lacks them. A background worker fills it, starting with every such file at
/// startup, and the results persist in <see cref="SfxOptions.CachePath"/> keyed by path, size and mtime, so a re-synced file is redone.
/// </summary>
public sealed class SfxAudioCache(
    FileSfxSource files,
    IOptions<SfxOptions> options,
    ISfxPcmDecoder decoder,
    ILogger<SfxAudioCache> logger) : BackgroundService, ISfxAudio
{
    private sealed record Entry(string Stamp, string Peaks, string Sha256);

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _queued = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _failed = new(StringComparer.Ordinal);
    private readonly Channel<string> _work = Channel.CreateUnbounded<string>(new() { SingleReader = true });
    private int _loaded;

    public SfxAudioInfo? TryGet(string libraryPath)
    {
        Load();
        var file = files.Resolve("library", libraryPath);
        if (file is null) return null;
        var stamp = Stamp(file);
        if (_entries.TryGetValue(libraryPath, out var entry) && entry.Stamp == stamp) return new SfxAudioInfo(entry.Peaks, entry.Sha256);
        if (!(_failed.TryGetValue(libraryPath, out var failedAt) && failedAt == stamp) && _queued.TryAdd(libraryPath, 0))
            _work.Writer.TryWrite(libraryPath);
        return null;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        Load();
        foreach (var sound in files.Load().Categories.SelectMany(c => c.Sounds))
            if (sound.Peaks is null || sound.Sha256 is null) TryGet(sound.LibraryPath);

        try
        {
            await foreach (var path in _work.Reader.ReadAllAsync(ct))
            {
                await ComputeAsync(path, ct);
                if (!_work.Reader.TryPeek(out _)) Save();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        finally
        {
            Save();
        }
    }

    private async Task ComputeAsync(string path, CancellationToken ct)
    {
        try
        {
            var file = files.Resolve("library", path);
            if (file is null) return;
            var stamp = Stamp(file);
            var peaks = SfxPeaks.Encode(await decoder.DecodeAsync(file, ct));
            await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            _entries[path] = new Entry(stamp, peaks, Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, ct)));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not compute peaks for SFX {Path}", path);
            var file = files.Resolve("library", path);
            if (file is not null) _failed[path] = Stamp(file);
        }
        finally
        {
            _queued.TryRemove(path, out _);
        }
    }

    private static string Stamp(string file)
    {
        var info = new FileInfo(file);
        return $"{info.Length}:{info.LastWriteTimeUtc.Ticks}";
    }

    private void Load()
    {
        if (Interlocked.Exchange(ref _loaded, 1) == 1 || string.IsNullOrWhiteSpace(options.Value.CachePath) || !File.Exists(options.Value.CachePath)) return;
        try
        {
            var saved = JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllBytes(options.Value.CachePath));
            foreach (var (path, entry) in saved ?? []) _entries.TryAdd(path, entry);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            logger.LogWarning(ex, "SFX cache {Path} is unreadable and will be rebuilt", options.Value.CachePath);
        }
    }

    private void Save()
    {
        var target = options.Value.CachePath;
        if (string.IsNullOrWhiteSpace(target)) return;
        try
        {
            if (Path.GetDirectoryName(Path.GetFullPath(target)) is { } dir) Directory.CreateDirectory(dir);
            var tmp = target + ".tmp";
            File.WriteAllBytes(tmp, JsonSerializer.SerializeToUtf8Bytes(_entries));
            File.Move(tmp, target, overwrite: true);
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Could not write SFX cache {Path}", target);
        }
    }
}
