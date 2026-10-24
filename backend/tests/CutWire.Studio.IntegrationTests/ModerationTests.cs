using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using CutWire.Studio.Application.Moderation;
using CutWire.Studio.Infrastructure.Jobs;
using CutWire.Studio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace CutWire.Studio.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class ModerationTests(PostgresFixture postgres) : ModerationTestBase(postgres)
{
    private async Task<(string Id, string Package, HttpClient Owner, string Handle, Guid OwnerId)> HiddenItem(string title = "Soft Dreamy Glow")
    {
        var (owner, handle, ownerId) = await F.User();
        var item = await F.PublishEffect(owner, title);
        var id = Id(item);
        foreach (var (client, _, _) in await Users(3, "T1")) await Report(client, id);
        (await Row(id)).Status.ShouldBe("hidden");
        var sha = await F.Db(db => db.ItemVersions.AsNoTracking().Where(v => v.ItemId == Guid.Parse(id)).Select(v => v.PackageSha256).SingleAsync(Ct));
        return (id, sha.Value, owner, handle, ownerId);
    }

    private Task<HttpResponseMessage> Post(HttpClient c, string url, object? body = null) => c.PostJson($"/api/v1{url}", body, Ct);

    [Fact]
    public async Task Authorization_NonStaffGet403_AnonymousGet401_AdminOnlyRoles()
    {
        var (user, _, userId) = await F.User("T3");
        var (mod, _) = await Staff("moderator");
        var (admin, _) = await Staff("admin");
        var anon = F.Factory.Browser(cookies: false);

        foreach (var url in new[] { "/api/v1/mod/queue?type=hidden", "/api/v1/mod/users", "/api/v1/mod/audit" })
        {
            (await anon.GetAsync(url, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            (await user.GetAsync(url, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await mod.GetAsync(url, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
            (await admin.GetAsync(url, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        (await Post(user, $"/mod/items/{Guid.NewGuid()}/restore")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Post(user, "/mod/items/bulk", new { ids = new[] { Guid.NewGuid() }, action = "restore" })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Post(user, $"/mod/users/{userId}/ban", new { permanent = true, reason = "x" })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await Post(mod, $"/mod/users/{userId}/roles", new { moderator = true })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Post(admin, $"/mod/users/{userId}/roles", new { moderator = true })).StatusCode.ShouldBe(HttpStatusCode.OK);
        var users = await F.Ok(await admin.GetAsync($"/api/v1/mod/users?q={userId}", Ct));
        users["users"]!.AsArray().Count.ShouldBe(0);
        var listed = (await F.Ok(await admin.GetAsync("/api/v1/mod/users?limit=100", Ct)))["users"]!.AsArray().Single(u => u!["id"]!.GetValue<string>() == userId.ToString())!;
        listed["roles"]!.AsArray().Select(r => r!.GetValue<string>()).ShouldBe(["moderator"]);
        (await Post(admin, $"/mod/users/{userId}/roles", new { moderator = false })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Audit(userId)).ShouldBe(["grant_moderator", "revoke_moderator"]);
    }

    [Fact]
    public async Task Queues_Reported_Hidden_Flagged()
    {
        var (mod, _) = await Staff("moderator");
        var (owner, _, _) = await F.User();
        var reported = Id(await F.PublishEffect(owner, "Crisp Neon Wave", ["neon", "wave"]));
        var (r1, _, _) = await F.User("T1");
        var (r2, _, _) = await F.User("T0");
        await Report(r1, reported, "spam", "buy now");
        await Report(r2, reported, "broken");
        var hidden = await HiddenItem("Warm Film Grain");

        var queue = await F.Ok(await mod.GetAsync("/api/v1/mod/queue?type=reported", Ct));
        var entry = queue["entries"]!.AsArray().Single(e => e!["item"]!["id"]!.GetValue<string>() == reported)!;
        entry["reports"]!.AsArray().Count.ShouldBe(2);
        entry["reasonCounts"]!["spam"]!.GetValue<int>().ShouldBe(1);
        entry["item"]!["title"]!.GetValue<string>().ShouldBe("Crisp Neon Wave");

        var hiddenQueue = await F.Ok(await mod.GetAsync("/api/v1/mod/queue?type=hidden", Ct));
        var h = hiddenQueue["entries"]!.AsArray().Single()!;
        h["item"]!["id"]!.GetValue<string>().ShouldBe(hidden.Id);
        h["visibilityReason"]!.GetValue<string>().ShouldBe("community_reported");
        h["purgeAt"].ShouldNotBeNull();

        (await mod.GetAsync("/api/v1/mod/queue?type=nope", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var thumb = CatalogFixture.Thumb(512, 288, 777);
        var (owner2, _, _) = await F.User();
        var original = await F.PublishEffect(owner2, "Cool Pixel Shake", ["pixel", "shake"], thumbnail: thumb);
        var copy = await F.PublishEffect(owner, "Bright Mirror Flash", ["mirror", "flash"], thumbnail: thumb);
        var flagged = (await F.Ok(await mod.GetAsync("/api/v1/mod/queue?type=flagged", Ct)))["entries"]!.AsArray();
        var flagEntry = flagged.Single(e => e!["item"]!["id"]!.GetValue<string>() == Id(copy))!;
        var flag = flagEntry["flags"]![0]!;
        flag["code"]!.GetValue<string>().ShouldBe("similar_thumbnail");
        flag["matchedItem"]!["id"]!.GetValue<string>().ShouldBe(Id(original));

        (await Post(mod, $"/mod/flags/{flag["id"]!.GetValue<string>()}/resolve", new { note = "different" })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Post(mod, $"/mod/flags/{flag["id"]!.GetValue<string>()}/resolve")).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await F.Ok(await mod.GetAsync("/api/v1/mod/queue?type=flagged", Ct)))["entries"]!.AsArray().Count.ShouldBe(0);
        (await Audit(Guid.Parse(Id(copy)))).ShouldBe(["flag_resolved"]);
    }

    [Fact]
    public async Task Restore_CancelsCleanup_DismissesReports_AndWritesAudit()
    {
        var (mod, modId) = await Staff("moderator");
        var item = await HiddenItem();

        (await Post(mod, $"/mod/items/{item.Id}/restore", new { dismissReports = true, reason = "false positive" })).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await Row(item.Id)).Status.ShouldBe("published");
        (await Detail(item.Owner, item.Id))["visibilityReason"].ShouldBeNull();
        (await F.Db(db => db.CleanupSchedules.CountAsync(c => c.ItemId == Guid.Parse(item.Id), Ct))).ShouldBe(0);
        (await F.Db(db => db.Reports.CountAsync(r => r.ItemId == Guid.Parse(item.Id) && r.Status == CutWire.Studio.Domain.Moderation.ReportStatus.Dismissed, Ct))).ShouldBe(3);
        (await Post(mod, $"/mod/items/{item.Id}/restore")).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Audit(Guid.Parse(item.Id))).ShouldBe(["auto_hide", "restore"]);

        var entries = (await F.Ok(await mod.GetAsync($"/api/v1/mod/audit?target={item.Id}", Ct)))["entries"]!.AsArray();
        entries.Count.ShouldBe(2);
        var restore = entries[0]!;
        (restore["action"]!.GetValue<string>(), restore["auto"]!.GetValue<bool>(), restore["reason"]!.GetValue<string>()).ShouldBe(("restore", false, "false positive"));
        entries[1]!["actor"].ShouldBeNull();
        entries[1]!["auto"]!.GetValue<bool>().ShouldBeTrue();
        (await F.Ok(await mod.GetAsync($"/api/v1/mod/audit?actor={modId}", Ct)))["entries"]!.AsArray().Count.ShouldBe(1);
    }

    [Fact]
    public async Task Remove_ReleasesBlobs_RequiresReason_SchedulePurge_AndPurgeJob()
    {
        var (mod, _) = await Staff("moderator");
        var item = await HiddenItem();
        (await RefCount(item.Package)).ShouldBe(1);

        (await Post(mod, $"/mod/items/{item.Id}/remove", new { reason = "" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Post(mod, $"/mod/items/{item.Id}/schedule-purge", new { days = 0 })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Post(mod, $"/mod/items/{item.Id}/schedule-purge", new { days = 3 })).StatusCode.ShouldBe(HttpStatusCode.OK);
        var purgeAt = await F.Db(db => db.CleanupSchedules.AsNoTracking().Select(c => c.PurgeAt).SingleAsync(Ct));
        (purgeAt - DateTimeOffset.UtcNow).TotalDays.ShouldBeInRange(2.9, 3.1);
        (await Detail(item.Owner, item.Id))["purgeAt"].ShouldNotBeNull();

        await F.Sql("update cleanup_schedule set purge_at = now() - interval '1 minute'");
        await F.Factory.Services.GetRequiredService<IEnumerable<IRecurringJob>>().OfType<CleanupPurgeJob>().Single().RunAsync(Ct);

        (await Row(item.Id)).Status.ShouldBe("removed");
        (await RefCount(item.Package)).ShouldBe(0);
        (await Audit(Guid.Parse(item.Id))).ShouldBe(["auto_hide", "schedule_purge", "purge"]);
        (await F.Db(db => db.ModerationActions.AsNoTracking().SingleAsync(a => a.Action == "purge", Ct))).Auto.ShouldBeTrue();
        (await item.Owner.GetAsync($"/api/v1/items/{item.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var published = Id(await F.PublishEffect((await F.User()).Client, "Retro Tape Blur"));
        var sha = await F.Db(db => db.ItemVersions.AsNoTracking().Where(v => v.ItemId == Guid.Parse(published)).Select(v => v.PackageSha256).SingleAsync(Ct));
        (await Post(mod, $"/mod/items/{published}/schedule-purge", new { days = 1 })).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Post(mod, $"/mod/items/{published}/remove", new { reason = "malware" })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Row(published)).Status.ShouldBe("removed");
        (await RefCount(sha.Value)).ShouldBe(0);
        (await Post(mod, $"/mod/items/{published}/remove", new { reason = "again" })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PurgeJob_LeavesRestoredAndFutureItems()
    {
        var future = await HiddenItem("Crisp Neon Wave");
        var past = await HiddenItem("Warm Film Grain");
        await F.Sql($"update cleanup_schedule set purge_at = now() - interval '1 minute' where item_id = '{past.Id}'");
        await F.Sql($"update items set status = 'published', visibility_reason = null where id = '{past.Id}'");

        await using var scope = F.Factory.Services.CreateAsyncScope();
        var (purged, _) = await scope.ServiceProvider.GetRequiredService<ModerationMaintenance>().CleanupAsync(Ct);

        purged.ShouldBe(0);
        (await Row(future.Id)).Status.ShouldBe("hidden");
        (await Row(past.Id)).Status.ShouldBe("published");
    }

    [Fact]
    public async Task ReportResolution_UpholdDemotesOwner_Dismiss_AndBulk()
    {
        var (mod, _) = await Staff("moderator");
        var (owner, _, ownerId) = await F.User("T2");
        var item = Id(await F.PublishEffect(owner, "Soft Dreamy Glow"));
        var (reporter, _, _) = await F.User("T1");
        var (reporter2, _, _) = await F.User("T1");
        await Report(reporter, item, "offensive");
        await Report(reporter2, item, "spam");
        var reports = await F.Db(db => db.Reports.AsNoTracking().Where(r => r.ItemId == Guid.Parse(item)).OrderBy(r => r.CreatedAt).Select(r => r.Id).ToListAsync(Ct));

        (await Post(mod, $"/mod/reports/{reports[0]}/uphold", new { note = "confirmed" })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await F.Db(db => db.UserProfiles.AsNoTracking().Where(p => p.Id == ownerId).Select(p => p.TrustLevel).SingleAsync(Ct))).ToString().ShouldBe("T1");
        (await Post(mod, $"/mod/reports/{reports[0]}/uphold")).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Post(mod, $"/mod/reports/{reports[1]}/dismiss")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await F.Db(db => db.UserProfiles.AsNoTracking().Where(p => p.Id == ownerId).Select(p => p.TrustLevel).SingleAsync(Ct))).ToString().ShouldBe("T1");
        (await Audit(Guid.Parse(item))).ShouldBe(["report_upheld", "report_dismissed"]);
        (await F.Ok(await mod.GetAsync("/api/v1/mod/queue?type=reported", Ct)))["entries"]!.AsArray().Count.ShouldBe(0);

        var a = await HiddenItem("Crisp Neon Wave");
        var b = await HiddenItem("Warm Film Grain");
        var c = await HiddenItem("Retro Tape Blur");
        var bulk = await F.Ok(await Post(mod, "/mod/items/bulk", new { ids = new[] { a.Id, b.Id, Guid.NewGuid().ToString() }, action = "restore", reason = "ok" }));
        bulk["succeeded"]!.AsArray().Count.ShouldBe(2);
        bulk["failed"]!.AsArray().Single()!["code"]!.GetValue<string>().ShouldBe("item.not_found");
        (await Row(a.Id)).Status.ShouldBe("published");
        (await Row(b.Id)).Status.ShouldBe("published");

        (await Post(mod, "/mod/items/bulk", new { ids = new[] { c.Id }, action = "remove" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Post(mod, "/mod/items/bulk", new { ids = Array.Empty<string>(), action = "restore" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await F.Ok(await Post(mod, "/mod/items/bulk", new { ids = new[] { c.Id }, action = "remove", reason = "spam wave" })))["succeeded"]!.AsArray().Count.ShouldBe(1);
        (await Row(c.Id)).Status.ShouldBe("removed");
        (await RefCount(c.Package)).ShouldBe(0);
        (await Audit(Guid.Parse(a.Id))).ShouldBe(["auto_hide", "restore"]);
    }

    [Fact]
    public async Task Ban_HidesItems_Unban_Restores_OnlyOwnerBanned()
    {
        var (mod, _) = await Staff("moderator");
        var (admin, adminId) = await Staff("admin");
        var (owner, _, ownerId) = await F.User();
        var one = Id(await F.PublishEffect(owner, "Soft Dreamy Glow"));
        var two = Id(await F.PublishEffect(owner, "Crisp Neon Wave", ["neon", "wave"]));
        var draft = Id(await F.Create(owner, "Warm Film Grain", tags: ["film", "grain"]));
        foreach (var (client, _, _) in await Users(3, "T1")) await Report(client, two);
        (await Row(two)).Reason.ShouldBe("community_reported");

        (await Post(mod, $"/mod/users/{ownerId}/ban", new { permanent = true })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Post(mod, $"/mod/users/{ownerId}/ban", new { reason = "x" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Post(mod, $"/mod/users/{ownerId}/ban", new { until = DateTimeOffset.UtcNow.AddDays(-1), reason = "x" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Post(mod, $"/mod/users/{adminId}/ban", new { permanent = true, reason = "x" })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var ban = await F.Ok(await Post(mod, $"/mod/users/{ownerId}/ban", new { until = DateTimeOffset.UtcNow.AddDays(7), reason = "abuse" }));
        ban["affectedItems"]!.GetValue<int>().ShouldBe(1);
        (await Row(one)).Reason.ShouldBe("owner_banned");
        (await Row(two)).Reason.ShouldBe("community_reported");
        (await Row(draft)).Status.ShouldBe("draft");
        (await F.Db(db => db.CleanupSchedules.CountAsync(c => c.ItemId == Guid.Parse(one), Ct))).ShouldBe(0);
        (await Detail(owner, one))["visibilityReason"]!.GetValue<string>().ShouldBe("owner_banned");
        (await owner.PostJson("/api/v1/items", CatalogFixture.Metadata("Cool Pixel Shake"), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var found = (await F.Ok(await mod.GetAsync($"/api/v1/mod/users?q={ownerId}", Ct)));
        found["users"]!.AsArray().Count.ShouldBe(0);

        (await Post(admin, $"/mod/users/{ownerId}/unban", new { reason = "appeal" })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Row(one)).Status.ShouldBe("published");
        (await Row(two)).Status.ShouldBe("hidden");
        (await Audit(ownerId)).ShouldBe(["ban", "unban"]);
        (await owner.PostJson("/api/v1/items", CatalogFixture.Metadata("Cool Pixel Shake"), Ct)).StatusCode.ShouldBe(HttpStatusCode.Created);

        await Post(mod, $"/mod/users/{ownerId}/ban", new { until = DateTimeOffset.UtcNow.AddDays(1), reason = "again" });
        (await Row(one)).Status.ShouldBe("hidden");
        await F.Sql($"update user_profiles set banned_until = now() - interval '1 minute' where user_id = '{ownerId}'");
        await using var scope = F.Factory.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<ModerationMaintenance>().CleanupAsync(Ct)).Restored.ShouldBe(1);
        (await Row(one)).Status.ShouldBe("published");
    }

    [Fact]
    public async Task SetTrust_AndUserSearch()
    {
        var (mod, _) = await Staff("moderator");
        var (_, handle, userId) = await F.User("T0");

        var users = (await F.Ok(await mod.GetAsync($"/api/v1/mod/users?q={handle[..8].ToUpperInvariant()}", Ct)))["users"]!.AsArray();
        users.Single()!["handle"]!.GetValue<string>().ShouldBe(handle);
        (await Post(mod, $"/mod/users/{userId}/trust", new { level = "T3", reason = "trusted" })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Post(mod, $"/mod/users/{userId}/trust", new { level = "T9" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Post(mod, $"/mod/users/{Guid.NewGuid()}/trust", new { level = "T1" })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await F.Ok(await mod.GetAsync($"/api/v1/mod/users?q={handle}", Ct)))["users"]![0]!["trustLevel"]!.GetValue<string>().ShouldBe("T3");
        (await Audit(userId)).ShouldBe(["set_trust"]);
    }
}

[Collection(PostgresCollection.Name)]
public sealed class MaintenanceJobTests(PostgresFixture postgres) : ModerationTestBase(postgres)
{
    private T Job<T>() where T : IRecurringJob => F.Factory.Services.GetRequiredService<IEnumerable<IRecurringJob>>().OfType<T>().Single();

    private async Task<string> TrustOf(Guid id) =>
        (await F.Db(db => db.UserProfiles.AsNoTracking().Where(p => p.Id == id).Select(p => p.TrustLevel).SingleAsync(Ct))).ToString();

    [Fact]
    public async Task TrustLevels_PromoteOnly_AndRespectUpheldReports()
    {
        var (_, _, old) = await F.User("T0");
        var (_, _, young) = await F.User("T0");
        var (_, _, tainted) = await F.User("T0");
        var (_, _, stays) = await F.User("T2");
        var (_, _, banned) = await F.User("T0");
        await F.Sql($"update user_profiles set created_at = now() - interval '8 days' where user_id in ('{old}', '{tainted}', '{banned}')");
        await F.Sql($"update user_profiles set is_permanently_banned = true where user_id = '{banned}'");

        var (taintedClient, _, _) = await F.User("T3");
        var item = Id(await F.PublishEffect(taintedClient, "Soft Dreamy Glow"));
        await F.Sql($"update items set owner_id = '{tainted}' where id = '{item}'");
        var (reporter, _, _) = await F.User("T1");
        await Report(reporter, item);
        var (mod, _) = await Staff("moderator");
        var reportId = await F.Db(db => db.Reports.AsNoTracking().Select(r => r.Id).SingleAsync(Ct));
        await mod.PostJson($"/api/v1/mod/reports/{reportId}/uphold", null, Ct);

        await Job<TrustLevelJob>().RunAsync(Ct);

        (await TrustOf(old)).ShouldBe("T1");
        (await TrustOf(young)).ShouldBe("T0");
        (await TrustOf(tainted)).ShouldBe("T0");
        (await TrustOf(banned)).ShouldBe("T0");
        (await TrustOf(stays)).ShouldBe("T2");
        (await Audit(old)).ShouldBe(["trust_promoted"]);
    }

    [Fact]
    public async Task TrustLevels_T1ToT2_NeedsThreePositivelyScoredPublishedItems()
    {
        var (a, _, aId) = await F.User("T1");
        var (b, _, bId) = await F.User("T1");
        var titles = new[] { ("Soft Dreamy Glow", new[] { "glow", "dream" }), ("Crisp Neon Wave", new[] { "neon", "wave" }), ("Warm Film Grain", new[] { "film", "grain" }) };
        foreach (var (title, tags) in titles)
        {
            await F.PublishEffect(a, title, tags);
            await F.PublishEffect(b, title, tags);
        }

        await F.Sql($"update items set wilson_score = 0.4 where owner_id = '{aId}'");
        await F.Sql($"update items set wilson_score = 0.4 where owner_id = '{bId}' and title <> 'Warm Film Grain'");
        await Job<TrustLevelJob>().RunAsync(Ct);

        (await TrustOf(aId)).ShouldBe("T2");
        (await TrustOf(bId)).ShouldBe("T1");
    }

    [Fact]
    public async Task HotScoreDecay_AppliesHalfLife_ToRecentPublishedItemsOnly()
    {
        var (owner, _, _) = await F.User();
        var recent = Id(await F.PublishEffect(owner, "Soft Dreamy Glow"));
        var stale = Id(await F.PublishEffect(owner, "Crisp Neon Wave", ["neon", "wave"]));
        await F.Sql($"update items set wilson_score = 0.8, hot_score = 0.8, published_at = now() - interval '7 days' where id = '{recent}'");
        await F.Sql($"update items set wilson_score = 0.8, hot_score = 0.8, published_at = now() - interval '200 days' where id = '{stale}'");

        await Job<HotScoreDecayJob>().RunAsync(Ct);

        (await F.Db(db => db.Items.AsNoTracking().Where(i => i.Id == Guid.Parse(recent)).Select(i => i.HotScore).SingleAsync(Ct))).ShouldBe(0.4, 0.001);
        (await F.Db(db => db.Items.AsNoTracking().Where(i => i.Id == Guid.Parse(stale)).Select(i => i.HotScore).SingleAsync(Ct))).ShouldBe(0.8);
    }

    [Fact]
    public async Task DownloadEventsPrune_DeletesOnlyOldRolledUpEvents()
    {
        var (owner, _, _) = await F.User();
        var id = Id(await F.PublishEffect(owner, "Soft Dreamy Glow"));
        await F.Sql($"""
            insert into download_events (item_id, ip_hash, at, rolled_up_at) values
              ('{id}', 'a', now() - interval '100 days', now() - interval '99 days'),
              ('{id}', 'b', now() - interval '100 days', null),
              ('{id}', 'c', now() - interval '10 days', now() - interval '9 days')
            """);

        await Job<DownloadEventsPruneJob>().RunAsync(Ct);

        (await F.Db(db => db.DownloadEvents.AsNoTracking().OrderBy(e => e.IpHash).Select(e => e.IpHash).ToListAsync(Ct))).ShouldBe(["b", "c"]);
    }
}
