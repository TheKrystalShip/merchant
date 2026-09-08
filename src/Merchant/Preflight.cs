using Merchant.Feeds;
using Merchant.Sources;

namespace Merchant;

/// <summary>
/// Fetches every feed in the catalog and reports what came back, without touching Discord.
///
/// A feed bot's failures are nearly all upstream and nearly all silent — a source changes shape,
/// starts rate-limiting, or moves. This turns that into one command that either says every feed is
/// answering or names the one that is not, and it needs no bot token to run.
///
/// Since the catalog became a file, this is also how an edit gets checked: the settings file has
/// already been read and validated by the time it runs, so a typo shows up as a named error here
/// rather than as a channel that quietly stops posting.
/// </summary>
public static class Preflight
{
    /// <summary>Runs the check.</summary>
    /// <param name="catalog">The configured feeds.</param>
    /// <param name="userAgent">Sent on every request; several sources refuse a generic one.</param>
    /// <param name="region">Region for the sources that vary by storefront.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>0 when every feed returned something, 1 when any came back empty.</returns>
    public static async Task<int> RunAsync(
        FeedCatalog catalog, string userAgent, string region, CancellationToken ct)
    {
        using HttpClient http = new() { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);

        GuildSettings settings = new(0, region.ToUpperInvariant(), "USD");
        bool healthy = true;

        Console.WriteLine($"Checking {catalog.All.Count} feed(s) for region {settings.Region}.\n");

        foreach (Category category in catalog.All)
        {
            ISource source = catalog.SourceFor(category.Key, http, settings);

            DateTimeOffset started = DateTimeOffset.UtcNow;
            IReadOnlyList<FeedItem> items = await source.FetchAsync(ct);
            int ms = (int)(DateTimeOffset.UtcNow - started).TotalMilliseconds;

            string mark = items.Count > 0 ? "ok  " : "EMPTY";
            healthy &= items.Count > 0;

            Console.WriteLine($"{mark} {category.Key,-14} {items.Count,3} items  {ms,5} ms  {category.Label}");

            if (items.Count > 0)
            {
                FeedItem first = items[0];
                string price = first.Price is { Length: > 0 } p
                    ? $"  {p}{(first.DiscountPercent is { } d ? $" (-{d}%)" : "")}"
                    : string.Empty;

                Console.WriteLine($"     └ {Trim(first.Title, 66)}{price}");
            }
        }

        Console.WriteLine(healthy
            ? "\nEvery feed answered."
            : "\nAt least one feed came back empty — see EMPTY above.");

        return healthy ? 0 : 1;
    }

    private static string Trim(string text, int limit) =>
        text.Length <= limit ? text : string.Concat(text.AsSpan(0, limit - 1), "…");
}
