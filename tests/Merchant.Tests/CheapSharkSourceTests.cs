using System.Net;
using Merchant.Sources;
using Xunit;

namespace Merchant.Tests;

/// <summary>
/// The priced source, driven against a captured CheapShark response so the field mapping is
/// pinned without a network call.
/// </summary>
public class CheapSharkSourceTests
{
    private static CheapSharkSource Source(string body) =>
        new(new HttpClient(new CannedHandler(body)), upperPrice: 10, minMetacritic: null,
            sortBy: "Deal Rating");

    [Fact]
    public async Task Maps_prices_discounts_and_stores()
    {
        IReadOnlyList<FeedItem> items =
            await Source(Fixtures.Read(Fixtures.CheapSharkDeals)).FetchAsync(CancellationToken.None);

        Assert.NotEmpty(items);

        FeedItem first = items[0];
        Assert.StartsWith("cheapshark:", first.Id);
        Assert.StartsWith("$", first.Price);
        Assert.StartsWith("https://www.cheapshark.com/redirect?dealID=", first.Url);
        Assert.NotNull(first.Store);
    }

    [Fact]
    public async Task Every_item_the_ceiling_promised_is_under_it()
    {
        IReadOnlyList<FeedItem> items =
            await Source(Fixtures.Read(Fixtures.CheapSharkDeals)).FetchAsync(CancellationToken.None);

        Assert.All(items, item =>
        {
            Assert.NotNull(item.Price);
            Assert.True(decimal.Parse(item.Price!.TrimStart('$')) <= 10m,
                $"{item.Title} is priced {item.Price}, above the ceiling requested.");
        });
    }

    [Fact]
    public async Task Ids_are_distinct_so_the_ledger_can_key_on_them()
    {
        IReadOnlyList<FeedItem> items =
            await Source(Fixtures.Read(Fixtures.CheapSharkDeals)).FetchAsync(CancellationToken.None);

        Assert.Equal(items.Count, items.Select(i => i.Id).Distinct().Count());
    }

    [Fact]
    public async Task A_failed_request_yields_nothing_rather_than_throwing()
    {
        // One unreachable source must cost its own items and not the sweep.
        CheapSharkSource source = new(
            new HttpClient(new CannedHandler("boom", HttpStatusCode.ServiceUnavailable)),
            upperPrice: 10, minMetacritic: null, sortBy: "Deal Rating");

        Assert.Empty(await source.FetchAsync(CancellationToken.None));
    }

    /// <summary>Answers every request with the same canned body.</summary>
    private sealed class CannedHandler(string body, HttpStatusCode status = HttpStatusCode.OK)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
    }
}
