using System.Globalization;
using System.Text.Json;

namespace Merchant.Sources;

/// <summary>
/// The priced categories, over CheapShark's free deals API.
///
/// It is here rather than an RSS feed for one reason: a price ceiling has to be expressible in a
/// URL merchant builds at sweep time. IsThereAnyDeal's filtered feeds encode their filters into a
/// token minted by its own web UI, so "under ten in this server's currency" cannot be constructed
/// programmatically. CheapShark takes the ceiling as a query parameter, and answers with the sale
/// price, the list price, the discount and a review score as separate fields — which is also why
/// these embeds can show a struck-through price when the RSS-backed ones cannot.
/// </summary>
public sealed class CheapSharkSource : ISource
{
    private const string Endpoint = "https://www.cheapshark.com/api/1.0/deals";

    /// <summary>Storefront names by CheapShark id, so an embed can say where the deal is.</summary>
    private static readonly Dictionary<string, string> Stores = new()
    {
        ["1"] = "Steam", ["2"] = "GamersGate", ["3"] = "Green Man Gaming", ["7"] = "GOG",
        ["11"] = "Humble Store", ["13"] = "Uplay", ["15"] = "Fanatical", ["21"] = "WinGameStore",
        ["23"] = "GameBillet", ["25"] = "Epic Games Store", ["27"] = "Gamesplanet",
        ["28"] = "Gamesload", ["30"] = "IndieGala", ["35"] = "DreamGame",
    };

    private readonly HttpClient _http;
    private readonly decimal? _upperPrice;
    private readonly int? _minMetacritic;
    private readonly string _sortBy;

    /// <summary>Configures one slice of the deals endpoint.</summary>
    /// <param name="upperPrice">Sale-price ceiling in USD, or null for no ceiling.</param>
    /// <param name="minMetacritic">Review-score floor, or null to include unreviewed games.</param>
    /// <param name="sortBy">CheapShark sort key, e.g. <c>Deal Rating</c> or <c>Savings</c>.</param>
    public CheapSharkSource(HttpClient http, decimal? upperPrice, int? minMetacritic, string sortBy)
    {
        _http = http;
        _upperPrice = upperPrice;
        _minMetacritic = minMetacritic;
        _sortBy = sortBy;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FeedItem>> FetchAsync(CancellationToken ct)
    {
        List<string> query = ["onSale=1", "pageSize=20", $"sortBy={Uri.EscapeDataString(_sortBy)}"];

        if (_upperPrice is { } cap)
        {
            query.Add($"upperPrice={cap.ToString(CultureInfo.InvariantCulture)}");
        }

        if (_minMetacritic is { } floor)
        {
            query.Add($"metacritic={floor.ToString(CultureInfo.InvariantCulture)}");
        }

        string url = $"{Endpoint}?{string.Join('&', query)}";

        try
        {
            await using Stream body = await _http.GetStreamAsync(url, ct);
            using JsonDocument doc = await JsonDocument.ParseAsync(body, cancellationToken: ct);

            return [.. doc.RootElement.EnumerateArray()
                .Select(ToItem)
                .OfType<FeedItem>()];
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return [];
        }
    }

    /// <summary>Flattens one deal, or null when it is missing the fields an announcement needs.</summary>
    private static FeedItem? ToItem(JsonElement deal)
    {
        string? dealId = Text(deal, "dealID");
        string? title = Text(deal, "title");

        if (dealId is null || title is null)
        {
            return null;
        }

        decimal? sale = Money(deal, "salePrice");
        decimal? normal = Money(deal, "normalPrice");
        int? savings = Money(deal, "savings") is { } s ? (int)Math.Round(s) : null;

        string? storeId = Text(deal, "storeID");

        // A CheapShark deal id is stable while the deal stands and is reissued when the price
        // moves, which is exactly the identity the ledger wants: a price drop on a game already
        // posted is a new deal worth announcing again, a re-listing at the same price is not.
        return new FeedItem(
            Id: $"cheapshark:{dealId}",
            Title: title,
            Url: $"https://www.cheapshark.com/redirect?dealID={Uri.EscapeDataString(dealId)}",
            Summary: SteamVerdict(deal),
            ImageUrl: Links.Http(Text(deal, "thumb")),
            Published: Released(deal),
            Price: sale is { } p ? $"${p:0.00}" : null,
            WasPrice: normal is { } w ? $"${w:0.00}" : null,
            DiscountPercent: savings is > 0 ? savings : null,
            Store: storeId is not null && Stores.TryGetValue(storeId, out string? name) ? name : null,
            Score: Number(deal, "metacriticScore") is > 0 and var m ? m : null);
    }

    /// <summary>The Steam review verdict, e.g. <c>Very Positive — 94% of 12,431 reviews</c>.</summary>
    private static string? SteamVerdict(JsonElement deal)
    {
        string? verdict = Text(deal, "steamRatingText");
        int? percent = Number(deal, "steamRatingPercent");
        int? count = Number(deal, "steamRatingCount");

        if (verdict is null || percent is null or 0)
        {
            return null;
        }

        return count is > 0
            ? $"{verdict} — {percent}% of {count:N0} reviews"
            : $"{verdict} — {percent}%";
    }

    /// <summary>Release date, which CheapShark sends as a Unix timestamp of 0 when unknown.</summary>
    private static DateTimeOffset? Released(JsonElement deal) =>
        Number(deal, "releaseDate") is > 0 and var epoch
            ? DateTimeOffset.FromUnixTimeSeconds(epoch)
            : null;

    /// <summary>A string field, or null when absent or blank.</summary>
    private static string? Text(JsonElement deal, string name) =>
        deal.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(v.GetString())
            ? v.GetString()
            : null;

    /// <summary>A numeric field CheapShark sends as a JSON string.</summary>
    private static decimal? Money(JsonElement deal, string name) =>
        Text(deal, name) is { } raw
        && decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal value)
            ? value
            : null;

    /// <summary>An integer field, tolerating the string encoding CheapShark uses for numbers.</summary>
    private static int? Number(JsonElement deal, string name) =>
        Money(deal, name) is { } value ? (int)Math.Round(value) : null;
}
