using System.Globalization;
using Merchant.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Merchant.Tests;

/// <summary>
/// What happens to a database that already exists. Every other test in this suite starts from an
/// empty file, which is the one case that cannot go wrong: the interesting ones are a ledger
/// carrying live subscriptions through an upgrade, and a ledger this build must refuse.
/// </summary>
public class LedgerMigrationTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), $"merchant-migration-{Guid.NewGuid():N}.db");

    /// <summary>Reads a pragma back out of the file, without going through the ledger.</summary>
    private long Pragma(string name)
    {
        using SqliteConnection db = new(new SqliteConnectionStringBuilder
        {
            DataSource = _path,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
        }.ToString());

        db.Open();
        using SqliteCommand cmd = db.CreateCommand();
        cmd.CommandText = $"PRAGMA {name};";
        return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    /// <summary>Writes a database stamped at a version, with whatever schema is asked for.</summary>
    private void Existing(long version, string schema = "")
    {
        using SqliteConnection db = new(new SqliteConnectionStringBuilder
        {
            DataSource = _path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());

        db.Open();
        using SqliteCommand cmd = db.CreateCommand();
        cmd.CommandText = $"{schema} PRAGMA user_version = {version};";
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public void A_new_ledger_is_stamped_with_the_schema_it_was_built_from()
    {
        using (Ledger ledger = new(_path))
        {
            ledger.Subscribe(1, 2, "under-10", Cadence.Daily, null);
        }

        Assert.Equal(Ledger.SchemaVersion, Pragma("user_version"));
    }

    [Fact]
    public void Opening_an_up_to_date_ledger_twice_changes_nothing()
    {
        using (Ledger first = new(_path))
        {
            first.Subscribe(1, 2, "under-10", Cadence.Daily, null);
        }

        using Ledger second = new(_path);

        Assert.Single(second.ForGuild(1));
        Assert.Equal(Ledger.SchemaVersion, Pragma("user_version"));
    }

    /// <summary>
    /// A database from before the version stamp existed reads as version 0 while already holding
    /// its tables. It has to be adopted rather than rebuilt: somebody's channels are in it.
    /// </summary>
    [Fact]
    public void An_unstamped_ledger_keeps_its_rows_and_is_adopted()
    {
        Existing(version: 0, schema: """
            CREATE TABLE subscriptions (
                id              INTEGER PRIMARY KEY AUTOINCREMENT,
                guild_id        INTEGER NOT NULL,
                channel_id      INTEGER NOT NULL,
                category        TEXT    NOT NULL,
                cadence         INTEGER NOT NULL,
                mention_role_id INTEGER NULL,
                created_at      TEXT    NOT NULL,
                last_posted_at  TEXT    NULL,
                UNIQUE (channel_id, category)
            );

            INSERT INTO subscriptions
                (guild_id, channel_id, category, cadence, mention_role_id, created_at)
            VALUES (7, 70, 'free-games', 0, NULL, '2026-01-01T00:00:00.0000000Z');
            """);

        using Ledger ledger = new(_path);

        Subscription kept = Assert.Single(ledger.ForGuild(7));

        Assert.Equal("free-games", kept.CategoryKey);
        Assert.Equal(Ledger.SchemaVersion, Pragma("user_version"));

        // The tables migration 1 did not find are created around what was already there.
        Assert.Equal("US", ledger.Settings(7).Region);
    }

    /// <summary>
    /// The refusal that matters. An older merchant meeting a newer file would otherwise fail one
    /// query at a time, at whatever hour the sweep next ran, with an error naming a column.
    /// </summary>
    [Fact]
    public void A_ledger_from_a_newer_merchant_is_refused_by_name()
    {
        Existing(version: Ledger.SchemaVersion + 5);

        Ledger? opened = Ledger.Open(_path, out string? problem);

        Assert.Null(opened);
        Assert.NotNull(problem);
        Assert.Contains(_path, problem);
        Assert.Contains($"version {Ledger.SchemaVersion + 5}", problem);
        Assert.Contains($"version {Ledger.SchemaVersion}", problem);
    }

    [Fact]
    public void A_ledger_that_cannot_be_opened_is_reported_rather_than_thrown()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"merchant-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            // A directory where the file should be: openable by nothing, and the shape of every
            // other permission and path mistake.
            Ledger? opened = Ledger.Open(directory, out string? problem);

            Assert.Null(opened);
            Assert.NotNull(problem);
            Assert.Contains(directory, problem);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (string file in (string[])[_path, $"{_path}-wal", $"{_path}-shm"])
        {
            File.Delete(file);
        }

        GC.SuppressFinalize(this);
    }
}
