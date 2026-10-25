using CutWire.Studio.Api.Auth;
using CutWire.Studio.Api.Endpoints;
using CutWire.Studio.Api.Http;
using CutWire.Studio.Api.Mcp;
using CutWire.Studio.Api.Spa;
using CutWire.Studio.Application;
using CutWire.Studio.Infrastructure;
using CutWire.Studio.Infrastructure.Identity;
using CutWire.Studio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

ProductionGuard.Validate(builder.Configuration, builder.Environment);

builder.WebHost.ConfigureKestrel(o =>
{
    o.AddServerHeader = false;
    o.Limits.MaxRequestBodySize = HttpExtensions.MaxRequestBytes;
});

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration, builder.Environment)
    .AddStudioHttp(builder.Configuration)
    .AddStudioApiAuth();

builder.Services.AddSingleton(new FixedWindowLimiter(TimeSpan.FromMinutes(1)));

builder.Services.AddStudioMcp();

var app = builder.Build();

if (app.Configuration.GetValue("Database:AutoMigrate", true))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<StudioDbContext>().Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<AuthSeeder>().SeedAsync();
    await scope.ServiceProvider.GetRequiredService<CutWire.Studio.Infrastructure.Catalog.CatalogSeeder>().SeedAsync();
}

app.UseForwardedHeaders();
app.UseStudioSecurityHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseStudioCsrf();
app.UseDeviceFlowLimit();
app.UseAppHosts();
app.UseMcpChallenge();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapOpenApi("/api/v1/openapi.json");
var api = app.MapGroup("/api/v1").RequireRateLimiting(RateLimitingSettings.DefaultPolicy);
api.MapHealth();
api.MapCspReport();
api.MapAccount();
api.MapItems();
api.MapUsers();
api.MapMyItems();
api.MapModeration();
api.MapMeta();
api.MapForge();
api.MapForgeAi();
api.MapThemeDrafts();
api.MapStock();
api.MapStockMedia();
var account = api.MapGroup("/account");
account.MapProfile();
account.MapTwoFactor();
account.MapPasskeys();
account.MapExternalLogins();
account.MapTokens();
account.MapConnectedApps();
account.MapConsent();
foreach (var contributor in app.Services.GetServices<IApiContributor>()) contributor.Map(api);
app.MapConnect();
app.MapWellKnown();
app.MapFiles();
app.MapStudioMcp();

app.Run();

public partial class Program;
