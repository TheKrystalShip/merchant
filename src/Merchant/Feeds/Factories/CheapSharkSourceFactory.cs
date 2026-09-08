using Merchant.Sources;
using Microsoft.Extensions.Configuration;

namespace Merchant.Feeds.Factories;

/// <summary>How CheapShark orders a page of deals. The members are its own sort keys.</summary>
public enum CheapSharkSort
{
    /// <summary>CheapShark's own ranking of how good a deal is. Its default and ours.</summary>
    DealRating,
    Title,
    Savings,
    Price,
    Metacritic,
    Reviews,
    Release,
    Store,
    Recent,
}

/// <summary>What a <c>cheapshark</c> source needs: one slice of the deals endpoint.</summary>
/// <param name="UpperPrice">Sale-price ceiling in USD, or null for no ceiling.</param>
/// <param name="MinMetacritic">Review-score floor, or null to include unreviewed games.</param>
/// <param name="SortBy">How the page is ordered.</param>
public sealed record CheapSharkOptions(
    decimal? UpperPrice,
    int? MinMetacritic,
    CheapSharkSort SortBy);

/// <summary>
/// <c>"type": "cheapshark"</c> — a slice of CheapShark's deals API.
///
/// This driver exists because a price ceiling has to be expressible in a URL merchant builds at
/// sweep time, which IsThereAnyDeal's tokenised filter feeds do not allow. Everything it returns is
/// quoted in US dollars whatever a server's region is: CheapShark has no regional pricing.
/// </summary>
public sealed class CheapSharkSourceFactory : ISourceFactory
{
    /// <summary>The <c>type</c> a feed names to ask for this driver.</summary>
    public const string TypeName = "cheapshark";

    /// <inheritdoc />
    public string Type => TypeName;

    /// <inheritdoc />
    public IReadOnlyList<string> Keys =>
        [Schema.SourceKeys.UpperPrice, Schema.SourceKeys.MinMetacritic, Schema.SourceKeys.SortBy];

    /// <inheritdoc />
    public ISourceBlueprint? Create(IConfigurationSection source, ICollection<string> errors)
    {
        int before = errors.Count;

        decimal? upperPrice = ConfigRead.Decimal(source, Schema.SourceKeys.UpperPrice, errors);
        int? minMetacritic = ConfigRead.Int(source, Schema.SourceKeys.MinMetacritic, errors);

        // A misspelled sort key does not fail — it silently returns a differently ordered feed —
        // which is exactly the kind of mistake worth catching at startup.
        CheapSharkSort sortBy = ConfigRead.Enum(
            source, Schema.SourceKeys.SortBy, CheapSharkSort.DealRating, errors);

        if (upperPrice is <= 0)
        {
            errors.Add($"{Schema.SourceKeys.UpperPrice} should be above zero.");
        }

        if (minMetacritic is < Schema.Limits.MinMetacritic or > Schema.Limits.MaxMetacritic)
        {
            errors.Add($"{Schema.SourceKeys.MinMetacritic} should be between " +
                       $"{Schema.Limits.MinMetacritic} and {Schema.Limits.MaxMetacritic}.");
        }

        return errors.Count == before
            ? new Blueprint(new CheapSharkOptions(upperPrice, minMetacritic, sortBy))
            : null;
    }

    private sealed class Blueprint(CheapSharkOptions options) : ISourceBlueprint
    {
        public string Type => TypeName;

        public ISource Build(HttpClient http, GuildSettings settings) => new CheapSharkSource(
            http, options.UpperPrice, options.MinMetacritic, options.SortBy.Query());
    }
}

/// <summary>Turns the sort enum back into the string CheapShark's query parameter expects.</summary>
public static class CheapSharkSortExtensions
{
    /// <summary>The wire value, which differs from the member name only where it has a space.</summary>
    public static string Query(this CheapSharkSort sort) => sort switch
    {
        CheapSharkSort.DealRating => "Deal Rating",
        _ => sort.ToString(),
    };
}
