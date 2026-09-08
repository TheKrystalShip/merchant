using System.Globalization;
using System.Text;
using Discord;
using Merchant.Feeds;

namespace Merchant.Discord;

/// <summary>
/// Turns items into something worth looking at in a Discord channel.
///
/// Two shapes, because two things are being said. A live post is one deal and gets the full
/// treatment — art, price, review verdict. A digest is a period's worth and gets one embed with a
/// list, because fifteen separate cards is not a digest, it is the flood a digest exists to avoid.
/// </summary>
public static class Announcer
{
    /// <summary>Most items a digest lists before it starts counting the rest.</summary>
    public const int DigestLines = 12;

    /// <summary>Most single-item posts one sweep will make, so a busy feed cannot flood a channel.</summary>
    public const int LiveBurst = 4;

    /// <summary>
    /// Lists a catalog into an embed, taking as many feeds as Discord will carry.
    ///
    /// The catalog is a file, so its size is somebody else's decision. Discord refuses an embed
    /// whose parts total more than 6000 characters — around thirteen feeds at the length a feed is
    /// allowed — and it refuses it at build time, which would take <c>/merchant help</c> down
    /// altogether rather than shortening it. What fits goes in; the footer says what did not.
    /// </summary>
    /// <param name="shell">The embed's own copy: title, description, colour, footer.</param>
    /// <param name="feeds">The catalog, in menu order.</param>
    public static Embed Catalog(EmbedBuilder shell, IReadOnlyList<Category> feeds)
    {
        int shown = 0;

        foreach (Category feed in feeds)
        {
            string name = Truncate(feed.Label, EmbedFieldBuilder.MaxFieldNameLength);
            string value = Truncate(
                $"{feed.Description}\nSuggested channel: `#{feed.SuggestedChannelName}` · " +
                $"posts {feed.DefaultCadence.Describe()}",
                EmbedFieldBuilder.MaxFieldValueLength);

            // Counted before the field is added: the check has to be able to say no.
            if (shown == EmbedBuilder.MaxFieldCount
                || shell.Length + name.Length + value.Length > EmbedBuilder.MaxEmbedLength)
            {
                break;
            }

            shell.AddField(name, value);
            shown++;
        }

        if (shown < feeds.Count)
        {
            shell.WithFooter($"{shell.Footer?.Text} · and {feeds.Count - shown} more not shown here"
                .TrimStart(' ', '·'));
        }

        return shell.Build();
    }

    /// <summary>One deal, at full size.</summary>
    public static Embed Item(Category category, FeedItem item)
    {
        EmbedBuilder embed = new EmbedBuilder()
            .WithTitle(Truncate(item.Title, 250))
            .WithUrl(item.Url)
            .WithColor(new Color(category.Colour))
            .WithFooter(category.Label);

        if (item.Summary is { Length: > 0 } summary)
        {
            embed.WithDescription(Truncate(summary, 500));
        }

        if (item.ImageUrl is { Length: > 0 } image)
        {
            embed.WithThumbnailUrl(image);
        }

        if (Price(item) is { Length: > 0 } price)
        {
            embed.AddField("Price", price, inline: true);
        }

        if (item.Store is { Length: > 0 } store)
        {
            embed.AddField("Store", store, inline: true);
        }

        if (item.Score is { } score)
        {
            // Invariant, like every other number and date merchant renders: the host's culture
            // decides the digits otherwise, and a score reads as Eastern Arabic under fa-IR.
            embed.AddField(
                "Metacritic", score.ToString(CultureInfo.InvariantCulture), inline: true);
        }

        if (item.Published is { } when)
        {
            embed.WithTimestamp(when);
        }

        return embed.Build();
    }

    /// <summary>A period's worth, as one list.</summary>
    /// <param name="period">Reads into the heading, e.g. <c>today</c> or <c>this week</c>.</param>
    public static Embed Digest(Category category, IReadOnlyList<FeedItem> items, string period)
    {
        StringBuilder body = new();

        foreach (FeedItem item in items.Take(DigestLines))
        {
            body.Append("**[").Append(Escape(Truncate(item.Title, 90))).Append("](")
                .Append(item.Url).Append(")**");

            if (Price(item) is { Length: > 0 } price)
            {
                body.Append(" — ").Append(price);
            }

            if (item.Store is { Length: > 0 } store)
            {
                body.Append(" · ").Append(store);
            }

            body.AppendLine().AppendLine();
        }

        EmbedBuilder embed = new EmbedBuilder()
            .WithTitle($"{category.Label} — {items.Count} {(items.Count == 1 ? "pick" : "picks")} {period}")
            .WithDescription(Truncate(body.ToString().TrimEnd(), 4000))
            .WithColor(new Color(category.Colour))
            .WithCurrentTimestamp();

        if (items.Count > DigestLines)
        {
            embed.WithFooter($"…and {items.Count - DigestLines} more");
        }

        // The first item's art carries the whole digest, which beats a wall of text with no image.
        if (items.FirstOrDefault(i => i.ImageUrl is { Length: > 0 })?.ImageUrl is { } image)
        {
            embed.WithThumbnailUrl(image);
        }

        return embed.Build();
    }

    /// <summary>
    /// The price line: the sale price, then what it was and how far it fell when the source knows.
    /// </summary>
    private static string? Price(FeedItem item)
    {
        if (item.Price is not { Length: > 0 } price)
        {
            return null;
        }

        StringBuilder line = new($"**{price}**");

        if (item.WasPrice is { Length: > 0 } was && was != price)
        {
            line.Append(" ~~").Append(was).Append("~~");
        }

        if (item.DiscountPercent is > 0 and var off)
        {
            line.Append(" (-").Append(off).Append("%)");
        }

        return line.ToString();
    }

    /// <summary>Defuses the markdown in a game's own title, which is full of brackets and asterisks.</summary>
    private static string Escape(string text) =>
        text.Replace("[", "\\[").Replace("]", "\\]").Replace("*", "\\*").Replace("_", "\\_");

    /// <summary>Cuts text down to what Discord accepts in the place it is going.</summary>
    internal static string Truncate(string text, int limit) =>
        text.Length <= limit ? text : string.Concat(text.AsSpan(0, limit - 1).TrimEnd(), "…");
}
