using System.IO.Compression;
using System.Text;
using System.Text.Json;
using CutWire.Studio.Application.Quality;
using CutWire.Studio.Domain.Catalog;
using Shouldly;

namespace CutWire.Studio.UnitTests;

internal static class RepoPaths
{
    public static string BackendRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "CutWire.Studio.slnx"))) return dir.FullName;
        throw new InvalidOperationException("backend root not found");
    }

    public static string ContentRules() => Path.GetFullPath(Path.Combine(BackendRoot(), "..", "web", "packages", "content-rules"));

    public static void WriteIfChanged(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (!File.Exists(path) || File.ReadAllText(path) != content) File.WriteAllText(path, content);
    }
}

public class RankingTests
{
    [Fact]
    public void Wilson_NoVotes_IsZero() => Ranking.Wilson(0, 0).ShouldBe(0);

    [Theory]
    [InlineData(1, 0, 0.2065)]
    [InlineData(10, 0, 0.7225)]
    [InlineData(100, 0, 0.9630)]
    [InlineData(80, 20, 0.7112)]
    [InlineData(0, 10, 0)]
    public void Wilson_MatchesReferenceValues(double up, double down, double expected) =>
        Ranking.Wilson(up, down).ShouldBe(expected, 0.001);

    [Fact]
    public void Wilson_PrefersMoreEvidenceAtSameRatio() =>
        Ranking.Wilson(90, 10).ShouldBeGreaterThan(Ranking.Wilson(9, 1));

    [Fact]
    public void Wilson_AcceptsWeightedVotes() =>
        Ranking.Wilson(0.5, 0).ShouldBeLessThan(Ranking.Wilson(1, 0));

    [Fact]
    public void Hot_HalvesEverySevenDays()
    {
        var published = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        Ranking.Hot(0.8, published, published).ShouldBe(0.8, 1e-12);
        Ranking.Hot(0.8, published, published.AddDays(7)).ShouldBe(0.4, 1e-12);
        Ranking.Hot(0.8, published, published.AddDays(14)).ShouldBe(0.2, 1e-12);
        Ranking.Hot(0.8, published, published.AddDays(-3)).ShouldBe(0.8, 1e-12);
    }

    [Fact]
    public void RecomputeScores_UsesTalliesAndPublicationTime()
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var item = Item.CreateDraft(Guid.NewGuid(), AssetKindId.Effect, "a-b", Guid.NewGuid(), "t", "s", "d", "MIT", "color", now);
        item.RecomputeScores(now);
        item.WilsonScore.ShouldBe(0);
        item.HotScore.ShouldBe(0);
    }
}

public class SemVersionTests
{
    [Theory]
    [InlineData("1.0.0", "1.0.1", -1)]
    [InlineData("1.10.0", "1.9.0", 1)]
    [InlineData("2.0.0", "2.0.0", 0)]
    [InlineData("1.0.0-beta", "1.0.0", -1)]
    [InlineData("1.0.0-beta.2", "1.0.0-beta.10", -1)]
    [InlineData("1.0.0-alpha", "1.0.0-beta", -1)]
    [InlineData("1.0.0+build1", "1.0.0+build2", 0)]
    public void Compare(string a, string b, int expected)
    {
        SemVersion.TryParse(a, out var left).ShouldBeTrue();
        SemVersion.TryParse(b, out var right).ShouldBeTrue();
        Math.Sign(left!.CompareTo(right)).ShouldBe(expected);
    }

    [Theory]
    [InlineData("1.0")]
    [InlineData("01.0.0")]
    [InlineData("v1.0.0")]
    public void Invalid(string value) => SemVersion.TryParse(value, out _).ShouldBeFalse();
}

public class BloomTests
{
    private static List<string> SourceWords()
    {
        using var reader = new StreamReader(new GZipStream(File.OpenRead(Path.Combine(RepoPaths.BackendRoot(), "tools", "BloomGen", "data", "english-words.txt.gz")), CompressionMode.Decompress));
        var words = new List<string>();
        while (reader.ReadLine() is { } line)
            if (line.Length > 0 && line.All(char.IsAsciiLetterLower)) words.Add(line);
        return words.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
    }

    [Fact]
    public void HashFunctions_MatchPublishedVectors()
    {
        BloomFilter.Fnv1a("hello"u8, 0).ShouldBe(0x4f9f2cabu);
        BloomFilter.Fnv1a(""u8, 0).ShouldBe(0x811C9DC5u);
        BloomFilter.Murmur3("hello"u8, 0).ShouldBe(0x248bfa47u);
        BloomFilter.Murmur3(""u8, 0).ShouldBe(0u);
        BloomFilter.Murmur3(""u8, 1).ShouldBe(0x514E28B7u);
        BloomFilter.Murmur3("The quick brown fox jumps over the lazy dog"u8, 0x9747b28c).ShouldBe(0x2FA826CDu);
    }

