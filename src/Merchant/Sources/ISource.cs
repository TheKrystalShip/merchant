namespace Merchant.Sources;

/// <summary>
/// Somewhere announceable things come from. Implementations fetch and flatten; they do not
/// deduplicate, rank or decide what gets posted — the ledger and the cadence own that.
/// </summary>
public interface ISource
{
    /// <summary>
    /// The items this source currently offers, newest first. Returns empty rather than throwing
    /// when a remote is unreachable: one dead feed must not stop the sweep.
    /// </summary>
    Task<IReadOnlyList<FeedItem>> FetchAsync(CancellationToken ct);
}
