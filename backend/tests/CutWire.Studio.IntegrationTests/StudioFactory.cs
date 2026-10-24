using CutWire.Studio.Application.Abstractions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CutWire.Studio.IntegrationTests;

public sealed class StudioFactory(string? connectionString = null, bool migrate = true, Action<IWebHostBuilder>? configure = null)
    : WebApplicationFactory<Program>
{
    public CapturingEmailSender Emails { get; } = new();

    public string BlobRoot { get; } = Directory.CreateTempSubdirectory("studio-blobs-").FullName;
    public string SpaRoot { get; } = Directory.CreateTempSubdirectory("studio-spa-").FullName;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.UseSetting("Storage:BlobRoot", BlobRoot);
        builder.UseSetting("Spa:Root", SpaRoot);
        builder.UseSetting("Jobs:Enabled", "false");
        builder.UseSetting("Database:AutoMigrate", (connectionString is not null && migrate).ToString());
        builder.UseSetting("Auth:OpenIddict:UseEphemeralKeys", "true");
        builder.UseSetting("RateLimiting:StrictPermitLimit", "1000");
        if (connectionString is not null) builder.UseSetting("ConnectionStrings:Default", connectionString);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Emails);
        });
        configure?.Invoke(builder);
    }

    public void WriteSpa(string relativePath, string content)
    {
        var path = Path.Combine(SpaRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;
        foreach (var dir in new[] { BlobRoot, SpaRoot })
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }
}
