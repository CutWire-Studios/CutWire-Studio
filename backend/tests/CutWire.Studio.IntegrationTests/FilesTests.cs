using System.Net;
using System.Net.Http.Headers;
using CutWire.Studio.Application.Abstractions;
using CutWire.Studio.Application.Blobs;
using CutWire.Studio.Domain.Blobs;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace CutWire.Studio.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class FilesTests(PostgresFixture postgres) : IDisposable
{
    private const string Content = "0123456789abcdef";

    private readonly StudioFactory _factory = new(postgres.ConnectionString);
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _factory.Dispose();

    private async Task<StoreBlobResult> Seed(string content = Content, string mime = "text/plain")
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<StoreBlobCommand, StoreBlobResult>>();
        var result = await handler.Handle(new StoreBlobCommand(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content)), mime, 1024), Ct);
        return result.Value;
    }

    [Fact]
    public async Task Get_ServesImmutableWithEtagAndMime()
    {
        var blob = await Seed();

        var response = await _factory.CreateClient().GetAsync($"/files/{blob.Hash}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl!.ToString().ShouldBe("public, max-age=31536000, immutable");
        response.Headers.ETag!.Tag.ShouldBe($"\"{blob.Hash}\"");
        response.Headers.AcceptRanges.ShouldContain("bytes");
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/plain");
        (await response.Content.ReadAsStringAsync(Ct)).ShouldBe(Content);
    }

    [Fact]
    public async Task Get_WithRange_Returns206Slice()
    {
        var blob = await Seed();
        var request = new HttpRequestMessage(HttpMethod.Get, $"/files/{blob.Hash}")
        {
            Headers = { Range = new RangeHeaderValue(2, 5) },
        };

        var response = await _factory.CreateClient().SendAsync(request, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.PartialContent);
        response.Content.Headers.ContentRange!.ToString().ShouldBe("bytes 2-5/16");
        (await response.Content.ReadAsStringAsync(Ct)).ShouldBe("2345");
        response.Headers.CacheControl!.Immutable().ShouldBeTrue();
    }

    [Fact]
    public async Task Get_WithMatchingEtag_Returns304()
    {
        var blob = await Seed();
        var request = new HttpRequestMessage(HttpMethod.Get, $"/files/{blob.Hash}");
        request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue($"\"{blob.Hash}\""));

        var response = await _factory.CreateClient().SendAsync(request, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotModified);
    }

    [Fact]
    public async Task Head_ReturnsHeadersWithoutBody()
    {
        var blob = await Seed();

        var response = await _factory.CreateClient().SendAsync(new HttpRequestMessage(HttpMethod.Head, $"/files/{blob.Hash}"), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentLength.ShouldBe(16);
        (await response.Content.ReadAsByteArrayAsync(Ct)).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("not-a-hash")]
    [InlineData("0000000000000000000000000000000000000000000000000000000000000000")]
    public async Task Get_UnknownOrMalformed_Returns404Problem(string sha)
    {
        var response = await _factory.CreateClient().GetAsync($"/files/{sha}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        response.Headers.CacheControl?.Immutable().ShouldNotBe(true);
    }

    [Fact]
    public async Task Get_RowWithoutFile_Returns404()
    {
        var blob = await Seed("orphan");
        await _factory.Services.GetRequiredService<IBlobStore>().DeleteAsync(blob.Hash, Ct);

        var response = await _factory.CreateClient().GetAsync($"/files/{blob.Hash}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}

internal static class CacheControlExtensions
{
    public static bool Immutable(this CacheControlHeaderValue header) =>
        header.Extensions.Any(e => e.Name == "immutable");
}
