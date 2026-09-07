using Merchant.Feeds;
using Merchant.Sources;

namespace Merchant;

/// <summary>
/// Fetches every feed in the catalog and reports what came back, without touching Discord.
///
/// A feed bot's failures are nearly all upstream and nearly all silent — a source changes shape,
/// starts rate-limiting, or moves. This turns that into one command that either says every feed is
/// answering or names the one that is not, and it needs no bot token to run.
/// </summary>
public static class Preflight
{
    /// <summary>Runs the check.</summary>
    /// <param name="region">Region for the sources that vary by storefront.</param>
    /// <returns>0 when every feed returned something, 1 when any came back empty.</returns>
    public static async Task<int> RunAsync(string region, CancellationToken ct)
    {
        using HttpClient http = new() { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(
            Environment.GetEnvironmentVariable("MERCHANT_USER_AGENT")
            ?? "merchant/1.0 (Discord game-deal announcer)");

        GuildSettings settings = new(0, region.ToUpperInvariant(), "USD");
        bool healthy = true;

        Console.WriteLine($"Checking {Catalog.All.Count} feeds for region {settings.Region}.\n");

        foreach (Category category in Catalog.All)
        {
            ISource source = Catalog.SourceFor(category.Key, http, settings);

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
