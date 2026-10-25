using System.Threading.RateLimiting;
using CutWire.Studio.Api.Spa;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

namespace CutWire.Studio.Api.Http;

public sealed class RateLimitingSettings
{
    public const string Section = "RateLimiting";
    public const string DefaultPolicy = "default";
    public const string StrictPolicy = "strict";
    public const string VotePolicy = "vote";
    public const string ReportPolicy = "report";
    public const string StockWritePolicy = "stock-write";
    public const string CspReportPolicy = "csp-report";

    public int PermitLimit { get; set; } = 120;
    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);
    public int StrictPermitLimit { get; set; } = 10;
    public TimeSpan StrictWindow { get; set; } = TimeSpan.FromMinutes(5);
    public int VotePermitLimit { get; set; } = 30;
    public TimeSpan VoteWindow { get; set; } = TimeSpan.FromMinutes(1);
    public int ReportPermitLimit { get; set; } = 10;
    public TimeSpan ReportWindow { get; set; } = TimeSpan.FromMinutes(10);
    public int StockWritePermitLimit { get; set; } = 30;
    public TimeSpan StockWriteWindow { get; set; } = TimeSpan.FromMinutes(1);
    /// <summary>Per IP and minute across /connect/device and /connect/verify: user codes are short enough to be worth guessing.</summary>
    public int DeviceFlowPermitLimit { get; set; } = 20;
    public int CspReportPermitLimit { get; set; } = 30;
    public TimeSpan CspReportWindow { get; set; } = TimeSpan.FromMinutes(1);
}

public static class HttpExtensions
{
    /// <summary>The largest legitimate body is an 8 MB Forge asset; nginx in front allows 16 MB.</summary>
    public const long MaxRequestBytes = 16 * 1024 * 1024;

    private static string UserOrIp(HttpContext http) =>
        http.User.Identity?.IsAuthenticated == true && http.User.FindFirst("sub")?.Value is { } sub
            ? "u:" + sub
            : "ip:" + (http.Connection.RemoteIpAddress?.ToString() ?? "unknown");

    /// <summary>Runs before authentication, which is where OpenIddict answers /connect/device and validates user codes, so the rate limiter middleware is too late.</summary>
    public static IApplicationBuilder UseDeviceFlowLimit(this IApplicationBuilder app) => app.Use(async (http, next) =>
    {
        if (http.Request.Path.StartsWithSegments("/connect/device") || http.Request.Path.StartsWithSegments("/connect/verify"))
        {
            var limit = http.RequestServices.GetRequiredService<IOptions<RateLimitingSettings>>().Value.DeviceFlowPermitLimit;
            if (!http.RequestServices.GetRequiredService<Endpoints.FixedWindowLimiter>().Take("device-flow:" + http.Connection.RemoteIpAddress, limit))
            {
                http.Response.Headers.RetryAfter = "60";
                await Domain.Common.Error.RateLimited("rate_limited", "Too many requests.", 60).ToHttpResult().ExecuteAsync(http);
                return;
            }
        }

        await next(http);
    });

    public static IServiceCollection AddStudioHttp(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
        {
            var problem = ctx.ProblemDetails;
            problem.Status ??= ctx.HttpContext.Response.StatusCode;
            problem.Extensions.TryAdd(ProblemMapping.CodeKey, ProblemMapping.DefaultCodeFor(problem.Status.Value));
        });

        services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            o.ForwardedForHeaderName = "CF-Connecting-IP";
            o.ForwardLimit = 1;
        });

        services.AddOptions<RateLimitingSettings>().Bind(configuration.GetSection(RateLimitingSettings.Section));
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy(RateLimitingSettings.DefaultPolicy, http =>
            {
                var settings = http.RequestServices.GetRequiredService<IOptions<RateLimitingSettings>>().Value;
                return RateLimitPartition.GetFixedWindowLimiter(
                    http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = settings.PermitLimit,
                        Window = settings.Window,
                        QueueLimit = 0,
                    });
            });
            o.AddPolicy(RateLimitingSettings.StrictPolicy, http =>
            {
                var settings = http.RequestServices.GetRequiredService<IOptions<RateLimitingSettings>>().Value;
                return RateLimitPartition.GetFixedWindowLimiter(
                    http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = settings.StrictPermitLimit,
                        Window = settings.StrictWindow,
                        QueueLimit = 0,
                    });
            });
            o.AddPolicy(RateLimitingSettings.VotePolicy, http =>
            {
                var settings = http.RequestServices.GetRequiredService<IOptions<RateLimitingSettings>>().Value;
                return RateLimitPartition.GetFixedWindowLimiter(UserOrIp(http), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = settings.VotePermitLimit,
                    Window = settings.VoteWindow,
                    QueueLimit = 0,
                });
            });
            o.AddPolicy(RateLimitingSettings.ReportPolicy, http =>
            {
                var settings = http.RequestServices.GetRequiredService<IOptions<RateLimitingSettings>>().Value;
                return RateLimitPartition.GetFixedWindowLimiter(UserOrIp(http), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = settings.ReportPermitLimit,
                    Window = settings.ReportWindow,
                    QueueLimit = 0,
                });
            });
            o.AddPolicy(RateLimitingSettings.StockWritePolicy, http =>
            {
                var settings = http.RequestServices.GetRequiredService<IOptions<RateLimitingSettings>>().Value;
                return RateLimitPartition.GetFixedWindowLimiter(UserOrIp(http), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = settings.StockWritePermitLimit,
                    Window = settings.StockWriteWindow,
                    QueueLimit = 0,
                });
            });
            o.AddPolicy(RateLimitingSettings.CspReportPolicy, http =>
            {
                var settings = http.RequestServices.GetRequiredService<IOptions<RateLimitingSettings>>().Value;
                return RateLimitPartition.GetFixedWindowLimiter(
                    http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = settings.CspReportPermitLimit,
                        Window = settings.CspReportWindow,
                        QueueLimit = 0,
                    });
            });
            o.OnRejected = async (ctx, ct) =>
            {
                if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry))
                    ctx.HttpContext.Response.Headers.RetryAfter = ((int)retry.TotalSeconds).ToString();
                await ctx.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new()
                {
                    HttpContext = ctx.HttpContext,
                    ProblemDetails = { Status = StatusCodes.Status429TooManyRequests, Detail = "Too many requests." },
                });
            };
        });

        services.AddOpenApi(o => o.AddDocumentTransformer((doc, ctx, _) =>
        {
            doc.Info = new OpenApiInfo { Title = "CutWire Studio API", Version = "v1" };
            var publicUrl = ctx.ApplicationServices.GetRequiredService<IConfiguration>()["PublicUrl"];
            if (!string.IsNullOrEmpty(publicUrl)) doc.Servers = [new OpenApiServer { Url = publicUrl }];
            return Task.CompletedTask;
        }).AddDocumentTransformer(OpenApiFixes.Apply));

        services.AddStudioSecurityHeaders(configuration);

        services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o => o.MultipartBodyLengthLimit = MaxRequestBytes);

        services.AddOptions<SpaOptions>().Bind(configuration.GetSection(SpaOptions.Section));

        return services;
    }
}
