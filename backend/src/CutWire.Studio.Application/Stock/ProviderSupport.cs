using System.Text;

namespace CutWire.Studio.Application.Stock;

/// <summary>Helpers shared by providers; behaviour ported from the marketplace providers/fetch.go.</summary>
public static class ProviderSupport
{
    /// <summary>Absolute http(s) URL, no userinfo, no control characters, at most 2048 chars.</summary>
    public static bool TryParseHttpUrl(string? raw, out Uri uri)
    {
        uri = null!;
        raw = raw?.Trim();
        if (string.IsNullOrEmpty(raw) || raw.Length > 2048 || raw.IndexOfAny(['\r', '\n', '\0', '\\']) >= 0) return false;
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var parsed)) return false;
        if (parsed.Scheme is not ("http" or "https") || string.IsNullOrEmpty(parsed.Host) || !string.IsNullOrEmpty(parsed.UserInfo)) return false;
        uri = parsed;
        return true;
    }

    /// <summary>Keeps a provider-supplied title from escaping the workspace.</summary>
    public static string SanitizeFileName(string? name)
    {
        var sb = new StringBuilder();
        foreach (var c in name ?? "")
            sb.Append(char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_');
        var result = sb.ToString().TrimStart('.');
        if (result.Length == 0) return "asset";
        return result.Length > 120 ? result[..120] : result;
    }

    /// <summary>A stable, safe download name from a title and extension.</summary>
    public static string FileName(string? title, string ext)
    {
        var baseName = SanitizeFileName(title);
        if (baseName == "asset") baseName = "download";
        return baseName.TrimEnd('.') + "." + ext.TrimStart('.');
    }

    /// <summary>Opaque cursor &lt;-&gt; 1-based page number (all keyed REST sources paginate by page).</summary>
    public static int Page(string? cursor) => int.TryParse(cursor, out var n) && n > 1 ? n : 1;

    public static string? NextCursor(int page, int returned, int limit) => returned < limit || returned == 0 ? null : (page + 1).ToString();

    public static int ClampLimit(int limit, int max, int fallback = 30) => limit <= 0 ? fallback : Math.Min(limit, max);

    /// <summary>Streams a URL into the workspace and returns the file path (all providers except the yt-dlp family).</summary>
    public static async Task<string> DownloadAsync(StockWorkspace ws, string url, string filename, CancellationToken ct)
    {
        if (!TryParseHttpUrl(url, out var uri))
            throw new StockException(StockReason.DownloadError, "refusing to fetch this url");

        HttpResponseMessage response;
        try
        {
            response = await ws.Http.SendAsync(new HttpRequestMessage(HttpMethod.Get, uri), HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new StockException(StockReason.SourceUnreachable, ex.Message, ex);
        }

        using (response)
        {
            if (response.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Gone)
                throw new StockException(StockReason.ItemRemoved, $"upstream status {(int)response.StatusCode}");
            if (response.StatusCode != System.Net.HttpStatusCode.OK)
                throw new StockException(StockReason.SourceError, $"upstream status {(int)response.StatusCode}");
            var total = response.Content.Headers.ContentLength ?? -1;
            if (total > ws.MaxBytes) throw new StockException(StockReason.FileTooLarge, "content-length exceeds the per-job cap");

            var path = Path.Combine(ws.Dir, SanitizeFileName(filename));
            await using var source = await response.Content.ReadAsStreamAsync(ct);
            await using var target = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
            var buffer = new byte[81920];
            long written = 0;
            int read;
            try
            {
                while ((read = await source.ReadAsync(buffer, ct)) > 0)
                {
                    written += read;
                    if (written > ws.MaxBytes) throw new StockException(StockReason.FileTooLarge, "body exceeded the per-job cap while streaming");
                    await target.WriteAsync(buffer.AsMemory(0, read), ct);
                    // Fetching is the first two thirds of a job; normalising is the rest.
                    if (total > 0) ws.Report((double)written / total * 0.66);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException)
            {
                throw new StockException(StockReason.SourceUnreachable, ex.Message, ex);
            }

            return path;
        }
    }
}
