using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using CutWire.Studio.Domain.Blobs;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace CutWire.Studio.IntegrationTests;

public abstract class ModerationTestBase : IDisposable
{
    protected readonly CatalogFixture F;

    protected ModerationTestBase(PostgresFixture postgres, Action<Microsoft.AspNetCore.Hosting.IWebHostBuilder>? configure = null) =>
        F = new CatalogFixture(postgres, configure);

    protected CancellationToken Ct => F.Ct;

    public void Dispose() => F.Dispose();

    protected async Task<(HttpClient Client, Guid Id)> Staff(string role)
    {
        var email = AuthTestSupport.NewEmail();
        var client = F.Factory.Browser();
        var userId = await F.Factory.SignUpAndVerify(client, email, AuthTestSupport.NewHandle(), Ct);
        await F.Sql($"insert into user_roles (user_id, role_id) select '{userId}', id from roles where name = '{role}'");
        (await client.PostJson("/api/v1/account/sign-in", new { email, password = AuthTestSupport.Password }, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        return (client, Guid.Parse(userId));
    }

    protected async Task<List<(HttpClient Client, string Handle, Guid Id)>> Users(int count, string trust)
    {
        var users = new List<(HttpClient, string, Guid)>();
        for (var i = 0; i < count; i++) users.Add(await F.User(trust));
        return users;
    }

    protected Task<HttpResponseMessage> Vote(HttpClient client, string itemId, int value) =>
        client.PutAsJsonAsync($"/api/v1/items/{itemId}/vote", new { value }, Ct);

    protected Task<HttpResponseMessage> Report(HttpClient client, string itemId, string reason = "spam", string? note = null) =>
        client.PostJson($"/api/v1/items/{itemId}/reports", new { reason, note }, Ct);

    protected static string Id(JsonNode item) => item["id"]!.GetValue<string>();

    protected async Task<JsonNode> Detail(HttpClient client, string itemId) => await F.Ok(await client.GetAsync($"/api/v1/items/{itemId}", Ct));

    protected async Task<(string Status, string? Reason, double Up, double Down)> Row(string itemId)
    {
        var id = Guid.Parse(itemId);
        var item = await F.Db(db => db.Items.AsNoTracking().SingleAsync(i => i.Id == id, Ct));
        return (item.Status.ToString().ToLowerInvariant(), item.VisibilityReason, item.ScoreUp, item.ScoreDown);
    }

    protected Task<int> RefCount(string sha) => F.Db(async db => (await db.Blobs.SingleAsync(b => b.Id == new Sha256Hash(sha), Ct)).RefCount);

    protected async Task<List<string>> Audit(Guid target) =>
        (await F.Db(db => db.ModerationActions.AsNoTracking().Where(a => a.TargetId == target || a.ItemId == target)
            .OrderBy(a => a.Id).Select(a => a.Action).ToListAsync(Ct)));
}
