using System.Net;
using System.Text.Json.Nodes;
using CutWire.Studio.Application.Quality;
using CutWire.Studio.Domain.Blobs;
using CutWire.Studio.Infrastructure.Jobs;
using CutWire.Studio.UnitTests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace CutWire.Studio.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class CatalogReadTests(PostgresFixture postgres) : IDisposable
{
    private readonly CatalogFixture _f = new(postgres);
    private CancellationToken Ct => _f.Ct;

    public void Dispose() => _f.Dispose();

    private HttpClient Anonymous() => _f.Factory.Browser(cookies: false);

    private static List<string> Titles(JsonNode page) => page["items"]!.AsArray().Select(i => i!["title"]!.GetValue<string>()).ToList();

    private async Task<(HttpClient Client, string Handle, string[] Titles)> Seed()
    {
        var (client, handle, _) = await _f.User();
        string[] titles = ["Soft Dreamy Glow", "Crisp Neon Wave", "Warm Film Grain", "Cool Pixel Shake", "Retro Tape Blur", "Bright Mirror Flash"];
        string[][] tags = [["glow", "dream"], ["neon", "wave"], ["film", "grain"], ["pixel", "shake"], ["retro", "vhs"], ["mirror", "flash"]];
        for (var i = 0; i < titles.Length; i++)
        {
            var description = i == 2 ? Drafts.Description + " The texture resembles a butterfly wing." : null;
            await _f.PublishEffect(client, titles[i], tags[i], i == 4 ? "retro" : "dreamy", description);
            await Task.Delay(5, Ct);
        }

        return (client, handle, titles);
    }

    [Fact]
    public async Task Search_FullText_Tags_Kind_Category_AndFuzzy()
    {
        var (_, handle, _) = await Seed();
        var anon = Anonymous();
        string Url(string query) => $"/api/v1/items?owner={handle}&{query}";

        Titles(await _f.Ok(await anon.GetAsync(Url("q=butterfly"), Ct))).ShouldBe(["Warm Film Grain"]);
        Titles(await _f.Ok(await anon.GetAsync(Url("q=neon"), Ct))).ShouldBe(["Crisp Neon Wave"]);
        Titles(await _f.Ok(await anon.GetAsync(Url("q=%22film%20grain%22"), Ct))).ShouldBe(["Warm Film Grain"]);
        Titles(await _f.Ok(await anon.GetAsync(Url("q=Pixle%20Shak"), Ct))).ShouldContain("Cool Pixel Shake");
        Titles(await _f.Ok(await anon.GetAsync(Url("tags=vhs"), Ct))).ShouldBe(["Retro Tape Blur"]);
        Titles(await _f.Ok(await anon.GetAsync(Url("tags=mirror,flash"), Ct))).ShouldBe(["Bright Mirror Flash"]);
        Titles(await _f.Ok(await anon.GetAsync(Url("tags=mirror,vhs"), Ct))).ShouldBeEmpty();
        Titles(await _f.Ok(await anon.GetAsync(Url("category=retro"), Ct))).ShouldBe(["Retro Tape Blur"]);
        Titles(await _f.Ok(await anon.GetAsync(Url("kind=transition"), Ct))).ShouldBeEmpty();
        (await _f.Ok(await anon.GetAsync(Url("kind=effect"), Ct)))["items"]!.AsArray().Count.ShouldBe(6);

        var tagWeighted = Titles(await _f.Ok(await anon.GetAsync(Url("q=grain"), Ct)));
        tagWeighted.ShouldBe(["Warm Film Grain"]);
    }

    [Fact]
    public async Task Card_HasTheDocumentedShape()
    {
        var (_, handle, _) = await Seed();
        var page = await _f.Ok(await Anonymous().GetAsync($"/api/v1/items?owner={handle}&limit=1", Ct));
        var card = page["items"]![0]!;

        card["id"].ShouldNotBeNull();
        card["kind"]!.GetValue<string>().ShouldBe("effect");
        card["slug"].ShouldNotBeNull();
        card["owner"]!["handle"]!.GetValue<string>().ShouldBe(handle);
        card["thumbnailUrl"]!.GetValue<string>().ShouldStartWith("/files/");
        card["score"]!.GetValue<double>().ShouldBe(0);
        card["downloads"]!.GetValue<long>().ShouldBe(0);
        card["tags"]!.AsArray().Count.ShouldBe(2);
        card["publishedAt"].ShouldNotBeNull();
    }

    [Theory]
    [InlineData("new")]
    [InlineData("top")]
    [InlineData("trending")]
    [InlineData("downloads")]
    public async Task CursorPagination_IsStableAndComplete_ForEverySort(string sort)
    {
        var (_, handle, _) = await Seed();
        var ids = await _f.Db(db => db.Items.Where(i => i.Owner.Handle == handle).OrderBy(i => i.Title).Select(i => i.Id).ToListAsync(Ct));
        for (var i = 0; i < ids.Count; i++)
            await _f.Sql($"update items set wilson_score = {(i % 3) * 0.1:0.0}, hot_score = {(i % 2) * 0.2:0.0}, downloads_total = {i % 4} where id = '{ids[i]}'");

        var anon = Anonymous();
        var all = Titles(await _f.Ok(await anon.GetAsync($"/api/v1/items?owner={handle}&sort={sort}&limit=50", Ct)));
        all.Count.ShouldBe(6);

        var paged = new List<string>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var url = $"/api/v1/items?owner={handle}&sort={sort}&limit=2" + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");
            var page = await _f.Ok(await anon.GetAsync(url, Ct));
            paged.AddRange(Titles(page));
            cursor = page["nextCursor"]?.GetValue<string>();
            pages++;
        } while (cursor is not null && pages < 10);

        pages.ShouldBe(3);
        paged.ShouldBe(all);
        paged.Distinct().Count().ShouldBe(6);
        if (sort == "new") paged.ShouldBe(["Bright Mirror Flash", "Retro Tape Blur", "Cool Pixel Shake", "Warm Film Grain", "Crisp Neon Wave", "Soft Dreamy Glow"]);
    }

    [Fact]
    public async Task List_RejectsBadInput_AndEnforcesVisibility()
    {
        var (client, handle, _) = await Seed();
        var anon = Anonymous();
        (await anon.GetAsync("/api/v1/items?sort=sideways", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await anon.GetAsync("/api/v1/items?cursor=garbage", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var cursor = (await _f.Ok(await anon.GetAsync($"/api/v1/items?owner={handle}&limit=1", Ct)))["nextCursor"]!.GetValue<string>();
        (await anon.GetAsync($"/api/v1/items?owner={handle}&sort=new&cursor={Uri.EscapeDataString(cursor)}", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await _f.Ok(await anon.GetAsync($"/api/v1/items?owner={handle}&limit=500", Ct)))["items"]!.AsArray().Count.ShouldBe(6);

        await _f.Create(client, "Hidden Draft Work");
        (await anon.GetAsync($"/api/v1/items?owner={handle}&status=all", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        Titles(await _f.Ok(await client.GetAsync($"/api/v1/items?owner={handle}&status=draft", Ct))).ShouldBe(["Hidden Draft Work"]);
        (await _f.Ok(await client.GetAsync($"/api/v1/items?owner={handle}&status=all", Ct)))["items"]!.AsArray().Count.ShouldBe(7);
        (await _f.Ok(await anon.GetAsync($"/api/v1/items?owner={handle}", Ct)))["items"]!.AsArray().Count.ShouldBe(6);

        await _f.Sql($"update items set status = 'hidden', visibility_reason = 'test' where title = 'Soft Dreamy Glow'");
        Titles(await _f.Ok(await anon.GetAsync($"/api/v1/items?owner={handle}", Ct))).ShouldNotContain("Soft Dreamy Glow");
        (await anon.GetAsync("/api/v1/items/effect/soft-dreamy-glow", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await _f.Ok(await client.GetAsync("/api/v1/items/effect/soft-dreamy-glow", Ct)))["visibilityReason"]!.GetValue<string>().ShouldBe("test");
    }

    [Fact]
    public async Task Etags_AllowConditionalRequests()
    {
        var (_, handle, _) = await Seed();
        var anon = Anonymous();
        foreach (var url in new[] { $"/api/v1/items?owner={handle}", "/api/v1/items/effect/soft-dreamy-glow", $"/api/v1/users/{handle}", "/api/v1/meta/quality-rules", "/api/v1/meta/kinds" })
        {
            var first = await anon.GetAsync(url, Ct);
            first.StatusCode.ShouldBe(HttpStatusCode.OK, url);
            var etag = first.Headers.ETag!.ToString();
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("If-None-Match", etag);
            (await anon.SendAsync(request, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotModified, url);
        }
    }

    [Fact]
    public async Task UserProfile_ListsPublishedItemsPaged()
    {
        var (_, handle, _) = await Seed();
        var anon = Anonymous();
        var profile = await _f.Ok(await anon.GetAsync($"/api/v1/users/{handle.ToUpperInvariant()}?limit=4", Ct));

        profile["handle"]!.GetValue<string>().ShouldBe(handle);
        profile["publishedItems"]!.GetValue<int>().ShouldBe(6);
        profile["items"]!["items"]!.AsArray().Count.ShouldBe(4);
        var next = profile["items"]!["nextCursor"]!.GetValue<string>();
        (await _f.Ok(await anon.GetAsync($"/api/v1/users/{handle}?limit=4&cursor={Uri.EscapeDataString(next)}", Ct)))["items"]!["items"]!.AsArray().Count.ShouldBe(2);
        (await anon.GetAsync("/api/v1/users/nobody-here", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Meta_KindsRulesAndHashedBloom()
    {
        var anon = Anonymous();
        var kinds = await _f.Ok(await anon.GetAsync("/api/v1/meta/kinds", Ct));
        kinds.AsArray().Select(k => k!["id"]!.GetValue<string>()).ShouldNotContain("language-pack");
        var effect = kinds.AsArray().Single(k => k!["id"]!.GetValue<string>() == "effect")!;
        effect["categories"]!.AsArray().Count.ShouldBeGreaterThan(0);
        effect["media"]![0]!["width"]!.GetValue<int>().ShouldBe(512);
        effect["acceptedExtensions"]![0]!.GetValue<string>().ShouldBe(".driftfx");

        var rules = await _f.Ok(await anon.GetAsync("/api/v1/meta/quality-rules", Ct));
        rules["title"]!["max"]!.GetValue<int>().ShouldBe(60);
        rules["text"]!["profanity"]!.AsArray().Count.ShouldBeGreaterThan(10);
        rules["text"]!["techTerms"]!.AsArray().Count.ShouldBeGreaterThan(5);
        var url = rules["englishWordList"]!["url"]!.GetValue<string>();
        url.ShouldMatch(@"^/api/v1/meta/english-[0-9a-f]{8}\.bloom$");

        var bloom = await anon.GetAsync(url, Ct);
        bloom.StatusCode.ShouldBe(HttpStatusCode.OK);
        bloom.Headers.CacheControl!.ToString().ShouldContain("immutable");
        bloom.Content.Headers.ContentType!.MediaType.ShouldBe("application/octet-stream");
        var bytes = await bloom.Content.ReadAsByteArrayAsync(Ct);
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)).ShouldBe(rules["englishWordList"]!["sha256"]!.GetValue<string>());
        BloomFilter.Parse(bytes).MightContain("glow").ShouldBeTrue();
        bloom.Headers.ETag!.Tag.ShouldBe($"\"{rules["englishWordList"]!["sha256"]!.GetValue<string>()}\"");
        (await anon.GetAsync("/api/v1/meta/english-00000000.bloom", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Download_RedirectsRecordsAndRollsUpDeduplicated()
    {
        var (client, _, _) = await _f.User();
        var item = await _f.PublishEffect(client, "Dreamy Glow", package: Fixture.Bytes("effect.driftfx"));
        var id = item["id"]!.GetValue<string>();
        var sha = item["versions"]![0]!["sha256"]!.GetValue<string>();

        HttpClient Fresh(string? install = null)
        {
            var http = _f.Factory.Browser(cookies: false);
            if (install is not null) http.DefaultRequestHeaders.Add("X-Studio-Install", install);
            return http;
        }

        var response = await Fresh("install-a").GetAsync($"/api/v1/items/{id}/download", Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().ShouldBe($"/files/{sha}?name=dreamy-glow-1.0.0.driftfx");
        (await Fresh("install-a").GetAsync($"/api/v1/items/{id}/download?version=1.0.0", Ct)).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await Fresh("install-a").GetAsync($"/api/v1/items/{id}/download", Ct)).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await Fresh("install-b").GetAsync($"/api/v1/items/{id}/download", Ct)).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await client.GetAsync($"/api/v1/items/{id}/download", Ct)).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await client.GetAsync($"/api/v1/items/{id}/download", Ct)).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await Fresh().GetAsync($"/api/v1/items/{id}/download?version=9.9.9", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Fresh().GetAsync($"/api/v1/items/{Guid.NewGuid()}/download", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await _f.Db(db => db.DownloadEvents.CountAsync(Ct))).ShouldBe(6);
        var job = _f.Factory.Services.GetRequiredService<IEnumerable<IRecurringJob>>().OfType<DownloadRollupJob>().Single();
        await job.RunAsync(Ct);

        // install-a, install-b and the signed-in user: the anonymous-IP hash only counts for events without user or install id.
        (await _f.Db(db => db.Items.Where(i => i.Id == Guid.Parse(id)).Select(i => i.DownloadsTotal).SingleAsync(Ct))).ShouldBe(3);
        (await _f.Db(db => db.DownloadEvents.CountAsync(e => e.RolledUpAt == null, Ct))).ShouldBe(0);

        await job.RunAsync(Ct);
        (await _f.Db(db => db.Items.Where(i => i.Id == Guid.Parse(id)).Select(i => i.DownloadsTotal).SingleAsync(Ct))).ShouldBe(3);

        await _f.Sql($"insert into download_events (item_id, ip_hash, at) values ('{id}', 'h1', now() - interval '2 days'), ('{id}', 'h1', now() - interval '2 days'), ('{id}', 'h1', now() - interval '3 days')");
        await (await Fresh("install-a").GetAsync($"/api/v1/items/{id}/download", Ct)).Content.ReadAsStringAsync(Ct);
        await job.RunAsync(Ct);
        (await _f.Db(db => db.Items.Where(i => i.Id == Guid.Parse(id)).Select(i => i.DownloadsTotal).SingleAsync(Ct))).ShouldBe(5);
        (await _f.Db(db => db.ItemDownloadDaily.CountAsync(d => d.ItemId == Guid.Parse(id), Ct))).ShouldBe(3);

        var sorted = await _f.Ok(await Anonymous().GetAsync("/api/v1/items?sort=downloads&owner=" + (await _f.Db(db => db.Items.Where(i => i.Id == Guid.Parse(id)).Select(i => i.Owner.Handle).SingleAsync(Ct))), Ct));
        sorted["items"]![0]!["downloads"]!.GetValue<long>().ShouldBe(5);
    }

    [Fact]
    public async Task Download_OfDraftIsNotFound_AndFilesNameIsSanitised()
    {
        var (client, _, _) = await _f.User();
        var draft = await _f.Create(client, "Dreamy Glow");
        (await client.GetAsync($"/api/v1/items/{draft["id"]}/download", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var published = await _f.PublishEffect(client, "Crisp Neon Wave");
        var url = published["versions"]![0]!["downloadUrl"]!.GetValue<string>();
        (await client.GetAsync(url, Ct)).StatusCode.ShouldBe(HttpStatusCode.Redirect);

        var sha = published["versions"]![0]!["sha256"]!.GetValue<string>();
        var anon = Anonymous();
        var named = await anon.GetAsync($"/files/{sha}?name={Uri.EscapeDataString("../../evil name\"x.driftfx")}", Ct);
        var disposition = named.Content.Headers.ContentDisposition!;
        disposition.DispositionType.ShouldBe("attachment");
        disposition.FileName.ShouldBe("evil-name-x.driftfx");
        foreach (var url2 in new[] { $"/files/{sha}", $"/files/{sha}?name=...." })
        {
            // A package is not passive media, so it always downloads, with or without a usable name.
            var unnamed = (await anon.GetAsync(url2, Ct)).Content.Headers.ContentDisposition!;
            unnamed.DispositionType.ShouldBe("attachment");
            unnamed.FileName.ShouldBeNull();
        }
    }

    [Fact]
    public async Task DuplicateLookup_UsesHammingDistanceInPostgres()
    {
        var (client, _, _) = await _f.User();
        var item = await _f.PublishEffect(client, "Dreamy Glow");
        var stored = await _f.Db(db => db.ItemMedia.Select(m => m.DHash).SingleAsync(Ct));
        await using var scope = _f.Factory.Services.CreateAsyncScope();
        var lookup = scope.ServiceProvider.GetRequiredService<IDuplicateLookup>();
        var hash = unchecked((ulong)stored);

        (await lookup.SimilarThumbnailExistsAsync(hash, 6, null, Ct)).ShouldBeTrue();
        (await lookup.SimilarThumbnailExistsAsync(hash ^ 0b111111UL, 6, null, Ct)).ShouldBeTrue();
        (await lookup.SimilarThumbnailExistsAsync(hash ^ 0b1111111UL, 6, null, Ct)).ShouldBeFalse();
        (await lookup.SimilarThumbnailExistsAsync(hash ^ 0x8000000000000000UL, 0, null, Ct)).ShouldBeFalse();
        (await lookup.SimilarThumbnailExistsAsync(hash, 6, Guid.Parse(item["id"]!.GetValue<string>()), Ct)).ShouldBeFalse();
    }
}
