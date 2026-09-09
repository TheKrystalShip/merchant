namespace Merchant.Sources;

/// <summary>
/// The one thing every source has to agree on: a link merchant hands to Discord is an absolute
/// http(s) address.
///
/// Discord refuses an embed carrying anything else, and refuses it while the embed is being built,
/// which puts the failure inside the sweep rather than on the wire. A live subscription that hits
/// one is stuck for good: the post throws, the item is never marked sent, and the next sweep picks
/// the same item up again. Feeds do produce these — a relative path, a <c>javascript:</c> href, a
/// truncated <c>src</c> attribute — so they are dropped here, where the cost is one item.
/// </summary>
internal static class Links
{
    /// <summary>The address if Discord will take it, or null.</summary>
    public static string? Http(string? url) =>
        url?.Trim() is { Length: > 0 } value
        && Uri.TryCreate(value, UriKind.Absolute, out Uri? parsed)
        && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps)
            ? value
            : null;
}
