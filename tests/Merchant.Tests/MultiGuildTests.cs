using Xunit;

namespace Merchant.Tests;

/// <summary>
/// merchant is one process serving many servers. These pin the isolation between them: what one
/// server configures, sees, removes and receives must be unaffected by every other.
/// </summary>
public class MultiGuildTests : IDisposable
{
    private const ulong Hers = 385730677141929985;
    private const ulong Mine = 111111111111111111;

    private readonly string _path = Path.Combine(
        Path.GetTempPath(), $"merchant-guilds-{Guid.NewGuid():N}.db");

    private readonly Merchant.Store.Store _store;

    /// <summary>Opens a throwaway database per test.</summary>
    public MultiGuildTests() => _store = new Merchant.Store.Store(_path);

    private static FeedItem Item(string id) => new(id, $"Game {id}", $"https://example.test/{id}");

    [Fact]
    public void Two_servers_hold_independent_subscription_lists()
    {
        _store.Subscribe(Hers, 10, "under-10", Cadence.Daily, null);
        _store.Subscribe(Hers, 11, "free-games", Cadence.Live, null);
        _store.Subscribe(Mine, 20, "best-deals", Cadence.Weekly, null);

        Assert.Equal(2, _store.ForGuild(Hers).Count);
        Assert.Single(_store.ForGuild(Mine));
        Assert.Equal(3, _store.All().Count);

        Assert.DoesNotContain(_store.ForGuild(Hers), s => s.CategoryKey == "best-deals");
        Assert.DoesNotContain(_store.ForGuild(Mine), s => s.CategoryKey == "under-10");
    }

    [Fact]
    public void The_same_feed_in_two_servers_is_two_subscriptions()
    {
        (long hers, bool createdHers) = _store.Subscribe(Hers, 10, "under-10", Cadence.Daily, null);
        (long mine, bool createdMine) = _store.Subscribe(Mine, 20, "under-10", Cadence.Live, null);

        Assert.True(createdHers);
        Assert.True(createdMine);
        Assert.NotEqual(hers, mine);

        // …each keeping its own cadence.
        Assert.Equal(Cadence.Daily, Assert.Single(_store.ForGuild(Hers)).Cadence);
        Assert.Equal(Cadence.Live, Assert.Single(_store.ForGuild(Mine)).Cadence);
    }

    [Fact]
    public void The_same_deal_reaches_both_servers()
    {
        // Separate ledgers per subscription: a deal one server has already been shown must not be
        // suppressed for the other.
        (long hers, _) = _store.Subscribe(Hers, 10, "under-10", Cadence.Live, null);
        (long mine, _) = _store.Subscribe(Mine, 20, "under-10", Cadence.Live, null);

        Assert.Equal(1, _store.Record(hers, [Item("deal-a")], alreadyPosted: false));
        Assert.Equal(1, _store.Record(mine, [Item("deal-a")], alreadyPosted: false));

        Assert.Equal(1, _store.PendingCount(hers));
        Assert.Equal(1, _store.PendingCount(mine));
    }

    [Fact]
    public void Posting_in_one_server_leaves_the_other_waiting()
    {
        (long hers, _) = _store.Subscribe(Hers, 10, "under-10", Cadence.Live, null);
        (long mine, _) = _store.Subscribe(Mine, 20, "under-10", Cadence.Live, null);

        _store.Record(hers, [Item("deal-a")], alreadyPosted: false);
        _store.Record(mine, [Item("deal-a")], alreadyPosted: false);

        _store.MarkFlushed(hers, ["deal-a"], DateTimeOffset.UtcNow);

        Assert.Equal(0, _store.PendingCount(hers));
        Assert.Equal(1, _store.PendingCount(mine));
    }

    [Fact]
    public void Region_is_per_server()
    {
        _store.SaveSettings(new GuildSettings(Hers, "ES", "EUR"));

        Assert.Equal("ES", _store.Settings(Hers).Region);
        Assert.Equal("US", _store.Settings(Mine).Region);
    }

    [Fact]
    public void One_server_cannot_remove_or_see_anothers_subscription()
    {
        (long hers, _) = _store.Subscribe(Hers, 10, "under-10", Cadence.Daily, null);

        Assert.False(_store.Unsubscribe(Mine, hers));
        Assert.Single(_store.ForGuild(Hers));
        Assert.Empty(_store.ForGuild(Mine));
    }

    [Fact]
    public void Leaving_one_server_does_not_disturb_another()
    {
        (long hers, _) = _store.Subscribe(Hers, 10, "under-10", Cadence.Daily, null);
        _store.Subscribe(Mine, 20, "under-10", Cadence.Daily, null);
        _store.Record(hers, [Item("deal-a")], alreadyPosted: false);

        Assert.True(_store.Unsubscribe(Hers, hers));

        Assert.Empty(_store.ForGuild(Hers));
        Assert.Single(_store.ForGuild(Mine));
        Assert.Single(_store.All());
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _store.Dispose();

        foreach (string file in (string[])[_path, $"{_path}-wal", $"{_path}-shm"])
        {
            File.Delete(file);
        }

        GC.SuppressFinalize(this);
    }
}
