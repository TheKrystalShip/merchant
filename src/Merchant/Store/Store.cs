using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Merchant.Store;

/// <summary>
/// Everything merchant remembers: which channels want which feeds, and every item it has already
/// seen for each of them.
///
/// The seen table doubles as the digest buffer. A swept item is written once with its rendered
/// payload and <c>posted = 0</c>; a live subscription flushes those rows on the next sweep and a
/// weekly one leaves them sitting for seven days. That is what lets merchant do digests at all —
/// an ordinary RSS bot posts on discovery and so can only ever be "live".
/// </summary>
public sealed class Store : IDisposable
{
    private readonly SqliteConnection _db;

    /// <summary>Opens (creating if needed) the database at this path and brings the schema up.</summary>
    public Store(string path)
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
        EnsureSchema();
    }

    private void EnsureSchema() => Execute("""
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
        """);

    // ---- guild settings -------------------------------------------------------------------

    /// <summary>This server's preferences, falling back to the defaults when it has set none.</summary>
    public GuildSettings Settings(ulong guildId)
    {
        using SqliteCommand cmd = Command(
            "SELECT region, currency FROM guilds WHERE guild_id = $g",
            ("$g", (long)guildId));

        using SqliteDataReader reader = cmd.ExecuteReader();
        return reader.Read()
            ? new GuildSettings(guildId, reader.GetString(0), reader.GetString(1))
            : GuildSettings.Default(guildId);
    }

    /// <summary>Stores this server's preferences, replacing whatever was there.</summary>
    public void SaveSettings(GuildSettings settings) => Execute(
        """
        INSERT INTO guilds (guild_id, region, currency) VALUES ($g, $r, $c)
        ON CONFLICT (guild_id) DO UPDATE SET region = $r, currency = $c
        """,
        ("$g", (long)settings.GuildId), ("$r", settings.Region), ("$c", settings.Currency));

    // ---- subscriptions --------------------------------------------------------------------

    /// <summary>
    /// Wires a category to a channel. Re-adding the same pair updates its cadence and mention role
    /// rather than duplicating it, so running the command twice is harmless.
    /// </summary>
    /// <returns>The subscription's id, and whether this call created it.</returns>
    public (long Id, bool Created) Subscribe(
        ulong guildId, ulong channelId, string category, Cadence cadence, ulong? mentionRoleId)
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

    /// <summary>Drops a subscription and everything merchant remembered for it.</summary>
    /// <returns>True when a row in this guild matched; false when the id is wrong or not theirs.</returns>
    public bool Unsubscribe(ulong guildId, long id) =>
        Execute("DELETE FROM subscriptions WHERE id = $i AND guild_id = $g",
            ("$i", id), ("$g", (long)guildId)) > 0;

    /// <summary>Every subscription in one server, oldest first.</summary>
    public IReadOnlyList<Subscription> ForGuild(ulong guildId) =>
        ReadSubscriptions("WHERE guild_id = $g ORDER BY id", ("$g", (long)guildId));

    /// <summary>Every subscription merchant holds, across all servers. The sweep's work list.</summary>
    public IReadOnlyList<Subscription> All() => ReadSubscriptions("ORDER BY id");

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
                LastPostedAt: reader.IsDBNull(6) ? null : DateTimeOffset.Parse(reader.GetString(6))));
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

    /// <summary>True when merchant has never swept this subscription.</summary>
    public bool IsUnswept(long subscriptionId) =>
        ScalarLong("SELECT COUNT(*) FROM seen WHERE subscription_id = $s", ("$s", subscriptionId)) == 0;

    /// <summary>
    /// What is waiting to go out for this subscription, newest first, capped.
    /// </summary>
    public IReadOnlyList<FeedItem> Pending(long subscriptionId, int limit)
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

    /// <summary>How many items are waiting for this subscription.</summary>
    public int PendingCount(long subscriptionId) => (int)(ScalarLong(
        "SELECT COUNT(*) FROM seen WHERE subscription_id = $s AND posted = 0",
        ("$s", subscriptionId)) ?? 0);

    /// <summary>
    /// Marks exactly the items that went out as sent, and stamps the last-posted time.
    ///
    /// Only these ids, never the whole backlog: a live burst deliberately posts a few of what is
    /// waiting, and clearing the rest would drop items nobody ever saw. What is left stays pending
    /// and goes out on the next sweep.
    /// </summary>
    public void MarkFlushed(long subscriptionId, IEnumerable<string> itemIds, DateTimeOffset when)
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

    /// <summary>
    /// Forgets seen items older than the retention window, except anything still unposted.
    /// Without this the ledger grows forever on a live feed.
    /// </summary>
    /// <returns>How many rows were dropped.</returns>
    public int Prune(TimeSpan retention) => Execute(
        "DELETE FROM seen WHERE posted = 1 AND first_seen < $t",
        ("$t", Iso(DateTimeOffset.UtcNow - retention)));

    // ---- plumbing -------------------------------------------------------------------------

    /// <summary>A snowflake boxed for SQLite, where "no role" has to travel as a null.</summary>
    private static object? NullableId(ulong? id) => id is { } value ? (long)value : null;

    private static string Iso(DateTimeOffset when) => when.ToUniversalTime().ToString("O");

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
    public void Dispose() => _db.Dispose();
}
