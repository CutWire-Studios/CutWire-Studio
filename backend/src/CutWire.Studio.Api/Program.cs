using CutWire.Studio.Api.Endpoints;
using CutWire.Studio.Api.Http;
using CutWire.Studio.Api.Spa;
using CutWire.Studio.Application;
using CutWire.Studio.Infrastructure;
using CutWire.Studio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration)
    .AddStudioHttp(builder.Configuration);

var app = builder.Build();

if (app.Configuration.GetValue("Database:AutoMigrate", true))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<StudioDbContext>().Database.MigrateAsync();
}

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseRateLimiter();
app.UseAppHosts();

app.MapOpenApi("/api/v1/openapi.json");
var api = app.MapGroup("/api/v1").RequireRateLimiting(RateLimitingSettings.DefaultPolicy);
api.MapHealth();
app.MapFiles();

app.Run();

public partial class Program;