    [Fact]
    public void SmallFilter_HasNoFalseNegatives_AndRoundTrips()
    {
        var filter = BloomFilter.Build(["alpha", "beta", "gamma"]);
        var copy = BloomFilter.Parse(filter.ToBytes());

        foreach (var word in new[] { "alpha", "beta", "gamma" }) copy.MightContain(word).ShouldBeTrue();
        copy.MightContain("delta").ShouldBeFalse();
        copy.WordCount.ShouldBe(3u);
        copy.HashCount.ShouldBe((byte)7);
        (copy.BitCount % 8).ShouldBe(0u);
    }

    [Fact]
    public void Parse_RejectsBadHeaders()
    {
        var good = BloomFilter.Build(["alpha"]).ToBytes();
        Should.Throw<InvalidDataException>(() => BloomFilter.Parse(good[..10]));
        var badMagic = (byte[])good.Clone();
        badMagic[0] = (byte)'X';
        Should.Throw<InvalidDataException>(() => BloomFilter.Parse(badMagic));
        Should.Throw<InvalidDataException>(() => BloomFilter.Parse(good[..^1]));
    }

    [Fact]
    public void EmbeddedFilter_IsDeterministicBuildOfSourceList()
    {
        var rebuilt = BloomFilter.Build(SourceWords()).ToBytes();
        var embedded = new BloomWordList().Bytes;
        rebuilt.ShouldBe(embedded);
    }

    [Fact]
    public void EmbeddedFilter_ContainsEverySourceWord()
    {
        var list = new BloomWordList();
        SourceWords().Where(w => !list.Contains(w)).ShouldBeEmpty();
    }

    [Fact]
    public void EmbeddedFilter_FalsePositiveRate_IsAboutOnePercent()
    {
        var list = new BloomWordList();
        var source = SourceWords().ToHashSet();
        var random = new Random(12345);
        int tested = 0, hits = 0;
        while (tested < 200_000)
        {
            var word = new string(Enumerable.Range(0, random.Next(7, 14)).Select(_ => (char)('a' + random.Next(26))).ToArray());
            if (source.Contains(word)) continue;
            tested++;
            if (list.Contains(word)) hits++;
        }

        var rate = (double)hits / tested;
        Console.WriteLine($"bloom words={list.Filter.WordCount} m={list.Filter.BitCount} k={list.Filter.HashCount} bytes={list.Bytes.Length} measuredFPR={rate:P3}");
        rate.ShouldBeInRange(0.005, 0.015);
    }

    [Fact]
    public void WritesCrossLanguageVectors()
    {
        var list = new BloomWordList();
        var words = new[] { "glow", "dream", "shader", "colour", "color", "the", "analyse", "analyze", "effects", "running", "quickly", "bigger",
            "flies", "tried", "zzzqx", "qwxzv", "asdfghjk", "vhs", "glsl", "xkcdz", "" , "a" };
        var vectors = words.Select(w =>
        {
            var bytes = Encoding.UTF8.GetBytes(w);
            var (h1, h2) = BloomFilter.Hashes(bytes, list.Filter.Seed);
            return new
            {
                word = w,
                h1,
                h2,
                positions = BloomFilter.Positions(bytes, list.Filter.BitCount, list.Filter.HashCount, list.Filter.Seed).ToArray(),
                member = list.Filter.MightContain(bytes),
            };
        }).ToArray();
        var tokens = new[] { "glows", "glowing", "tried", "flies", "bigger", "biggest", "quickly", "boxes", "making", "stopped", "ties", "is", "bus", "ed", "xyz" };
        var normalization = tokens.Select(t => new { token = t, candidates = EnglishGate.Candidates(t).ToArray(), known = EnglishGate.IsKnown(list, t) }).ToArray();

        var json = JsonSerializer.Serialize(new
        {
            sha256 = list.Sha256,
            bitCount = list.Filter.BitCount,
            hashCount = list.Filter.HashCount,
            seed = list.Filter.Seed,
            wordCount = list.Filter.WordCount,
            vectors,
            normalization,
        }, new JsonSerializerOptions { WriteIndented = true }) + "\n";
        RepoPaths.WriteIfChanged(Path.Combine(RepoPaths.ContentRules(), "test-fixtures", "bloom-vectors.json"), json);
    }
}
