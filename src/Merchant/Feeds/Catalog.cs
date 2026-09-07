using Merchant.Sources;

namespace Merchant.Feeds;

/// <summary>
/// Every feed merchant knows how to post, and the source that fills it.
///
/// The catalog is deliberately short. A person setting this up picks from five things and is
/// finished; there is no URL to find, no filter to encode, no polling interval to reason about.
/// Adding a sixth is a row here and a source class — no command, storage or scheduling change.
/// </summary>
public static class Catalog
{
    /// <summary>The week's ten best-selling games on Steam, straight from Valve's own chart feed.</summary>
    public const string TopOfTheWeek = "top-week";

    /// <summary>Discounted games at or under ten, ordered by how good the deal actually is.</summary>
    public const string UnderTen = "under-10";

    /// <summary>Deep discounts on games that reviewed well.</summary>
    public const string BestDeals = "best-deals";

    /// <summary>Editorial and community picks — what is worth playing rather than what is cheap.</summary>
    public const string WorthPlaying = "worth-playing";

    /// <summary>Limited-time giveaways: games that are free to keep right now.</summary>
    public const string FreeGames = "free-games";

    /// <summary>The catalog, in the order the command menu shows it.</summary>
    public static IReadOnlyList<Category> All { get; } =
    [
        new(TopOfTheWeek,
            "Top Games of the Week",
            "Steam's ten best sellers of the week, ranked. Valve publishes it every Tuesday.",
            "top-games-of-the-week",
            Cadence.Weekly,
            0x1B2838),

        new(UnderTen,
            "Games Under $10",
            "Everything discounted to ten or less, best deals first. The bargain bin, filtered.",
            "games-under-10",
            Cadence.Daily,
            0xC24A12),

        new(BestDeals,
            "Best Game Deals",
            "Big discounts on games that actually reviewed well — 75+ on Metacritic.",
            "best-game-deals",
            Cadence.Daily,
            0x2A7150),

        new(WorthPlaying,
            "Worth Checking Out",
            "Picks from Rock Paper Shotgun, PC Gamer and the top of r/GameDeals this week.",
            "worth-checking-out",
            Cadence.Daily,
            0x453EA0),

        new(FreeGames,
            "Free Games & Giveaways",
            "Games that are free to keep right now, while the giveaway lasts.",
            "free-games",
            Cadence.Live,
            0x9B59B6),
    ];

    /// <summary>The category with this key, or null when the key is unknown.</summary>
    public static Category? Find(string key) =>
        All.FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Builds the source that fills a category. Sources are cheap request-builders over a shared
    /// <see cref="HttpClient"/>, so this runs per sweep rather than being held in the container.
    /// </summary>
    /// <exception cref="ArgumentException">The key is not in the catalog.</exception>
    public static ISource SourceFor(string key, HttpClient http, GuildSettings settings) => key switch
    {
        TopOfTheWeek => new RssSource(http,
            ["https://store.steampowered.com/feeds/weeklytopsellers.xml"]),

        // CheapShark rather than a price-filtered IsThereAnyDeal feed: ITAD encodes its filters
        // into an opaque token its own site mints, so a price ceiling cannot be expressed in a URL
        // this bot builds. CheapShark takes the ceiling as a query parameter and answers with
        // structured prices, which is also what makes the embeds worth looking at.
        UnderTen => new CheapSharkSource(http,
            upperPrice: 10,
            minMetacritic: null,
            sortBy: "Deal Rating"),

        BestDeals => new CheapSharkSource(http,
            upperPrice: null,
            minMetacritic: 75,
            sortBy: "Savings"),

        WorthPlaying => new RssSource(http,
        [
            "https://www.rockpapershotgun.com/feed",
            "https://www.pcgamer.com/rss/",
            "https://www.reddit.com/r/GameDeals/top/.rss?t=week&limit=25",
        ]),

        FreeGames => new RssSource(http,
            [$"https://isthereanydeal.com/feeds/{settings.Region}/giveaways.rss"]),

        _ => throw new ArgumentException($"Unknown category '{key}'.", nameof(key)),
    };
}
