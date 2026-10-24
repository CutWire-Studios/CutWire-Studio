namespace CutWire.Studio.Application.Quality;

/// <summary>
/// Drops words that suffix normalisation already reaches from a shorter kept word (for example "glowing" when "glow" is kept), so the
/// Bloom filter holds stems only. Every candidate is strictly shorter than the word, so processing by ascending length and checking
/// against the kept set guarantees that lookup-with-normalisation still accepts every input word.
/// </summary>
public static class WordListPruner
{
    public static List<string> Prune(IEnumerable<string> words)
    {
        var kept = new HashSet<string>(StringComparer.Ordinal);
        foreach (var word in words.Distinct(StringComparer.Ordinal).OrderBy(w => w.Length).ThenBy(w => w, StringComparer.Ordinal))
            if (!EnglishGate.Candidates(word).Skip(1).Any(kept.Contains))
                kept.Add(word);
        return kept.Order(StringComparer.Ordinal).ToList();
    }

    /// <summary>The words that <see cref="EnglishGate.IsKnown"/> rejects against <paramref name="list"/>; empty when pruning preserved membership.</summary>
    public static List<string> Rejected(IEnumerable<string> original, IWordList list) =>
        original.Where(w => !EnglishGate.IsKnown(list, w)).ToList();
}

public sealed class BloomFilterWordList(BloomFilter filter) : IWordList
{
    public bool Contains(string lowerCaseWord) => filter.MightContain(lowerCaseWord);
}
