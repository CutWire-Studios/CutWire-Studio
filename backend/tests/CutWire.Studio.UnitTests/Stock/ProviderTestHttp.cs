using System.Net;
using System.Text;

namespace CutWire.Studio.UnitTests.Stock;

internal sealed record SeenRequest(Uri Uri, Dictionary<string, string> Headers)
{
    public System.Collections.Specialized.NameValueCollection Query => System.Web.HttpUtility.ParseQueryString(Uri.Query);
}

/// <summary>Routes by URL substring (first registered match wins), records every request, and never touches the network.</summary>
internal sealed class FakeHandler : HttpMessageHandler
{
    private readonly List<(string Match, Func<HttpResponseMessage> Respond)> _routes = [];
    public List<SeenRequest> Seen { get; } = [];

    public FakeHandler Json(string match, string fixture, HttpStatusCode status = HttpStatusCode.OK) =>
        Add(match, () => new HttpResponseMessage(status) { Content = new StringContent(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "stock", fixture)), Encoding.UTF8, "application/json") });

    public FakeHandler Bytes(string match, byte[] body) => Add(match, () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });

    public FakeHandler Status(string match, HttpStatusCode status) => Add(match, () => new HttpResponseMessage(status) { Content = new StringContent("{}") });

    private FakeHandler Add(string match, Func<HttpResponseMessage> respond)
    {
        _routes.Add((match, respond));
        return this;
    }

    public HttpClient Client(Action<HttpClient>? configure = null)
    {
        var c = new HttpClient(this);
        configure?.Invoke(c);
        return c;
    }

    public SeenRequest Last => Seen[^1];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
        Seen.Add(new SeenRequest(request.RequestUri!, headers));
        foreach (var (match, respond) in _routes)
            if (request.RequestUri!.ToString().Contains(match, StringComparison.Ordinal)) return Task.FromResult(respond());
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("no route for " + request.RequestUri) });
    }
}

internal sealed class TempWorkspace : IDisposable
{
    public string Dir { get; } = Directory.CreateTempSubdirectory("cwstock").FullName;

    public CutWire.Studio.Application.Stock.StockWorkspace Create(HttpClient http) => new(Dir, http, 10_000_000);

    public void Dispose() => Directory.Delete(Dir, true);
}
