using System.Text;
using System.Text.Json;
using CutWire.Studio.Api.Spa;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace CutWire.Studio.Api.Http;

/// <summary>
/// Per-app overrides. Keys are <see cref="AppHost.Dir"/> values (shell, forge, themes, edit), one per SPA prefix. A directive value replaces
/// the default one; <c>~</c> removes the directive.
/// </summary>
public sealed class AppSecurityOptions
{
    public Dictionary<string, string> Csp { get; set; } = [];

    public string? PermissionsPolicy { get; set; }
}

public sealed class SecurityHeadersOptions
{
    public const string Section = "SecurityHeaders";
    public const string RemoveDirective = "~";
    public const string ReportGroup = "csp-endpoint";

    public bool Enabled { get; set; } = true;

    /// <summary>Sends Content-Security-Policy-Report-Only instead of the enforcing header, for every route class.</summary>
    public bool CspReportOnly { get; set; }

    /// <summary>Where browsers post violations. Empty disables reporting.</summary>
    public string ReportUri { get; set; } = CspReportEndpoint.Path;

    /// <summary>Null means on outside Development (it breaks plain-http dev servers).</summary>
    public bool? UpgradeInsecureRequests { get; set; }

    /// <summary>Null means on outside Development. Only sent on https requests.</summary>
    public bool? Hsts { get; set; }

    public TimeSpan HstsMaxAge { get; set; } = TimeSpan.FromDays(365);
    public bool HstsIncludeSubDomains { get; set; } = true;
    public bool HstsPreload { get; set; }

    public string ReferrerPolicy { get; set; } = "strict-origin-when-cross-origin";
    public string PermissionsPolicy { get; set; } =
        "accelerometer=(), ambient-light-sensor=(), bluetooth=(), camera=(), display-capture=(), geolocation=(), gyroscope=(), hid=(), magnetometer=(), microphone=(), midi=(), payment=(), serial=(), usb=(), xr-spatial-tracking=()";
    public string CrossOriginOpenerPolicy { get; set; } = "same-origin";
    public string CrossOriginResourcePolicy { get; set; } = "same-origin";

    /// <summary>/files/{sha256} is public, content-addressed and immutable, so other origins may embed it.</summary>
    public string FilesCrossOriginResourcePolicy { get; set; } = "cross-origin";

    /// <summary>/api, /connect, /mcp and /.well-known: JSON and redirects only.</summary>
    public string ApiCsp { get; set; } = "default-src 'none'; frame-ancestors 'none'";

    /// <summary>/files and the stock byte routes: user and provider supplied bytes, never a document.</summary>
    public string BytesCsp { get; set; } = "default-src 'none'; sandbox";

    /// <summary>Overrides on the default SPA policy, for every app.</summary>
    public Dictionary<string, string> SpaCsp { get; set; } = [];

