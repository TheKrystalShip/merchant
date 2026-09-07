using Merchant.Discord;
using Merchant.Feeds;
using Xunit;

namespace Merchant.Tests;

/// <summary>
/// The catalog and the command menu are two hand-maintained lists that have to agree. These are
/// the tests that notice when someone adds a feed to one and not the other.
/// </summary>
public class CatalogTests
{
    [Fact]
    public void Every_menu_choice_names_a_real_category()
    {
        foreach (FeedChoice choice in Enum.GetValues<FeedChoice>())
        {
            Assert.NotNull(Catalog.Find(choice.ToKey()));
        }
    }

    [Fact]
    public void Every_category_is_reachable_from_the_menu()
    {
        HashSet<string> offered = [.. Enum.GetValues<FeedChoice>().Select(c => c.ToKey())];

        foreach (Category category in Catalog.All)
        {
            Assert.Contains(category.Key, offered);
        }
    }

    [Fact]
    public void Every_category_can_build_its_source()
    {
        using HttpClient http = new();
        GuildSettings settings = GuildSettings.Default(1);

        foreach (Category category in Catalog.All)
        {
            Assert.NotNull(Catalog.SourceFor(category.Key, http, settings));
        }
    }

    [Fact]
    public void Keys_are_unique()
    {
        Assert.Equal(Catalog.All.Count, Catalog.All.Select(c => c.Key).Distinct().Count());
    }

    [Fact]
    public void An_unknown_key_is_rejected_rather_than_silently_ignored()
    {
        using HttpClient http = new();

        Assert.Throws<ArgumentException>(() =>
            Catalog.SourceFor("no-such-feed", http, GuildSettings.Default(1)));
    }

    [Fact]
    public void The_giveaways_feed_follows_the_guild_region()
    {
        // A server that set itself to ES must not be shown US-only giveaways.
        using HttpClient http = new();

        Assert.NotNull(Catalog.SourceFor(
            Catalog.FreeGames, http, new GuildSettings(1, "ES", "EUR")));
    }

    [Fact]
    public void Cadence_defaults_suit_their_source()
    {
        // Steam publishes the chart once a week; posting it live would repost the same ten games
        // every sweep for seven days.
        Assert.Equal(Cadence.Weekly, Catalog.Find(Catalog.TopOfTheWeek)!.DefaultCadence);
        Assert.Equal(Cadence.Live, Catalog.Find(Catalog.FreeGames)!.DefaultCadence);
    }

    [Fact]
    public void Default_cadence_is_what_the_recommended_choice_resolves_to()
    {
        foreach (Category category in Catalog.All)
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
        Category weekly = Catalog.Find(Catalog.TopOfTheWeek)!;

        Assert.Equal(expected, choice.Resolve(weekly));
    }
}
