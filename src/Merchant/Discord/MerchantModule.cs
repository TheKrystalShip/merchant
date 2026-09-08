using System.Text;
using Merchant.Feeds;
using Merchant.Sources;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;

namespace Merchant.Discord;

/// <summary>
/// The whole configurable surface: five commands under <c>/merchant</c>.
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

    private readonly Store.Store _store;
    private readonly IHttpClientFactory _http;

    /// <summary>Wires the commands to the ledger and the network.</summary>
    public MerchantModule(Store.Store store, IHttpClientFactory http)
    {
        _store = store;
        _http = http;
    }

    /// <summary>Wires a feed to a channel.</summary>
    [SlashCommand("add", "Start posting a feed to a channel.")]
    public async Task AddAsync(
        [Summary("feed", "What should be announced.")] FeedChoice feed,
        [Summary("channel", "Where it should be posted.")] ITextChannel channel,
        [Summary("how-often", "Leave as recommended unless you have a reason.")]
        CadenceChoice howOften = CadenceChoice.Default,
        [Summary("ping", "A role to mention when something is posted.")] IRole? ping = null)
    {
        await DeferAsync(ephemeral: true);

        string key = feed.ToKey();
        Category category = Catalog.Find(key)!;

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
        (long id, bool created) = _store.Subscribe(
            GuildId, channel.Id, key, cadence, ping?.Id);

        EmbedBuilder embed = new EmbedBuilder()
            .WithColor(new Color(category.Colour))
            .WithTitle(created ? $"{category.Label} → {channel.Name}" : $"{category.Label} updated")
            .WithDescription($"""
                 {category.Description}

                 Posting **{cadence.Describe()}** in {channel.Mention}{(ping is null ? "" : $", pinging {ping.Mention}")}.
                 {(created ? "The first few will appear within the hour." : "")}
                 """)
            .WithFooter($"Subscription #{id} · remove it with /merchant remove id:{id}");

        await FollowupAsync(embed: embed.Build(), ephemeral: true);
    }

    /// <summary>Shows what is wired up in this server.</summary>
    [SlashCommand("list", "Show which feeds are posting where.")]
    public async Task ListAsync()
    {
        await DeferAsync(ephemeral: true);

        IReadOnlyList<Subscription> subscriptions = _store.ForGuild(GuildId);

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
            Category? category = Catalog.Find(subscription.CategoryKey);

            body.Append("`#").Append(subscription.Id).Append("` **")
                .Append(category?.Label ?? subscription.CategoryKey).Append("** → ")
                .Append(MentionUtils.MentionChannel(subscription.ChannelId))
                .Append(" · ").Append(subscription.Cadence.Describe());

            if (subscription.MentionRoleId is { } role)
            {
                body.Append(" · pings ").Append(MentionUtils.MentionRole(role));
            }

            int waiting = _store.PendingCount(subscription.Id);
            if (waiting > 0 && subscription.Cadence != Cadence.Live)
            {
                body.Append(" · ").Append(waiting).Append(" waiting");
            }

            body.AppendLine();
        }

        GuildSettings settings = _store.Settings(GuildId);

        await FollowupAsync(embed: new EmbedBuilder()
            .WithTitle("What merchant is announcing")
            .WithDescription(body.ToString())
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

        bool removed = _store.Unsubscribe(GuildId, id);

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
        [Summary("feed", "The feed to sample.")] FeedChoice feed)
    {
        await DeferAsync(ephemeral: true);

        string key = feed.ToKey();
        Category category = Catalog.Find(key)!;
        GuildSettings settings = _store.Settings(GuildId);

        ISource source = Catalog.SourceFor(
            key, _http.CreateClient(BotOptions.HttpClientName), settings);

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

        GuildSettings settings = new(
            GuildId, country.Trim().ToUpperInvariant(), currency.Trim().ToUpperInvariant());

        _store.SaveSettings(settings);

        await FollowupAsync(embed: new EmbedBuilder()
            .WithTitle($"Prices now quoted for {settings.Region}")
            .WithDescription($"""
                 Giveaways and deals will use the **{settings.Region}** storefront and **{settings.Currency}**.

                 One caveat worth knowing: *Games Under $10* and *Best Game Deals* come from a source
                 that only quotes US dollars, so those two stay in USD whatever this is set to.
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
            .WithFooter("/merchant add · /merchant list · /merchant remove · /merchant preview · /merchant region");

        foreach (Category category in Catalog.All)
        {
            embed.AddField(
                category.Label,
                $"{category.Description}\nSuggested channel: `#{category.SuggestedChannelName}` · " +
                $"posts {category.DefaultCadence.Describe()}");
        }

        await FollowupAsync(embed: embed.Build(), ephemeral: true);
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

    /// <summary>A refusal that says what to do about it.</summary>
    private static Embed Problem(string title, string what) => new EmbedBuilder()
        .WithTitle(title)
        .WithDescription(what)
        .WithColor(new Color(0xC24A12))
        .Build();
}
