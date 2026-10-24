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

    public int PermitLimit { get; set; } = 120;
    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);
}

public static class HttpExtensions
{
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
        }));

        services.AddOptions<SpaOptions>().Bind(configuration.GetSection(SpaOptions.Section));

        return services;
    }
}
