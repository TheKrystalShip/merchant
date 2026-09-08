# CLAUDE.md

Guidance for Claude Code working in this repository.

## What this is

merchant announces game deals in Discord channels. Somebody invites it, runs one command per channel,
and never touches it again. It is not a KGSM component and shares nothing with one: it lives in the
`tks` workspace beside `magpie` and `moviebot`, runs as the owner's user under `systemd --user` or
as a container, and depends on nothing of TheKrystalShip's — Discord.Net and Microsoft.Data.Sqlite
are the whole package surface, and `nuget.config` names only nuget.org so the repo builds on a
machine that has never held a GitHub Packages token.

It was written for somebody else's Discord server, which is the constraint that shapes everything:
**the person setting it up is not the person who wrote it, and will not read this file.** Every
design decision below follows from that.

## The shape

```
appsettings.json ── the catalog: every feed, its source, colour and cadence   ~/.config/merchant/
    │               seeded from deploy/appsettings.example.jsonc on a first run
FeedCatalog ─ loads and validates it once at startup                        Feeds/FeedCatalog.cs
    │         a bad entry is dropped and named; an empty catalog is fatal
SourceRegistry ─ ISourceFactory[] keyed by "type" → validated blueprints          Feeds/
    │            RssSourceFactory, CheapSharkSourceFactory                Feeds/Factories/
Sources ── ISource[]: RssSource (RSS 1.0 / RSS 2.0 / Atom), CheapSharkSource     Sources/
    │      flatten everything to FeedItem; a dead feed returns empty, never throws
    │      config-blind: a source fetches, and does not know where its URL came from
    │
Ledger ─── SQLite. subscriptions · guilds · seen                        Storage/Ledger.cs
    │      `seen` doubles as the digest buffer: posted = 0 means "waiting"
    │      versioned: PRAGMA user_version, one migration per schema
    │
Sweeper ── BackgroundService. Fetch everything on one interval,          Discord/Sweeper.cs
    │      post per subscription on its own cadence
    │
Announcer ─ FeedItem → Embed. One card when live, one list when a digest  Discord/Announcer.cs
    │
MerchantModule ─ /merchant add · list · remove · preview · region · help      Discord/MerchantModule.cs
    │            the feed parameter is autocompleted from the catalog   Discord/FeedAutocomplete.cs
```

## Decisions worth keeping

**The catalog is the product.** A person picks "Games Under $10", not a URL, a filter and a poll
interval. If a change would make somebody paste a feed URL into a *command*, it is the wrong change —
that is the thing merchant exists to avoid. Around five entries is the right size: a curation
guideline for whoever edits the settings file, not something the code enforces.

**The catalog is data, and drivers are code.** Nothing in this assembly knows what a feed is: URLs,
filters, labels, colours and cadences all live in `appsettings.json`, and adding one is an edit and a
restart. What stays in C# is a *driver* — a class that knows a wire format. `ISourceFactory` is the
seam: it owns both what its options mean and what a bad value looks like, so a new driver is one
class in `Feeds/Factories` and one line in `Program.cs`, with no switch anywhere to keep in step.
Resist a generic JSON-mapping driver until something actually needs it; that trades a rebuild for a
mini-language living in a settings file.

**The feed menu is autocompleted, not registered.** Discord is told a command's fixed choices once,
when it is registered, so an `enum` of feeds would pin the catalog inside the assembly.
`FeedAutocomplete` is asked on every keystroke including the empty one, so the list appears the
moment the field is focused, and editing the settings file never requires re-registering a command.
The cost is that free text is submittable: `add` and `preview` answer an unknown feed by naming what
does exist.

**The settings vocabulary lives in `Feeds/Schema.cs`.** Every key the file may contain, every bound
it is held to, and every default it falls back to is a constant there, and the reader, the validation
and the error messages all name their fields from it. No key is written as a literal anywhere else,
so the file format cannot be described in one place and read in another. A driver's own options are
the exception it owns: a factory declares its keys and binds them into a typed record
(`RssOptions`, `CheapSharkOptions`), and anything with a closed set of values is an enum —
`CheapSharkSort` rather than a string, so the sort keys are visible in code and a misspelling is
caught at startup instead of silently reordering a feed.

