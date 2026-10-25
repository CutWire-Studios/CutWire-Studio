using System.Text.RegularExpressions;

namespace CutWire.Studio.Application.Quality;

internal static partial class TextRules
{
    public static GateViolation V(string field, string code, string message, params (string Key, object Value)[] parameters) =>
        new(field, code, message, parameters.Length == 0 ? null : parameters.ToDictionary(p => p.Key, p => p.Value));

    /// <summary>The link pattern backtracks quadratically on long dotted input; a timeout counts as "has links" so abuse is rejected, not slow.</summary>
    public static int CountLinks(string text)
    {
        try
        {
            return LinkPattern().Count(text);
        }
        catch (RegexMatchTimeoutException)
        {
            return int.MaxValue;
        }
    }

    public static bool HasLink(string text)
    {
        try
        {
            return LinkPattern().IsMatch(text);
        }
        catch (RegexMatchTimeoutException)
        {
            return true;
        }
    }

    public static int FirstNonAscii(string text, bool allowNewline)
    {
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c is >= ' ' and <= '~') continue;
            if (allowNewline && c == '\n') continue;
            return i;
        }

        return -1;
    }

    public static string CodePoint(string text, int index) =>
        $"U+{(char.IsSurrogatePair(text, index) ? char.ConvertToUtf32(text, index) : text[index]):X4}";

    public static string NormalizeLineEndings(string text) => text.Replace("\r\n", "\n");

    public static string NormalizeTitle(string title) =>
        string.Join(' ', NonAlnum().Split(title.ToLowerInvariant()).Where(s => s.Length > 0));

    public static IEnumerable<string> Words(string text) =>
        WordPattern().Matches(text).Select(m => m.Value.ToLowerInvariant());

    public const string LinkPatternSource = @"(https?://|ftp://|www\.)\S+|\b[a-z0-9][a-z0-9-]*(\.[a-z0-9-]+)*\.(com|net|org|io|co|me|ly|gg|xyz|app|dev|info|biz|tv|us|uk|ru|cn|top|site|online|shop|link|click|ai|to|cc|ws|vip|club|store)\b(/\S*)?";

    [GeneratedRegex(LinkPatternSource, RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 250)]
    private static partial Regex LinkPattern();

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlnum();

    [GeneratedRegex("[A-Za-z0-9]+(?:'[A-Za-z]+)?")]
    private static partial Regex WordPattern();
}
