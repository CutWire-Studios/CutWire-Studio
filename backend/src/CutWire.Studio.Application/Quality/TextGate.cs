using static CutWire.Studio.Application.Quality.TextRules;

namespace CutWire.Studio.Application.Quality;

public sealed class TextGate(ContentRulesOptions rules) : IContentGate
{
    public Task<IReadOnlyList<GateViolation>> CheckAsync(SubmissionDraft draft, CancellationToken ct)
    {
        var found = new List<GateViolation>();
        var summary = NormalizeLineEndings(draft.Summary ?? "");
        var description = NormalizeLineEndings(draft.Description ?? "");

        Check(found, "summary", summary, rules.SummaryMinLength, rules.SummaryMaxLength);
        if (HasLink(summary))
            found.Add(V("summary", "links_not_allowed", "Summary must not contain links."));

        Check(found, "description", description, rules.DescriptionMinLength, rules.DescriptionMaxLength);
        var links = CountLinks(description);
        if (links > rules.DescriptionMaxLinks)
            found.Add(V("description", "too_many_links", $"Description may contain at most {rules.DescriptionMaxLinks} links.", ("max", rules.DescriptionMaxLinks), ("count", links)));
        if (System.Text.RegularExpressions.Regex.IsMatch(description, $"(\\n[ \\t]*){{{rules.MaxConsecutiveBlankLines + 2},}}"))
            found.Add(V("description", "too_many_blank_lines", $"Description may have at most {rules.MaxConsecutiveBlankLines} blank lines in a row.", ("max", rules.MaxConsecutiveBlankLines)));
        return Task.FromResult<IReadOnlyList<GateViolation>>(found);
    }

    private static void Check(List<GateViolation> found, string field, string text, int min, int max)
    {
        if (text.Trim().Length == 0)
        {
            found.Add(V(field, "required", $"{Capitalize(field)} is required."));
            return;
        }

        var bad = FirstNonAscii(text, allowNewline: true);
        if (bad >= 0)
            found.Add(V(field, "non_ascii", $"{Capitalize(field)} may only use printable ASCII characters.", ("index", bad), ("char", CodePoint(text, bad))));
        if (text.Length < min)
            found.Add(V(field, "too_short", $"{Capitalize(field)} must be at least {min} characters.", ("min", min)));
        if (text.Length > max)
            found.Add(V(field, "too_long", $"{Capitalize(field)} must be at most {max} characters.", ("max", max)));
    }

    private static string Capitalize(string s) => char.ToUpperInvariant(s[0]) + s[1..];
}
