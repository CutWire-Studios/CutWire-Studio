using CutWire.Studio.Api.Http;
using CutWire.Studio.Domain.Common;
using CutWire.Studio.Infrastructure.Identity;
using Microsoft.Extensions.Options;

namespace CutWire.Studio.Api.Auth;

public sealed class CsrfMiddleware(RequestDelegate next, PublicUrl publicUrl, IOptions<AuthOptions> options)
{
    public const string HeaderName = "X-Requested-With";

    public async Task InvokeAsync(HttpContext context)
    {
        if (RequiresCheck(context.Request))
        {
            if (!context.Request.Headers.ContainsKey(HeaderName))
            {
                await Reject(context, "csrf_header_required", $"The {HeaderName} header is required.");
                return;
            }

            if (context.Request.Headers.Origin.ToString() is { Length: > 0 } origin && !IsAllowedOrigin(origin))
            {
                await Reject(context, "csrf_origin", "Cross-origin requests are not allowed.");
                return;
            }
        }

        await next(context);
    }

    private static bool RequiresCheck(HttpRequest request) =>
        !HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method) &&
        !HttpMethods.IsOptions(request.Method) && !HttpMethods.IsTrace(request.Method) &&
        !request.Headers.ContainsKey("Authorization") &&
        // Browsers send violation reports themselves and cannot add a custom header; the endpoint only logs.
        !request.Path.StartsWithSegments(CspReportEndpoint.Path) &&
        (request.Path.StartsWithSegments("/api/v1") || request.Path.StartsWithSegments("/connect/verify"));

    private bool IsAllowedOrigin(string origin) =>
        string.Equals(origin, publicUrl.Origin, StringComparison.OrdinalIgnoreCase) ||
        options.Value.AllowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase);

    private static Task Reject(HttpContext context, string code, string message) =>
        Error.Forbidden(code, message).ToHttpResult().ExecuteAsync(context);
}
