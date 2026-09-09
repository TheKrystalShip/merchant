using System.Text;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Merchant.Feeds;
using Merchant.Feeds.Factories;
using Merchant.Sources;
using Merchant.Storage;

namespace Merchant.Discord;

/// <summary>
/// The whole configurable surface, under <c>/merchant</c>.
///
/// The design rule throughout is that a command either succeeds or says exactly what to fix. The
/// most common way a Discord feed bot appears broken is a silent permission failure in the target
/// channel, so <see cref="AddAsync"/> checks for that before it writes anything and names the
/// missing permission rather than letting the first sweep fail quietly hours later.
/// </summary>
[Group("merchant", "Announce game deals and gaming news in your channels.")]
[RequireContext(ContextType.Guild)]
[DefaultMemberPermissions(GuildPermission.ManageGuild)]
public sealed class MerchantModule : InteractionModuleBase<SocketInteractionContext>
{
    /// <summary>Permissions merchant needs in a channel before it can announce anything there.</summary>
    private static readonly (Func<ChannelPermissions, bool> Held, string Name)[] Required =
    [
        (p => p.ViewChannel, "View Channel"),
        (p => p.SendMessages, "Send Messages"),
        (p => p.EmbedLinks, "Embed Links"),
    ];

    private readonly Ledger _ledger;
    private readonly IHttpClientFactory _http;
    private readonly FeedCatalog _catalog;

    /// <summary>Wires the commands to the ledger, the network and the configured catalog.</summary>
    public MerchantModule(Ledger ledger, IHttpClientFactory http, FeedCatalog catalog)
    {
        _ledger = ledger;
        _http = http;
        _catalog = catalog;
    }

    /// <summary>Wires a feed to a channel.</summary>
    [SlashCommand("add", "Start posting a feed to a channel.")]
    public async Task AddAsync(
        [Summary("feed", "What should be announced.")]
        [Autocomplete(typeof(FeedAutocomplete))] string feed,
        [Summary("channel", "Where it should be posted.")] ITextChannel channel,
        [Summary("how-often", "Leave as recommended unless you have a reason.")]
        CadenceChoice howOften = CadenceChoice.Default,
        [Summary("ping", "A role to mention when something is posted.")] IRole? ping = null)
    {
        await DeferAsync(ephemeral: true);

        if (_catalog.Find(feed) is not { } category)
        {
            await FollowupAsync(embed: Unknown(feed), ephemeral: true);
            return;
        }

        if (Missing(channel) is { Count: > 0 } missing)
        {
            await FollowupAsync(embed: Problem(
                $"merchant cannot post in {channel.Mention}",
                $"""
                 It is missing: **{string.Join("**, **", missing)}**.

                 Fix it in *Edit Channel → Permissions*, add the **merchant** role there and turn those on,
                 then run this command again. Nothing has been saved.
                 """),
                ephemeral: true);
            return;
        }

        Cadence cadence = howOften.Resolve(category);
        (long id, bool created) = _ledger.Subscribe(
            GuildId, channel.Id, category.Key, cadence, ping?.Id);

        string pinging = ping is null ? string.Empty : $", pinging {ping.Mention}";
        string opening = created ? "The first few will appear within the hour." : string.Empty;

        EmbedBuilder embed = new EmbedBuilder()
            .WithColor(new Color(category.Colour))
            .WithTitle(created ? $"{category.Label} → {channel.Name}" : $"{category.Label} updated")
            .WithDescription($"""
                 {category.Description}

                 Posting **{cadence.Describe()}** in {channel.Mention}{pinging}.
                 {opening}
                 """)
            .WithFooter($"Subscription #{id} · remove it with /merchant remove id:{id}");

        await FollowupAsync(embed: embed.Build(), ephemeral: true);
    }

