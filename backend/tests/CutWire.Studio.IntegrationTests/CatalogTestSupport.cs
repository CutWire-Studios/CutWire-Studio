using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using CutWire.Studio.Infrastructure.Persistence;
using CutWire.Studio.UnitTests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using SkiaSharp;

namespace CutWire.Studio.IntegrationTests;

public static class HttpExtensions
{
    public static Task<HttpResponseMessage> PatchAsJsonAsync(this HttpClient client, string url, object body, CancellationToken ct) =>
        client.PatchAsync(url, System.Net.Http.Json.JsonContent.Create(body), ct);
}

public sealed class CatalogFixture : IDisposable
{
    public StudioFactory Factory { get; }

    public CatalogFixture(PostgresFixture postgres, Action<Microsoft.AspNetCore.Hosting.IWebHostBuilder>? configure = null)
    {
        var builder = new NpgsqlConnectionStringBuilder(postgres.ConnectionString);
        builder.Database = $"cat_{Guid.NewGuid():N}";
        using (var admin = new NpgsqlConnection(postgres.ConnectionString))
        {
            admin.Open();
            using var create = new NpgsqlCommand($"create database {builder.Database}", admin);
            create.ExecuteNonQuery();
        }

        Factory = new StudioFactory(builder.ConnectionString, configure: configure);
    }

    public CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => Factory.Dispose();

    public async Task<(HttpClient Client, string Handle, Guid UserId)> User(string trust = "T3")
    {
        var handle = AuthTestSupport.NewHandle();
        var client = await Factory.SignedIn(Ct, handle: handle);
        var me = await (await client.GetAsync("/api/v1/me", Ct)).Json(Ct);
        var id = Guid.Parse(me["id"]!.GetValue<string>());
        await Sql($"update user_profiles set trust_level = '{trust}' where user_id = '{id}'");
        return (client, handle, id);
    }

    public async Task Sql(string sql)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<StudioDbContext>().Database.ExecuteSqlRawAsync(sql, Ct);
    }

    public async Task<T> Db<T>(Func<StudioDbContext, Task<T>> query)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<StudioDbContext>());
    }

    public static ByteArrayContent Bytes(byte[] data, string type = "application/octet-stream")
    {
        var content = new ByteArrayContent(data);
        content.Headers.ContentType = new MediaTypeHeaderValue(type);
        return content;
    }

    public static MultipartFormDataContent Multipart(byte[] data, string fileName, params (string Key, string Value)[] fields)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(data);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);
        foreach (var (key, value) in fields) form.Add(new StringContent(value), key);
        return form;
    }

    public static object Metadata(string title, string kind = "effect", string[]? tags = null, string category = "dreamy", string? description = null) => new
    {
        kind,
        title,
        summary = Drafts.Summary,
        description = description ?? Drafts.Description,
        category,
        tags = tags ?? ["glow", "dream"],
        license = "CC0-1.0",
    };

    public async Task<JsonNode> Create(HttpClient client, string title, string kind = "effect", string[]? tags = null, string category = "dreamy", string? description = null)
    {
        var response = await client.PostJson("/api/v1/items", Metadata(title, kind, tags, category, description), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return await response.Json(Ct);
    }

    public async Task<JsonNode> Ok(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, text);
        return JsonNode.Parse(text)!;
    }

    public Task<HttpResponseMessage> PutPackage(HttpClient client, string id, byte[] package) =>
        client.PutAsync($"/api/v1/items/{id}/package", Bytes(package), Ct);

    public Task<HttpResponseMessage> PutMedia(HttpClient client, string id, string role, byte[] image) =>
        client.PutAsync($"/api/v1/items/{id}/media/{role}", Bytes(image, "image/webp"), Ct);

    public Task<HttpResponseMessage> Publish(HttpClient client, string id) => client.PostJson($"/api/v1/items/{id}/publish", null, Ct);

    /// <summary>Creates, uploads and publishes an effect with a unique package and thumbnail unless overridden.</summary>
    public async Task<JsonNode> PublishEffect(HttpClient client, string title, string[]? tags = null, string category = "dreamy",
        string? description = null, byte[]? package = null, byte[]? thumbnail = null)
    {
        var item = await Create(client, title, "effect", tags, category, description);
        var id = item["id"]!.GetValue<string>();
        await Ok(await PutPackage(client, id, package ?? UniqueEffect()));
        await Ok(await PutMedia(client, id, "thumbnail", thumbnail ?? Thumb(512, 288, Random.Shared.Next())));
        return await Ok(await Publish(client, id));
    }

    public static byte[] UniqueEffect(string version = "1.0.0")
    {
        var builder = DriftfxBuilder.ValidEffect();
        builder.Manifest["description"] = Guid.NewGuid().ToString("N");
        builder.Manifest["version"] = version;
        return builder.Build();
    }

    public static byte[] Thumb(int width, int height, int seed)
    {
        var random = new Random(seed);
        var data = Images.Webp(width, height, (canvas, w, h) =>
        {
            const int block = 32;
            for (var y = 0; y < h; y += block)
                for (var x = 0; x < w; x += block)
                {
                    using var paint = new SKPaint { Color = new SKColor((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256)) };
                    canvas.DrawRect(x, y, block, block, paint);
                }
        }, quality: 50);
        return data;
    }

    public static List<string> Violations(JsonNode problem) =>
        problem["violations"]!.AsArray().Select(v => $"{v!["field"]}:{v["code"]}").ToList();
}
