using System.Net;
using System.Net.Http.Headers;
using CutWire.Studio.Domain.Blobs;
using CutWire.Studio.Domain.Catalog;
using CutWire.Studio.UnitTests;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace CutWire.Studio.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class CatalogUploadTests(PostgresFixture postgres) : IDisposable
{
    private readonly CatalogFixture _f = new(postgres);
    private CancellationToken Ct => _f.Ct;

    public void Dispose() => _f.Dispose();

    private Task<int> RefCount(string sha) => _f.Db(async db => (await db.Blobs.SingleAsync(b => b.Id == new Sha256Hash(sha), Ct)).RefCount);

    [Fact]
    public async Task HappyPath_RealFixture_DraftIsPrivateUntilPublished()
    {
        var (owner, handle, _) = await _f.User();
        var item = await _f.Create(owner, "Dreamy Glow");
        var id = item["id"]!.GetValue<string>();
        var slug = item["slug"]!.GetValue<string>();
        slug.ShouldBe("dreamy-glow");
        item["status"]!.GetValue<string>().ShouldBe("draft");

        var anonymous = _f.Factory.Browser(cookies: false);
        (await anonymous.GetAsync($"/api/v1/items/effect/{slug}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await anonymous.GetAsync($"/api/v1/items/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var (other, _, _) = await _f.User();
        (await other.GetAsync($"/api/v1/items/effect/{slug}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await owner.GetAsync($"/api/v1/items/effect/{slug}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await _f.Ok(await anonymous.GetAsync($"/api/v1/items?owner={handle}", Ct)))["items"]!.AsArray().Count.ShouldBe(0);
        (await other.PatchAsJsonAsync($"/api/v1/items/{id}", new { title = "Stolen Glow" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var withPackage = await _f.Ok(await _f.PutPackage(owner, id, Fixture.Bytes("effect.driftfx")));
        withPackage["versions"]![0]!["version"]!.GetValue<string>().ShouldBe("1.0.0");
        var packageSha = withPackage["versions"]![0]!["sha256"]!.GetValue<string>();
        var thumb = CatalogFixture.Thumb(512, 288, 1);
        var withMedia = await _f.Ok(await _f.PutMedia(owner, id, "thumbnail", thumb));
        var thumbSha = withMedia["media"]![0]!["url"]!.GetValue<string>()[7..];
        (await RefCount(packageSha)).ShouldBe(1);
        (await RefCount(thumbSha)).ShouldBe(1);

        var published = await _f.Ok(await _f.Publish(owner, id));
        published["status"]!.GetValue<string>().ShouldBe("published");
        published["currentVersion"]!.GetValue<string>().ShouldBe("1.0.0");

        var detail = await _f.Ok(await anonymous.GetAsync($"/api/v1/items/effect/{slug}", Ct));
        detail["owner"]!["handle"]!.GetValue<string>().ShouldBe(handle);
        detail["viewerVote"].ShouldBeNull();
        detail["kindMeta"]!["packageId"]!.GetValue<string>().ShouldBe("user.forge_fixture_tint");
        detail["media"]![0]!["role"]!.GetValue<string>().ShouldBe("thumbnail");
        (await anonymous.GetAsync(detail["media"]![0]!["url"]!.GetValue<string>(), Ct)).Content.Headers.ContentType!.MediaType.ShouldBe("image/webp");
        (await _f.Ok(await anonymous.GetAsync($"/api/v1/items?owner={handle}", Ct)))["items"]!.AsArray().Count.ShouldBe(1);
    }

    [Fact]
    public async Task Multipart_Transition_WithTwoMediaRoles_PassesCsrfAndPublishes()
    {
        var (client, _, _) = await _f.User();
        var item = await _f.Create(client, "Smooth Quick Swipe", "transition", ["smooth", "swipe"], "basic");
        var id = item["id"]!.GetValue<string>();

        var package = await client.PutAsync($"/api/v1/items/{id}/package", CatalogFixture.Multipart(Fixture.Bytes("transition.driftfx"), "t.driftfx"), Ct);
        await _f.Ok(package);
        await _f.Ok(await client.PutAsync($"/api/v1/items/{id}/media/preview-strip", CatalogFixture.Multipart(CatalogFixture.Thumb(1536, 128, 5), "s.webp"), Ct));
        await _f.Ok(await _f.PutMedia(client, id, "thumbnail", CatalogFixture.Thumb(512, 288, 6)));

        var published = await _f.Ok(await _f.Publish(client, id));
        published["media"]!.AsArray().Count.ShouldBe(2);

        var noHeader = _f.Factory.Browser();
        noHeader.DefaultRequestHeaders.Remove("X-Requested-With");
        (await noHeader.PutAsync($"/api/v1/items/{id}/package", CatalogFixture.Multipart([1], "x"), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Create_ReturnsEveryTextViolationWithStableCodes()
    {
        var (client, _, _) = await _f.User();
        var response = await client.PostJson("/api/v1/items", new
        {
            kind = "effect", title = "BAD!!!!", summary = "tiny", description = "short", category = "nope", tags = new[] { "glow", "glow", "nonsense" }, license = "WTFPL",
        }, Ct);

        response.StatusCode.ShouldBe((HttpStatusCode)422);
        var problem = await response.Json(Ct);
        problem["code"]!.GetValue<string>().ShouldBe("content_rejected");
        var codes = CatalogFixture.Violations(problem);
        codes.ShouldContain("title:repeated_chars");
        codes.ShouldContain("title:repeated_punctuation");
        codes.ShouldContain("summary:too_short");
        codes.ShouldContain("description:too_short");
        codes.ShouldContain("category:invalid_category");
        codes.ShouldContain("tags:duplicate_tag");
        codes.ShouldContain("tags:unknown_tag");
        codes.ShouldContain("license:invalid_license");
        problem["errors"]!["title"]!.AsArray().Count.ShouldBeGreaterThan(1);
        problem["violations"]![0]!["message"].ShouldNotBeNull();

        var unknown = await (await client.PostJson("/api/v1/items", CatalogFixture.Metadata("Dreamy Glow", "mystery"), Ct)).Json(Ct);
        CatalogFixture.Violations(unknown).ShouldBe(["kind:unknown_kind"]);
    }

    [Fact]
    public async Task Media_EachGateFailureIsReportedWithItsCode()
    {
        var (client, _, _) = await _f.User();
        var id = (await _f.Create(client, "Dreamy Glow"))["id"]!.GetValue<string>();

        async Task<List<string>> Codes(string role, byte[] data)
        {
            var response = await _f.PutMedia(client, id, role, data);
            response.StatusCode.ShouldBe((HttpStatusCode)422);
            return CatalogFixture.Violations(await response.Json(Ct));
        }

        (await Codes("thumbnail", CatalogFixture.Thumb(400, 300, 1))).ShouldBe(["media.thumbnail:media_wrong_dimensions"]);
        (await Codes("thumbnail", Images.Solid(512, 288, SkiaSharp.SKColors.Gray))).ShouldBe(["media.thumbnail:media_blank"]);
        (await Codes("thumbnail", Images.Png(512, 288))).ShouldContain("media.thumbnail:media_not_webp");
        (await Codes("thumbnail", new byte[100])).ShouldBe(["media.thumbnail:media_undecodable"]);
        (await Codes("thumbnail", Images.Good(512, 288, 100).Concat(new byte[41_000]).ToArray())).ShouldBe(["media.thumbnail:media_too_large"]);
        (await Codes("preview-strip", CatalogFixture.Thumb(1536, 128, 1))).ShouldBe(["media.preview-strip:media_role_not_allowed"]);
        (await Codes("banner", [1])).ShouldBe(["media.banner:media_role_not_allowed"]);
    }

    [Fact]
    public async Task Package_Failures()
    {
        var (client, _, _) = await _f.User();
        var id = (await _f.Create(client, "Dreamy Glow"))["id"]!.GetValue<string>();

        var garbage = await _f.PutPackage(client, id, new byte[200]);
        garbage.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await garbage.Json(Ct);
        problem["code"]!.GetValue<string>().ShouldBe("driftfx_bad_magic");
        CatalogFixture.Violations(problem).ShouldBe(["package:driftfx_bad_magic"]);

        var wrongKind = await _f.PutPackage(client, id, Fixture.Bytes("transition.driftfx"));
        (await wrongKind.Json(Ct))["code"]!.GetValue<string>().ShouldBe("wrong_kind");

        var tooLarge = await _f.PutPackage(client, id, new byte[2 * 1024 * 1024 + 10]);
        tooLarge.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        (await tooLarge.Json(Ct))["code"]!.GetValue<string>().ShouldBe("package_too_large");
    }

    [Fact]
    public async Task Publish_ReportsMissingPackageAndMedia()
    {
        var (client, _, _) = await _f.User();
        var id = (await _f.Create(client, "Dreamy Glow"))["id"]!.GetValue<string>();

        var noPackage = await _f.Publish(client, id);
        noPackage.StatusCode.ShouldBe((HttpStatusCode)422);
        CatalogFixture.Violations(await noPackage.Json(Ct)).ShouldBe(["package:package_missing"]);

        await _f.Ok(await _f.PutPackage(client, id, CatalogFixture.UniqueEffect()));
        var noMedia = await _f.Publish(client, id);
        CatalogFixture.Violations(await noMedia.Json(Ct)).ShouldBe(["media.thumbnail:media_missing"]);

        await _f.Ok(await _f.PutMedia(client, id, "thumbnail", CatalogFixture.Thumb(512, 288, 3)));
        await _f.Ok(await _f.Publish(client, id));
        (await _f.Publish(client, id)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await _f.PutPackage(client, id, CatalogFixture.UniqueEffect())).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Duplicates_PackageTitleAndThumbnail()
    {
        var (a, _, _) = await _f.User();
        var (b, _, _) = await _f.User();
        var package = Fixture.Bytes("effect.driftfx");
        var thumbnail = CatalogFixture.Thumb(512, 288, 42);
        var first = await _f.PublishEffect(a, "Dreamy Glow", package: package, thumbnail: thumbnail);

        var dup = await _f.Create(b, "Other Name Here");
        var dupId = dup["id"]!.GetValue<string>();
        await _f.Ok(await _f.PutPackage(b, dupId, package));
        await _f.Ok(await _f.PutMedia(b, dupId, "thumbnail", CatalogFixture.Thumb(512, 288, 7)));
        var rejected = await _f.Publish(b, dupId);
        rejected.StatusCode.ShouldBe((HttpStatusCode)422);
        CatalogFixture.Violations(await rejected.Json(Ct)).ShouldBe(["package:duplicate_package"]);

        var sameTitle = await _f.Create(a, "Dreamy glow");
        var titleId = sameTitle["id"]!.GetValue<string>();
        await _f.Ok(await _f.PutPackage(a, titleId, CatalogFixture.UniqueEffect()));
        await _f.Ok(await _f.PutMedia(a, titleId, "thumbnail", CatalogFixture.Thumb(512, 288, 8)));
        CatalogFixture.Violations(await (await _f.Publish(a, titleId)).Json(Ct)).ShouldBe(["title:duplicate_title"]);

        var similar = await _f.PublishEffect(b, "Cool Pixel Shake", thumbnail: thumbnail);
        var flags = await _f.Db(db => db.ItemFlags.Where(f => f.ItemId == Guid.Parse(similar["id"]!.GetValue<string>())).Select(f => f.Code).ToListAsync(Ct));
        flags.ShouldBe(["similar_thumbnail"]);
        (await _f.Db(db => db.ItemFlags.CountAsync(f => f.ItemId == Guid.Parse(first["id"]!.GetValue<string>()), Ct))).ShouldBe(0);
    }

    [Fact]
    public async Task UploadQuota_LimitsPublishesPerDayByTrustLevel()
    {
        var (client, _, _) = await _f.User("T0");
        foreach (var title in new[] { "Soft Dreamy Glow", "Crisp Neon Wave", "Warm Film Grain" })
            await _f.PublishEffect(client, title);

        var fourth = await _f.Create(client, "Cool Pixel Shake");
        var id = fourth["id"]!.GetValue<string>();
        await _f.Ok(await _f.PutPackage(client, id, CatalogFixture.UniqueEffect()));
        await _f.Ok(await _f.PutMedia(client, id, "thumbnail", CatalogFixture.Thumb(512, 288, 99)));
        var blocked = await _f.Publish(client, id);
        blocked.StatusCode.ShouldBe((HttpStatusCode)422);
        CatalogFixture.Violations(await blocked.Json(Ct)).ShouldBe(["account:upload_limit"]);
    }

    [Fact]
    public async Task Versions_MustIncrease_AndSwitchCurrentVersion()
    {
        var (client, _, _) = await _f.User();
        var item = await _f.PublishEffect(client, "Dreamy Glow", package: Fixture.Bytes("effect.driftfx"));
        var id = item["id"]!.GetValue<string>();
        var oldSha = item["versions"]![0]!["sha256"]!.GetValue<string>();

        var same = await client.PostAsync($"/api/v1/items/{id}/versions", CatalogFixture.Bytes(CatalogFixture.UniqueEffect("1.0.0")), Ct);
        same.StatusCode.ShouldBe((HttpStatusCode)422);
        CatalogFixture.Violations(await same.Json(Ct)).ShouldBe(["package:version_not_increased"]);

        var next = CatalogFixture.UniqueEffect("1.1.0");
        var updated = await _f.Ok(await client.PostAsync($"/api/v1/items/{id}/versions?changelog=Faster", CatalogFixture.Bytes(next), Ct));
        updated["currentVersion"]!.GetValue<string>().ShouldBe("1.1.0");
        updated["versions"]!.AsArray().Count.ShouldBe(2);
        updated["versions"]![0]!["changelog"]!.GetValue<string>().ShouldBe("Faster");
        (await RefCount(oldSha)).ShouldBe(1);

        var dup = await client.PostAsync($"/api/v1/items/{id}/versions", CatalogFixture.Multipart(next, "p.driftfx", ("changelog", "again")), Ct);
        CatalogFixture.Violations(await dup.Json(Ct)).ShouldBe(["package:version_not_increased"]);

        var downloaded = await _f.Factory.Browser(cookies: false).GetAsync($"/api/v1/items/{id}/download", Ct);
        downloaded.Headers.Location!.ToString().ShouldContain("dreamy-glow-1.1.0.driftfx");
    }

    [Fact]
    public async Task Patch_DraftEditsFreely_PublishedEditsRerunGates()
    {
        var (client, _, _) = await _f.User();
        var draft = await _f.Create(client, "Dreamy Glow");
        var draftId = draft["id"]!.GetValue<string>();
        var edited = await _f.Ok(await client.PatchAsJsonAsync($"/api/v1/items/{draftId}", new { title = "Dreamy Glow Two", tags = new[] { "film" } }, Ct));
        edited["title"]!.GetValue<string>().ShouldBe("Dreamy Glow Two");
        edited["tags"]!.AsArray().Select(t => t!.GetValue<string>()).ShouldBe(["film"]);

        var published = await _f.PublishEffect(client, "Crisp Neon Wave");
        var id = published["id"]!.GetValue<string>();
        var bad = await client.PatchAsJsonAsync($"/api/v1/items/{id}", new { summary = "tiny", tags = new[] { "nonsense" } }, Ct);
        bad.StatusCode.ShouldBe((HttpStatusCode)422);
        CatalogFixture.Violations(await bad.Json(Ct)).ShouldBe(["summary:too_short", "tags:unknown_tag"], ignoreOrder: true);
        var clash = await client.PatchAsJsonAsync($"/api/v1/items/{id}", new { title = "Dreamy Glow Two" }, Ct);
        clash.StatusCode.ShouldBe(HttpStatusCode.OK);

        var good = await _f.Ok(await client.PatchAsJsonAsync($"/api/v1/items/{id}", new { title = "Crisp Neon Waves", category = "retro" }, Ct));
        good["category"]!.GetValue<string>().ShouldBe("retro");
        good["slug"]!.GetValue<string>().ShouldBe("crisp-neon-wave");
    }

    [Fact]
    public async Task Delete_SoftRemovesAndReleasesBlobs()
    {
        var (owner, handle, _) = await _f.User();
        var item = await _f.PublishEffect(owner, "Dreamy Glow");
        var id = item["id"]!.GetValue<string>();
        var packageSha = item["versions"]![0]!["sha256"]!.GetValue<string>();
        var thumbSha = item["media"]![0]!["url"]!.GetValue<string>()[7..];
        var (other, _, _) = await _f.User();

        (await other.DeleteAsync($"/api/v1/items/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await owner.DeleteAsync($"/api/v1/items/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await _f.Factory.Browser(cookies: false).GetAsync($"/api/v1/items/effect/dreamy-glow", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await owner.GetAsync($"/api/v1/items/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await owner.DeleteAsync($"/api/v1/items/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await _f.Ok(await owner.GetAsync($"/api/v1/items?owner={handle}&status=all", Ct)))["items"]!.AsArray().Count.ShouldBe(0);
        (await RefCount(packageSha)).ShouldBe(0);
        (await RefCount(thumbSha)).ShouldBe(0);
        (await _f.Db(db => db.Blobs.SingleAsync(b => b.Id == new Sha256Hash(packageSha), Ct))).ZeroRefSince.ShouldNotBeNull();
        (await _f.Db(db => db.Items.SingleAsync(i => i.Id == Guid.Parse(id), Ct))).Status.ShouldBe(ItemStatus.Removed);
    }

    [Fact]
    public async Task SharedBlob_IsReferenceCountedPerItem()
    {
        var (client, _, _) = await _f.User();
        var thumbnail = CatalogFixture.Thumb(512, 288, 77);
        var a = await _f.PublishEffect(client, "Soft Dreamy Glow", thumbnail: thumbnail);
        var b = await _f.PublishEffect(client, "Crisp Neon Wave", thumbnail: thumbnail);
        var sha = a["media"]![0]!["url"]!.GetValue<string>()[7..];

        (await RefCount(sha)).ShouldBe(2);
        await client.DeleteAsync($"/api/v1/items/{b["id"]}", Ct);
        (await RefCount(sha)).ShouldBe(1);
    }

    [Fact]
    public async Task Auth_AnonymousBannedAndScopes()
    {
        (await _f.Factory.Browser(cookies: false).PostJson("/api/v1/items", CatalogFixture.Metadata("Dreamy Glow"), Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var (client, _, userId) = await _f.User();
        async Task<HttpClient> Pat(params string[] scopes)
        {
            var created = await (await client.PostJson("/api/v1/account/tokens", new { name = "t", scopes }, Ct)).Json(Ct);
            var bearer = _f.Factory.Browser(cookies: false);
            bearer.DefaultRequestHeaders.Remove("X-Requested-With");
            bearer.DefaultRequestHeaders.Remove("Origin");
            bearer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", created["secret"]!.GetValue<string>());
            return bearer;
        }

        var readOnly = await Pat("forge:read");
        var denied = await readOnly.PostJson("/api/v1/items", CatalogFixture.Metadata("Dreamy Glow"), Ct);
        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await denied.Json(Ct))["code"]!.GetValue<string>().ShouldBe("insufficient_scope");

        var publisher = await Pat("community:publish");
        (await publisher.PostJson("/api/v1/items", CatalogFixture.Metadata("Dreamy Glow"), Ct)).StatusCode.ShouldBe(HttpStatusCode.Created);

        await _f.Sql($"update user_profiles set banned_until = now() + interval '1 day' where user_id = '{userId}'");
        foreach (var caller in new[] { client, publisher })
        {
            var banned = await caller.PostJson("/api/v1/items", CatalogFixture.Metadata("Crisp Neon Wave"), Ct);
            banned.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await banned.Json(Ct))["code"]!.GetValue<string>().ShouldBe("banned");
        }
    }
}