    /// <summary>Shows what is wired up in this server.</summary>
    [SlashCommand("list", "Show which feeds are posting where.")]
    public async Task ListAsync()
    {
        await DeferAsync(ephemeral: true);

        IReadOnlyList<Subscription> subscriptions = _ledger.ForGuild(GuildId);

        if (subscriptions.Count == 0)
        {
            await FollowupAsync(embed: Problem(
                "Nothing is set up yet",
                "Run **/merchant add** to point a feed at a channel."),
                ephemeral: true);
            return;
        }

        StringBuilder body = new();

        foreach (Subscription subscription in subscriptions)
        {
            Category? category = _catalog.Find(subscription.CategoryKey);

            body.Append("`#").Append(subscription.Id).Append("` **")
                .Append(category?.Label ?? subscription.CategoryKey).Append("** → ")
                .Append(MentionUtils.MentionChannel(subscription.ChannelId))
                .Append(" · ").Append(subscription.Cadence.Describe());

            // A feed can be taken out of the settings file while a channel is still subscribed to
            // it. The sweep already skips those; saying so here is the difference between a
            // channel that has quietly stopped and one that is visibly waiting to be cleaned up.
            if (category is null)
            {
                body.Append(" · **retired** — remove it with `/merchant remove id:")
                    .Append(subscription.Id).Append('`');
            }

            if (subscription.MentionRoleId is { } role)
            {
                body.Append(" · pings ").Append(MentionUtils.MentionRole(role));
            }

            int waiting = _ledger.PendingCount(subscription.Id);
            if (waiting > 0 && subscription.Cadence != Cadence.Live)
            {
                body.Append(" · ").Append(waiting).Append(" waiting");
            }

            body.AppendLine();
        }

        GuildSettings settings = _ledger.Settings(GuildId);

        await FollowupAsync(embed: new EmbedBuilder()
            .WithTitle("What merchant is announcing")
            .WithDescription(Announcer.Truncate(body.ToString(), EmbedBuilder.MaxDescriptionLength))
            .WithColor(new Color(0x453EA0))
            .WithFooter($"Region {settings.Region} · prices in {settings.Currency}")
            .Build(),
            ephemeral: true);
    }

    /// <summary>Stops a feed.</summary>
    [SlashCommand("remove", "Stop posting one of the feeds from /merchant list.")]
    public async Task RemoveAsync(
        [Summary("id", "The number shown by /merchant list.")] int id)
    {
        await DeferAsync(ephemeral: true);

        bool removed = _ledger.Unsubscribe(GuildId, id);

        await FollowupAsync(embed: removed
            ? new EmbedBuilder()
                .WithTitle($"Subscription #{id} removed")
                .WithDescription("Nothing else changed. Posts already in the channel stay put.")
                .WithColor(new Color(0x2A7150))
                .Build()
            : Problem($"No subscription #{id} here",
                "Run **/merchant list** to see the numbers for this server."),
            ephemeral: true);
    }

    /// <summary>Fetches a feed right now and shows one item, without wiring anything up.</summary>
    [SlashCommand("preview", "See what a feed looks like before you set it up.")]
    public async Task PreviewAsync(
        [Summary("feed", "The feed to sample.")]
        [Autocomplete(typeof(FeedAutocomplete))] string feed)
    {
        await DeferAsync(ephemeral: true);

        if (_catalog.Find(feed) is not { } category)
        {
            await FollowupAsync(embed: Unknown(feed), ephemeral: true);
            return;
        }

        GuildSettings settings = _ledger.Settings(GuildId);

        ISource source = _catalog.SourceFor(
            category.Key, _http.CreateClient(BotOptions.HttpClientName), settings);

        IReadOnlyList<FeedItem> items = await source.FetchAsync(CancellationToken.None);

        if (items.Count == 0)
        {
            await FollowupAsync(embed: Problem(
                $"{category.Label} has nothing right now",
                "The source may be briefly unreachable. This does not affect feeds already set up."),
                ephemeral: true);
            return;
        }

        await FollowupAsync(
            text: $"This is what **{category.Label}** would post — only you can see this.",
            embed: Announcer.Item(category, items[0]),
            ephemeral: true);
    }