**Config children come back sorted by key, not in document order.** `ConfigurationProvider.GetChildKeys`
sorts with `ConfigurationKeyComparer`, so the file's own order cannot be relied on. The menus order
by label instead, which is decided in one place — `FeedCatalog`'s constructor.

**One malformed feed is dropped, not fatal.** The person editing that file is usually not the person
who wrote merchant. A resident bot dying at 3am over one typo is a worse failure than four working
feeds and a loud line at startup, so every rejection names its feed and what was expected. A catalog
that ends up *empty* is fatal: that bot is broken either way and should say so.

**The token is not in the settings file and not in the container.** It is read straight from
`MERCHANT_TOKEN` into a local in `Program.cs` and handed to `LoginAsync`. Keeping it off `BotOptions`
keeps the one secret merchant holds out of a DI singleton every command can reach, and out of a file
that gets copied around. A `bot.token` in the file is reported and ignored, never used.

**The schema is a list of migrations, and the file says which it has had.** `Migrations` in
`Ledger` is an ordered array; `PRAGMA user_version` records how far a database has been brought;
opening one applies whatever is missing, one migration and one stamp per transaction. Changing the
schema is appending an entry — never editing one that has shipped, because every database already
carries its effects and only later entries will run. The first entry is written with IF NOT EXISTS
so a database predating the stamp is adopted rather than rebuilt. A file from a *newer* merchant is
refused at startup by name: the alternative is an older build meeting the change one query at a
time, hours later, mid-sweep, with an error that names a column and explains nothing.
`LedgerMigrationTests` covers adoption, the refusal, and that neither loses a row.

**The ledger's default path is absolute, and it is the only default that is computed.** A relative
default resolves against the working directory, so the same install reads a different database
depending on where it was started — one under the unit, one in the checkout under `dotnet run` — and
presents as a bot that has forgotten every subscription. `MerchantConfig.ResolveDatabasePath` puts it
in the XDG state directory, next door to how the settings file is already found. The unit and the
container still pass `MERCHANT_DB`, because each has to name the path it grants write access to.

**Every level of the settings file is held to the schema, including its top.** `BotOptions.Load`
runs `ConfigRead.Unknown` over the root before it reads anything, so a catalog under `"feed"` is
named rather than producing a bot that starts, reports itself healthy and announces nothing. The
`"logging"` section is recognised there without merchant reading it: the host does, which is what
makes "turn the log up" an edit to the same file as everything else. `SettingsFileTests` pins that
the level actually reaches the logger, because that instruction is the first thing anybody debugging
a quiet channel is given.

**The ledger is one connection, and every entry point takes the same gate.** Two callers reach
`Ledger` without taking turns: the sweep on its background loop, and every slash command on the
gateway's threads. SQLite scopes a transaction to the connection rather than to the caller, so an
ungated write from a command lands inside whatever transaction `Record` has open and is rolled back
with it — `/merchant add` answers with a subscription number for a row that no longer exists.
`LedgerConcurrencyTests` reproduces exactly that. The operations are single-digit milliseconds; the
contention costs nothing.

**Dates are read against the invariant culture, never the host's.** This is the other half of
keeping globalization on. A feed writes English month names on a Gregorian calendar whatever locale
the machine has; under `fa-IR` or `th-TH` the current culture refuses those strings or reads them
six centuries out, and the item silently loses its place in every digest. `GlobalizationTests`
pins it for both, along with the ledger's own timestamps.

**A source hands out absolute http(s) links and nothing else.** Discord validates a URL while the
embed is being *built*, so a junk link throws inside the sweep: the item is never marked sent, the
next sweep picks it up again, and that channel is stuck for good. Feeds do produce these — relative
paths, `javascript:` hrefs, truncated `src` attributes — so `Links.Http` drops them where the cost
is one item. Anything reaching `Announcer` is postable.

**Embeds are packed to Discord's budget, because the size of the catalog is a stranger's decision.**
An embed refuses to build past 6000 characters total, which is around thirteen feeds at the length a
feed is allowed. `Announcer.Catalog` fits what it can and says what it left out, so a long catalog
shortens `/merchant help` instead of taking it offline; `/merchant list` and every refusal are
truncated for the same reason.

