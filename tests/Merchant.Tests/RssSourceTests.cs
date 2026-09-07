using Merchant.Sources;
using Xunit;

namespace Merchant.Tests;

/// <summary>
/// The parser against the three syndication formats merchant actually receives. These are captured
/// documents, not hand-written ones: the point is to catch the day a feed changes shape.
/// </summary>
public class RssSourceTests
{
    [Fact]
    public void Reads_rss_1_where_items_sit_outside_the_channel()
    {
        // Steam's chart is RDF. Items are siblings of <channel>, so a parser that walks
        // rss/channel/item finds nothing at all here.
        IReadOnlyList<FeedItem> items = RssSource.Parse(Fixtures.Read(Fixtures.SteamWeekly));

        Assert.Equal(10, items.Count);
        Assert.All(items, item =>
        {
            Assert.StartsWith("#", item.Title);
            Assert.StartsWith("https://store.steampowered.com/app/", item.Url);
            Assert.NotNull(item.Published);
        });
    }

    [Fact]
    public void Falls_back_to_the_link_when_an_entry_has_no_guid()
    {
        // RSS 1.0 has no <guid>; identity has to come from rdf:about or the link, or every sweep
        // would treat the whole chart as new and repost it.
        IReadOnlyList<FeedItem> items = RssSource.Parse(Fixtures.Read(Fixtures.SteamWeekly));

        Assert.All(items, item => Assert.False(string.IsNullOrWhiteSpace(item.Id)));
        Assert.Equal(items.Count, items.Select(i => i.Id).Distinct().Count());
    }

    [Fact]
    public void Pulls_capsule_art_out_of_an_html_body()
    {
        IReadOnlyList<FeedItem> items = RssSource.Parse(Fixtures.Read(Fixtures.SteamWeekly));

        Assert.Contains(items, item =>
            item.ImageUrl is not null && item.ImageUrl.StartsWith("https://", StringComparison.Ordinal));
    }

    [Fact]
    public void Reads_rss_2_and_decodes_entities_in_titles()
    {
        IReadOnlyList<FeedItem> items = RssSource.Parse(Fixtures.Read(Fixtures.ItadGiveaways));

        Assert.NotEmpty(items);
        Assert.All(items, item => Assert.False(string.IsNullOrWhiteSpace(item.Title)));

        // The feed writes apostrophes as &#039;, and Discord renders neither entities nor tags.
        Assert.DoesNotContain(items, item => item.Title.Contains("&#0", StringComparison.Ordinal));
        Assert.DoesNotContain(items, item => item.Title.Contains("&amp;", StringComparison.Ordinal));
    }

    [Fact]
    public void Strips_markup_out_of_summaries()
    {
        IReadOnlyList<FeedItem> items = RssSource.Parse(Fixtures.Read(Fixtures.ItadGiveaways));

        Assert.All(items, item =>
        {
            if (item.Summary is not null)
            {
                Assert.DoesNotContain('<', item.Summary);
                Assert.DoesNotContain('>', item.Summary);
            }
        });
    }

    [Fact]
    public void Reads_atom_where_the_link_is_an_attribute()
    {
        IReadOnlyList<FeedItem> items = RssSource.Parse(Fixtures.Read(Fixtures.RedditGameDeals));

        Assert.NotEmpty(items);
        Assert.All(items, item =>
            Assert.StartsWith("http", item.Url, StringComparison.Ordinal));
    }

    [Fact]
    public void Caps_a_summary_so_an_embed_cannot_overflow()
    {
        IReadOnlyList<FeedItem> items = RssSource.Parse(Fixtures.Read(Fixtures.RedditGameDeals));

        Assert.All(items, item => Assert.True((item.Summary?.Length ?? 0) <= 281));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not xml at all")]
    [InlineData("<html><body>a 404 page</body></html>")]
    public void Returns_nothing_rather_than_throwing_on_junk(string body)
    {
        // Feeds serve HTML error pages under a 200 often enough that this is the normal path,
        // not a defensive one.
        Assert.Empty(RssSource.Parse(body));
    }
}
