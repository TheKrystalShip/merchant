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

**A feed's name is taken exactly as the file writes it.** It is not trimmed on the way in, because
`"free-games "` and `"free-games"` would then be one feed with the second quietly replacing the
first — no error, a summary reading "Loaded 1 of 2", and which of them survives decided by the order
configuration hands its children back rather than by the file. The name pattern refuses the stray
space instead, and the rejection quotes the name so the space is visible. Lookups still trim, because
that side is what Discord sends rather than what the file says.

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

**Every number and date crossing merchant's edge is invariant, never the host's culture.** This is
the other half of keeping globalization on, and it cuts both ways. Reading: a feed writes English
month names on a Gregorian calendar whatever locale the machine has, and under `fa-IR` or `th-TH`
the current culture refuses those strings or reads them six centuries out, so the item silently
loses its place in every digest. Writing: CheapShark quotes USD whatever region a server picks, and
a price formatted in the host's convention is a different number — `$3,49` on a German host, in
Eastern Arabic digits on a Persian one, in an embed read by people who have never seen that host.

So a price goes through `CheapSharkSource.Priced`, a review count and a score name the invariant
culture explicitly, and the ledger's timestamps are written and read with it. `GlobalizationTests`
pins dates and prices across several locales, and CI runs the whole suite a second time under
`de_DE.UTF-8`. The analyzers help but do not cover this: CA1305 catches a bare `ToString()` and
does not see inside an interpolated string, which is exactly where the prices were.

**Everything a person types that reaches a URL is checked where it is typed.** A region is
substituted into a feed's address, so two arbitrary characters build a request for the wrong page or
for nothing, and the channel goes quiet with no error anywhere: the fetch failure is swallowed by
design, one feed down costing only itself. `GuildSettings.IsRegion` and `IsCurrency` are what stop
it, on `/merchant region` and on `--check`'s argument both — the same code refusing it in both
places, because those are the only two ways a code gets in.

**`{currency}` is filled in and nothing uses it.** `/merchant region` takes a currency, the ledger
stores it, and `Template` substitutes it, but neither driver prices in anything but USD — which is
what `UsdCaveat` exists to say out loud. It stays because it is what a source that quotes a server's
own money would need, and because it is genuinely part of the `Fetch` key: two servers whose URLs
differ only by currency are two requests. Do not treat a feed built on it as a supported thing until
a driver can honour it.

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

The line saying what was left out is part of that budget, not an afterthought to it. It is written
by `Announcer.Note`, measured before the first field goes in and set after the last one, because a
budget that fills the embed to exactly 6000 and *then* appends a footer has spent the whole margin
it was protecting — and the throw lands on the one command whose entire job is to still answer.

**One fetch per feed per storefront, per sweep.** Subscriptions multiply with servers and channels;
upstreams do not care why merchant is asking the same question three times, and Reddit answers 429
to far less than that. `Sweeper` caches a sweep's fetches by feed and region — two servers on
different regions genuinely are two requests, everything else is one.

**Sweeping and posting are separate.** Everything is fetched on one interval; each subscription
posts on its own clock, reading the backlog the sweep filed. This is the only reason a weekly
channel is possible — an RSS bot that posts on discovery can only ever be live. Do not collapse
these back together.

They are separate on the failing path too: a sweep that fetches nothing files nothing and still
posts. A daily channel whose window opens on the half hour its upstream happens to be down is owed
the backlog the *last* sweep filed, and holding it behind an outage it has nothing to do with is the
one thing this split exists to make impossible.

**The backlog comes back most recently found first, and within one sweep in the order the source
offered.** Two orderings, and both are load-bearing: `Pending` is what a digest lists and what a
capped live burst picks from, so what a channel is short of room for has to be the least worth
saying. It works because `Record` stamps `first_seen` once per sweep rather than once per row —
a per-row timestamp is unique, the `rowid` tiebreaker never runs, and the sweep's own order is lost.
`Sweeper` reverses the burst before posting, so the channel still reads forwards.

**Only what reached the channel is marked as sent.** A burst is several messages and one of them can
be refused while the ones before it are already up, so `FlushAsync` collects the ids it actually
delivered and stamps those in a `finally`. Marking the whole page after the loop posts the delivered
ones a second time on the next sweep; marking nothing on a failure does the same thing.

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

