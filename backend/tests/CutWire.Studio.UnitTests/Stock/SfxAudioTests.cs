using System.Security.Cryptography;
using CutWire.Studio.Application.Stock;
using CutWire.Studio.Infrastructure.Stock;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace CutWire.Studio.UnitTests.Stock;

public class SfxPeaksTests
{
    [Fact]
    public void Encodes64LogScaledBytesTheClientDecodes()
    {
        var samples = new short[128];
        samples[2] = 32767;
        samples[4] = -32768;
        samples[5] = 5;
        samples[6] = 16384;
        samples[7] = -100;
        samples[8] = 1024;
        samples[9] = 1;
        samples[10] = 1;

        var bytes = Convert.FromBase64String(SfxPeaks.Encode(samples));

        bytes.Length.ShouldBe(64);
        bytes.Take(6).ToArray().ShouldBe(new byte[] { 0, 255, 255, 223, 95, 0 });
        bytes.Skip(6).ShouldAllBe(b => b == 0);
    }

    [Fact]
    public void ShortInputStillYieldsFullLength()
    {
        Convert.FromBase64String(SfxPeaks.Encode([])).Length.ShouldBe(64);
        Convert.FromBase64String(SfxPeaks.Encode([short.MaxValue])).Length.ShouldBe(64);
    }
}

public class SfxSubcategoryTests
{
    private const string Json = """
        {"categories":[
          {"id":"ui","label":"UI","subcategories":[{"id":"click","label":"Clicks"},{"id":"beep","label":"Beeps"},{"id":"unused","label":"Unused"}]}],
         "sounds":[
          {"id":"a","name":"A","category":"ui","subcategory":"click","duration":1,"size":1,"path":"a.flac","preview":"a.webm","peaks":"AP8=","sha256":"ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789"},
          {"id":"b","name":"B","category":"ui","subcategory":"beep","duration":1,"size":1,"path":"b.flac","preview":"b.webm","peaks":"not base64!","sha256":"short"}]}
        """;

    [Fact]
    public void KeepsUsedSubcategoriesInIndexOrderAndTheirIds()
    {
        var ui = SfxIndex.Parse(Json, "v").Categories.Single();

        ui.Subcategories!.Select(s => (s.Id, s.Label)).ShouldBe([("click", "Clicks"), ("beep", "Beeps")]);
        ui.Sounds.Select(s => s.Subcategory).ShouldBe(["click", "beep"]);
    }

    [Fact]
    public void PassesValidPeaksAndHashThroughAndDropsMalformedOnes()
    {
        var sounds = SfxIndex.Parse(Json, "v").Categories.Single().Sounds;

        sounds[0].Peaks.ShouldBe("AP8=");
        sounds[0].Sha256.ShouldBe("abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789");
        sounds[1].Peaks.ShouldBeNull();
        sounds[1].Sha256.ShouldBeNull();
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData("ui", "ui", null)]
    [InlineData("ui/click", "ui", "click")]
    public void SplitsTheCategoryFilter(string? value, string? category, string? sub) =>
        Application.Marketplace.MarketplaceBrowse.SplitSfxCategory(value).ShouldBe((category, sub));
}

public class SfxAudioCacheTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("sfx-cache-").FullName;

    private sealed class CountingDecoder : ISfxPcmDecoder
    {
        public int Calls;

        public Task<short[]> DecodeAsync(string path, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(new short[] { 16384, 16384 });
        }
    }

    public void Dispose() => Directory.Delete(_dir, true);

    private SfxAudioCache Create(CountingDecoder decoder)
    {
        var options = Options.Create(new SfxOptions { Path = _dir, CachePath = Path.Combine(_dir, "cache", "peaks.json") });
        return new SfxAudioCache(new FileSfxSource(options, NullLogger<FileSfxSource>.Instance), options, decoder, NullLogger<SfxAudioCache>.Instance);
    }

    private static async Task<SfxAudioInfo> Ready(SfxAudioCache cache, string path)
    {
        for (var i = 0; i < 200; i++)
        {
            if (cache.TryGet(path) is { } info) return info;
            await Task.Delay(25);
        }

        throw new TimeoutException();
    }

    [Fact]
    public async Task QueuesMissingFilesThenServesPeaksAndHashFromThePersistedCache()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "library"));
        File.WriteAllText(Path.Combine(_dir, "library", "a.flac"), "audio");
        var decoder = new CountingDecoder();
        var first = Create(decoder);
        first.TryGet("a.flac").ShouldBeNull();
        first.TryGet("missing.flac").ShouldBeNull();
        await first.StartAsync(CancellationToken.None);

        var info = await Ready(first, "a.flac");
        await first.StopAsync(CancellationToken.None);

        info.Sha256.ShouldBe(Convert.ToHexStringLower(SHA256.HashData("audio"u8)));
        Convert.FromBase64String(info.Peaks).Length.ShouldBe(64);

        var second = Create(decoder);
        second.TryGet("a.flac").ShouldBe(info);
        decoder.Calls.ShouldBe(1);

        File.WriteAllText(Path.Combine(_dir, "library", "a.flac"), "changed audio");
        second.TryGet("a.flac").ShouldBeNull();
    }
}
