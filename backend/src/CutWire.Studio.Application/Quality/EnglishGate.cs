using static CutWire.Studio.Application.Quality.TextRules;

namespace CutWire.Studio.Application.Quality;

public sealed class EnglishGate(ContentRulesOptions rules, IWordList words) : IContentGate
{
    private static readonly HashSet<string> TechTerms = Resources.LoadLines("tech-terms.txt");

    public Task<IReadOnlyList<GateViolation>> CheckAsync(SubmissionDraft draft, CancellationToken ct)
    {
        var result = new List<GateViolation>();
        var text = $"{draft.Summary}\n{draft.Description}";

        var ignored = new HashSet<string>(TechTerms);
        foreach (var tag in draft.Tags ?? [])
            foreach (var word in Words(tag)) ignored.Add(word);
        var description = new HashSet<string>(Words(draft.Description ?? ""));
        foreach (var word in Words(draft.Title ?? ""))
            if (description.Contains(word)) ignored.Add(word);

        var considered = 0;
        var known = 0;
        foreach (var token in Words(text))
        {
            if (token.Any(char.IsAsciiDigit) || token.Contains('\'') || ignored.Contains(token)) continue;
            considered++;
            if (IsKnown(words, token)) known++;
        }

        if (considered < rules.EnglishMinTokens) return Task.FromResult<IReadOnlyList<GateViolation>>(result);
        var ratio = (double)known / considered;
        if (ratio < rules.EnglishMinRatio)
            result.Add(V("description", "not_english", "Summary and description must be written in English.",
                ("ratio", Math.Round(ratio, 3)), ("min", rules.EnglishMinRatio), ("tokens", considered)));
        return Task.FromResult<IReadOnlyList<GateViolation>>(result);
    }

    public static bool IsKnown(IWordList words, string word)
    {
        foreach (var candidate in Candidates(word))
            if (words.Contains(candidate)) return true;
        return false;
    }

    private static IEnumerable<string> Candidates(string w)
    {
        yield return w;
        if (w.Length > 3 && w.EndsWith("ies")) yield return w[..^3] + "y";
        if (w.Length > 3 && w.EndsWith("ied")) yield return w[..^3] + "y";
        foreach (var suffix in new[] { "s", "es", "ed", "ing", "ly", "er", "est" })
        {
            if (w.Length <= suffix.Length + 2 || !w.EndsWith(suffix)) continue;
            var stem = w[..^suffix.Length];
            yield return stem;
            if (suffix is "ed" or "ing" or "er" or "est")
            {
                yield return stem + "e";
                if (stem.Length > 2 && stem[^1] == stem[^2]) yield return stem[..^1];
            }
        }
    }
}
