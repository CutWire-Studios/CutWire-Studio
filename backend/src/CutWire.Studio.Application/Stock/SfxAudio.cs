namespace CutWire.Studio.Application.Stock;

public sealed record SfxAudioInfo(string Peaks, string Sha256);

/// <summary>
/// Waveform peaks and content hash for library files the index has none for. Never waits for the work: a file that is not ready yet
/// yields null and is queued, so the next listing has it.
/// </summary>
public interface ISfxAudio
{
    SfxAudioInfo? TryGet(string libraryPath);
}

public static class SfxAudioExtensions
{
    /// <summary>The index's own peaks and hash where it has them, the cache's for what is missing; null for what is not ready.</summary>
    public static (string? Peaks, string? Sha256) Lookup(this ISfxAudio audio, SfxSoundData sound)
    {
        if (sound is { Peaks: not null, Sha256: not null }) return (sound.Peaks, sound.Sha256);
        var computed = audio.TryGet(sound.LibraryPath);
        return (sound.Peaks ?? computed?.Peaks, sound.Sha256 ?? computed?.Sha256);
    }
}

public interface ISfxPcmDecoder
{
    /// <summary>The file as mono signed 16-bit samples at <see cref="SfxPeaks.SampleRate"/>.</summary>
    Task<short[]> DecodeAsync(string path, CancellationToken ct);
}

/// <summary>The peak format the Drift client decodes: <see cref="Count"/> unsigned bytes, base64, each the loudest sample of its slice on a 48 dB log scale (255 is full scale).</summary>
public static class SfxPeaks
{
    public const int Count = 64;
    public const int SampleRate = 8000;
    private const double RangeDb = 48.0;

    public static string Encode(ReadOnlySpan<short> samples)
    {
        var bytes = new byte[Count];
        var n = samples.Length;
        for (var i = 0; i < Count; i++)
        {
            var from = (int)((long)i * n / Count);
            var to = Math.Max((int)((long)(i + 1) * n / Count), from + 1);
            var peak = 0;
            for (var j = from; j < Math.Min(to, n); j++) peak = Math.Max(peak, Math.Abs((int)samples[j]));
            if (peak == 0) continue;
            var level = 1 + 20 * Math.Log10(peak / 32768.0) / RangeDb;
            bytes[i] = (byte)Math.Round(Math.Clamp(level, 0, 1) * 255);
        }

        return Convert.ToBase64String(bytes);
    }
}
