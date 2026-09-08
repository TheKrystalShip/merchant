using System.Globalization;
using System.Text.Json;
using Merchant.Feeds;
using Microsoft.Data.Sqlite;

namespace Merchant.Storage;

/// <summary>
/// Everything merchant remembers: which channels want which feeds, and every item it has already
/// seen for each of them.
///
/// The seen table doubles as the digest buffer. A swept item is written once with its rendered
/// payload and <c>posted = 0</c>; a live subscription flushes those rows on the next sweep and a
/// weekly one leaves them sitting for seven days. That is what lets merchant do digests at all —
/// an ordinary RSS bot posts on discovery and so can only ever be "live".
///
/// One connection, serialised. Two callers reach this object without taking turns — the sweep on
/// its background loop, and every slash command on the gateway's threads — and SQLite scopes a
/// transaction to the connection, not to the caller. An unsynchronised write from a command lands
/// inside whatever transaction the sweep has open and is rolled back with it, which is a
/// subscription that reports itself created and then does not exist. Every entry point below takes
/// the same gate; the operations are single-digit milliseconds, so the contention costs nothing.
/// </summary>
public sealed class Ledger : IDisposable
{
    private readonly SqliteConnection _db;
    private readonly Lock _gate = new();

    /// <summary>Opens (creating if needed) the database at this path and brings the schema up.</summary>
    public Ledger(string path)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _db = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());

        _db.Open();
        Execute("PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000; PRAGMA foreign_keys=ON;");
        Migrate();
    }

    /// <summary>
    /// Opens the ledger, answering in a sentence rather than an exception when it cannot. A person
    /// whose bot will not start needs to read the reason, not a stack trace.
    /// </summary>
    /// <param name="path">Where the database is, or is to be created.</param>
    /// <param name="problem">What stopped it, when the answer is null.</param>
    /// <returns>The open ledger, or null.</returns>
    public static Ledger? Open(string path, out string? problem)
    {
        try
        {
            problem = null;
            return new Ledger(path);
        }
        catch (Exception ex)
            when (ex is InvalidOperationException or SqliteException
                     or IOException or UnauthorizedAccessException)
        {
            problem = $"Could not open the ledger at {path}: {ex.Message}";
            return null;
        }
    }

    /// <summary>
    /// The schema, one entry per version, each applied in order to a database that has not had it.
    ///
    /// How far a database has been brought is stamped in its own header, as <c>PRAGMA
    /// user_version</c>, so changing the schema is: append one entry here and ship. Never edit an
    /// entry that has shipped — every database already carries its effects and only later entries
    /// will run — and never renumber one.
    ///
    /// The first entry is written with IF NOT EXISTS because it is also what a database predating
    /// the stamp meets: it finds its tables already there, is stamped, and carries on.
    /// </summary>
    private static readonly string[] Migrations =
    [
        // 1 — subscriptions, the per-server settings they are read with, and the seen ledger that
        // doubles as the digest buffer.
        """
        CREATE TABLE IF NOT EXISTS guilds (
            guild_id  INTEGER PRIMARY KEY,
            region    TEXT NOT NULL,
            currency  TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS subscriptions (
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

        CREATE TABLE IF NOT EXISTS seen (
            subscription_id INTEGER NOT NULL REFERENCES subscriptions(id) ON DELETE CASCADE,
            item_id         TEXT    NOT NULL,
            first_seen      TEXT    NOT NULL,
            posted          INTEGER NOT NULL DEFAULT 0,
            payload         TEXT    NOT NULL,
            PRIMARY KEY (subscription_id, item_id)
        );

        CREATE INDEX IF NOT EXISTS seen_pending ON seen (subscription_id, posted);
        """,
    ];

    /// <summary>The schema version this build of merchant writes and expects.</summary>
    public static int SchemaVersion => Migrations.Length;

    /// <summary>
    /// Brings the database up to <see cref="SchemaVersion"/>, one migration and one stamp per
    /// transaction, so an interrupted upgrade leaves the file at a version that was fully applied.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The file was written by a newer merchant. Running an older build against it would meet
    /// columns it does not know, one query at a time, hours after starting; saying so here is the
    /// only point at which that is still one legible sentence.
    /// </exception>
    private void Migrate()
    {
        long version = ScalarLong("PRAGMA user_version") ?? 0;

        if (version > Migrations.Length)
        {
            throw new InvalidOperationException(
                $"its schema is version {version}, and this merchant knows version " +
                $"{Migrations.Length}. A newer merchant wrote it: upgrade this one, or point " +
                $"{Schema.BotKeys.DatabasePath} at a different file.");
        }

        for (int applied = (int)version; applied < Migrations.Length; applied++)
        {
            using SqliteTransaction tx = _db.BeginTransaction();

            using (SqliteCommand step = Command(Migrations[applied]))
            {
                step.Transaction = tx;
                step.ExecuteNonQuery();
            }

            // A pragma takes no parameters. The value is this loop's own counter, never input.
            using (SqliteCommand stamp = Command($"PRAGMA user_version = {applied + 1};"))
            {
                stamp.Transaction = tx;
                stamp.ExecuteNonQuery();
            }

            tx.Commit();
        }
    }

    // ---- guild settings -------------------------------------------------------------------

    /// <summary>This server's preferences, falling back to the defaults when it has set none.</summary>
    public GuildSettings Settings(ulong guildId)
    {
        lock (_gate)
        {
            using SqliteCommand cmd = Command(
                "SELECT region, currency FROM guilds WHERE guild_id = $g",
                ("$g", (long)guildId));

            using SqliteDataReader reader = cmd.ExecuteReader();
            return reader.Read()
                ? new GuildSettings(guildId, reader.GetString(0), reader.GetString(1))
                : GuildSettings.Default(guildId);
        }
    }

    /// <summary>Stores this server's preferences, replacing whatever was there.</summary>
    public void SaveSettings(GuildSettings settings)
    {
        lock (_gate)
        {
            Execute(
                """
                INSERT INTO guilds (guild_id, region, currency) VALUES ($g, $r, $c)
                ON CONFLICT (guild_id) DO UPDATE SET region = $r, currency = $c
                """,
                ("$g", (long)settings.GuildId), ("$r", settings.Region), ("$c", settings.Currency));
        }
    }

    // ---- subscriptions --------------------------------------------------------------------

    /// <summary>
    /// Wires a category to a channel. Re-adding the same pair updates its cadence and mention role
    /// rather than duplicating it, so running the command twice is harmless.
    /// </summary>
    /// <returns>The subscription's id, and whether this call created it.</returns>
    public (long Id, bool Created) Subscribe(
        ulong guildId, ulong channelId, string category, Cadence cadence, ulong? mentionRoleId)
    {
        lock (_gate)
        {
            long? existing = ScalarLong(
                "SELECT id FROM subscriptions WHERE channel_id = $c AND category = $k",
                ("$c", (long)channelId), ("$k", category));

            if (existing is { } id)
            {
                Execute(
                    "UPDATE subscriptions SET cadence = $d, mention_role_id = $m WHERE id = $i",
                    ("$d", (int)cadence), ("$m", NullableId(mentionRoleId)), ("$i", id));
                return (id, false);
            }

            Execute(
                """
                INSERT INTO subscriptions
                    (guild_id, channel_id, category, cadence, mention_role_id, created_at)
                VALUES ($g, $c, $k, $d, $m, $t)
                """,
                ("$g", (long)guildId), ("$c", (long)channelId), ("$k", category),
                ("$d", (int)cadence), ("$m", NullableId(mentionRoleId)),
                ("$t", Iso(DateTimeOffset.UtcNow)));

            return (ScalarLong("SELECT last_insert_rowid()") ?? 0, true);
        }
    }

    /// <summary>Drops a subscription and everything merchant remembered for it.</summary>
    /// <returns>True when a row in this guild matched; false when the id is wrong or not theirs.</returns>
    public bool Unsubscribe(ulong guildId, long id)
    {
        lock (_gate)
        {
            return Execute("DELETE FROM subscriptions WHERE id = $i AND guild_id = $g",
                ("$i", id), ("$g", (long)guildId)) > 0;
        }
    }

    /// <summary>Every subscription in one server, oldest first.</summary>
    public IReadOnlyList<Subscription> ForGuild(ulong guildId)
    {
        lock (_gate)
        {
            return ReadSubscriptions("WHERE guild_id = $g ORDER BY id", ("$g", (long)guildId));
        }
    }

    /// <summary>Every subscription merchant holds, across all servers. The sweep's work list.</summary>
    public IReadOnlyList<Subscription> All()
    {
        lock (_gate)
        {
            return ReadSubscriptions("ORDER BY id");
        }
    }

    private IReadOnlyList<Subscription> ReadSubscriptions(
        string tail, params (string Name, object? Value)[] parameters)
    {
        using SqliteCommand cmd = Command(
            $"""
             SELECT id, guild_id, channel_id, category, cadence, mention_role_id, last_posted_at
             FROM subscriptions {tail}
             """,
            parameters);

        using SqliteDataReader reader = cmd.ExecuteReader();
        List<Subscription> rows = [];

        while (reader.Read())
        {
            rows.Add(new Subscription(
                Id: reader.GetInt64(0),
                GuildId: (ulong)reader.GetInt64(1),
                ChannelId: (ulong)reader.GetInt64(2),
                CategoryKey: reader.GetString(3),
                Cadence: (Cadence)reader.GetInt32(4),
                MentionRoleId: reader.IsDBNull(5) ? null : (ulong)reader.GetInt64(5),
                LastPostedAt: reader.IsDBNull(6)
                    ? null
                    : DateTimeOffset.Parse(
                        reader.GetString(6), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)));
        }

        return rows;
    }

    // ---- the ledger -----------------------------------------------------------------------

    /// <summary>
    /// Records what a sweep found. Items already known are ignored, so a feed that repeats itself
    /// every twenty minutes produces no work.
    /// </summary>
    /// <param name="alreadyPosted">
    /// True on a subscription's very first sweep: the backlog is filed as seen so that wiring up a
    /// channel does not dump a month of history into it.
    /// </param>
    /// <returns>How many of these items merchant had not seen before.</returns>
    public int Record(long subscriptionId, IEnumerable<FeedItem> items, bool alreadyPosted)
    {
        lock (_gate)
        {
            using SqliteTransaction tx = _db.BeginTransaction();
            int added = 0;

            foreach (FeedItem item in items)
            {
                using SqliteCommand cmd = Command(
                    """
                    INSERT INTO seen (subscription_id, item_id, first_seen, posted, payload)
                    VALUES ($s, $i, $t, $p, $j)
                    ON CONFLICT (subscription_id, item_id) DO NOTHING
                    """,
                    ("$s", subscriptionId), ("$i", item.Id), ("$t", Iso(DateTimeOffset.UtcNow)),
                    ("$p", alreadyPosted ? 1 : 0), ("$j", JsonSerializer.Serialize(item)));

                cmd.Transaction = tx;
                added += cmd.ExecuteNonQuery();
            }

            tx.Commit();
            return added;
        }
    }

    /// <summary>True when merchant has never swept this subscription.</summary>
    public bool IsUnswept(long subscriptionId)
    {
        lock (_gate)
        {
            return ScalarLong(
                "SELECT COUNT(*) FROM seen WHERE subscription_id = $s", ("$s", subscriptionId)) == 0;
        }
    }

    /// <summary>
    /// What is waiting to go out for this subscription, newest first, capped.
    /// </summary>
    public IReadOnlyList<FeedItem> Pending(long subscriptionId, int limit)
    {
        lock (_gate)
        {
            using SqliteCommand cmd = Command(
                """
                SELECT payload FROM seen
                WHERE subscription_id = $s AND posted = 0
                ORDER BY first_seen DESC, rowid DESC
                LIMIT $n
                """,
                ("$s", subscriptionId), ("$n", limit));

            using SqliteDataReader reader = cmd.ExecuteReader();
            List<FeedItem> items = [];

            while (reader.Read())
            {
                FeedItem? item = JsonSerializer.Deserialize<FeedItem>(reader.GetString(0));
                if (item is not null)
                {
                    items.Add(item);
                }
            }

            return items;
        }
    }

    /// <summary>How many items are waiting for this subscription.</summary>
    public int PendingCount(long subscriptionId)
    {
        lock (_gate)
        {
            return (int)(ScalarLong(
                "SELECT COUNT(*) FROM seen WHERE subscription_id = $s AND posted = 0",
                ("$s", subscriptionId)) ?? 0);
        }
    }

    /// <summary>
    /// Marks exactly the items that went out as sent, and stamps the last-posted time.
    ///
    /// Only these ids, never the whole backlog: a live burst deliberately posts a few of what is
    /// waiting, and clearing the rest would drop items nobody ever saw. What is left stays pending
    /// and goes out on the next sweep.
    /// </summary>
    public void MarkFlushed(long subscriptionId, IEnumerable<string> itemIds, DateTimeOffset when)
    {
        lock (_gate)
        {
            using SqliteTransaction tx = _db.BeginTransaction();

            foreach (string itemId in itemIds)
            {
                using SqliteCommand cmd = Command(
                    "UPDATE seen SET posted = 1 WHERE subscription_id = $s AND item_id = $i",
                    ("$s", subscriptionId), ("$i", itemId));

                cmd.Transaction = tx;
                cmd.ExecuteNonQuery();
            }

            using (SqliteCommand stamp = Command(
                       "UPDATE subscriptions SET last_posted_at = $t WHERE id = $i",
                       ("$t", Iso(when)), ("$i", subscriptionId)))
            {
                stamp.Transaction = tx;
                stamp.ExecuteNonQuery();
            }

            tx.Commit();
        }
    }

    /// <summary>
    /// Forgets seen items older than the retention window, except anything still unposted.
    /// Without this the ledger grows forever on a live feed.
    /// </summary>
    /// <returns>How many rows were dropped.</returns>
    public int Prune(TimeSpan retention)
    {
        lock (_gate)
        {
            return Execute(
                "DELETE FROM seen WHERE posted = 1 AND first_seen < $t",
                ("$t", Iso(DateTimeOffset.UtcNow - retention)));
        }
    }

    // ---- plumbing -------------------------------------------------------------------------

    /// <summary>A snowflake boxed for SQLite, where "no role" has to travel as a null.</summary>
    private static object? NullableId(ulong? id) => id is { } value ? (long)value : null;

    /// <summary>
    /// How a time is written and read back. Both ends name the invariant culture: merchant runs
    /// with globalization on, and a host culture whose default calendar is not Gregorian reads its
    /// own dates back as different ones.
    /// </summary>
    private static string Iso(DateTimeOffset when) =>
        when.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private SqliteCommand Command(string sql, params (string Name, object? Value)[] parameters)
    {
        SqliteCommand cmd = _db.CreateCommand();
        cmd.CommandText = sql;

        foreach ((string name, object? value) in parameters)
        {
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return cmd;
    }

    private int Execute(string sql, params (string Name, object? Value)[] parameters)
    {
        using SqliteCommand cmd = Command(sql, parameters);
        return cmd.ExecuteNonQuery();
    }

    private long? ScalarLong(string sql, params (string Name, object? Value)[] parameters)
    {
        using SqliteCommand cmd = Command(sql, parameters);
        object? value = cmd.ExecuteScalar();
        return value is null or DBNull ? null : Convert.ToInt64(value);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            _db.Dispose();
        }
    }
}
