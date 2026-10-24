using System.Reflection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;

namespace CutWire.Studio.Api.Spa;

public static class SpaHosting
{
    private const string EmbeddedManifest = "Microsoft.Extensions.FileProviders.Embedded.Manifest.xml";
    private const string EmbeddedRoot = "spa";

    public static void UseAppHosts(this WebApplication app, IReadOnlyList<AppHost>? hosts = null)
    {
        var dirProvider = ResolveProvider(app.Environment, app.Services.GetRequiredService<IOptions<SpaOptions>>().Value);
        var logger = app.Logger;
        if (dirProvider is null)
        {
            logger.LogWarning("No SPA files found; app hosts are disabled");
            return;
        }

        foreach (var host in hosts ?? AppHostRegistry.Default)
        {
            var files = dirProvider(host.Dir);
            if (files is null)
            {
                logger.LogWarning("SPA directory '{Dir}' for {Prefix} not found; skipping", host.Dir, host.Prefix);
                continue;
            }

            if (host.IsRoot)
                app.UseWhen(ctx => !IsReserved(ctx.Request.Path), b => Configure(b, files, host));
            else
                app.Map(host.Prefix, b => Configure(b, files, host));
        }
    }

    private static bool IsReserved(PathString path) =>
        AppHostRegistry.ReservedPrefixes.Any(p => path.StartsWithSegments(p));

    private static void Configure(IApplicationBuilder branch, IFileProvider files, AppHost host)
    {
        branch.Use(async (ctx, next) =>
        {
            if (host.Headers is not null)
                foreach (var (name, value) in host.Headers)
                    ctx.Response.Headers[name] = value;

            var isRead = HttpMethods.IsGet(ctx.Request.Method) || HttpMethods.IsHead(ctx.Request.Method);
            if (isRead && !Path.HasExtension(ctx.Request.Path.Value))
                ctx.Request.Path = "/index.html";

            await next();
        });

        branch.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = files,
            OnPrepareResponse = ctx =>
            {
                var headers = ctx.Context.Response.Headers;
                headers.CacheControl = ctx.File.Name == "index.html"
                    ? "no-cache"
                    : ctx.Context.Request.Path.StartsWithSegments("/assets")
                        ? "public, max-age=31536000, immutable"
                        : "public, max-age=3600";
            },
        });
    }

    private static Func<string, IFileProvider?>? ResolveProvider(IWebHostEnvironment env, SpaOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.Root))
            return Physical(Path.GetFullPath(options.Root));

        if (env.IsDevelopment() && FindDevDist(env.ContentRootPath) is { } dist)
            return Physical(dist);

        var assembly = Assembly.GetExecutingAssembly();
        if (assembly.GetManifestResourceNames().Contains(EmbeddedManifest))
        {
            return dir =>
            {
                try
                {
                    var provider = new ManifestEmbeddedFileProvider(assembly, $"{EmbeddedRoot}/{dir}");
                    return provider.GetFileInfo("index.html").Exists ? provider : null;
                }
                catch (InvalidOperationException)
                {
                    return null;
                }
            };
        }

        return FindDevDist(env.ContentRootPath) is { } fallback ? Physical(fallback) : null;
    }

    private static Func<string, IFileProvider?> Physical(string root) => dir =>
    {
        var path = Path.Combine(root, dir);
        return Directory.Exists(path) ? new PhysicalFileProvider(path) : null;
    };

    private static string? FindDevDist(string start)
    {
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "web", "dist");
            if (Directory.Exists(candidate)) return candidate;
        }

        return null;
    }
}
