using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CutWire.Studio.Domain.Catalog;
using CutWire.Studio.Domain.Stock;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace CutWire.Studio.Api.Http;

public sealed class DriftClientOptions
{
    public const string Section = "DriftClient";

    /// <summary>Accepted HMAC keys, used as their UTF-8 bytes. Several allow rotation; none disables verification of signed requests.</summary>
    public string[] Keys { get; set; } = [];

    public int MaxSkewSeconds { get; set; } = 300;
}

public sealed record DriftClient(string ClientId, SemVersion? AppVersion, string? Platform, string Distribution);

public sealed record MarketplaceCaller(string Distribution, SemVersion? AppVersion, string? ClientId, bool IsWeb);

public static partial class DriftClientAuth
{
    public const string SignatureHeader = "X-Cutwire-Signature";
    public const string ClientHeader = "X-Cutwire-Client";
    public const string TimestampHeader = "X-Cutwire-Timestamp";
    public const string NonceHeader = "X-Cutwire-Nonce";
    public const string AppHeader = "X-Cutwire-App";
    public const string WebDistribution = "web";

    private static readonly object ClientKey = new();
    private static readonly object CallerKey = new();

    [GeneratedRegex(@"^Drift/(\S+) \(([^)]+)\)$")]
    private static partial Regex AppPattern();

    public static IServiceCollection AddDriftClientAuth(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DriftClientOptions>().Bind(configuration.GetSection(DriftClientOptions.Section));
        services.AddMemoryCache();
        return services;
    }

    public static DriftClient? DriftClientOf(this HttpContext http) => http.Items[ClientKey] as DriftClient;

    public static MarketplaceCaller? MarketplaceCallerOf(this HttpContext http) => http.Items[CallerKey] as MarketplaceCaller;

    public static string SignatureFor(string key, string timestamp, string nonce, string clientId, string method, string target, byte[] body) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(key),
            Encoding.UTF8.GetBytes($"{timestamp}\n{nonce}\n{clientId}\n{method}\n{target}\n{Convert.ToHexStringLower(SHA256.HashData(body))}")));

    public static IApplicationBuilder UseDriftClientSignature(this IApplicationBuilder app) => app.Use(async (http, next) =>
    {
        if (!http.Request.Headers.TryGetValue(SignatureHeader, out var signature))
        {
            await next(http);
            return;
        }

        var failure = await VerifyAsync(http, signature.ToString());
        if (failure is not null)
        {
            await failure.ExecuteAsync(http);
            return;
        }

        await next(http);
    });

    /// <summary>Returns null and records the <see cref="DriftClient"/> when the request is correctly signed, otherwise the 401 to send.</summary>
    public static async Task<IResult?> VerifyAsync(HttpContext http, string signature)
    {
        var options = http.RequestServices.GetRequiredService<IOptions<DriftClientOptions>>().Value;
        var cache = http.RequestServices.GetRequiredService<IMemoryCache>();
        var headers = http.Request.Headers;
        var clientId = headers[ClientHeader].ToString();
        var timestamp = headers[TimestampHeader].ToString();
        var nonce = headers[NonceHeader].ToString();

        if (clientId.Length != 64 || !clientId.All(Uri.IsHexDigit)
            || nonce.Length is < 8 or > 64 || !nonce.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
            || !long.TryParse(timestamp, out var seconds)
            || options.Keys.Length == 0)
            return Invalid("Missing or malformed client signature.");

        if (Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - seconds) > options.MaxSkewSeconds)
        {
            http.Response.Headers.Date = DateTime.UtcNow.ToString("R");
            return Invalid("The request timestamp is outside the accepted window.");
        }

        var request = http.Request;
        request.EnableBuffering(MaxBuffered, HttpExtensions.MaxRequestBytes);
        byte[] bodyHash;
        try
        {
            bodyHash = await SHA256.HashDataAsync(request.Body, http.RequestAborted);
        }
        catch (InvalidOperationException)
        {
            return Invalid("The request body is too large to verify.");
        }

        request.Body.Position = 0;

        var raw = http.Features.Get<IHttpRequestFeature>()?.RawTarget;
        var target = string.IsNullOrEmpty(raw) ? request.GetEncodedPathAndQuery() : raw;
        var message = Encoding.UTF8.GetBytes($"{timestamp}\n{nonce}\n{clientId}\n{request.Method}\n{target}\n{Convert.ToHexStringLower(bodyHash)}");
        var presented = Encoding.ASCII.GetBytes(signature.Trim().ToLowerInvariant());
        var matched = false;
        foreach (var key in options.Keys)
        {
            var expected = Encoding.ASCII.GetBytes(Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), message)));
            matched |= CryptographicOperations.FixedTimeEquals(expected, presented);
        }

        if (!matched) return Invalid("The client signature is invalid.");

        if (cache.TryGetValue("drift-nonce:" + clientId + "|" + nonce, out _)) return Invalid("The request nonce was already used.");
        cache.Set("drift-nonce:" + clientId + "|" + nonce, true, TimeSpan.FromSeconds(options.MaxSkewSeconds * 2));

        SemVersion? version = null;
        string? platform = null;
        if (AppPattern().Match(headers[AppHeader].ToString().Trim()) is { Success: true } m)
        {
            if (SemVersion.TryParse(m.Groups[1].Value, out var parsed)) version = parsed;
            platform = m.Groups[2].Value;
        }

        var distribution = headers[Distributions.Header].ToString().Trim().ToLowerInvariant();
        http.Items[ClientKey] = new DriftClient(clientId, version, platform, distribution.Length == 0 ? Distributions.Unknown : distribution);
        return null;
    }

    private const int MaxBuffered = 64 * 1024;

    private static IResult Invalid(string detail)
    {
        var problem = new ProblemDetails { Status = StatusCodes.Status401Unauthorized, Detail = detail };
        problem.Extensions[ProblemMapping.CodeKey] = "invalid_client";
        return Results.Problem(problem);
    }

    /// <summary>Accepts a signed Drift client, or the web app (same-origin fetch) as distribution <c>web</c>. The caller is available through <see cref="MarketplaceCallerOf"/>.</summary>
    public static TBuilder RequireMarketplaceCaller<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            if (http.DriftClientOf() is { } client)
                http.Items[CallerKey] = new MarketplaceCaller(client.Distribution, client.AppVersion, client.ClientId, IsWeb: false);
            else if (http.Request.Headers["Sec-Fetch-Site"].ToString() == "same-origin")
                http.Items[CallerKey] = new MarketplaceCaller(WebDistribution, null, null, IsWeb: true);
            else
                return Invalid("A signed Drift client or the web app is required.");
            return await next(context);
        });
}
