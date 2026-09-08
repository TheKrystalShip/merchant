using Discord.WebSocket;
using Merchant.Discord;
using Merchant.Feeds;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Merchant.Tests;

/// <summary>
/// The sweep as the network sees it. Subscriptions multiply with servers and channels; the
/// upstreams merchant reads do not, and several of them rate-limit.
/// </summary>
public class SweeperTests : IDisposable
{
    private const string Feed = "https://example.test/deals.rss";

    private readonly string _path = Path.Combine(
        Path.GetTempPath(), $"merchant-sweep-{Guid.NewGuid():N}.db");

    private readonly Merchant.Storage.Ledger _ledger;
    private readonly CountingHandler _network = new();
    private readonly DiscordSocketClient _discord = new();

    public SweeperTests() => _ledger = new Merchant.Storage.Ledger(_path);

    private Sweeper Build()
    {
        FeedCatalog catalog = Settings.From($$"""
            {
              "feeds": {
                "deals": {
                  "label": "Deals",
                  "description": "Deals.",
                  "source": { "type": "rss", "urls": [ "{{Feed}}?region={region}" ] }
                }
              }
            }
            """, out _);

        return new Sweeper(
            _discord,
            _ledger,
            new OneClient(_network),
            catalog,
            new BotOptions
            {
                DatabasePath = _path,
                SweepInterval = TimeSpan.FromMinutes(30),
                UserAgent = "merchant/test",
            },
            NullLogger<Sweeper>.Instance);
    }

    [Fact]
    public async Task Every_channel_waiting_on_one_feed_is_one_request()
    {
        // Two servers and three channels, all wanting the same feed in the same region.
        _ledger.Subscribe(1, 10, "deals", Cadence.Daily, null);
        _ledger.Subscribe(1, 11, "deals", Cadence.Daily, null);
        _ledger.Subscribe(2, 20, "deals", Cadence.Weekly, null);

        await Build().SweepAsync(CancellationToken.None);

        Assert.Equal(1, _network.Requests);

        // …and each of them still got the items filed against its own ledger.
        Assert.All(_ledger.All(), s => Assert.True(_ledger.PendingCount(s.Id) > 0));
    }

    [Fact]
    public async Task Two_regions_are_two_requests_because_they_are_two_urls()
    {
        _ledger.Subscribe(1, 10, "deals", Cadence.Daily, null);
        _ledger.Subscribe(2, 20, "deals", Cadence.Daily, null);
        _ledger.SaveSettings(new GuildSettings(2, "ES", "EUR"));

        await Build().SweepAsync(CancellationToken.None);

        Assert.Equal(2, _network.Requests);
        Assert.Contains(_network.Urls, url => url.EndsWith("region=US", StringComparison.Ordinal));
        Assert.Contains(_network.Urls, url => url.EndsWith("region=ES", StringComparison.Ordinal));
    }

    private sealed class OneClient(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        private int _requests;

        public int Requests => Volatile.Read(ref _requests);

        public List<string> Urls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref _requests);

            lock (Urls)
            {
                Urls.Add(request.RequestUri!.ToString());
            }

            return Task.FromResult(new HttpResponseMessage
            {
                Content = new StringContent("""
                    <rss version="2.0"><channel>
                      <item>
                        <title>A Deal</title>
                        <link>https://example.test/a</link>
                        <pubDate>Tue, 08 Sep 2026 10:00:00 GMT</pubDate>
                      </item>
                    </channel></rss>
                    """),
            });
        }
    }

    public void Dispose()
    {
        _ledger.Dispose();
        _discord.Dispose();
        _network.Dispose();

        foreach (string file in (string[])[_path, $"{_path}-wal", $"{_path}-shm"])
        {
            File.Delete(file);
        }

        GC.SuppressFinalize(this);
    }
}
