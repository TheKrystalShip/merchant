using Merchant.Sources;
using Microsoft.Extensions.Configuration;

namespace Merchant.Feeds.Factories;

/// <summary>
/// <c>"type": "cheapshark"</c> — one slice of CheapShark's deals API.
///
/// <code>
/// "source": { "type": "cheapshark", "upperPrice": 10, "minMetacritic": 75, "sortBy": "Savings" }
/// </code>
///
/// This driver exists because a price ceiling has to be expressible in a URL merchant builds at
/// sweep time, which IsThereAnyDeal's tokenised filter feeds do not allow. Everything it returns is
/// quoted in US dollars whatever a server's region is set to — CheapShark has no regional pricing.
/// </summary>
public sealed class CheapSharkSourceFactory : ISourceFactory
{
    /// <summary>
    /// The sort keys CheapShark accepts. A typo here does not fail — it silently returns a
    /// differently ordered feed — which is exactly the kind of mistake worth catching at startup.
    /// </summary>
    private static readonly string[] SortKeys =
    [
        "Deal Rating", "Title", "Savings", "Price", "Metacritic", "Reviews", "Release", "Store", "Recent",
    ];

    /// <inheritdoc />
    public string Type => "cheapshark";

    /// <inheritdoc />
    public ISourceBlueprint? Create(IConfigurationSection source, ICollection<string> errors)
    {
        int before = errors.Count;

        decimal? upperPrice = ConfigRead.Decimal(source, "upperPrice", errors);
        int? minMetacritic = ConfigRead.Int(source, "minMetacritic", errors);
        string sortBy = ConfigRead.Optional(source, "sortBy") ?? "Deal Rating";

        if (upperPrice is <= 0)
        {
            errors.Add("source.upperPrice should be above zero.");
        }

        if (minMetacritic is < 0 or > 100)
        {
            errors.Add("source.minMetacritic should be between 0 and 100.");
        }

        if (!SortKeys.Contains(sortBy, StringComparer.OrdinalIgnoreCase))
        {
            errors.Add($"source.sortBy should be one of {string.Join(", ", SortKeys)}, not '{sortBy}'.");
        }

        return errors.Count == before
            ? new Blueprint(upperPrice, minMetacritic, sortBy)
            : null;
    }

    private sealed class Blueprint(decimal? upperPrice, int? minMetacritic, string sortBy) : ISourceBlueprint
    {
        public string Type => "cheapshark";

        public ISource Build(HttpClient http, GuildSettings settings) =>
            new CheapSharkSource(http, upperPrice, minMetacritic, sortBy);
    }
}
