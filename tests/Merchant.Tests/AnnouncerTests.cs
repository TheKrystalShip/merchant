using Merchant.Discord;
using Merchant.Feeds;
using Merchant.Sources;
using Discord;
using Xunit;

namespace Merchant.Tests;

/// <summary>What actually lands in the channel.</summary>
public class AnnouncerTests
{
    private static readonly Category Deals = new(
        "best-deals", "Best Game Deals", "Big discounts on games that reviewed well.",
        "best-game-deals", Cadence.Daily, 0x2A7150);

    private static FeedItem Deal(string title = "Hollow Knight") => new(
        "cheapshark:x", title, "https://example.test/deal",
        Summary: "Very Positive — 97% of 200,000 reviews",
        ImageUrl: "https://example.test/art.jpg",
        Price: "$4.99", WasPrice: "$14.99", DiscountPercent: 67, Store: "Steam", Score: 90);

    [Theory]
    [InlineData(3)]
    [InlineData(13)]
    [InlineData(40)]
    public void The_catalog_embed_holds_whatever_the_settings_file_asks_for(int feeds)
    {
        // How many feeds exist is decided in a file merchant does not own. Thirteen at full length
        // is already past what Discord carries, and it refuses the embed while it is being built —
        // so without a budget /merchant help does not shorten, it stops answering.
        Category Fat(int n) => new(
            $"feed-{n}", new string('L', Schema.Limits.MenuText),
            new string('d', Schema.Limits.DescriptionText),
            $"channel-{n}", Cadence.Daily, 0x2A7150);

        EmbedBuilder shell = new EmbedBuilder()
            .WithTitle("What're ya buyin'?")
            .WithDescription("Pick a feed, pick a channel, done.")
            .WithFooter("/merchant add · /merchant list");

        Embed embed = Announcer.Catalog(shell, [.. Enumerable.Range(0, feeds).Select(Fat)]);

        Assert.True(embed.Length <= EmbedBuilder.MaxEmbedLength);
        Assert.True(embed.Fields.Length <= EmbedBuilder.MaxFieldCount);
        Assert.InRange(embed.Fields.Length, 1, feeds);

        // A catalog that fits is shown whole; only an oversized one is cut.
        Assert.Equal(feeds <= 3, embed.Fields.Length == feeds);

        // Nothing is quietly dropped: a shortened list says so.
        if (embed.Fields.Length < feeds)
        {
            Assert.Contains($"{feeds - embed.Fields.Length} more", embed.Footer!.Value.Text);
        }
    }

    [Theory]
    [InlineData(Fixtures.SteamWeekly)]
    [InlineData(Fixtures.ItadGiveaways)]
    [InlineData(Fixtures.RedditGameDeals)]
    public void Every_item_a_real_feed_produces_can_be_announced(string document)
    {
        // Discord validates an embed as it is built, so anything it refuses throws inside the sweep
        // — where the item is left unposted and retried forever. The end of the contract the
        // sources hold up: whatever comes out of a real document is postable.
        foreach (FeedItem item in RssSource.Parse(Fixtures.Read(document)))
        {
            Announcer.Item(Deals, item);
        }
    }

    [Fact]
    public void An_item_embed_carries_the_price_the_store_and_the_score()
    {
        Embed embed = Announcer.Item(Deals, Deal());

        Assert.Equal("Hollow Knight", embed.Title);
        Assert.Equal("https://example.test/deal", embed.Url);
        Assert.Contains(embed.Fields, f => f.Name == "Price" && f.Value.Contains("$4.99"));
        Assert.Contains(embed.Fields, f => f.Name == "Store" && f.Value == "Steam");
        Assert.Contains(embed.Fields, f => f.Name == "Metacritic" && f.Value == "90");
    }

    [Fact]
    public void The_price_line_strikes_through_what_it_used_to_cost()
    {
        Embed embed = Announcer.Item(Deals, Deal());

        EmbedField price = Assert.Single(embed.Fields, f => f.Name == "Price");
        Assert.Contains("~~$14.99~~", price.Value);
        Assert.Contains("-67%", price.Value);
    }

    [Fact]
    public void An_item_with_no_price_shows_no_price_field()
    {
        // The RSS-backed feeds know a title and a link and nothing else.
        Embed embed = Announcer.Item(Deals, new FeedItem("a", "Some News", "https://example.test/n"));

        Assert.DoesNotContain(embed.Fields, f => f.Name == "Price");
        Assert.Equal("Some News", embed.Title);
    }

    [Fact]
    public void A_very_long_title_is_cut_to_fit_the_embed_limit()
    {
        Embed embed = Announcer.Item(Deals, Deal(new string('x', 400)));

        Assert.True(embed.Title!.Length <= 256);
    }

    [Fact]
    public void A_digest_lists_its_items_and_counts_them()
    {
        List<FeedItem> items = [.. Enumerable.Range(0, 5).Select(i =>
            Deal($"Game {i}") with { Id = $"deal-{i}" })];

        Embed embed = Announcer.Digest(Deals, items, "today");

        Assert.Contains("5 picks today", embed.Title);
        Assert.Contains("Game 0", embed.Description);
        Assert.Contains("Game 4", embed.Description);
    }

    [Fact]
    public void A_single_pick_is_not_called_picks()
    {
        Embed embed = Announcer.Digest(Deals, [Deal()], "today");

        Assert.Contains("1 pick today", embed.Title);
    }

    [Fact]
    public void A_long_digest_lists_a_dozen_and_counts_the_rest()
    {
        List<FeedItem> items = [.. Enumerable.Range(0, 30).Select(i =>
            Deal($"Game {i}") with { Id = $"deal-{i}" })];

        Embed embed = Announcer.Digest(Deals, items, "this week");

        Assert.Equal($"…and {30 - Announcer.DigestLines} more", embed.Footer?.Text);
        Assert.True(embed.Description!.Length <= 4096);
    }

    [Fact]
    public void Markdown_in_a_game_title_cannot_break_the_digest_links()
    {
        // Real titles contain brackets and asterisks; unescaped they swallow the link that follows.
        Embed embed = Announcer.Digest(Deals, [Deal("S.T.A.L.K.E.R. [Enhanced] *Deluxe*")], "today");

        Assert.Contains("\\[Enhanced\\]", embed.Description);
        Assert.Contains("\\*Deluxe\\*", embed.Description);
    }

    [Fact]
    public void A_digest_never_exceeds_the_description_limit()
    {
        List<FeedItem> items = [.. Enumerable.Range(0, 20).Select(i =>
            Deal(new string('x', 200)) with { Id = $"deal-{i}" })];

        Embed embed = Announcer.Digest(Deals, items, "today");

        Assert.True(embed.Description!.Length <= 4096);
    }
}
