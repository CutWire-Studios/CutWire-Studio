using CutWire.Studio.Application.Stock;

namespace CutWire.Studio.Infrastructure.Stock.Providers;

/// <summary>Turns yt-dlp stderr into a <see cref="StockReason"/>. Order matters because the phrasing overlaps.</summary>
public static class YtdlpErrors
{
    // Checked before the bot-check list: "Sign in to confirm your age" shares a prefix with "Sign in to confirm you're not a bot".
    private static readonly string[] AgeGate =
    [
        "confirm your age",
        "age-restricted",
        "age restricted",
        "inappropriate for some users",
    ];

    private static readonly string[] Live =
    [
        "this live event will begin",
        "live stream recording is not available",
    ];

    // The extractor reached the site and was refused for who we are, not for what we asked.
    private static readonly string[] Blocked =
    [
        "confirm you're not a bot",
        "confirm you are not a bot",
        "sign in to confirm",
        "http error 429",
        "too many requests",
        "rate-limited",
        "rate limited",
        "your ip address is blocked",
        "failed to extract any player response",
    ];

    private static readonly string[] Unreachable =
    [
        "temporary failure in name resolution",
        "connection timed out",
        "connection refused",
        "network is unreachable",
        "read timed out",
        "the read operation timed out",
        "tunnel connection failed",
        "ssl:",
    ];

    private static readonly string[] GeoBlocked =
    [
        "not available in your country",
        "blocked it in your country",
        "not available from your location",
        "made this video available in your country",
        "geo restricted",
        "geo-restricted",
    ];

    private static readonly string[] NoMedia =
    [
        "no video could be found",
        "no media found",
        "there's no video",
        "unsupported url",
    ];

    private static readonly string[] Removed =
    [
        "video unavailable",
        "video is unavailable",
        "this video is not available",
        "video does not exist",
        "incomplete youtube id",
        "has been removed",
        "account has been terminated",
        "no longer available",
        "requested content is not available",
        "unable to extract shared data",
        "404: not found",
        "the page doesn't exist",
        "post not found",
    ];

    private static readonly string[] AuthWall =
    [
        "sign in",
        "login required",
        "log in",
        "private video",
        "members-only",
        "this video is available to this channel's members",
        "cookies",
    ];

    public static StockReason Reason(string stderr)
    {
        var lower = stderr.ToLowerInvariant();
        if (Has(lower, AgeGate)) return StockReason.ItemPrivate;
        if (Has(lower, Live)) return StockReason.LiveStream;
        if (Has(lower, Blocked)) return StockReason.SourceBlocked;
        if (Has(lower, Unreachable)) return StockReason.SourceUnreachable;
        if (Has(lower, GeoBlocked)) return StockReason.ItemGeoblocked;
        if (Has(lower, NoMedia)) return StockReason.LinkNoMedia;
        if (Has(lower, Removed)) return StockReason.ItemRemoved;
        if (Has(lower, AuthWall)) return StockReason.ItemPrivate;
        return StockReason.SourceError;
    }

    public static StockException Classify(string stderr, string fallback) =>
        new(Reason(stderr), "yt-dlp: " + Trim(string.IsNullOrWhiteSpace(stderr) ? fallback : stderr));

    private static bool Has(string lower, string[] phrases) => phrases.Any(lower.Contains);

    private static string Trim(string s)
    {
        s = s.Trim();
        return s.Length > 400 ? s[..400] : s;
    }
}
