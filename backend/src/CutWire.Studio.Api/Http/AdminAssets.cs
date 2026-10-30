using System.Reflection;

namespace CutWire.Studio.Api.Http;

public static class AdminAssets
{
    public static void MapAdminAssets(this IEndpointRouteBuilder app) =>
        app.MapGet("/admin/admin.css", (HttpContext http) =>
            {
                http.Response.Headers.CacheControl = "public, max-age=300";
                return Results.Stream(Assembly.GetExecutingAssembly().GetManifestResourceStream("admin.css")!, "text/css; charset=utf-8");
            })
            .AllowAnonymous()
            .ExcludeFromDescription();
}
