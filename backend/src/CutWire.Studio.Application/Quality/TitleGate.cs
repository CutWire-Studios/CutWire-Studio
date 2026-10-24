using System.Text.RegularExpressions;
using static CutWire.Studio.Application.Quality.TextRules;

namespace CutWire.Studio.Application.Quality;

public sealed class TitleGate(ContentRulesOptions rules, IWordList words) : IMetadataGate
{
    private const string Field = "title";

    public Task<IReadOnlyList<GateViolation>> CheckAsync(SubmissionDraft draft, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<GateViolation>>(Check(draft.Title ?? ""));

    private List<GateViolation> Check(string title)
    {
        var found = new List<GateViolation>();
        if (title.Length == 0)
        {
            found.Add(V(Field, "required", "Title is required."));
            return found;
        }

        var bad = FirstNonAscii(title, allowNewline: false);
        if (bad >= 0)
            found.Add(V(Field, "non_ascii", "Title may only use printable ASCII characters.", ("index", bad), ("char", CodePoint(title, bad))));
        if (title.Length < rules.TitleMinLength)
            found.Add(V(Field, "too_short", $"Title must be at least {rules.TitleMinLength} characters.", ("min", rules.TitleMinLength)));
        if (title.Length > rules.TitleMaxLength)
            found.Add(V(Field, "too_long", $"Title must be at most {rules.TitleMaxLength} characters.", ("max", rules.TitleMaxLength)));
        if (title != title.Trim() || title.Contains("  "))
            found.Add(V(Field, "bad_whitespace", "Title must not have leading, trailing or doubled spaces."));

        var parts = title.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 && !IsDictionaryWord(parts.FirstOrDefault() ?? ""))
            found.Add(V(Field, "needs_words", $"Use at least two words, or one real word of {rules.TitleSingleWordMinLength}+ letters."));

        var letters = title.Where(char.IsAsciiLetter).ToArray();
        if (letters.Length >= rules.TitleAllCapsMinLetters && letters.All(char.IsAsciiLetterUpper))
            found.Add(V(Field, "all_caps", "Title must not be all capitals."));
        if (HasLink(title))
            found.Add(V(Field, "contains_url", "Title must not contain a URL or domain."));
        if (Regex.IsMatch(title, $"(.)\\1{{{rules.TitleMaxIdenticalRun},}}", RegexOptions.IgnoreCase))
            found.Add(V(Field, "repeated_chars", $"Title must not repeat a character more than {rules.TitleMaxIdenticalRun} times in a row."));
        if (Regex.IsMatch(title, $"([!-/:-@\\[-`{{-~])\\1{{{rules.TitleMaxPunctuationRun},}}"))
            found.Add(V(Field, "repeated_punctuation", $"Title must not repeat punctuation more than {rules.TitleMaxPunctuationRun} times in a row."));
        return found;
    }

    private bool IsDictionaryWord(string word)
    {
        var lower = word.ToLowerInvariant();
        return lower.Length >= rules.TitleSingleWordMinLength && lower.All(char.IsAsciiLetterLower) && EnglishGate.IsKnown(words, lower);
    }
}
