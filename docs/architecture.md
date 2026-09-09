# Architecture

What the pieces are, and which parts of the arrangement are load-bearing. This is the document to
read before changing the sweep, the ledger or the catalog; `CLAUDE.md` at the repository root
carries the same reasoning in the form an agent working in the tree reads first.

Discord.Net and Microsoft.Data.Sqlite are the whole package surface, and `nuget.config` names only
nuget.org, so the repository builds on any machine with the SDK on it.

## The constraint everything follows from

**Whoever sets merchant up does not read the source.** The settings file, the startup log and the
command replies are the entire interface, and each has to explain itself to somebody who has never
seen this repository. Every decision below is downstream of that.

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

## The catalog

**The catalog is the product.** A person picks "Games Under $10", not a URL, a filter and a poll
interval. If a change would make somebody paste a feed URL into a *command*, it is the wrong change
— that is the thing merchant exists to avoid.

**The catalog is data, and drivers are code.** Nothing in this assembly knows what a feed is: URLs,
filters, labels, colours and cadences all live in `appsettings.json`. What stays in C# is a
*driver* — a class that knows a wire format — and `ISourceFactory` is the seam.

**The feed menu is autocompleted, not registered.** Discord is told a command's fixed choices once,
when it is registered, so an `enum` of feeds would pin the catalog inside the assembly.
`FeedAutocomplete` is asked on every keystroke including the empty one, so the list appears the
moment the field is focused, and editing the settings file never requires re-registering a command.
The cost is that free text is submittable: `add` and `preview` answer an unknown feed by naming what
does exist.

**The settings vocabulary lives in `Feeds/Schema.cs`.** Every key the file may contain, every bound
it is held to, and every default it falls back to is a constant there, and the reader, the
validation and the error messages all name their fields from it. No key is written as a literal
anywhere else, so the format cannot be described in one place and read in another. A driver's own
options are the exception it owns.

**One malformed feed is dropped, not fatal.** A resident bot dying at 3am over one typo is a worse
failure than four working feeds and a loud line at startup, so every rejection names its feed and
what was expected. A catalog that ends up *empty* is fatal: that bot is broken either way and should
say so.

**Every level of the settings file is held to the schema, including its top.** `BotOptions.Load`
runs `ConfigRead.Unknown` over the root before it reads anything, so a catalog under `"feed"` is
named rather than producing a bot that starts, reports itself healthy and announces nothing. The
`"logging"` section is recognised there without merchant reading it — the host does, which is what
makes "turn the log up" an edit to the same file as everything else.

**Config children come back sorted by key, not in document order.**
`ConfigurationProvider.GetChildKeys` sorts with `ConfigurationKeyComparer`, so the file's own order
cannot be relied on. The menus order by label instead, decided in one place: `FeedCatalog`'s
constructor.

**A feed's name is taken exactly as the file writes it,** not trimmed, because `"free-games "` and
`"free-games"` would then be one feed with the second quietly replacing the first — no error, and
which survives decided by the order configuration hands its children back. Lookups still trim,
because that side is what Discord sends rather than what the file says.

## Sweeping and posting

**Sweeping and posting are separate.** Everything is fetched on one interval; each subscription
posts on its own clock, reading the backlog the sweep filed. This is the only reason a weekly
channel is possible — an RSS bot that posts on discovery can only ever be live. Do not collapse
these back together.

They are separate on the failing path too: a sweep that fetches nothing files nothing and still
posts. A daily channel whose window opens on the half hour its upstream happens to be down is owed
the backlog the *last* sweep filed, and holding it behind an outage it has nothing to do with is the
one thing this split exists to make impossible.

**One fetch per feed per storefront, per sweep.** Subscriptions multiply with servers and channels;
upstreams do not care why merchant is asking the same question three times, and Reddit answers 429
to far less than that. `Sweeper` caches a sweep's fetches by feed and region — two servers on
different regions genuinely are two requests, everything else is one.

**The backlog comes back most recently found first, and within one sweep in the order the source
offered.** Both orderings are load-bearing: `Pending` is what a digest lists and what a capped live
burst picks from, so what a channel is short of room for has to be the least worth saying. It works
because `Record` stamps `first_seen` once per sweep rather than once per row — a per-row timestamp
is unique, the `rowid` tiebreaker never runs, and the sweep's own order is lost. `Sweeper` reverses
the burst before posting, so the channel still reads forwards.

