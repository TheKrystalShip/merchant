using Merchant.Feeds;
using Discord.Interactions;

namespace Merchant.Discord;

/// <summary>
/// The feed menu, as Discord needs it: a compile-time enum, because slash-command choices are
/// registered with Discord up front rather than resolved per invocation.
/// <see cref="ToKey"/> is the single place this and <see cref="Catalog"/> are tied together, and
/// a test asserts the two stay in step.
/// </summary>
public enum FeedChoice
{
    /// <summary>See <see cref="Catalog.TopOfTheWeek"/>.</summary>
    [ChoiceDisplay("Top Games of the Week")]
    TopOfTheWeek,

    /// <summary>See <see cref="Catalog.UnderTen"/>.</summary>
    [ChoiceDisplay("Games Under $10")]
    UnderTen,

    /// <summary>See <see cref="Catalog.BestDeals"/>.</summary>
    [ChoiceDisplay("Best Game Deals")]
    BestDeals,

    /// <summary>See <see cref="Catalog.WorthPlaying"/>.</summary>
    [ChoiceDisplay("Worth Checking Out")]
    WorthPlaying,

    /// <summary>See <see cref="Catalog.FreeGames"/>.</summary>
    [ChoiceDisplay("Free Games & Giveaways")]
    FreeGames,
}

/// <summary>How often the channel hears from merchant, with a default that suits the feed.</summary>
public enum CadenceChoice
{
    /// <summary>Whatever the feed is best suited to. Almost always the right answer.</summary>
    [ChoiceDisplay("Recommended for this feed")]
    Default,

    /// <summary>Each item as it turns up.</summary>
    [ChoiceDisplay("Every item, as it appears")]
    Live,

    /// <summary>One digest a day.</summary>
    [ChoiceDisplay("One post a day")]
    Daily,

    /// <summary>One digest a week.</summary>
    [ChoiceDisplay("One post a week")]
    Weekly,
}

/// <summary>Translates the command menus into catalog and domain values.</summary>
public static class Choices
{
    /// <summary>The catalog key behind a menu selection.</summary>
    public static string ToKey(this FeedChoice choice) => choice switch
    {
        FeedChoice.TopOfTheWeek => Catalog.TopOfTheWeek,
        FeedChoice.UnderTen => Catalog.UnderTen,
        FeedChoice.BestDeals => Catalog.BestDeals,
        FeedChoice.WorthPlaying => Catalog.WorthPlaying,
        FeedChoice.FreeGames => Catalog.FreeGames,
        _ => throw new ArgumentOutOfRangeException(nameof(choice)),
    };

    /// <summary>The cadence to store, resolving <see cref="CadenceChoice.Default"/> against the feed.</summary>
    public static Cadence Resolve(this CadenceChoice choice, Category category) => choice switch
    {
        CadenceChoice.Default => category.DefaultCadence,
        CadenceChoice.Live => Cadence.Live,
        CadenceChoice.Daily => Cadence.Daily,
        CadenceChoice.Weekly => Cadence.Weekly,
        _ => category.DefaultCadence,
    };

    /// <summary>How a cadence reads in a confirmation message.</summary>
    public static string Describe(this Cadence cadence) => cadence switch
    {
        Cadence.Live => "as they appear",
        Cadence.Daily => "once a day",
        Cadence.Weekly => "once a week",
        _ => cadence.ToString(),
    };
}
