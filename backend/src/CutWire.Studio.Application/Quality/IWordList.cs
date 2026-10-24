namespace CutWire.Studio.Application.Quality;

public interface IWordList
{
    bool Contains(string lowerCaseWord);
}

/// <summary>The embedded english.bloom word list. Membership is probabilistic (about 1% false positives, no false negatives).</summary>
public sealed class BloomWordList : IWordList
{
    private readonly BloomFilter _filter;

    public BloomWordList()
    {
        using var stream = Resources.Open("english.bloom");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        Bytes = buffer.ToArray();
        _filter = BloomFilter.Parse(Bytes);
        Sha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Bytes));
    }

    public byte[] Bytes { get; }
    public string Sha256 { get; }
    public string Hash8 => Sha256[..8];
    public string UrlPath => $"/api/v1/meta/english-{Hash8}.bloom";
    public BloomFilter Filter => _filter;

    public bool Contains(string lowerCaseWord) => _filter.MightContain(lowerCaseWord);
}

public sealed class SetWordList(IEnumerable<string> words) : IWordList
{
    private readonly HashSet<string> _words = new(words, StringComparer.Ordinal);

    public bool Contains(string lowerCaseWord) => _words.Contains(lowerCaseWord);
}
