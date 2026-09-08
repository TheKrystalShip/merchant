using Merchant.Discord;
using Merchant.Feeds;
using Merchant.Feeds.Factories;
using Merchant.Sources;
using Xunit;

namespace Merchant.Tests;

/// <summary>
/// Where the settings file is held to account: that the shipped example describes the catalog
/// merchant is meant to post, and that a hand-edited mistake is answered with a sentence naming the
/// feed rather than with a stack trace or with silence.
/// </summary>
public class FeedCatalogTests
{
    /// <summary>The catalog merchant ships, which the example is expected to describe exactly.</summary>
    public static TheoryData<string, string, string, Cadence, uint, string> Shipped => new()
    {
        { "top-week", "Top Games of the Week", "top-games-of-the-week", Cadence.Weekly, 0x1B2838, "rss" },
        { "under-10", "Games Under $10", "games-under-10", Cadence.Daily, 0xC24A12, "cheapshark" },
        { "best-deals", "Best Game Deals", "best-game-deals", Cadence.Daily, 0x2A7150, "cheapshark" },
        { "worth-playing", "Worth Checking Out", "worth-checking-out", Cadence.Daily, 0x453EA0, "rss" },
        { "free-games", "Free Games & Giveaways", "free-games", Cadence.Live, 0x9B59B6, "rss" },
    };

    [Theory]
    [MemberData(nameof(Shipped))]
    public void The_example_describes_the_catalog_merchant_ships(
        string key, string label, string channel, Cadence cadence, uint colour, string type)
    {
        FeedCatalog catalog = Settings.Example(out _);

        Category category = Assert.IsType<Category>(catalog.Find(key));

        Assert.Equal(label, category.Label);
        Assert.Equal(channel, category.SuggestedChannelName);
        Assert.Equal(cadence, category.DefaultCadence);
        Assert.Equal(colour, category.Colour);
        Assert.Equal(type, catalog.SourceType(key));
        Assert.NotEmpty(category.Description);
    }

    [Fact]
    public void The_example_loads_completely_and_without_complaint()
    {
        FeedCatalog catalog = Settings.Example(out CatalogReport report);

        Assert.Empty(report.Errors);
        Assert.Equal(5, report.Loaded);
        Assert.Equal(5, report.Configured);
        Assert.Equal(0, report.Disabled);
        Assert.Equal(5, catalog.All.Count);
    }

    [Fact]
    public void The_example_parses_despite_its_comments_and_trailing_commas()
    {
        // The example is annotated line by line, which only works because the JSON reader skips
        // comments. If that ever stops being true, every install seeds a file that cannot be read.
        Assert.Contains("//", File.ReadAllText(Settings.ExamplePath), StringComparison.Ordinal);
        Assert.Equal(5, Settings.Example(out _).All.Count);
    }

    [Fact]
    public void The_menu_is_ordered_by_label_because_a_file_has_no_order_to_keep()
    {
        // Config children come back sorted by key, not as the file lists them, so the order has to
        // be decided somewhere. Alphabetical by label is what the menus show.
        IReadOnlyList<Category> all = Settings.Example(out _).All;

        Assert.Equal([.. all.Select(c => c.Label).Order(StringComparer.OrdinalIgnoreCase)],
            all.Select(c => c.Label));
    }

    [Fact]
    public void Each_feed_gets_its_own_colour_so_channels_read_apart()
    {
        List<uint> colours = [.. Settings.Example(out _).All.Select(c => c.Colour)];

        Assert.Equal(colours.Count, colours.Distinct().Count());
    }

    [Fact]
    public void The_giveaways_feed_follows_the_guild_region()
    {
        // A server that set itself to ES must not be shown US-only giveaways.
        using HttpClient http = new();
        FeedCatalog catalog = Settings.Example(out _);

        RssSource source = Assert.IsType<RssSource>(
            catalog.SourceFor("free-games", http, new GuildSettings(1, "ES", "EUR")));

        Assert.Contains(source.Urls, url => url.Contains("/ES/", StringComparison.Ordinal));
        Assert.DoesNotContain(source.Urls, url => url.Contains("{region}", StringComparison.Ordinal));
    }

    [Fact]
    public void An_unknown_key_is_rejected_rather_than_silently_ignored()
    {
        using HttpClient http = new();

        Assert.Throws<ArgumentException>(() =>
            Settings.Example(out _).SourceFor("no-such-feed", http, GuildSettings.Default(1)));
    }

    [Fact]
    public void Default_cadence_is_what_the_recommended_choice_resolves_to()
    {
        foreach (Category category in Settings.Example(out _).All)
        {
            Assert.Equal(category.DefaultCadence, CadenceChoice.Default.Resolve(category));
        }
    }

