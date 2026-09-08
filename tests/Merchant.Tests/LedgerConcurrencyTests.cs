using Xunit;

namespace Merchant.Tests;

/// <summary>
/// The ledger is one connection reached by two callers that never take turns: the sweep on its
/// background loop, and every slash command on the gateway's threads. SQLite scopes a transaction
/// to the connection rather than to the caller, so these pin that one caller's work cannot be
/// swallowed by the other's.
/// </summary>
public class LedgerConcurrencyTests : IDisposable
{
    private const ulong Guild = 385730677141929985;

    private readonly string _path = Path.Combine(
        Path.GetTempPath(), $"merchant-concurrent-{Guid.NewGuid():N}.db");

    private readonly Merchant.Storage.Ledger _ledger;

    public LedgerConcurrencyTests() => _ledger = new Merchant.Storage.Ledger(_path);

    private static FeedItem Item(string id) => new(id, $"Game {id}", $"https://example.test/{id}");

    [Fact]
    public async Task A_subscription_survives_a_sweep_that_fails_while_filing_what_it_found()
    {
        (long swept, _) = _ledger.Subscribe(Guild, 10, "under-10", Cadence.Daily, null);

        using ManualResetEventSlim reached = new();
        Task<(long Id, bool Created)>? subscribing = null;

        // The sweep hands Record a lazy sequence — fetched.Skip(…) — so a source that dies partway
        // through leaves the transaction open and then rolls it back. An ungated command writing in
        // that window is inside that transaction and goes down with it: /merchant add answers with
        // a subscription number for a row that no longer exists.
        IEnumerable<FeedItem> Filing()
        {
            yield return Item("deal-a");

            subscribing = Task.Run(() =>
            {
                reached.Set();
                return _ledger.Subscribe(Guild, 20, "free-games", Cadence.Live, null);
            });

            reached.Wait(TimeSpan.FromSeconds(5));
            Thread.Sleep(100);

            throw new IOException("the feed died halfway through the page.");
        }

        Assert.Throws<IOException>(() => _ledger.Record(swept, Filing(), alreadyPosted: false));

        (long added, bool created) = await subscribing!;

        Assert.True(created);
        Assert.Contains(_ledger.ForGuild(Guild), s => s.Id == added && s.CategoryKey == "free-games");
    }

    [Fact]
    public async Task Sweeping_and_answering_commands_at_once_is_not_an_error()
    {
        (long id, _) = _ledger.Subscribe(Guild, 10, "under-10", Cadence.Daily, null);

        Task sweeping = Task.Run(() =>
        {
            for (int pass = 0; pass < 200; pass++)
            {
                _ledger.Record(id, Enumerable.Range(0, 20).Select(n => Item($"{pass}-{n}")), false);
                _ledger.MarkFlushed(id, [$"{pass}-0"], DateTimeOffset.UtcNow);
            }
        });

        Task commanding = Task.Run(() =>
        {
            for (int call = 0; call < 200; call++)
            {
                _ledger.ForGuild(Guild);
                _ledger.PendingCount(id);
                _ledger.Settings(Guild);
            }
        });

        await Task.WhenAll(sweeping, commanding);

        // Every write landed: 200 passes of 20 items, less the one item each pass marked posted.
        Assert.Single(_ledger.All());
        Assert.Equal(3800, _ledger.PendingCount(id));
    }

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
