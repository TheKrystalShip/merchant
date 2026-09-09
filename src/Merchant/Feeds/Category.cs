namespace Merchant.Feeds;

/// <summary>
/// One entry in the catalog: the thing a person picks from the <c>/merchant add</c> menu.
/// A category names an audience ("games under ten"), not a URL — choosing a source, its filters
/// and a sane cadence is merchant's job, and is the whole reason this bot exists rather than an
/// RSS bot plus a wiki page of feed URLs.
/// </summary>
/// <param name="Key">Stable identifier stored in the database and sent as the command choice value.</param>
/// <param name="Label">What the menu shows.</param>
/// <param name="Description">One line under the label, explaining what lands in the channel.</param>
/// <param name="SuggestedChannelName">Offered when somebody has not made the channel yet.</param>
/// <param name="DefaultCadence">The cadence that suits the source, used when none is given.</param>
/// <param name="Colour">Embed accent, so a reader tells the categories apart at a glance.</param>
public sealed record Category(
    string Key,
    string Label,
    string Description,
    string SuggestedChannelName,
    Cadence DefaultCadence,
    uint Colour);