**A refused token stops merchant; every other gateway failure is waited out.** Discord.Net
reconnects on anything, which is right for a dropped connection, an outage or a DNS hiccup and
wrong for exactly one case: a 401 means the token is wrong or has been reset, and no amount of
waiting changes that. Left alone it produces the worst failure this bot has — running, reporting
itself up, posting nothing, and explaining why only inside a stack trace nobody is reading. So the
401 is named once at `LogCritical`, the application is stopped, and the exit code is non-zero, which
is what makes it a failed unit and a restarting container instead of a quiet one.
`GatewayFailure.IsUnauthorized` looks *through* an exception rather than at it, because the status
arrives wrapped — the gateway's connect failure carries the REST call's exception underneath it —
and `GatewayFailureTests` pins both directions, since being trigger-happy here would mean quitting
over an outage Discord recovers from in a minute.

**Shutdown logs out before the host is disposed.** `RunAsync` disposes the host the moment it
returns and the host owns the gateway client, so anything said to Discord after it is said to a
disposed object. `StartAsync` plus `WaitForShutdownAsync` leaves the client alive until the `using`
at the end of `Program.cs`, which is after the logout — and the logout is what makes the bot show
offline at once on a restart rather than lingering until the gateway times it out.

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
runs `build`, `test` and `format` plus a line-length check, `shellcheck`, `docker build`, and the
tests again under a comma-decimal locale. `--check` is the one it leaves out, because it is the only
one that reaches the network and a feed having a quiet afternoon is not a broken commit.

**The style and the lint rules are the build's job, not review's.** `.editorconfig` holds both —
explicit types over `var`, file-scoped namespaces, `_camelCase` instance fields, PascalCase for
anything static and readonly, constructors written out rather than primary — and
`Directory.Build.props` turns on `EnforceCodeStyleInBuild` and the .NET analyzers at
`latest-Recommended`, on top of `TreatWarningsAsErrors`. A violation is a failed build, on the
machine that made it.

Two of those rules an analyzer cannot report, and each says so where it is written. Roslyn has no
line-length rule and the formatter does not wrap, so `max_line_length` is checked by a step in CI
that reads the same 110 from nowhere but its own line — keep the two in step. And nothing flags a
primary constructor that is already written, so that one is honoured by hand.

Everything the analyzers report is fixed rather than muted, with three exceptions, each switched
off beside its reason in `.editorconfig`: CA1848 and CA1873 want `LoggerMessage` delegates for
logging that happens a few times per half-hour, and CA1720 objects to `ConfigRead.Int`/`Decimal`
being named after what they read, which is the point of them. CA1707 is off for `tests/` alone,
because a test is named as the sentence it asserts. When a new rule fires, fix it or turn it off
with the reason written down — never turn it off silently, and never lower `AnalysisLevel` to make
one go away.

The one place the formatter is overruled is the storefront table in `CheapSharkSource`, which is a
table and is written as one; `#pragma warning disable format` is what holds it.

**The test suite runs twice in CI, the second time under `LC_ALL=de_DE.UTF-8`.** With globalization
on, everything merchant formats or parses can pick up the host's culture, and the result is wrong
only on machines nobody here owns — a USD price posted as `$3,49`, a review count as `7.716`, an
English feed date the parser refuses. The analyzers catch some of it (CA1305) but not interpolated
strings, which is exactly where the prices were; the locale run is what covers the rest.
`GlobalizationTests` pins dates and prices across `de-DE`, `es-ES` and `fa-IR` directly.

`--check` reads and validates the same settings file the bot does, so it is also how an edit gets
checked. Point `MERCHANT_CONFIG` at a scratch file to try a catalog without touching the real one.
It is the only argument merchant takes, and anything else that reads like a command is named and
refused rather than passed to the host as configuration, ignored, and answered by a demand for a
token nobody was trying to use.

Reading that file fails in two ways and both are sentences. A file the process cannot open throws
`UnauthorizedAccessException`, which is not an `IOException` and has to be caught by name — a
root-owned file in a mounted volume is the ordinary way to meet it. A file that will not parse
throws from the configuration provider, whose own message names the file and nothing else: the line
and the column are on the exception underneath, and both are printed, because somebody is looking at
that file in an editor.

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

In that file the account and the `chown` of `/data` come **before** `VOLUME /data`, and the order is
the whole thing: a build step that touches a path already declared as a volume is discarded by the
classic builder, which leaves `/data` owned by root while merchant runs unprivileged and cannot open
its own ledger. BuildKit keeps the `chown`, so the wrong order builds and passes CI and fails only on
somebody else's engine — which is the machine this image exists for.

The publish names no runtime identifier. A framework-dependent publish runs on whatever architecture
the host is, and the friend running this may be on an arm64 box; the SDK floor that does matter is in
`global.json`, where an old SDK is refused in a sentence rather than a resolver error.

Upgrading is `git pull && deploy/install.sh && systemctl --user restart merchant`. The schema comes
up on its own, the settings file and the ledger are untouched, and there is no step to remember.

The ledger is the only state. Losing it makes merchant repost whatever each feed currently offers,
once — annoying, not destructive.