**One fetch per feed per storefront, per sweep.** Subscriptions multiply with servers and channels;
upstreams do not care why merchant is asking the same question three times, and Reddit answers 429
to far less than that. `Sweeper` caches a sweep's fetches by feed and region — two servers on
different regions genuinely are two requests, everything else is one.

**Sweeping and posting are separate.** Everything is fetched on one interval; each subscription
posts on its own clock, reading the backlog the sweep filed. This is the only reason a weekly
channel is possible — an RSS bot that posts on discovery can only ever be live. Do not collapse
these back together.

**Cadence windows are shaved below their nominal period** (23h, 6.9d). A sweep landing a few
minutes late must not walk the daily post around the clock, and a weekly post must not drift past
Steam's Tuesday chart and become fortnightly. `CadenceTests` pins both.

**CheapShark, not IsThereAnyDeal, for the priced feeds.** ITAD encodes its filters into an opaque
token minted by its own web UI, so "under ten" cannot be expressed in a URL merchant builds. Verified:
a hand-constructed ITAD filter URL returns 400. CheapShark takes the ceiling as a query parameter
and returns structured prices, which is also why those embeds can strike through a list price.
ITAD is still right for giveaways, which need no filter and vary by region.

**CheapShark requires a descriptive `User-Agent`** or it refuses the request outright, and Reddit
throttles a generic one harder. That is why every fetch goes through the one named `HttpClient`.

**`InvariantGlobalization` must stay false.** Discord.Net constructs a `CultureInfo` from every
guild's `preferred_locale` while handling GUILD_CREATE. With the flag on, the bot connects,
registers commands and reports itself healthy — then throws `CultureNotFoundException` on the
first guild, which never enters the client's cache, and every command afterwards fails on a null
`Context.Guild`. It presents as "Unknown Guild" in the log and a `NullReferenceException` in the
command, which points nowhere near the cause. `GlobalizationTests` is the guard; it fails under
`-p:InvariantGlobalization=true`. The rest of the workspace sets this true — magpie is AOT and
touches no locales — so it is an easy flag to inherit by accident.

**Commands read the guild id from the interaction payload, not the gateway cache.** `Context.Guild`
is null whenever a guild is missing from the cache; `Context.Interaction.GuildId` is always
present. A permission check that cannot run returns null rather than an empty list, so "could not
check" is never mistaken for "nothing is missing".

**No privileged intents.** `GatewayIntents.Guilds` only. merchant reads no messages and no member
lists, so the application never needs intent review. Do not add an intent for a convenience.

**First sweep posts a handful and files the rest as seen.** A channel that stays empty for a day
after setup reads as a broken bot; a channel that receives a month of backlog reads as a broken
bot. `LedgerTests` covers both edges.

**`/merchant add` checks channel permissions before it writes anything** and names what is missing. A
silent permission failure is the most common way this class of bot appears broken, and the person
hitting it has no access to the logs.

**Every command reply is ephemeral.** Setting up announcements should not itself post in the server.

## Working on it

```bash
dotnet build
dotnet test                                       # no network: the feeds in them are captured files
dotnet format                                     # the style in .editorconfig, applied
dotnet run --project src/Merchant -- --check      # validate the settings file, fetch every feed
dotnet run --project src/Merchant -- --check ES   # …for another region
```

Nothing else is needed and nothing is bespoke: the repository is an ordinary .NET solution, and CI
runs those same commands plus `docker build`. The style is `.editorconfig` rather than convention —
explicit types over `var`, file-scoped namespaces, `_camelCase` instance fields and PascalCase for
anything static and readonly — and `dotnet format --verify-no-changes` is what enforces it. The one
place the formatter is overruled is the storefront table in `CheapSharkSource`, which is a table and
is written as one.

`--check` reads and validates the same settings file the bot does, so it is also how an edit gets
checked. Point `MERCHANT_CONFIG` at a scratch file to try a catalog without touching the real one.

It is the first thing to reach for when a channel goes quiet: it separates "the feed changed" from
"Discord is unhappy" without a token and without touching a server.

Set `MERCHANT_DEV_GUILD` while developing. Guild commands register instantly; global ones take up to
an hour, which makes iterating on a command signature unbearable otherwise.

## Tests

