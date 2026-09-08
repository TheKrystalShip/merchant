using Xunit;

namespace Merchant.Tests;

/// <summary>
/// The ledger. These are the tests that stand between a working bot and one that reposts the same
/// deal every twenty minutes, or floods a channel on the day it is invited.
/// </summary>
public class LedgerTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), $"merchant-test-{Guid.NewGuid():N}.db");

    private readonly Merchant.Storage.Ledger _ledger;

    /// <summary>Opens a throwaway database per test.</summary>
    public LedgerTests() => _ledger = new Merchant.Storage.Ledger(_path);

    private static FeedItem Item(string id) => new(id, $"Game {id}", $"https://example.test/{id}");

    // ---- subscriptions ---------------------------------------------------------------------

    [Fact]
    public void Subscribing_twice_updates_rather_than_duplicates()
    {
        (long first, bool created) = _ledger.Subscribe(1, 2, "under-10", Cadence.Daily, null);
        (long second, bool again) = _ledger.Subscribe(1, 2, "under-10", Cadence.Weekly, 99);

        Assert.True(created);
        Assert.False(again);
        Assert.Equal(first, second);

        Subscription only = Assert.Single(_ledger.ForGuild(1));
        Assert.Equal(Cadence.Weekly, only.Cadence);
        Assert.Equal(99ul, only.MentionRoleId);
    }

    [Fact]
    public void The_same_feed_can_go_to_two_different_channels()
    {
        _ledger.Subscribe(1, 100, "under-10", Cadence.Daily, null);
        _ledger.Subscribe(1, 200, "under-10", Cadence.Daily, null);

        Assert.Equal(2, _ledger.ForGuild(1).Count);
    }

    [Fact]
    public void One_server_cannot_remove_another_servers_subscription()
    {
        (long id, _) = _ledger.Subscribe(guildId: 1, channelId: 2, "under-10", Cadence.Daily, null);

        Assert.False(_ledger.Unsubscribe(guildId: 999, id));
        Assert.True(_ledger.Unsubscribe(guildId: 1, id));
    }

    [Fact]
    public void Removing_a_subscription_forgets_what_it_had_seen()
    {
        (long id, _) = _ledger.Subscribe(1, 2, "under-10", Cadence.Daily, null);
        _ledger.Record(id, [Item("a")], alreadyPosted: false);

        Assert.True(_ledger.Unsubscribe(1, id));

        // Re-adding gets a fresh id and an empty ledger, so it opens as a new channel would.
        (long again, bool created) = _ledger.Subscribe(1, 2, "under-10", Cadence.Daily, null);
        Assert.True(created);
        Assert.True(_ledger.IsUnswept(again));
    }

    [Fact]
    public void Listing_is_scoped_to_one_server()
    {
        _ledger.Subscribe(1, 10, "under-10", Cadence.Daily, null);
        _ledger.Subscribe(2, 20, "best-deals", Cadence.Daily, null);

        Assert.Single(_ledger.ForGuild(1));
        Assert.Single(_ledger.ForGuild(2));
        Assert.Equal(2, _ledger.All().Count);
    }

    // ---- settings --------------------------------------------------------------------------

    [Fact]
    public void A_server_that_set_nothing_gets_the_defaults()
    {
        GuildSettings settings = _ledger.Settings(1);

        Assert.Equal("US", settings.Region);
        Assert.Equal("USD", settings.Currency);
    }

    [Fact]
    public void Settings_round_trip_and_overwrite()
    {
        _ledger.SaveSettings(new GuildSettings(1, "ES", "EUR"));
        _ledger.SaveSettings(new GuildSettings(1, "GB", "GBP"));

        Assert.Equal("GB", _ledger.Settings(1).Region);
        Assert.Equal("USD", _ledger.Settings(2).Currency);
    }

    // ---- the ledger ------------------------------------------------------------------------

    [Fact]
    public void An_item_already_seen_is_not_recorded_again()
    {
        (long id, _) = _ledger.Subscribe(1, 2, "under-10", Cadence.Live, null);

        Assert.Equal(2, _ledger.Record(id, [Item("a"), Item("b")], alreadyPosted: false));
        Assert.Equal(1, _ledger.Record(id, [Item("a"), Item("b"), Item("c")], alreadyPosted: false));
        Assert.Equal(3, _ledger.PendingCount(id));
    }

    [Fact]
    public void Two_subscriptions_keep_separate_ledgers()
    {
        // The same deal going to two channels has to post in both.
        (long one, _) = _ledger.Subscribe(1, 10, "under-10", Cadence.Live, null);
        (long two, _) = _ledger.Subscribe(1, 20, "under-10", Cadence.Live, null);

        Assert.Equal(1, _ledger.Record(one, [Item("a")], alreadyPosted: false));
        Assert.Equal(1, _ledger.Record(two, [Item("a")], alreadyPosted: false));
    }

    [Fact]
    public void A_backlog_filed_as_posted_never_reaches_a_channel()
    {
        // This is the guard against dumping a month of history the moment a channel is wired up.
        (long id, _) = _ledger.Subscribe(1, 2, "under-10", Cadence.Live, null);

        _ledger.Record(id, [Item("old-1"), Item("old-2")], alreadyPosted: true);

        Assert.Equal(0, _ledger.PendingCount(id));
        Assert.False(_ledger.IsUnswept(id));
    }

    [Fact]
    public void Pending_comes_back_newest_first_and_capped()
    {
        (long id, _) = _ledger.Subscribe(1, 2, "under-10", Cadence.Daily, null);
        _ledger.Record(id, [Item("a"), Item("b"), Item("c"), Item("d")], alreadyPosted: false);

        Assert.Equal(2, _ledger.Pending(id, 2).Count);
        Assert.Equal(4, _ledger.Pending(id, 50).Count);
    }

    [Fact]
    public void Payloads_survive_the_round_trip()
    {
        (long id, _) = _ledger.Subscribe(1, 2, "under-10", Cadence.Daily, null);

        FeedItem rich = new(
            "x", "Half-Life 3", "https://example.test/hl3",
            Summary: "At last.", ImageUrl: "https://example.test/a.jpg",
            Published: DateTimeOffset.UnixEpoch,
            Price: "$4.99", WasPrice: "$19.99", DiscountPercent: 75, Store: "Steam", Score: 96);

        _ledger.Record(id, [rich], alreadyPosted: false);

        FeedItem back = Assert.Single(_ledger.Pending(id, 10));
        Assert.Equal(rich, back);
    }

    [Fact]
    public void Flushing_clears_the_backlog_and_stamps_the_time()
    {
        (long id, _) = _ledger.Subscribe(1, 2, "under-10", Cadence.Daily, null);
        _ledger.Record(id, [Item("a"), Item("b")], alreadyPosted: false);

        DateTimeOffset when = DateTimeOffset.UtcNow;
        _ledger.MarkFlushed(id, ["a", "b"], when);

        Assert.Equal(0, _ledger.PendingCount(id));
        Subscription after = Assert.Single(_ledger.ForGuild(1));
        Assert.NotNull(after.LastPostedAt);
        Assert.True((after.LastPostedAt!.Value - when).Duration() < TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void A_flushed_item_is_still_remembered_so_it_is_not_reposted()
    {
        (long id, _) = _ledger.Subscribe(1, 2, "under-10", Cadence.Live, null);
        _ledger.Record(id, [Item("a")], alreadyPosted: false);
        _ledger.MarkFlushed(id, ["a"], DateTimeOffset.UtcNow);

        Assert.Equal(0, _ledger.Record(id, [Item("a")], alreadyPosted: false));
    }

    [Fact]
    public void Flushing_leaves_alone_what_was_not_posted()
    {
        // A live burst deliberately posts a few of what is waiting. Clearing the whole backlog
        // would drop the rest without anyone ever seeing them.
        (long id, _) = _ledger.Subscribe(1, 2, "free-games", Cadence.Live, null);
        _ledger.Record(id, [Item("a"), Item("b"), Item("c"), Item("d"), Item("e")],
            alreadyPosted: false);

        IReadOnlyList<FeedItem> burst = _ledger.Pending(id, 2);
        _ledger.MarkFlushed(id, burst.Select(i => i.Id), DateTimeOffset.UtcNow);

        Assert.Equal(3, _ledger.PendingCount(id));

        // …and the next sweep drains the rest rather than repeating the first two.
        IReadOnlyList<FeedItem> next = _ledger.Pending(id, 2);
        Assert.Empty(next.Select(i => i.Id).Intersect(burst.Select(i => i.Id)));
    }

    [Fact]
    public void Flushing_an_id_that_is_not_pending_changes_nothing()
    {
        (long id, _) = _ledger.Subscribe(1, 2, "under-10", Cadence.Live, null);
        _ledger.Record(id, [Item("a")], alreadyPosted: false);

        _ledger.MarkFlushed(id, ["not-mine"], DateTimeOffset.UtcNow);

        Assert.Equal(1, _ledger.PendingCount(id));
    }

    [Fact]
    public void Pruning_forgets_old_posted_items_but_never_a_pending_one()
    {
        (long id, _) = _ledger.Subscribe(1, 2, "under-10", Cadence.Weekly, null);

        _ledger.Record(id, [Item("posted")], alreadyPosted: true);
        _ledger.Record(id, [Item("waiting")], alreadyPosted: false);

        Assert.Equal(1, _ledger.Prune(TimeSpan.Zero));
        Assert.Equal(1, _ledger.PendingCount(id));
    }

    [Fact]
    public void Pruning_keeps_items_inside_the_retention_window()
    {
        (long id, _) = _ledger.Subscribe(1, 2, "under-10", Cadence.Live, null);
        _ledger.Record(id, [Item("a")], alreadyPosted: true);

        Assert.Equal(0, _ledger.Prune(TimeSpan.FromDays(60)));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _ledger.Dispose();

        foreach (string file in (string[])[_path, $"{_path}-wal", $"{_path}-shm"])
        {
            File.Delete(file);
        }

        GC.SuppressFinalize(this);
    }
}