    /// <summary>Sets the region prices are quoted in.</summary>
    [SlashCommand("region", "Set the country and currency used for prices.")]
    public async Task RegionAsync(
        [Summary("country", "Two-letter country code, e.g. ES, GB, US.")]
        [MinLength(2), MaxLength(2)] string country,
        [Summary("currency", "Three-letter currency code, e.g. EUR, GBP, USD.")]
        [MinLength(3), MaxLength(3)] string currency)
    {
        await DeferAsync(ephemeral: true);

        if (!GuildSettings.IsRegion(country) || !GuildSettings.IsCurrency(currency))
        {
            await FollowupAsync(embed: Problem(
                "That is not a country and a currency",
                """
                 Give a two-letter country code and a three-letter currency code — **ES** and
                 **EUR**, **GB** and **GBP**, **US** and **USD**. Nothing has been changed.
                 """),
                ephemeral: true);
            return;
        }

        GuildSettings settings = new(
            GuildId, country.Trim().ToUpperInvariant(), currency.Trim().ToUpperInvariant());

        _ledger.SaveSettings(settings);

        await FollowupAsync(embed: new EmbedBuilder()
            .WithTitle($"Prices now quoted for {settings.Region}")
            .WithDescription($"""
                Giveaways and deals use the **{settings.Region}** storefront and **{settings.Currency}**.
                {UsdCaveat()}
                """)
            .WithColor(new Color(0x2A7150))
            .Build(),
            ephemeral: true);
    }

    /// <summary>Explains what merchant can post.</summary>
    [SlashCommand("help", "What merchant can announce, and how to set it up.")]
    public async Task HelpAsync()
    {
        await DeferAsync(ephemeral: true);

        EmbedBuilder embed = new EmbedBuilder()
            .WithTitle("What're ya buyin'?")
            .WithDescription(
                "Pick a feed, pick a channel, done. Everything below is ready to use — " +
                "there is nothing to configure beyond the channel.")
            .WithColor(new Color(0xC24A12))
            .WithFooter(
                "/merchant add · /merchant list · /merchant remove · /merchant preview · /merchant region");

        await FollowupAsync(embed: Announcer.Catalog(embed, _catalog.All), ephemeral: true);
    }

    /// <summary>
    /// The server this command came from, read from the interaction payload rather than the
    /// gateway cache. The two agree in normal operation, but a guild the gateway failed to cache
    /// must still be addressable — otherwise one bad dispatch takes every command down with it.
    /// </summary>
    private ulong GuildId => Context.Interaction.GuildId ?? Context.Guild.Id;

    /// <summary>
    /// The permissions merchant lacks in a channel, in the order a person would turn them on.
    /// </summary>
    /// <returns>
    /// Empty when merchant can post there, and <c>null</c> when the check could not run at all
    /// because the guild is absent from the gateway cache. Those are different answers: treating
    /// "could not check" as "nothing is missing" would report a problem that does not exist.
    /// </returns>
    private IReadOnlyList<string>? Missing(ITextChannel channel)
    {
        if (Context.Guild?.CurrentUser is not SocketGuildUser self)
        {
            return null;
        }

        ChannelPermissions permissions = self.GetPermissions(channel);
        return [.. Required.Where(r => !r.Held(permissions)).Select(r => r.Name)];
    }

    /// <summary>
    /// The caveat about US dollars, named from the catalog rather than from memory: it applies to
    /// whichever feeds are backed by CheapShark, which quotes USD and nothing else.
    /// </summary>
    private string UsdCaveat()
    {
        string[] priced = [.. _catalog.All
            .Where(c => _catalog.SourceType(c.Key) == CheapSharkSourceFactory.TypeName)
            .Select(c => $"*{c.Label}*")];

        return priced.Length == 0
            ? string.Empty
            : $"""

               One caveat worth knowing: {string.Join(" and ", priced)} come from a source that only
               quotes US dollars, so those stay in USD whatever this is set to.
               """;
    }

    /// <summary>The reply to a feed name that is not in the catalog.</summary>
    private Embed Unknown(string feed) => Problem(
        $"There is no feed called \"{Format.Sanitize(feed.Trim())}\"",
        _catalog.All.Count == 0
            ? "No feeds are configured yet. Whoever runs merchant needs to look at its settings file."
            : $"""
               Start typing in the **feed** box and pick from the list that appears.

               Right now merchant can post: {string.Join(", ", _catalog.All.Select(c => c.Label))}.
               """);

    /// <summary>
    /// A refusal that says what to do about it. Truncated on the way out: some of these name every
    /// feed in the catalog, and how many that is belongs to whoever edits the settings file.
    /// </summary>
    private static Embed Problem(string title, string what) => new EmbedBuilder()
        .WithTitle(Announcer.Truncate(title, EmbedBuilder.MaxTitleLength))
        .WithDescription(Announcer.Truncate(what, EmbedBuilder.MaxDescriptionLength))
        .WithColor(new Color(0xC24A12))
        .Build();
}
