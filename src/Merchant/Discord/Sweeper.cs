using Discord;
using Discord.WebSocket;
using Merchant.Feeds;
using Merchant.Sources;
using Merchant.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Merchant.Discord;

/// <summary>
/// The loop: fetch every subscribed feed, file what is new, and post whatever is due.
///
/// Fetching and posting are separated on purpose. Sweeping happens on one interval for everybody;
/// posting happens on each subscription's own cadence, reading the backlog the sweep filed. That
/// split is what makes a weekly channel possible without polling weekly and missing the week.
/// </summary>
public sealed class Sweeper : BackgroundService
{
    /// <summary>How long a posted item stays in the ledger before it is forgotten.</summary>
    private static readonly TimeSpan Retention = TimeSpan.FromDays(60);

    /// <summary>
    /// Cadence windows are shaved below their nominal period so a sweep that lands a few minutes
    /// late does not push a daily post into a permanent slow drift around the clock.
    /// </summary>
    private static readonly TimeSpan DailyWindow = TimeSpan.FromHours(23);
    private static readonly TimeSpan WeeklyWindow = TimeSpan.FromDays(6.9);

    /// <summary>Most items one digest speaks for. A week of a busy feed stays well inside this.</summary>
    private const int DigestCeiling = 500;

    private readonly DiscordSocketClient _discord;
    private readonly Ledger _ledger;
    private readonly IHttpClientFactory _http;
    private readonly FeedCatalog _catalog;
    private readonly BotOptions _options;
    private readonly ILogger<Sweeper> _log;

    /// <summary>Wires the sweep to the gateway, the ledger and the network.</summary>
    public Sweeper(
        DiscordSocketClient discord,
        Ledger ledger,
        IHttpClientFactory http,
        FeedCatalog catalog,
        BotOptions options,
        ILogger<Sweeper> log)
    {
        _discord = discord;
        _ledger = ledger;
        _http = http;
        _catalog = catalog;
        _options = options;
        _log = log;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Nothing can be posted before the gateway hands over the guild and channel caches.
        while (!ct.IsCancellationRequested && _discord.ConnectionState != ConnectionState.Connected)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }

        using PeriodicTimer timer = new(_options.SweepInterval);

