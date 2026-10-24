using System.Net;
using System.Net.Http.Json;
using CutWire.Studio.Application.Moderation;
using CutWire.Studio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace CutWire.Studio.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class VoteTests(PostgresFixture postgres) : ModerationTestBase(postgres)
{
    [Fact]
    public async Task Weights_Revote_Clear_AndViewerVote()
    {
        var (owner, ownerHandle, _) = await F.User();
        var item = await F.PublishEffect(owner, "Soft Dreamy Glow");
        var id = Id(item);

        var (fresh, _, freshId) = await F.User("T0");
        var (aged, _, agedId) = await F.User("T0");
        await F.Sql($"update user_profiles set created_at = now() - interval '5 days' where user_id = '{agedId}'");
        var (trusted, _, _) = await F.User("T1");

        var first = await F.Ok(await Vote(fresh, id, 1));
        first["scoreUp"]!.GetValue<double>().ShouldBe(0.5);
        first["viewerVote"]!.GetValue<int>().ShouldBe(1);
        (await Row(id)).Up.ShouldBe(0.5);

        await F.Ok(await Vote(aged, id, 1));
        await F.Ok(await Vote(trusted, id, -1));
        var row = await Row(id);
        (row.Up, row.Down).ShouldBe((1.5, 1.0));

        (await Vote(fresh, id, 1)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Row(id)).Up.ShouldBe(1.5);

        var flipped = await F.Ok(await Vote(fresh, id, -1));
        flipped["viewerVote"]!.GetValue<int>().ShouldBe(-1);
        row = await Row(id);
        (row.Up, row.Down).ShouldBe((1.0, 1.5));
        (await F.Db(db => db.Items.AsNoTracking().Where(i => i.Id == Guid.Parse(id)).Select(i => i.WilsonScore).SingleAsync(Ct))).ShouldBeGreaterThan(0);

        var detail = await Detail(fresh, id);
        detail["viewerVote"]!.GetValue<int>().ShouldBe(-1);
        detail["scoreDown"]!.GetValue<double>().ShouldBe(1.5);
        var card = (await F.Ok(await fresh.GetAsync($"/api/v1/items?owner={ownerHandle}", Ct)))["items"]![0]!;
        card["viewerVote"]!.GetValue<int>().ShouldBe(-1);
        (await F.Ok(await owner.GetAsync($"/api/v1/items?owner={ownerHandle}", Ct)))["items"]![0]!["viewerVote"].ShouldBeNull();
        var anon = F.Factory.Browser(cookies: false);
        (await Detail(anon, id))["viewerVote"].ShouldBeNull();

        (await fresh.DeleteAsync($"/api/v1/items/{id}/vote", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        row = await Row(id);
        (row.Up, row.Down).ShouldBe((1.0, 1.0));
        (await Detail(fresh, id))["viewerVote"].ShouldBeNull();
        (await fresh.DeleteAsync($"/api/v1/items/{id}/vote", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Row(id)).Down.ShouldBe(1.0);
        (await F.Db(db => db.Votes.CountAsync(v => v.UserId == freshId, Ct))).ShouldBe(0);
    }

    [Fact]
    public async Task Rules_SelfVote_Unpublished_Unverified_Banned_Invalid()
    {
        var (owner, _, _) = await F.User();
        var id = Id(await F.PublishEffect(owner, "Crisp Neon Wave"));
        var draft = Id(await F.Create(owner, "Warm Film Grain", tags: ["film", "grain"]));
        var (voter, _, voterId) = await F.User("T1");

        var self = await Vote(owner, id, 1);
        self.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await self.Json(Ct))["code"]!.GetValue<string>().ShouldBe("vote.own_item");
        (await Vote(voter, draft, 1)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Vote(voter, Guid.NewGuid().ToString(), 1)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Vote(voter, id, 0)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Vote(voter, id, 2)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await F.Factory.Browser(cookies: false).PutAsJsonAsync($"/api/v1/items/{id}/vote", new { value = 1 }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        await F.Sql($"update users set email_confirmed = false where id = '{voterId}'");
        var unverified = await Vote(voter, id, 1);
        unverified.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await unverified.Json(Ct))["code"]!.GetValue<string>().ShouldBe("vote.email_unverified");
        await F.Sql($"update users set email_confirmed = true where id = '{voterId}'");

        await F.Sql($"update user_profiles set is_permanently_banned = true where user_id = '{voterId}'");
        var banned = await Vote(voter, id, 1);
        banned.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await banned.Json(Ct))["code"]!.GetValue<string>().ShouldBe("banned");
        (await Row(id)).Up.ShouldBe(0);
    }

    [Fact]
    public async Task AutoHide_Downvotes_DefaultThresholds()
    {
        var (owner, _, ownerId) = await F.User();
        var id = Id(await F.PublishEffect(owner, "Retro Tape Blur"));
        var voters = await Users(12, "T2");

        for (var i = 0; i < 7; i++) (await Vote(voters[i].Client, id, -1)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Row(id)).Status.ShouldBe("published");

        // 8 down but 4 up is 66.7% down: below the 70% share.
        for (var i = 8; i < 12; i++) await F.Ok(await Vote(voters[i].Client, id, 1));
        await F.Ok(await Vote(voters[7].Client, id, -1));
        (await Row(id)).Status.ShouldBe("published");

        await F.Ok(await Vote(voters[8].Client, id, -1));
        var row = await Row(id);
        (row.Status, row.Reason).ShouldBe(("hidden", "community_downvoted"));

        var action = await F.Db(db => db.ModerationActions.AsNoTracking().SingleAsync(a => a.TargetId == Guid.Parse(id), Ct));
        (action.Action, action.ActorId, action.Auto).ShouldBe(("auto_hide", null, true));
        var schedule = await F.Db(db => db.CleanupSchedules.AsNoTracking().SingleAsync(c => c.ItemId == Guid.Parse(id), Ct));
        (schedule.PurgeAt - schedule.CreatedAt).ShouldBe(TimeSpan.FromDays(14), TimeSpan.FromSeconds(1));

        (await Vote(voters[9].Client, id, -1)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        ownerId.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task ConcurrentVotes_AreAllCounted()
    {
        var (owner, _, _) = await F.User();
        var id = Id(await F.PublishEffect(owner, "Cool Pixel Shake"));
        var voters = await Users(8, "T1");

        var responses = await Task.WhenAll(voters.Select((v, i) => Vote(v.Client, id, i % 2 == 0 ? 1 : -1)));
        responses.ShouldAllBe(r => r.StatusCode == HttpStatusCode.OK);

        var row = await Row(id);
        (row.Up, row.Down).ShouldBe((4.0, 4.0));
        (await F.Db(db => db.Votes.CountAsync(v => v.ItemId == Guid.Parse(id), Ct))).ShouldBe(8);
    }

    [Fact]
    public async Task ConcurrentBlobReferenceChanges_AreAtomic()
    {
        var content = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("refcount-race"));
        var store = F.Factory.Services.GetRequiredService<CutWire.Studio.Application.Abstractions.IBlobStore>();
        var stored = (await store.PutAsync(content, 1024, Ct)).Value;
        await F.Db(async db =>
        {
            db.Blobs.Add(new CutWire.Studio.Domain.Blobs.Blob(stored.Hash, stored.Size, "text/plain", DateTimeOffset.UtcNow));
            return await db.SaveChangesAsync(Ct);
        });

        async Task Change(bool add)
        {
            await using var scope = F.Factory.Services.CreateAsyncScope();
            var refs = scope.ServiceProvider.GetRequiredService<CutWire.Studio.Application.Catalog.BlobReferences>();
            if (add) await refs.AddAsync(stored.Hash, Ct);
            else await refs.ReleaseAsync(stored.Hash, Ct);
        }

        await Task.WhenAll(Enumerable.Range(0, 60).Select(_ => Change(true)));
        (await RefCount(stored.Hash.Value)).ShouldBe(60);

        await Task.WhenAll(Enumerable.Range(0, 40).Select(i => Change(i % 2 == 0)));
        (await RefCount(stored.Hash.Value)).ShouldBe(60);

        await Task.WhenAll(Enumerable.Range(0, 80).Select(_ => Change(false)));
        var blob = await F.Db(db => db.Blobs.AsNoTracking().SingleAsync(b => b.Id == stored.Hash, Ct));
        blob.RefCount.ShouldBe(0);
        blob.ZeroRefSince.ShouldNotBeNull();
    }
}

[Collection(PostgresCollection.Name)]
public sealed class VoteConfigTests(PostgresFixture postgres) : ModerationTestBase(postgres, b =>
{
    b.UseSetting("Moderation:AutoHideMinDownvotes", "2");
    b.UseSetting("Moderation:AutoHideMinDownShare", "0.5");
    b.UseSetting("RateLimiting:VotePermitLimit", "3");
})
{
    [Fact]
    public async Task Thresholds_AreConfigurable_AndVotesAreRateLimitedPerUser()
    {
        var (owner, _, _) = await F.User();
        var id = Id(await F.PublishEffect(owner, "Bright Mirror Flash"));
        var voters = await Users(2, "T1");

        await F.Ok(await Vote(voters[0].Client, id, -1));
        (await Row(id)).Status.ShouldBe("published");
        await F.Ok(await Vote(voters[1].Client, id, -1));
        (await Row(id)).Reason.ShouldBe("community_downvoted");

        var other = Id(await F.PublishEffect(owner, "Warm Film Grain", ["film", "grain"]));
        var (limited, _, _) = await F.User("T1");
        for (var i = 0; i < 3; i++) (await Vote(limited, other, i % 2 == 0 ? 1 : -1)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Vote(limited, other, 1)).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await Vote(voters[0].Client, other, 1)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}

[Collection(PostgresCollection.Name)]
public sealed class ReportTests(PostgresFixture postgres) : ModerationTestBase(postgres)
{
    [Fact]
    public async Task Rules_Validation_Duplicates_Ownership()
    {
        var (owner, _, _) = await F.User();
        var id = Id(await F.PublishEffect(owner, "Soft Dreamy Glow"));
        var draft = Id(await F.Create(owner, "Crisp Neon Wave", tags: ["neon", "wave"]));
        var (reporter, _, _) = await F.User("T1");

        (await Report(owner, id)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Report(reporter, id, "nonsense")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Report(reporter, id, note: new string('x', 501))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Report(reporter, draft)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var created = await Report(reporter, id, "low_quality", "meh");
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await created.Json(Ct);
        (body["reason"]!.GetValue<string>(), body["status"]!.GetValue<string>()).ShouldBe(("low_quality", "open"));
        var duplicate = await Report(reporter, id);
        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await duplicate.Json(Ct))["code"]!.GetValue<string>().ShouldBe("report.already_reported");

        foreach (var reason in new[] { "spam", "broken", "offensive", "copyright", "malware", "other" })
        {
            var (other, _, _) = await F.User("T0");
            (await Report(other, id, reason)).StatusCode.ShouldBe(HttpStatusCode.Created);
        }
    }

    [Fact]
    public async Task AutoHide_ThreeDistinctTrustedReporters_NotUntrustedOnes()
    {
        var (owner, _, _) = await F.User();
        var id = Id(await F.PublishEffect(owner, "Warm Film Grain", ["film", "grain"]));

        foreach (var (client, _, _) in await Users(5, "T0")) (await Report(client, id)).StatusCode.ShouldBe(HttpStatusCode.Created);
        var trusted = await Users(3, "T1");
        await Report(trusted[0].Client, id);
        await Report(trusted[1].Client, id);
        (await Row(id)).Status.ShouldBe("published");

        await Report(trusted[2].Client, id);
        var row = await Row(id);
        (row.Status, row.Reason).ShouldBe(("hidden", "community_reported"));
        (await Audit(Guid.Parse(id))).ShouldBe(["auto_hide"]);
        (await F.Db(db => db.CleanupSchedules.CountAsync(c => c.ItemId == Guid.Parse(id), Ct))).ShouldBe(1);
    }

    [Fact]
    public async Task OwnerSeesHiddenItemWithReason_OthersDont()
    {
        var (owner, ownerHandle, _) = await F.User();
        var id = Id(await F.PublishEffect(owner, "Retro Tape Blur"));
        var draft = Id(await F.Create(owner, "Cool Pixel Shake", tags: ["pixel", "shake"]));
        foreach (var (client, _, _) in await Users(3, "T2")) await Report(client, id);
        (await Row(id)).Status.ShouldBe("hidden");

        var ownDetail = await Detail(owner, id);
        (ownDetail["status"]!.GetValue<string>(), ownDetail["visibilityReason"]!.GetValue<string>()).ShouldBe(("hidden", "community_reported"));

        var (stranger, _, _) = await F.User("T1");
        (await stranger.GetAsync($"/api/v1/items/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await F.Factory.Browser(cookies: false).GetAsync($"/api/v1/items/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await F.Ok(await F.Factory.Browser(cookies: false).GetAsync($"/api/v1/items?owner={ownerHandle}", Ct)))["items"]!.AsArray().Count.ShouldBe(0);
        (await F.Ok(await stranger.GetAsync($"/api/v1/items?owner={ownerHandle}", Ct)))["items"]!.AsArray().Count.ShouldBe(0);
        (await stranger.GetAsync($"/api/v1/items?owner={ownerHandle}&status=hidden", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var mine = (await F.Ok(await owner.GetAsync("/api/v1/me/items", Ct)))["items"]!.AsArray();
        mine.Count.ShouldBe(2);
        var hidden = mine.Single(i => i!["id"]!.GetValue<string>() == id)!;
        (hidden["status"]!.GetValue<string>(), hidden["visibilityReason"]!.GetValue<string>()).ShouldBe(("hidden", "community_reported"));
        hidden["purgeAt"].ShouldNotBeNull();
        (await F.Ok(await owner.GetAsync("/api/v1/me/items?status=draft", Ct)))["items"]!.AsArray().Single()!["id"]!.GetValue<string>().ShouldBe(draft);
        (await F.Ok(await owner.GetAsync("/api/v1/me/items?status=hidden", Ct)))["items"]!.AsArray().Single()!["id"]!.GetValue<string>().ShouldBe(id);
        (await owner.GetAsync("/api/v1/me/items?status=removed", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await F.Factory.Browser(cookies: false).GetAsync("/api/v1/me/items", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var page = await F.Ok(await owner.GetAsync("/api/v1/me/items?limit=1", Ct));
        page["items"]!.AsArray().Count.ShouldBe(1);
        var next = page["nextCursor"]!.GetValue<string>();
        (await F.Ok(await owner.GetAsync($"/api/v1/me/items?limit=1&cursor={next}", Ct)))["items"]!.AsArray().Count.ShouldBe(1);
    }
}
