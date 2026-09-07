namespace Merchant;

/// <summary>How often a channel hears from merchant.</summary>
public enum Cadence
{
    /// <summary>Post items as they are found, capped per sweep so a busy feed cannot flood a channel.</summary>
    Live = 0,

    /// <summary>Hold what the day found and post it as one digest.</summary>
    Daily = 1,

    /// <summary>Hold what the week found and post it as one digest.</summary>
    Weekly = 2,
}

/// <summary>
/// One thing worth announcing. Every source flattens to this, so the announcer never learns
/// where a deal came from and a new source costs nothing beyond its own fetch.
/// </summary>
/// <param name="Id">Stable across sweeps: the ledger's identity for "already posted".</param>
/// <param name="Title">What the embed leads with.</param>
/// <param name="Url">Where the reader goes.</param>
/// <param name="Summary">A sentence at most; trimmed hard before it reaches Discord.</param>
/// <param name="ImageUrl">Thumbnail, when the source gives one.</param>
/// <param name="Published">When the source says it appeared. Used only for ordering.</param>
/// <param name="Price">Rendered sale price including its currency symbol, e.g. <c>$4.99</c>.</param>
/// <param name="WasPrice">Rendered list price, for the struck-through comparison.</param>
/// <param name="DiscountPercent">Whole percent off, when the source knows it.</param>
/// <param name="Store">Storefront name, e.g. <c>Steam</c>.</param>
/// <param name="Score">A review score out of 100, when the source knows one.</param>
public sealed record FeedItem(
    string Id,
    string Title,
    string Url,
    string? Summary = null,
    string? ImageUrl = null,
    DateTimeOffset? Published = null,
    string? Price = null,
    string? WasPrice = null,
    int? DiscountPercent = null,
    string? Store = null,
    int? Score = null);

/// <summary>One feed wired to one channel in one server.</summary>
public sealed record Subscription(
    long Id,
    ulong GuildId,
    ulong ChannelId,
    string CategoryKey,
    Cadence Cadence,
    ulong? MentionRoleId,
    DateTimeOffset? LastPostedAt);

/// <summary>Per-server preferences. One row per guild, created on first use.</summary>
/// <param name="Region">Two-letter region used by the sources that price things.</param>
/// <param name="Currency">ISO currency code, e.g. <c>EUR</c>.</param>
public sealed record GuildSettings(ulong GuildId, string Region, string Currency)
{
    /// <summary>What a server gets before anybody runs <c>/merchant region</c>.</summary>
    public static GuildSettings Default(ulong guildId) => new(guildId, "US", "USD");
}