    public Dictionary<string, AppSecurityOptions> Apps { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public enum RouteClass
{
    Spa,
    Api,
    Bytes,
}

/// <summary>Builds the CSP and Permissions-Policy strings once at startup.</summary>
public sealed class SecurityPolicies
{
    private static readonly string[] LocalHttp = ["http://localhost:*", "http://127.0.0.1:*", "http://[::1]:*"];

    private readonly Dictionary<string, string> _csp = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _permissions = new(StringComparer.OrdinalIgnoreCase);

    public SecurityPolicies(SecurityHeadersOptions options, IReadOnlyList<AppHost> hosts, string? imageHostOrigin, bool development)
    {
        foreach (var host in hosts)
        {
            var directives = Defaults(imageHostOrigin);
            // Forge: zstd/audio WebAssembly, and bring-your-own-key AI calls to Anthropic, OpenAI or a user-chosen (often local) server.
            if (host.Dir == "forge")
            {
                directives["script-src"] = "'self' 'wasm-unsafe-eval'";
                directives["connect-src"] = $"'self' https: {string.Join(' ', LocalHttp)}";
            }

            Apply(directives, options.SpaCsp);
            options.Apps.TryGetValue(host.Dir, out var app);
            if (app is not null) Apply(directives, app.Csp);

            var upgrade = options.UpgradeInsecureRequests ?? !development;
            if (upgrade) directives["upgrade-insecure-requests"] = "";
            if (!string.IsNullOrWhiteSpace(options.ReportUri))
            {
                directives["report-uri"] = options.ReportUri;
                directives["report-to"] = SecurityHeadersOptions.ReportGroup;
            }

            _csp[host.Dir] = Format(directives);
            _permissions[host.Dir] = app?.PermissionsPolicy
                ?? (host.Dir == "forge" ? options.PermissionsPolicy.Replace("microphone=()", "microphone=(self)") : options.PermissionsPolicy);
        }
    }

    public string Spa(AppHost host) => _csp[host.Dir];

    public string Permissions(AppHost host) => _permissions[host.Dir];

    private static Dictionary<string, string> Defaults(string? imageHostOrigin)
    {
        var images = imageHostOrigin is null ? "" : " " + imageHostOrigin;
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["default-src"] = "'self'",
            ["script-src"] = "'self'",
            ["style-src"] = "'self'",
            // Radix (react-remove-scroll-bar) and Sonner append <style> elements at runtime and Sonner cannot take a nonce.
            // React style props go through the CSSOM, so inline style attributes stay blocked.
            ["style-src-elem"] = "'self' 'unsafe-inline'",
            ["img-src"] = "'self' data: blob:" + images,
            ["media-src"] = "'self' blob:" + images,
            ["connect-src"] = "'self'",
            ["font-src"] = "'self'",
            ["worker-src"] = "'self'",
            ["manifest-src"] = "'self'",
            ["frame-src"] = "'none'",
            ["frame-ancestors"] = "'none'",
            ["base-uri"] = "'none'",
            ["form-action"] = "'self'",
            ["object-src"] = "'none'",
        };
    }

    private static void Apply(Dictionary<string, string> target, Dictionary<string, string> overrides)
    {
        foreach (var (name, value) in overrides)
        {
            if (value == SecurityHeadersOptions.RemoveDirective) target.Remove(name);
            else target[name] = value;
        }
    }

    private static string Format(Dictionary<string, string> directives) =>
        string.Join("; ", directives.Select(d => d.Value.Length == 0 ? d.Key : $"{d.Key} {d.Value}"));
}

public sealed class SecurityHeadersMiddleware(
    RequestDelegate next,
    IOptions<SecurityHeadersOptions> options,
    SecurityPolicies policies,
    IReadOnlyList<AppHost> hosts,
    IHostEnvironment environment)
{
    private static readonly string[] BytesPrefixes = ["/files", "/api/v1/stock/files", "/api/v1/stock/media", "/api/v1/stock/sfx/files"];
    private static readonly string[] ApiPrefixes = ["/api", "/connect", "/mcp", "/.well-known"];

    private readonly SecurityHeadersOptions _o = options.Value;

    public static RouteClass Classify(PathString path)
    {
        if (BytesPrefixes.Any(p => path.StartsWithSegments(p))) return RouteClass.Bytes;
        return ApiPrefixes.Any(p => path.StartsWithSegments(p)) ? RouteClass.Api : RouteClass.Spa;
    }

    public static bool IsInlineSafe(string? contentType)
    {
        var type = contentType?.Split(';')[0].Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(type) || type.Contains("svg", StringComparison.Ordinal)) return false;
        return type.StartsWith("image/", StringComparison.Ordinal) || type.StartsWith("audio/", StringComparison.Ordinal)
               || type.StartsWith("video/", StringComparison.Ordinal);
    }

    public Task InvokeAsync(HttpContext context)
    {
        if (!_o.Enabled) return next(context);

        var path = context.Request.Path;
        var routeClass = Classify(path);
        var host = routeClass == RouteClass.Spa ? HostFor(path) : null;
        context.Response.OnStarting(() =>
        {
            Apply(context, routeClass, host);
            return Task.CompletedTask;
        });
        return next(context);
    }

    private AppHost HostFor(PathString path) =>
        hosts.FirstOrDefault(h => !h.IsRoot && path.StartsWithSegments(h.Prefix)) ?? hosts.First(h => h.IsRoot);

    private void Apply(HttpContext context, RouteClass routeClass, AppHost? host)
    {
        var response = context.Response;
        var headers = response.Headers;
        var path = context.Request.Path;

        var csp = routeClass switch
        {
            RouteClass.Spa => policies.Spa(host!),
            RouteClass.Api => WithReporting(_o.ApiCsp),
            _ => WithReporting(_o.BytesCsp),
        };
        headers[_o.CspReportOnly ? "Content-Security-Policy-Report-Only" : HeaderNames.ContentSecurityPolicy] = csp;
        if (!string.IsNullOrWhiteSpace(_o.ReportUri)) headers["Reporting-Endpoints"] = $"{SecurityHeadersOptions.ReportGroup}=\"{_o.ReportUri}\"";

        headers.XContentTypeOptions = "nosniff";
        headers["Referrer-Policy"] = _o.ReferrerPolicy;
        headers["Permissions-Policy"] = routeClass == RouteClass.Spa ? policies.Permissions(host!) : _o.PermissionsPolicy;
        if (routeClass != RouteClass.Bytes) headers.XFrameOptions = "DENY";

        SetIfAbsent(headers, "Cross-Origin-Opener-Policy", _o.CrossOriginOpenerPolicy);
        SetIfAbsent(headers, "Cross-Origin-Resource-Policy",
            path.StartsWithSegments("/files") ? _o.FilesCrossOriginResourcePolicy : _o.CrossOriginResourcePolicy);

        if (_o.Hsts ?? !environment.IsDevelopment())
            if (context.Request.IsHttps)
                headers.StrictTransportSecurity = $"max-age={(long)_o.HstsMaxAge.TotalSeconds}" +
                    (_o.HstsIncludeSubDomains ? "; includeSubDomains" : "") + (_o.HstsPreload ? "; preload" : "");

        if (routeClass == RouteClass.Bytes && response.StatusCode is StatusCodes.Status200OK or StatusCodes.Status206PartialContent)
            ForceAttachmentUnlessPassiveMedia(response);
    }