    [Theory]
    [InlineData(CadenceChoice.Live, Cadence.Live)]
    [InlineData(CadenceChoice.Daily, Cadence.Daily)]
    [InlineData(CadenceChoice.Weekly, Cadence.Weekly)]
    public void An_explicit_cadence_overrides_the_recommendation(CadenceChoice choice, Cadence expected)
    {
        Category weekly = Settings.Example(out _).Find("top-week")!;

        Assert.Equal(expected, choice.Resolve(weekly));
    }

    [Fact]
    public void A_disabled_feed_is_left_out_without_being_called_a_mistake()
    {
        FeedCatalog catalog = Settings.From("""
            {
              "feeds": {
                "kept": {
                  "label": "Kept", "description": "Still here.",
                  "source": { "type": "rss", "urls": [ "https://example.test/feed" ] }
                },
                "parked": {
                  "enabled": false,
                  "label": "Parked", "description": "Off for now.",
                  "source": { "type": "rss", "urls": [ "https://example.test/other" ] }
                }
              }
            }
            """, out CatalogReport report);

        Assert.Empty(report.Errors);
        Assert.Equal(1, report.Disabled);
        Assert.Equal(1, report.Loaded);
        Assert.Null(catalog.Find("parked"));
    }

    [Fact]
    public void One_bad_feed_costs_only_itself()
    {
        // The person editing this file is usually not the person who wrote merchant. A typo in one
        // feed taking every channel offline is a worse failure than four feeds and a loud log line.
        FeedCatalog catalog = Settings.From("""
            {
              "feeds": {
                "good": {
                  "label": "Good", "description": "Fine.",
                  "source": { "type": "rss", "urls": [ "https://example.test/feed" ] }
                },
                "bad": {
                  "label": "Bad", "description": "Names a source that does not exist.",
                  "source": { "type": "carrier-pigeon" }
                }
              }
            }
            """, out CatalogReport report);

        Assert.Equal(1, report.Loaded);
        Assert.Equal(2, report.Configured);
        Assert.NotNull(catalog.Find("good"));
        Assert.Null(catalog.Find("bad"));

        string complaint = Assert.Single(report.Errors);
        Assert.Contains("bad", complaint, StringComparison.Ordinal);
        Assert.Contains("carrier-pigeon", complaint, StringComparison.Ordinal);
        Assert.Contains("rss", complaint, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every rejection names the feed it came from and what was expected instead. These assert the
    /// wording carries both, because the message is the entire interface for somebody with a text
    /// editor and no access to the source.
    /// </summary>
    [Theory]
    // A missing label leaves the menu with nothing to show.
    [InlineData("""{ "description": "No label.", "source": { "type": "rss", "urls": [ "https://e.test/f" ] } }""", "label")]
    [InlineData("""{ "label": "L", "source": { "type": "rss", "urls": [ "https://e.test/f" ] } }""", "description")]
    [InlineData("""{ "label": "L", "description": "D" }""", "source")]
    [InlineData("""{ "label": "L", "description": "D", "source": { "type": "rss" } }""", "urls")]
    [InlineData("""{ "label": "L", "description": "D", "source": { "type": "rss", "urls": [ "not-a-url" ] } }""", "http")]
    [InlineData("""{ "label": "L", "description": "D", "cadence": "Hourly", "source": { "type": "rss", "urls": [ "https://e.test/f" ] } }""", "cadence")]
    [InlineData("""{ "label": "L", "description": "D", "colour": "burnt orange", "source": { "type": "rss", "urls": [ "https://e.test/f" ] } }""", "#1B2838")]
    [InlineData("""{ "label": "L", "description": "D", "enabled": "sometimes", "source": { "type": "rss", "urls": [ "https://e.test/f" ] } }""", "true or false")]
    // A placeholder merchant does not fill in would be fetched literally, and quietly return nothing.
    [InlineData("""{ "label": "L", "description": "D", "source": { "type": "rss", "urls": [ "https://e.test/{country}/f" ] } }""", "{country}")]
    // Reserved so a secrets syntax can be added later without a key pasted today being fetched raw.
    [InlineData("""{ "label": "L", "description": "D", "source": { "type": "rss", "urls": [ "https://e.test/f?key=${ITAD_KEY}" ] } }""", "not supported yet")]
    [InlineData("""{ "label": "L", "description": "D", "source": { "type": "cheapshark", "sortBy": "Cheapness" } }""", "sortBy")]
    [InlineData("""{ "label": "L", "description": "D", "source": { "type": "cheapshark", "upperPrice": "ten" } }""", "upperPrice")]
    [InlineData("""{ "label": "L", "description": "D", "source": { "type": "cheapshark", "minMetacritic": 500 } }""", "minMetacritic")]
    public void A_rejection_names_the_feed_and_what_was_expected(string feed, string expected)
    {
        Settings.From(Settings.Feed(feed), out CatalogReport report);

        string complaint = Assert.Single(report.Errors);

        Assert.StartsWith("feed 'example':", complaint, StringComparison.Ordinal);
        Assert.Contains(expected, complaint, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, report.Loaded);
    }

    [Fact]
    public void A_feed_name_that_could_not_be_stored_or_typed_is_rejected()
    {
        // The key is three things at once: the row in the ledger, the value the menu sends back,
        // and what somebody types. Spaces and capitals break at least one of them.
        Settings.From("""
            { "feeds": { "Free Games": {
                "label": "L", "description": "D",
                "source": { "type": "rss", "urls": [ "https://e.test/f" ] } } } }
            """, out CatalogReport report);

        string complaint = Assert.Single(report.Errors);

        Assert.StartsWith("feed 'Free Games':", complaint, StringComparison.Ordinal);
        Assert.Contains("hyphens", complaint, StringComparison.Ordinal);
    }

    /// <summary>
    /// A key that is not in the schema is the worst mistake this file can hold: ignored in silence,
    /// with the default left in its place looking deliberate. "color" is the one to expect.
    /// </summary>
    [Theory]
    [InlineData("""{ "label": "L", "description": "D", "color": "#112233", "source": { "type": "rss", "urls": [ "https://e.test/f" ] } }""", "color")]
    [InlineData("""{ "label": "L", "description": "D", "frequency": "Daily", "source": { "type": "rss", "urls": [ "https://e.test/f" ] } }""", "frequency")]
    [InlineData("""{ "label": "L", "description": "D", "source": { "type": "rss", "url": "https://e.test/f" } }""", "url")]
    [InlineData("""{ "label": "L", "description": "D", "source": { "type": "cheapshark", "maxPrice": 10 } }""", "maxPrice")]
    public void A_setting_that_does_not_exist_is_reported_rather_than_ignored(string feed, string misspelt)
    {
        Settings.From(Settings.Feed(feed), out CatalogReport report);

        Assert.Contains(report.Errors, error =>
            error.Contains($"there is no setting '{misspelt}'", StringComparison.Ordinal));
        Assert.Equal(0, report.Loaded);
    }

    [Theory]
    [InlineData("DealRating")]
    // CheapShark spells its own sort keys with spaces, so the file may too.
    [InlineData("Deal Rating")]
    [InlineData("savings")]
    public void A_sort_key_is_taken_however_it_is_spelled(string sortBy)
    {
        FeedCatalog catalog = Settings.From(Settings.Feed($$"""
            { "label": "L", "description": "D",
              "source": { "type": "cheapshark", "sortBy": "{{sortBy}}" } }
            """), out CatalogReport report);

        Assert.Empty(report.Errors);
        Assert.NotNull(catalog.Find("example"));
    }

    [Fact]
    public void The_sort_enum_carries_the_value_CheapShark_expects()
    {
        Assert.Equal("Deal Rating", CheapSharkSort.DealRating.Query());
        Assert.Equal("Savings", CheapSharkSort.Savings.Query());
    }

    [Fact]
    public void A_feed_name_too_long_for_the_menu_is_rejected()
    {
        // Discord rejects the whole autocomplete response when one value is over 100 characters,
        // which would take the menu down for every feed rather than just this one.
        string overlong = new('a', 101);

        Settings.From($$"""
            { "feeds": { "{{overlong}}": {
                "label": "L", "description": "D",
                "source": { "type": "rss", "urls": [ "https://e.test/f" ] } } } }
            """, out CatalogReport report);

        Assert.Contains("100 characters", Assert.Single(report.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public void A_settings_file_with_no_feeds_is_empty_rather_than_broken()
    {
        FeedCatalog catalog = Settings.From("""{ "bot": { "sweepMinutes": 30 } }""", out CatalogReport report);

        // Program treats this as fatal — a bot with nothing to announce should say so, not idle.
        Assert.Empty(catalog.All);
        Assert.Empty(report.Errors);
        Assert.Equal(0, report.Configured);
    }

    [Fact]
    public void Optional_settings_fall_back_rather_than_failing()
    {
        FeedCatalog catalog = Settings.From(Settings.Feed("""
            { "label": "Bare", "description": "Only what is required.",
              "source": { "type": "rss", "urls": [ "https://example.test/feed" ] } }
            """), out CatalogReport report);

        Category category = Assert.IsType<Category>(catalog.Find("example"));

        Assert.Empty(report.Errors);
        Assert.Equal(Cadence.Daily, category.DefaultCadence);
        Assert.Equal("example", category.SuggestedChannelName);
    }
}
