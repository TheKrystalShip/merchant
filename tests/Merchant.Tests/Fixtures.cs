namespace Merchant.Tests;

/// <summary>The captured feed documents, read from beside the test assembly.</summary>
internal static class Fixtures
{
    /// <summary>Steam's weekly chart: RSS 1.0, items outside the channel, no guid.</summary>
    public const string SteamWeekly = "steam-weekly.rdf.xml";

    /// <summary>IsThereAnyDeal's giveaways: ordinary RSS 2.0 with CDATA bodies.</summary>
    public const string ItadGiveaways = "itad-giveaways.rss.xml";

    /// <summary>Reddit's top-of-week: Atom, with the link in an href attribute.</summary>
    public const string RedditGameDeals = "reddit-gamedeals.atom.xml";

    /// <summary>A CheapShark deals response.</summary>
    public const string CheapSharkDeals = "cheapshark-deals.json";

    /// <summary>Reads a captured document.</summary>
    public static string Read(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
}
