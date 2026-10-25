using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CutWire.Studio.Application.Stock;
using Microsoft.Extensions.Options;

namespace CutWire.Studio.Infrastructure.Stock;

public sealed class FfmpegOptions
{
    public const string Section = "Stock:Ffmpeg";

    public string Path { get; set; } = "ffmpeg";
    public string ProbePath { get; set; } = "ffprobe";

    /// <summary>"wav" or "aac".</summary>
    public string AudioFormat { get; set; } = "wav";
}

/// <summary>
/// Turns whatever a provider produced into the containers clients expect: mp4 video, wav or aac audio, jpeg/png/webp images. Re-encoding
/// is avoided where possible: most stock video is already H.264 in MP4, so the common path is a remux that only moves the moov atom to
/// the front for progressive playback.
/// </summary>
public sealed class FfmpegNormalizer(IOptions<FfmpegOptions> options) : IMediaNormalizer
{
    private static readonly Dictionary<string, string> PassthroughImages = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".png"] = "image/png", [".webp"] = "image/webp",
    };

    private readonly FfmpegOptions _o = options.Value;

    public async Task<NormalizedMedia> NormalizeAsync(ProviderAsset asset, string dir, CancellationToken ct)
    {
        var source = RegularFileWithin(dir, asset.Path);
        var (path, mime) = asset.Mime switch
        {
            var m when m.StartsWith("image/", StringComparison.Ordinal) => await ImageAsync(source, dir, ct),
            var m when m.StartsWith("audio/", StringComparison.Ordinal) => await AudioAsync(source, dir, ct),
            _ => await VideoAsync(source, dir, ct),
        };

        var info = new FileInfo(path);
        await using var stream = File.OpenRead(path);
        var sha = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, ct));
        var name = string.IsNullOrEmpty(asset.Filename) ? "download" : asset.Filename;
        return new NormalizedMedia(path, mime, System.IO.Path.ChangeExtension(name, System.IO.Path.GetExtension(path)), info.Length, sha);
    }

    /// <summary>ffmpeg treats some names as protocols, and a path outside the workspace would be an arbitrary file read into the blob store.</summary>
    internal static string RegularFileWithin(string dir, string name)
    {
        if (string.IsNullOrEmpty(name) || name.Contains('\0')) throw new StockException(StockReason.DownloadError, "path escaped the workspace");
        var root = System.IO.Path.GetFullPath(dir).TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
        var full = System.IO.Path.GetFullPath(name);
        var info = new FileInfo(full);
        if (!full.StartsWith(root, StringComparison.Ordinal) || !info.Exists || info.LinkTarget is not null)
            throw new StockException(StockReason.DownloadError, "path escaped the workspace");
        return full;
    }

    private async Task<(string, string)> ImageAsync(string source, string dir, CancellationToken ct)
    {
        if (PassthroughImages.TryGetValue(System.IO.Path.GetExtension(source), out var mime)) return (source, mime);
        var output = System.IO.Path.Combine(dir, "normalized.png");
        await FfmpegAsync(["-i", source, "-frames:v", "1", output], ct);
        return (output, "image/png");
    }

    private async Task<(string, string)> AudioAsync(string source, string dir, CancellationToken ct)
    {
        if (_o.AudioFormat == "aac")
        {
            var aac = System.IO.Path.Combine(dir, "normalized.m4a");
            await FfmpegAsync(["-i", source, "-vn", "-c:a", "aac", "-b:a", "192k", "-movflags", "+faststart", aac], ct);
            return (aac, "audio/aac");
        }

        var wav = System.IO.Path.Combine(dir, "normalized.wav");
        await FfmpegAsync(["-i", source, "-vn", "-c:a", "pcm_s16le", "-ar", "48000", "-ac", "2", wav], ct);
        return (wav, "audio/wav");
    }

    private async Task<(string, string)> VideoAsync(string source, string dir, CancellationToken ct)
    {
        var output = System.IO.Path.Combine(dir, "normalized.mp4");
        if (await RemuxableAsync(source, ct))
        {
            try
            {
                await FfmpegAsync(["-i", source, "-c", "copy", "-movflags", "+faststart", output], ct);
                return (output, "video/mp4");
            }
            catch (StockException)
            {
                // A stream copy can still fail on an odd container; fall through to a full transcode.
                File.Delete(output);
            }
        }

        await FfmpegAsync(["-i", source, "-c:v", "libx264", "-preset", "veryfast", "-crf", "20", "-pix_fmt", "yuv420p",
            "-c:a", "aac", "-b:a", "192k", "-movflags", "+faststart", output], ct);
        return (output, "video/mp4");
    }

    private async Task<bool> RemuxableAsync(string path, CancellationToken ct)
    {
        var result = await RunAsync(_o.ProbePath, ["-v", "quiet", "-protocol_whitelist", "file,crypto", "-print_format", "json", "-show_streams", "-show_format", path], 1 << 20, ct);
        if (result.ExitCode != 0) return false;
        try
        {
            using var doc = JsonDocument.Parse(result.Stdout);
            var format = doc.RootElement.TryGetProperty("format", out var f) && f.TryGetProperty("format_name", out var n) ? n.GetString() ?? "" : "";
            if (!format.Contains("mp4") && !format.Contains("mov")) return false;
            var video = false;
            foreach (var s in doc.RootElement.GetProperty("streams").EnumerateArray())
            {
                var codec = s.TryGetProperty("codec_name", out var c) ? c.GetString() : null;
                switch (s.GetProperty("codec_type").GetString())
                {
                    case "video":
                        var pix = s.TryGetProperty("pix_fmt", out var p) ? p.GetString() : null;
                        if (codec != "h264" || (!string.IsNullOrEmpty(pix) && pix != "yuv420p")) return false;
                        video = true;
                        break;
                    case "audio":
                        if (codec != "aac") return false;
                        break;
                }
            }

            return video;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return false;
        }
    }

    private async Task FfmpegAsync(IEnumerable<string> args, CancellationToken ct)
    {
        // file,crypto only: a downloaded file is attacker-controlled bytes, and ffmpeg follows http, concat and subfile references inside it
        // unless the protocol list is closed first.
        string[] full = ["-nostdin", "-y", "-loglevel", "error", "-protocol_whitelist", "file,crypto", .. args];
        var result = await RunAsync(_o.Path, full, 8 << 10, ct);
        if (result.ExitCode != 0)
            throw new StockException(StockReason.DownloadError, $"ffmpeg exited {result.ExitCode}: {result.Stderr.Trim()}");
    }

    private sealed record ProcessResult(int ExitCode, string Stdout, string Stderr);

    private static async Task<ProcessResult> RunAsync(string exe, IEnumerable<string> args, int maxCapture, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        Process process;
        try
        {
            process = Process.Start(psi) ?? throw new StockException(StockReason.DownloadError, "could not start " + exe);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new StockException(StockReason.DownloadError, $"could not start {exe}: {ex.Message}", ex);
        }

        using (process)
        {
            process.StandardInput.Close();
            var stdout = Capture(process.StandardOutput, maxCapture);
            var stderr = Capture(process.StandardError, maxCapture);
            try
            {
                await process.WaitForExitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                }

                throw;
            }

            return new ProcessResult(process.ExitCode, await stdout, await stderr);
        }
    }

    private static async Task<string> Capture(StreamReader reader, int max)
    {
        var sb = new StringBuilder();
        var buffer = new char[4096];
        int read;
        while ((read = await reader.ReadAsync(buffer)) > 0)
            if (sb.Length < max) sb.Append(buffer, 0, Math.Min(read, max - sb.Length));
        return sb.ToString();
    }
}
