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

    private readonly Merchant.Storage.Ledger _ledger;

    /// <summary>Opens a throwaway database per test.</summary>
    public MultiGuildTests() => _ledger = new Merchant.Storage.Ledger(_path);

    private static FeedItem Item(string id) => new(id, $"Game {id}", $"https://example.test/{id}");

    [Fact]
    public void Two_servers_hold_independent_subscription_lists()
    {
        _ledger.Subscribe(Hers, 10, "under-10", Cadence.Daily, null);
        _ledger.Subscribe(Hers, 11, "free-games", Cadence.Live, null);
        _ledger.Subscribe(Mine, 20, "best-deals", Cadence.Weekly, null);

        Assert.Equal(2, _ledger.ForGuild(Hers).Count);
        Assert.Single(_ledger.ForGuild(Mine));
        Assert.Equal(3, _ledger.All().Count);

        Assert.DoesNotContain(_ledger.ForGuild(Hers), s => s.CategoryKey == "best-deals");
        Assert.DoesNotContain(_ledger.ForGuild(Mine), s => s.CategoryKey == "under-10");
    }

    [Fact]
    public void The_same_feed_in_two_servers_is_two_subscriptions()
    {
        (long hers, bool createdHers) = _ledger.Subscribe(Hers, 10, "under-10", Cadence.Daily, null);
        (long mine, bool createdMine) = _ledger.Subscribe(Mine, 20, "under-10", Cadence.Live, null);

        Assert.True(createdHers);
        Assert.True(createdMine);
        Assert.NotEqual(hers, mine);

        // …each keeping its own cadence.
        Assert.Equal(Cadence.Daily, Assert.Single(_ledger.ForGuild(Hers)).Cadence);
        Assert.Equal(Cadence.Live, Assert.Single(_ledger.ForGuild(Mine)).Cadence);
    }

    [Fact]
    public void The_same_deal_reaches_both_servers()
    {
        // Separate ledgers per subscription: a deal one server has already been shown must not be
        // suppressed for the other.
        (long hers, _) = _ledger.Subscribe(Hers, 10, "under-10", Cadence.Live, null);
        (long mine, _) = _ledger.Subscribe(Mine, 20, "under-10", Cadence.Live, null);

        Assert.Equal(1, _ledger.Record(hers, [Item("deal-a")], alreadyPosted: false));
        Assert.Equal(1, _ledger.Record(mine, [Item("deal-a")], alreadyPosted: false));

        Assert.Equal(1, _ledger.PendingCount(hers));
        Assert.Equal(1, _ledger.PendingCount(mine));
    }

    [Fact]
    public void Posting_in_one_server_leaves_the_other_waiting()
    {
        (long hers, _) = _ledger.Subscribe(Hers, 10, "under-10", Cadence.Live, null);
        (long mine, _) = _ledger.Subscribe(Mine, 20, "under-10", Cadence.Live, null);

        _ledger.Record(hers, [Item("deal-a")], alreadyPosted: false);
        _ledger.Record(mine, [Item("deal-a")], alreadyPosted: false);

        _ledger.MarkFlushed(hers, ["deal-a"], DateTimeOffset.UtcNow);

        Assert.Equal(0, _ledger.PendingCount(hers));
        Assert.Equal(1, _ledger.PendingCount(mine));
    }

    [Fact]
    public void Region_is_per_server()
    {
        _ledger.SaveSettings(new GuildSettings(Hers, "ES", "EUR"));

        Assert.Equal("ES", _ledger.Settings(Hers).Region);
        Assert.Equal("US", _ledger.Settings(Mine).Region);
    }

    [Fact]
    public void One_server_cannot_remove_or_see_anothers_subscription()
    {
        (long hers, _) = _ledger.Subscribe(Hers, 10, "under-10", Cadence.Daily, null);

        Assert.False(_ledger.Unsubscribe(Mine, hers));
        Assert.Single(_ledger.ForGuild(Hers));
        Assert.Empty(_ledger.ForGuild(Mine));
    }

    [Fact]
    public void Leaving_one_server_does_not_disturb_another()
    {
        (long hers, _) = _ledger.Subscribe(Hers, 10, "under-10", Cadence.Daily, null);
        _ledger.Subscribe(Mine, 20, "under-10", Cadence.Daily, null);
        _ledger.Record(hers, [Item("deal-a")], alreadyPosted: false);

        Assert.True(_ledger.Unsubscribe(Hers, hers));

        Assert.Empty(_ledger.ForGuild(Hers));
        Assert.Single(_ledger.ForGuild(Mine));
        Assert.Single(_ledger.All());
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
