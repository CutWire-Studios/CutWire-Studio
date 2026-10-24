using CutWire.Studio.Api.Auth;
using CutWire.Studio.Api.Endpoints;
using CutWire.Studio.Api.Http;
using CutWire.Studio.Api.Spa;
using CutWire.Studio.Application;
using CutWire.Studio.Infrastructure;
using CutWire.Studio.Infrastructure.Identity;
using CutWire.Studio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration, builder.Environment)
    .AddStudioHttp(builder.Configuration)
    .AddStudioApiAuth();

var app = builder.Build();

if (app.Configuration.GetValue("Database:AutoMigrate", true))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<StudioDbContext>().Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<AuthSeeder>().SeedAsync();
}

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseStudioCsrf();
app.UseRateLimiter();
app.UseAppHosts();
app.UseAuthentication();
app.UseAuthorization();

app.MapOpenApi("/api/v1/openapi.json");
var api = app.MapGroup("/api/v1").RequireRateLimiting(RateLimitingSettings.DefaultPolicy);
api.MapHealth();
api.MapAccount();
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

app.Run();

public partial class Program;