        do
        {
            try
            {
                await SweepAsync(ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                // One bad sweep must not end the loop; the next one is a few minutes away.
                _log.LogError(ex, "Sweep failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(ct));
    }

    /// <summary>One pass over every subscription. Public so a test or a one-shot run can drive it.</summary>
    internal async Task SweepAsync(CancellationToken ct)
    {
        IReadOnlyList<Subscription> subscriptions = _ledger.All();
        if (subscriptions.Count == 0)
        {
            return;
        }

        _log.LogInformation("Sweeping {Count} subscription(s).", subscriptions.Count);
        HttpClient http = _http.CreateClient(BotOptions.HttpClientName);

        // One fetch per feed per storefront, however many channels are waiting on it. Subscriptions
        // multiply with servers and channels; upstreams do not care why merchant is asking twice,
        // and Reddit in particular answers 429 to far less than that.
        Dictionary<Fetch, IReadOnlyList<FeedItem>> fetched = [];

        foreach (Subscription subscription in subscriptions)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                await SweepOneAsync(subscription, http, fetched, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _log.LogWarning(ex, "Subscription {Id} ({Category}) failed.",
                    subscription.Id, subscription.CategoryKey);
            }
        }

        _log.LogDebug("Fetched {Count} feed(s) for {Subscriptions} subscription(s).",
            fetched.Count, subscriptions.Count);

        int pruned = _ledger.Prune(Retention);
        if (pruned > 0)
        {
            _log.LogDebug("Pruned {Count} expired ledger row(s).", pruned);
        }
    }

    private async Task SweepOneAsync(
        Subscription subscription,
        HttpClient http,
        Dictionary<Fetch, IReadOnlyList<FeedItem>> fetched,
        CancellationToken ct)
    {
        Category? category = _catalog.Find(subscription.CategoryKey);
        if (category is null)
        {
            // The feed was removed from the settings file while a channel was still subscribed to
            // it. Skipping is right — nothing can be fetched — and /merchant list shows the row as
            // retired so somebody can clear it.
            _log.LogWarning("Subscription {Id} names feed '{Key}', which is not in the catalog.",
                subscription.Id, subscription.CategoryKey);
            return;
        }

        GuildSettings settings = _ledger.Settings(subscription.GuildId);
        Fetch slot = new(category.Key, settings.Region, settings.Currency);

        if (!fetched.TryGetValue(slot, out IReadOnlyList<FeedItem>? items))
        {
            ISource source = _catalog.SourceFor(category.Key, http, settings);
            items = await source.FetchAsync(ct);
            fetched[slot] = items;
        }

        if (items.Count == 0)
        {
            return;
        }

        if (_ledger.IsUnswept(subscription.Id))
        {
            // First contact. The backlog is filed silently, but a handful goes out immediately:
            // a channel that stays empty for a day after setup reads as a bot that does not work.
            IReadOnlyList<FeedItem> opener = [.. items.Take(Announcer.LiveBurst)];
            _ledger.Record(subscription.Id, opener, alreadyPosted: false);
            _ledger.Record(subscription.Id, items.Skip(opener.Count), alreadyPosted: true);
        }
        else
        {
            int added = _ledger.Record(subscription.Id, items, alreadyPosted: false);
            if (added > 0)
            {
                _log.LogDebug("Subscription {Id}: {Count} new item(s).", subscription.Id, added);
            }
        }

        if (IsDue(subscription))
        {
            await FlushAsync(subscription, category, ct);
        }
    }

    /// <summary>Whether this subscription's channel is owed a post right now.</summary>
    internal static bool IsDue(Subscription subscription, DateTimeOffset? now = null)
    {
        DateTimeOffset moment = now ?? DateTimeOffset.UtcNow;

        return subscription.Cadence switch
        {
            Cadence.Live => true,
            Cadence.Daily => Elapsed(subscription, moment) >= DailyWindow,
            Cadence.Weekly => Elapsed(subscription, moment) >= WeeklyWindow,
            _ => false,
        };
    }

    /// <summary>Time since the last post; unbounded when the channel has never had one.</summary>
    private static TimeSpan Elapsed(Subscription subscription, DateTimeOffset now) =>
        subscription.LastPostedAt is { } last ? now - last : TimeSpan.MaxValue;

    private async Task FlushAsync(Subscription subscription, Category category, CancellationToken ct)
    {
        if (_ledger.PendingCount(subscription.Id) == 0)
        {
            return;
        }

        if (await _discord.GetChannelAsync(subscription.ChannelId) is not IMessageChannel channel)
        {
            _log.LogWarning("Subscription {Id} points at channel {Channel}, which merchant cannot see.",
                subscription.Id, subscription.ChannelId);
            return;
        }

        // A live channel drains a few at a time, so the rest must stay pending. A digest speaks for
        // the whole backlog — it lists the first dozen and counts the remainder — so it takes all
        // of it and clears all of it.
        int limit = subscription.Cadence == Cadence.Live ? Announcer.LiveBurst : DigestCeiling;
        IReadOnlyList<FeedItem> pending = _ledger.Pending(subscription.Id, limit);

        if (pending.Count == 0)
        {
            return;
        }

        string? mention = subscription.MentionRoleId is { } role ? MentionUtils.MentionRole(role) : null;

        try
        {
            if (subscription.Cadence == Cadence.Live)
            {
                // Oldest first, so a channel reads in the order things actually happened.
                foreach (FeedItem item in pending.Reverse())
                {
                    await channel.SendMessageAsync(text: mention, embed: Announcer.Item(category, item));
                    mention = null; // Ping once per burst, not once per deal.
                }
            }
            else
            {
                string period = subscription.Cadence == Cadence.Weekly ? "this week" : "today";
                await channel.SendMessageAsync(
                    text: mention, embed: Announcer.Digest(category, pending, period));
            }
        }
        catch (global::Discord.Net.HttpException ex)
        {
            // Missing Send Messages or Embed Links in that channel is the overwhelmingly common
            // cause. Leave the backlog unposted so it goes out once the permission is fixed.
            _log.LogWarning(ex, "Could not post to channel {Channel} for subscription {Id}.",
                subscription.ChannelId, subscription.Id);
            return;
        }

        _ledger.MarkFlushed(subscription.Id, pending.Select(i => i.Id), DateTimeOffset.UtcNow);
        _log.LogInformation("Posted {Count} item(s) to {Channel} for {Category}.",
            pending.Count, subscription.ChannelId, category.Key);
    }

    /// <summary>
    /// What makes two subscriptions the same fetch: the feed, and the storefront its URL is built
    /// for. Two servers on different regions genuinely are two requests; everything else is one.
    /// </summary>
    private readonly record struct Fetch(string Feed, string Region, string Currency);
}