Parser tests run against **captured documents**, not hand-written XML, under
`tests/Merchant.Tests/Fixtures`: Steam's RSS 1.0 (items outside `<channel>`, no `guid`), ITAD's
RSS 2.0 (CDATA and HTML entities), Reddit's Atom (link in an `href`), and a CheapShark response.
Hand-written fixtures agree with the parser by construction and prove nothing. Refresh them from
the live feeds when a source changes shape.

`FeedCatalogTests` holds the settings file to account rather than the code. Its theory data is the
catalog merchant ships — the five feeds, their labels, channels, cadences, colours and source types —
so the shipped example cannot drift from what the bot is meant to post. The rest of it asserts that
every rejection names its feed and what was expected, because that message is the entire interface
for somebody with a text editor and no access to this repository.

`LedgerMigrationTests` is the only suite that starts from a database that already exists, which is
the only interesting case: every other one opens an empty file, and an empty file cannot be migrated
wrong. It builds the old database by hand rather than through `Ledger`, because a fixture written
through the code under test agrees with it by construction — the same reason the parser fixtures are
captured rather than hand-written.

## Adding a feed

Edit `appsettings.json` and restart. No code, no rebuild, no command re-registration.

The schema is written down in three places, each for a different reader: `Feeds/Schema.cs` for the
code, the comment header of `deploy/appsettings.example.jsonc` for whoever has the file open, and the
README's *Editing the feeds* section for whoever has not opened it yet. A new field, or a new value
for an existing one, belongs in all three.

Adding a new *kind* of source is a class in `Feeds/Factories` implementing `ISourceFactory` and a
`AddSingleton<ISourceFactory, …>()` in `Program.cs`. It returns an `ISourceBlueprint`, which is a
source definition that has already been validated — everything that can be wrong about a feed has
been said out loud at startup, so nothing can fail for the first time during a sweep.

## Changing the ledger's schema

Append one entry to `Migrations` in `Storage/Ledger.cs`. That is the whole procedure: the array's
length *is* the schema version, and a database that has had fewer runs the rest on the next start.

```csharp
private static readonly string[] Migrations =
[
    """ … the first schema … """,

    // 2 — a channel can be muted without being removed.
    """
    ALTER TABLE subscriptions ADD COLUMN muted INTEGER NOT NULL DEFAULT 0;
    """,
];
```

Three rules, all of them about databases that are not yours:

- **Never edit or renumber an entry that has shipped.** Every database already carries its effects
  and will never run it again, so an edit only changes what a *fresh* install gets — which is how
  two installs of the same version end up with different schemas.
- **Every column added to an existing table needs a default**, or the migration fails on the rows
  that are already there.
- **Write it so it survives running against a database that has partly seen it.** The transaction
  makes a whole migration atomic, so this mostly means not depending on state a previous migration
  left in flight.

Then add a case to `LedgerMigrationTests`: build the old shape by hand, put a row in it, open the
`Ledger`, and assert the row is still there and reads correctly. Data surviving is the assertion
that matters — a migration that runs cleanly and empties a table is the failure worth catching.

Nothing needs to be run by hand on a deploy, and no operator step exists to forget. The one thing
merchant will not do is go backwards: a file stamped past `SchemaVersion` is refused by name at
startup.

## Deploying

`deploy/install.sh` publishes to `~/.local/share/merchant`, links it into `~/.local/bin` and installs
the user unit; the token lives in `~/.config/merchant/merchant.env` at mode 0600 and never in git, and
the catalog in `~/.config/merchant/appsettings.json` beside it. install.sh seeds both and overwrites
neither, which matters because it republishes over the whole install directory — anything editable has
to live outside it. The unit runs with `ProtectHome=read-only`, so the service can read that file but
not seed it; the container can, and points `MERCHANT_CONFIG` at its volume. The `Dockerfile` is the
portable half — same code, token passed at run time, ledger on a volume at `/data`.

The publish names no runtime identifier. A framework-dependent publish runs on whatever architecture
the host is, and the friend running this may be on an arm64 box; the SDK floor that does matter is in
`global.json`, where an old SDK is refused in a sentence rather than a resolver error.

Upgrading is `git pull && deploy/install.sh && systemctl --user restart merchant`. The schema comes
up on its own, the settings file and the ledger are untouched, and there is no step to remember.

The ledger is the only state. Losing it makes merchant repost whatever each feed currently offers,
once — annoying, not destructive.
