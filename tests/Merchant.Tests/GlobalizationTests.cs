using System.Globalization;
using Merchant.Sources;
using Xunit;

namespace Merchant.Tests;

/// <summary>
/// Globalization has to stay on. This is a regression test for a real failure, not a hypothetical:
/// with <c>InvariantGlobalization</c> enabled the bot connected, registered its commands and
/// reported itself healthy, then threw on the first GUILD_CREATE and left every command broken.
/// </summary>
public class GlobalizationTests
{
    [Theory]
    [InlineData("en-US")]
    [InlineData("en-GB")]
    [InlineData("es-ES")]
    [InlineData("de")]
    public void A_guild_locale_can_be_constructed(string locale)
    {
        // Discord.Net builds one of these from every guild's preferred_locale while handling
        // GUILD_CREATE. Under invariant globalization the constructor throws
        // CultureNotFoundException, the dispatch fails, the guild never enters the client's cache,
        // and every command then fails on a null Context.Guild — which is exactly how this
        // presented: "Unknown Guild" in the log, NullReferenceException in the command.
        CultureInfo culture = new(locale);

        Assert.Equal(locale, culture.Name);
    }

    [Theory]
    [InlineData("fa-IR")]   // Persian calendar by default
    [InlineData("th-TH")]   // Buddhist calendar by default
    [InlineData("ar-SA")]   // Umm al-Qura calendar by default
    [InlineData("en-US")]
    public void A_feed_date_reads_the_same_whatever_the_host_locale_is(string locale)
    {
        // The other half of keeping globalization on: the process picks up whatever culture the
        // host has, and a feed's dates are English and Gregorian regardless. Read against the
        // current culture, "Tue, 08 Sep 2026" is refused under fa-IR and th-TH and parsed as a
        // Persian date under some others — either way the item loses its place in the ordering.
        CultureInfo original = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(locale);

            IReadOnlyList<FeedItem> items = Merchant.Sources.RssSource.Parse("""
                <rss version="2.0"><channel>
                  <item>
                    <title>A Game</title>
                    <link>https://example.test/a</link>
                    <pubDate>Tue, 08 Sep 2026 10:00:00 GMT</pubDate>
                  </item>
                </channel></rss>
                """);

            FeedItem item = Assert.Single(items);
            Assert.Equal(
                new DateTimeOffset(2026, 9, 8, 10, 0, 0, TimeSpan.Zero),
                item.Published!.Value.ToUniversalTime());
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Theory]
    [InlineData("de-DE")]   // comma for the decimal point, dot for the group separator
    [InlineData("es-ES")]
    [InlineData("fa-IR")]   // Eastern Arabic digits
    [InlineData("en-US")]
    public async Task A_price_reads_the_same_whatever_the_host_locale_is(string locale)
    {
        // What merchant posts is a USD price, and a number written in the host's own convention is
        // simply a different number: under de-DE the same deal reads "$3,49". Nobody setting this
        // bot up ever sees the host's locale, so the output cannot depend on it.
        CultureInfo original = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(locale);

            IReadOnlyList<FeedItem> items = await CheapSharkSourceTests
                .Source(Fixtures.Read(Fixtures.CheapSharkDeals))
                .FetchAsync(CancellationToken.None);

            Assert.All(items, item =>
            {
                Assert.Matches(@"^\$[0-9]+\.[0-9]{2}$", item.Price);

                if (item.Summary is { Length: > 0 } summary)
                {
                    Assert.DoesNotContain(',', summary.Split('%')[0]);
                    Assert.Matches("^[^0-9]*[0-9]", summary);
                }
            });
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void The_runtime_is_not_in_invariant_mode()
    {
        // The direct assertion, in case a future runtime stops throwing and starts silently
        // handing back the invariant culture instead.
        Assert.NotEqual(CultureInfo.InvariantCulture, new CultureInfo("en-US"));
    }
}
