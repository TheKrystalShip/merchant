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
Catalog ── five categories, each naming a source and a default cadence      Feeds/Catalog.cs
    │
Sources ── ISource[]: RssSource (RSS 1.0 / RSS 2.0 / Atom), CheapSharkSource     Sources/
    │      flatten everything to FeedItem; a dead feed returns empty, never throws
    │
Store ──── SQLite. subscriptions · guilds · seen                            Store/Store.cs
    │      `seen` doubles as the digest buffer: posted = 0 means "waiting"
    │
Sweeper ── BackgroundService. Fetch everything on one interval,          Discord/Sweeper.cs
    │      post per subscription on its own cadence
    │
Announcer ─ FeedItem → Embed. One card when live, one list when a digest  Discord/Announcer.cs
    │
MerchantModule ─ /merchant add · list · remove · preview · region · help      Discord/MerchantModule.cs
```

## Decisions worth keeping

**The catalog is the product.** A person picks "Games Under $10", not a URL, a filter and a poll
interval. Keep it around five entries. If a change would make somebody paste a feed URL into a
command, it is the wrong change — that is the thing merchant exists to avoid.

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
bot. `StoreTests` covers both edges.

**`/merchant add` checks channel permissions before it writes anything** and names what is missing. A
silent permission failure is the most common way this class of bot appears broken, and the person
hitting it has no access to the logs.

**Every command reply is ephemeral.** Setting up announcements should not itself post in the server.

## Working on it

```bash
dotnet build
dotnet test                                    # 61 tests, no network
dotnet run --project src/Merchant -- --check      # fetch all five feeds live, no token needed
dotnet run --project src/Merchant -- --check ES   # …for another region
```

`--check` is the first thing to reach for when a channel goes quiet: it separates "the feed
changed" from "Discord is unhappy" without a token and without touching a server.

Set `MERCHANT_DEV_GUILD` while developing. Guild commands register instantly; global ones take up to
an hour, which makes iterating on a command signature unbearable otherwise.

## Tests

Parser tests run against **captured documents**, not hand-written XML, under
`tests/Merchant.Tests/Fixtures`: Steam's RSS 1.0 (items outside `<channel>`, no `guid`), ITAD's
RSS 2.0 (CDATA and HTML entities), Reddit's Atom (link in an `href`), and a CheapShark response.
Hand-written fixtures agree with the parser by construction and prove nothing. Refresh them from
the live feeds when a source changes shape.

`CatalogTests` asserts `Catalog.All` and `FeedChoice` stay in step — they are two hand-maintained
lists that must agree, and Discord needs the enum because choices are registered up front.

## Adding a feed

1. A row in `Catalog.All` with its own colour.
2. A case in `Catalog.SourceFor`.
3. A member on `FeedChoice` with a `[ChoiceDisplay]`.

Storage, scheduling, digests and the commands need no change. `CatalogTests` fails if step 3 is
forgotten.

## Deploying

`deploy/install.sh` publishes to `~/.local/share/merchant` and installs the user unit; the token lives
in `~/.config/merchant/merchant.env` at mode 0600 and never in git. The `Dockerfile` is the portable
half — same code, token passed at run time, ledger on a volume at `/data`.

The ledger is the only state. Losing it makes merchant repost whatever each feed currently offers,
once — annoying, not destructive.
