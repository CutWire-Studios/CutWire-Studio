using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CutWire.Studio.Application.Catalog;
using CutWire.Studio.Domain.Common;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace CutWire.Studio.Api.Http;

public static class CatalogHttp
{
    public const string InstallHeader = "X-Studio-Install";

    public static Viewer ViewerOf(this HttpContext http)
    {
        if (http.User.Identity?.IsAuthenticated != true || !Guid.TryParse(http.User.FindFirstValue(Claims.Subject), out var id))
            return Viewer.Anonymous;
        return new Viewer(id, http.User.IsInRole("moderator") || http.User.IsInRole("admin"));
    }

    public static string? InstallIdOf(this HttpContext http)
    {
        if (http.DriftClientOf() is { } drift) return "c:" + drift.ClientId;
        var value = http.Request.Headers[InstallHeader].ToString().Trim();
        return value.Length is > 0 and <= 64 && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.') ? value : null;
    }

    public static string IpHashOf(this HttpContext http, IConfiguration configuration)
    {
        var key = configuration["Downloads:IpHashKey"] is { Length: > 0 } k ? k : "cutwire-studio-download-ip-hash";
        var ip = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(ip)));
    }

    /// <summary>Returns the payload with a weak content ETag, or 304 when the client already has it.</summary>
    public static IResult JsonWithEtag<T>(this HttpContext http, T value)
    {
        var options = http.RequestServices.GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>().Value.SerializerOptions;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, options);
        var etag = $"W/\"{Convert.ToHexStringLower(SHA256.HashData(bytes))[..32]}\"";
        var personalised = http.User.Identity?.IsAuthenticated == true || http.Request.Headers.ContainsKey(HeaderNames.Authorization);

        http.Response.Headers.ETag = etag;
        http.Response.Headers.Vary = "Cookie, Authorization";
        http.Response.Headers.CacheControl = personalised ? "private, no-cache" : "public, max-age=30, must-revalidate";
        var match = http.Request.Headers.IfNoneMatch;
        if (match.Count > 0 && (match.ToString() == "*" || match.Any(m => m!.Split(',').Select(t => t.Trim()).Contains(etag))))
            return Results.StatusCode(StatusCodes.Status304NotModified);
        return Results.Bytes(bytes, "application/json");
    }

    public static string? SafeDownloadName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var sb = new StringBuilder();
        foreach (var c in name)
        {
            if (char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-') sb.Append(c);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }

        var clean = sb.ToString().Trim('.', '-');
        if (clean.Length > 100) clean = clean[..100].TrimEnd('.', '-');
        return clean.Length == 0 ? null : clean;
    }

    /// <summary>Reads an upload as a raw body or the first file of a multipart form. Fields are returned for multipart.</summary>
    public static async Task<Result<(Stream Content, IFormCollection? Form)>> ReadUploadAsync(this HttpContext http, CancellationToken ct)
    {
        if (!http.Request.HasFormContentType) return (http.Request.Body, null);

        var form = await http.Request.ReadFormAsync(ct);
        var file = form.Files.FirstOrDefault();
        return file is null
            ? Error.Validation("upload.missing_file", "The multipart form has no file.", [new FieldViolation("file", "required", "A file is required.")])
            : (file.OpenReadStream(), form);
    }
}