**Only what reached the channel is marked as sent.** A burst is several messages and one of them can
be refused while the ones before it are already up, so `FlushAsync` collects the ids it actually
delivered and stamps those in a `finally`. Marking the whole page after the loop posts the delivered
ones a second time on the next sweep; marking nothing on a failure does the same thing.

**Cadence windows are shaved below their nominal period** (23h, 6.9d). A sweep landing a few minutes
late must not walk the daily post around the clock, and a weekly post must not drift past Steam's
Tuesday chart and become fortnightly. `CadenceTests` pins both.

**First sweep posts a handful and files the rest as seen.** A channel that stays empty for a day
after setup reads as a broken bot; a channel that receives a month of backlog reads as a broken bot.
`LedgerTests` covers both edges.

## The ledger

**The schema is a list of migrations, and the file says which it has had.** `Migrations` in `Ledger`
is an ordered array; `PRAGMA user_version` records how far a database has been brought; opening one
applies whatever is missing, one migration and one stamp per transaction. The first entry is written
with IF NOT EXISTS so a database predating the stamp is adopted rather than rebuilt. A file from a
*newer* merchant is refused at startup by name: the alternative is an older build meeting the change
one query at a time, hours later, mid-sweep, with an error that names a column and explains nothing.
The procedure for adding one is in [Development](development.md#changing-the-ledgers-schema).

**The ledger is one connection, and every entry point takes the same gate.** Two callers reach
`Ledger` without taking turns: the sweep on its background loop, and every slash command on the
gateway's threads. SQLite scopes a transaction to the connection rather than to the caller, so an
ungated write from a command lands inside whatever transaction `Record` has open and is rolled back
with it — `/merchant add` answers with a subscription number for a row that no longer exists.
`LedgerConcurrencyTests` reproduces exactly that. The operations are single-digit milliseconds; the
contention costs nothing.

**The ledger's default path is absolute, and it is the only default that is computed.** A relative
default resolves against the working directory, so the same install reads a different database
depending on where it was started, and presents as a bot that has forgotten every subscription.
`MerchantConfig.ResolveDatabasePath` puts it in the XDG state directory. The unit and the container
still pass `MERCHANT_DB`, because each has to name the path it grants write access to.

## Sources and input

**A source hands out absolute http(s) links and nothing else.** Discord validates a URL while the
embed is being *built*, so a junk link throws inside the sweep: the item is never marked sent, the
next sweep picks it up again, and that channel is stuck for good. Feeds do produce these — relative
paths, `javascript:` hrefs, truncated `src` attributes — so `Links.Http` drops them where the cost
is one item. Anything reaching `Announcer` is postable.

**Everything a person types that reaches a URL is checked where it is typed.** A region is
substituted into a feed's address, so two arbitrary characters build a request for the wrong page or
for nothing, and the channel goes quiet with no error anywhere: the fetch failure is swallowed by
design. `GuildSettings.IsRegion` and `IsCurrency` are what stop it, on `/merchant region` and on
`--check`'s argument both — the same code refusing it in both places, because those are the only two
ways a code gets in.

**`{currency}` is filled in and nothing uses it.** `/merchant region` takes a currency, the ledger
stores it, and `Template` substitutes it, but neither driver prices in anything but USD — which is
what `UsdCaveat` exists to say out loud. It stays because it is what a source that quotes a server's
own money would need, and because it is genuinely part of the `Fetch` key: two servers whose URLs
differ only by currency are two requests. Do not treat a feed built on it as supported until a
driver can honour it.

**CheapShark, not IsThereAnyDeal, for the priced feeds.** ITAD encodes its filters into an opaque
token minted by its own web UI, so "under ten" cannot be expressed in a URL merchant builds.
Verified: a hand-constructed ITAD filter URL returns 400. CheapShark takes the ceiling as a query
parameter and returns structured prices, which is also why those embeds can strike through a list
price. ITAD is still right for giveaways, which need no filter and vary by region.

**CheapShark requires a descriptive `User-Agent`** or it refuses the request outright, and Reddit
throttles a generic one harder. That is why every fetch goes through the one named `HttpClient`.

## Globalization is on, and every value is invariant

**`InvariantGlobalization` must stay false.** Discord.Net constructs a `CultureInfo` from every
guild's `preferred_locale` while handling GUILD_CREATE. With the flag on, the bot connects,
registers commands and reports itself healthy — then throws `CultureNotFoundException` on the first
guild, which never enters the client's cache, and every command afterwards fails on a null
`Context.Guild`. It presents as "Unknown Guild" in the log and a `NullReferenceException` in the
command, which points nowhere near the cause. `GlobalizationTests` is the guard, and it fails under
`-p:InvariantGlobalization=true` rather than letting the flag arrive unnoticed.

**Every number and date crossing merchant's edge is invariant, never the host's culture.** That is
the other half, and it cuts both ways. Reading: a feed writes English month names on a Gregorian
calendar whatever locale the machine has, and under `fa-IR` or `th-TH` the current culture refuses
those strings or reads them six centuries out, so the item silently loses its place in every digest.
Writing: CheapShark quotes USD whatever region a server picks, and a price formatted in the host's
convention is a different number — `$3,49` on a German host, in Eastern Arabic digits on a Persian
one, in an embed read by people who have never seen that host.

So a price goes through `CheapSharkSource.Priced`, a review count and a score name the invariant
culture explicitly, and the ledger's timestamps are written and read with it. CI runs the whole
suite a second time under `de_DE.UTF-8`. The analyzers help but do not cover this: CA1305 catches a
bare `ToString()` and does not see inside an interpolated string, which is exactly where the prices
were.

## Discord

**The token is not in the settings file and not in the container image.** It is read straight from
`MERCHANT_TOKEN` into a local in `Program.cs` and handed to `LoginAsync`. Keeping it off
`BotOptions` keeps the one secret merchant holds out of a DI singleton every command can reach, and
out of a file that gets copied around. A `bot.token` in the file is reported and ignored.

**A refused token stops merchant; every other gateway failure is waited out.** Discord.Net
reconnects on anything, which is right for a dropped connection, an outage or a DNS hiccup and wrong
for exactly one case: a 401 means the token is wrong or has been reset, and no amount of waiting
changes that. Left alone it produces the worst failure this bot has — running, reporting itself up,
posting nothing, and explaining why only inside a stack trace nobody is reading. So the 401 is named
once at `LogCritical`, the application is stopped, and the exit code is non-zero, which is what makes
it a failed unit and a restarting container instead of a quiet one. `GatewayFailure.IsUnauthorized`
looks *through* an exception rather than at it, because the status arrives wrapped.

**Shutdown logs out before the host is disposed.** `RunAsync` disposes the host the moment it
returns and the host owns the gateway client, so anything said to Discord after it is said to a
disposed object. `StartAsync` plus `WaitForShutdownAsync` leaves the client alive until the `using`
at the end of `Program.cs` — and the logout is what makes the bot show offline at once on a restart
rather than lingering until the gateway times it out.

**Commands read the guild id from the interaction payload, not the gateway cache.** `Context.Guild`
is null whenever a guild is missing from the cache; `Context.Interaction.GuildId` is always present.
A permission check that cannot run returns null rather than an empty list, so "could not check" is
never mistaken for "nothing is missing".

**Embeds are packed to Discord's budget, because the size of the catalog is decided by whoever edits
the file.** An embed refuses to build past 6000 characters total, which is around thirteen feeds at
the length a feed is allowed. `Announcer.Catalog` fits what it can and says what it left out, so a
long catalog shortens `/merchant help` instead of taking it offline.

The line saying what was left out is part of that budget, not an afterthought to it. It is written by
`Announcer.Note`, measured before the first field goes in and set after the last one, because a
budget that fills the embed to exactly 6000 and *then* appends a footer has spent the whole margin it
was protecting — and the throw lands on the one command whose entire job is to still answer.

**No privileged intents.** `GatewayIntents.Guilds` only. Merchant reads no messages and no member
lists, so the application never needs intent review. Do not add an intent for a convenience.

**`/merchant add` checks channel permissions before it writes anything** and names what is missing. A
silent permission failure is the most common way this class of bot appears broken, and the person
hitting it has no access to the logs.

**Every command reply is ephemeral.** Setting up announcements should not itself post in the server.
