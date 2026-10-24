using static CutWire.Studio.Application.Quality.TextRules;

namespace CutWire.Studio.Application.Quality;

public sealed class ProfanityGate : IMetadataGate
{
    public static IReadOnlyCollection<string> Terms => Denylist;

    private static readonly HashSet<string> Denylist = Resources.LoadLines("profanity.txt");

    public Task<IReadOnlyList<GateViolation>> CheckAsync(SubmissionDraft draft, CancellationToken ct)
    {
        var found = new List<GateViolation>();
        Scan(found, "title", draft.Title);
        Scan(found, "summary", draft.Summary);
        Scan(found, "description", draft.Description);
        Scan(found, "tags", string.Join(' ', draft.Tags ?? []));
        return Task.FromResult<IReadOnlyList<GateViolation>>(found);
    }

    public static bool ContainsProfanity(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        foreach (var token in WordTokens(text))
            if (Denylist.Contains(Deleet(token))) return true;
        return false;
    }

    private static void Scan(List<GateViolation> found, string field, string? text)
    {
        if (ContainsProfanity(text))
            found.Add(V(field, "profanity", "This text contains language that is not allowed."));
    }

    private static IEnumerable<string> WordTokens(string text)
    {
        var start = -1;
        for (var i = 0; i <= text.Length; i++)
        {
            var inWord = i < text.Length && (char.IsAsciiLetterOrDigit(text[i]) || text[i] is '@' or '$');
            if (inWord && start < 0) start = i;
            else if (!inWord && start >= 0)
            {
                yield return text[start..i];
                start = -1;
            }
        }
    }

    private static string Deleet(string token) => string.Create(token.Length, token, static (span, t) =>
    {
        for (var i = 0; i < t.Length; i++)
            span[i] = char.ToLowerInvariant(t[i]) switch
            {
                '0' => 'o',
                '1' => 'i',
                '3' => 'e',
                '4' => 'a',
                '5' => 's',
                '@' => 'a',
                '$' => 's',
                var c => c,
            };
    });
}