    private string WithReporting(string policy) =>
        string.IsNullOrWhiteSpace(_o.ReportUri) ? policy : $"{policy}; report-uri {_o.ReportUri}; report-to {SecurityHeadersOptions.ReportGroup}";

    private static void SetIfAbsent(IHeaderDictionary headers, string name, string value)
    {
        if (!string.IsNullOrEmpty(value) && !headers.ContainsKey(name)) headers[name] = value;
    }

    private static void ForceAttachmentUnlessPassiveMedia(HttpResponse response)
    {
        if (IsInlineSafe(response.ContentType)) return;
        var current = response.Headers.ContentDisposition.ToString();
        if (current.StartsWith("attachment", StringComparison.OrdinalIgnoreCase)) return;
        response.Headers.ContentDisposition = current.StartsWith("inline", StringComparison.OrdinalIgnoreCase)
            ? "attachment" + current["inline".Length..]
            : "attachment";
    }
}

public static class SecurityHeadersExtensions
{
    public static IServiceCollection AddStudioSecurityHeaders(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SecurityHeadersOptions>().Bind(configuration.GetSection(SecurityHeadersOptions.Section));
        services.AddSingleton(sp =>
        {
            var imgproxy = configuration["Stock:Media:Imgproxy:Prefix"];
            return new SecurityPolicies(
                sp.GetRequiredService<IOptions<SecurityHeadersOptions>>().Value,
                AppHostRegistry.Default,
                Uri.TryCreate(imgproxy, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? uri.GetLeftPart(UriPartial.Authority) : null,
                sp.GetRequiredService<IHostEnvironment>().IsDevelopment());
        });
        services.AddSingleton<IReadOnlyList<AppHost>>(AppHostRegistry.Default);
        return services;
    }

    public static IApplicationBuilder UseStudioSecurityHeaders(this IApplicationBuilder app) => app.UseMiddleware<SecurityHeadersMiddleware>();
}

/// <summary>Browsers post CSP violations here (<c>report-uri</c> and the Reporting API). Anonymous, rate limited, log only.</summary>
public static class CspReportEndpoint
{
    public const string Path = "/api/v1/csp-report";
    public const int MaxBodyBytes = 16 * 1024;
    private const int MaxField = 200;
    private static readonly string[] Fields =
    [
        "document-uri", "documentURL", "blocked-uri", "blockedURL", "violated-directive", "effective-directive", "effectiveDirective",
        "disposition", "source-file", "sourceFile", "line-number", "lineNumber", "status-code", "statusCode",
    ];

    public static void MapCspReport(this RouteGroupBuilder api) =>
        api.MapPost("/csp-report", async (HttpContext http, ILoggerFactory loggers) =>
            {
                var logger = loggers.CreateLogger("CutWire.Studio.Csp");
                http.Features.Get<IHttpMaxRequestBodySizeFeature>()?.MaxRequestBodySize = MaxBodyBytes;
                try
                {
                    using var body = new MemoryStream();
                    var buffer = new byte[4096];
                    int read;
                    while ((read = await http.Request.Body.ReadAsync(buffer, http.RequestAborted)) > 0)
                    {
                        if (body.Length + read > MaxBodyBytes) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
                        body.Write(buffer, 0, read);
                    }

                    using var doc = JsonDocument.Parse(body.ToArray());
                    foreach (var report in Reports(doc.RootElement).Take(10))
                        logger.LogWarning("CSP violation: {Report}", Summarize(report));
                }
                catch (JsonException)
                {
                }

                return Results.NoContent();
            })
            .WithName("CspReport")
            .RequireRateLimiting(RateLimitingSettings.CspReportPolicy)
            .ExcludeFromDescription();

    private static IEnumerable<JsonElement> Reports(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
                if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("body", out var body) && body.ValueKind == JsonValueKind.Object)
                    yield return body;
        }
        else if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("csp-report", out var legacy) && legacy.ValueKind == JsonValueKind.Object)
        {
            yield return legacy;
        }
    }

    /// <summary>One line of bounded, control-character-free key=value pairs: report content is attacker-controlled.</summary>
    public static string Summarize(JsonElement report)
    {
        var sb = new StringBuilder();
        foreach (var name in Fields)
        {
            if (!report.TryGetProperty(name, out var value) || value.ValueKind is JsonValueKind.Object or JsonValueKind.Array) continue;
            var text = value.ToString();
            var clean = new string(text.Where(c => !char.IsControl(c)).Take(MaxField).ToArray());
            sb.Append(name).Append('=').Append(clean).Append(' ');
        }

        return sb.ToString().TrimEnd();
    }
}
