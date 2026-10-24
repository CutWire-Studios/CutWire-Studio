namespace CutWire.Studio.Application.Quality;

public interface IWordList
{
    bool Contains(string lowerCaseWord);
}

public sealed class EmbeddedWordList : IWordList
{
    private readonly HashSet<string> _words = Resources.LoadLines("english-words.txt.gz");

    public bool Contains(string lowerCaseWord) => _words.Contains(lowerCaseWord);
}

public sealed class SetWordList(IEnumerable<string> words) : IWordList
{
    private readonly HashSet<string> _words = new(words, StringComparer.Ordinal);

    public bool Contains(string lowerCaseWord) => _words.Contains(lowerCaseWord);
}
