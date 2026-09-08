# Merchant

> _"What're ya buyin'?"_

Game-deal announcements for Discord. Invite it, point it at a channel, and it posts.

Named for the one in Resident Evil 4, on the grounds that a bot which turns up unannounced to
offer you things cheap should be honest about what it is.

`Merchant` exists because the alternative is five services: an RSS bot for one feed, a deals bot for
another, a no-code automation for the third, and a wiki page of feed URLs to keep them straight.
Here there is one bot, five feeds it already knows about, and one command.

```
/merchant add  feed: Games Under $10  channel: #bargain-bin
```

That is the entire setup.

## What it posts

| Feed                       | Where it comes from                              | Default cadence |
| -------------------------- | ------------------------------------------------ | --------------- |
| **Top Games of the Week**  | Steam's own weekly top-sellers chart             | once a week     |
| **Games Under $10**        | CheapShark, price-capped, best deals first       | once a day      |
| **Best Game Deals**        | CheapShark, 75+ Metacritic, largest discounts    | once a day      |
| **Worth Checking Out**     | Rock Paper Shotgun, PC Gamer, top of r/GameDeals | once a day      |
| **Free Games & Giveaways** | IsThereAnyDeal giveaways                         | as they appear  |

Each posts in its own colour, so channels read apart at a glance.

Those five are what merchant ships with, not what it can do: the catalog is a settings file, so
adding, changing or removing a feed is an edit and a restart. See
[Configuration](#configuration).

## Commands

Everything is under `/merchant`, and every reply is ephemeral — setup does not clutter the channel.
The commands default to **Manage Server**; change that in _Server Settings → Integrations_.

| Command             | What it does                                                            |
| ------------------- | ----------------------------------------------------------------------- |
| `/merchant add`     | Point a feed at a channel. Optionally set a cadence and a role to ping. |
| `/merchant list`    | What is posting where, and how much is waiting.                         |
| `/merchant remove`  | Stop one feed, by the number `/merchant list` shows.                    |
| `/merchant preview` | See what a feed would post, before wiring it up. Only you see it.       |
| `/merchant region`  | Set the country and currency used for prices.                           |
| `/merchant help`    | The catalog, with a suggested channel name for each feed.               |

`/merchant add` checks that merchant can actually post in the target channel **before** it saves
anything, and names the missing permission if it cannot. A feed bot that silently fails on a
channel permission is the single most common way this kind of setup goes wrong.

## Live and digest

An ordinary RSS bot can only post on discovery, which makes "weekly" impossible. merchant separates
the two: every feed is fetched on one interval, and each channel posts on its own clock from what
the sweep filed. So a live channel gets each giveaway as it appears, and a weekly channel gets one
digest listing the week — from the same machinery.

The first sweep after `/merchant add` posts a handful straight away and files the rest of the backlog
as already seen, so a new channel proves it works without a month of history landing in it.

## Running it

Two things are needed before any of the below.

**A Discord application:** <https://discord.com/developers/applications> → **New Application** →
**Bot** → **Reset Token**. Merchant requests no privileged intents, so nothing there needs enabling.

**.NET 10.** `deploy/install.sh` needs the SDK to build; the container needs nothing at all, because
it builds and runs inside the image. The exact SDK floor is in `global.json`, and a machine with an
older one says so rather than failing obscurely.

```bash
dotnet --version        # 10.0.100 or newer
```

Invite it with this URL, substituting the application id — the permissions integer is
View Channels + Send Messages + Embed Links, and nothing else:

```
https://discord.com/api/oauth2/authorize?client_id=YOUR_APP_ID&permissions=19456&scope=bot%20applications.commands
```

### On a host with systemd

```bash
deploy/install.sh                            # publishes, installs the unit, links merchant onto PATH
$EDITOR ~/.config/merchant/merchant.env      # put the token in
systemctl --user enable --now merchant
journalctl --user -u merchant -f
```

It is architecture-neutral — the same command installs on an arm64 box as on an x86-64 one — and
re-runnable: an upgrade is `git pull && deploy/install.sh && systemctl --user restart merchant`. It
seeds the token file and the settings file and overwrites neither, so an upgrade never touches the
feeds or the ledger.

### As a container

```bash
docker build -t merchant .
docker run -d --name merchant \
  -e MERCHANT_TOKEN=... \
  -v merchant-data:/data \
  merchant
```

### Configuration

One file, `~/.config/merchant/appsettings.json` — or wherever `MERCHANT_CONFIG` points, which the
container sets to `/data/appsettings.json`. If it is not there, merchant writes the shipped example
to it on first run and uses that, so an empty volume still produces a working bot.

It takes comments and trailing commas, and the copy merchant seeds is annotated throughout.

```jsonc
{
  "bot": {
    "sweepMinutes": 30,              // how often every feed is fetched, 5–720
    "userAgent": "merchant/1.0 (…)"  // CheapShark rejects a generic one
    // "databasePath": "…"           // default: ~/.local/state/merchant/merchant.db
    // "devGuildId": 123…            // register commands to one server; instant, vs an hour
  },
  "feeds": { /* … */ },
  "logging": { "logLevel": { "default": "Debug" } }   // optional; read by the host
}
```

A key that is not in the schema is named at startup, at every level — including a section, so
`"feed"` for `"feeds"` is caught rather than producing a bot that announces nothing.

The **token is never read from this file** — it comes from `MERCHANT_TOKEN` and nowhere else, so the
settings file can be copied around, pasted into a chat window or committed without leaking anything.

Every setting above can also come from the environment — `MERCHANT_DB`, `MERCHANT_SWEEP_MINUTES`,
`MERCHANT_USER_AGENT`, `MERCHANT_DEV_GUILD` — which wins over the file. That is how the unit file
and the container pass them.

## Checking the feeds

```bash
merchant --check        # or: merchant --check ES
```

Fetches every configured feed and reports counts, timings and the first item of each. It reads and
checks the settings file first, so a mistake in an edit shows up here — named — rather than as a
channel that quietly stops posting. Needs no token and touches no Discord, so it is also the first
thing to run when a channel goes quiet.

```
ok   top-week         5 items    470 ms  Top Games of the Week
     └ #1 - Counter-Strike 2
ok   under-10        20 items    680 ms  Games Under $10
     └ Suicide Squad: Kill the Justice League  $3.49 (-95%)
```

In a container, where there is no `merchant` on the path:

```bash
docker exec merchant dotnet /app/merchant.dll --check
```

To try an edit against a scratch file, without touching the one the bot is reading:

```bash
MERCHANT_CONFIG=/tmp/try.json merchant --check
```

### When a channel goes quiet

In order, because each step rules out the one below it:

```bash
merchant --check                              # is the feed still answering?
systemctl --user status merchant              # is the bot even running?
journalctl --user -u merchant -n 50           # what did it say?
```

`/merchant list` reports how many items are waiting per channel, which separates "nothing new
upstream" from "posting is stuck". If the answer is still not obvious, turn the log up — add
`"logging": { "logLevel": { "default": "Debug" } }` to the settings file and restart. That reports
every sweep, every fetch and every post, including the ones that decided to do nothing.

A silent channel with everything else healthy is almost always a permission that was removed after
`/merchant add` ran. Re-running `add` on the same channel re-checks them and names what is missing.

## The ledger

One SQLite file, and the only state merchant has: which channels want which feeds, and what each has
already been shown. It lives at `~/.local/state/merchant/merchant.db`, or `/data/merchant.db` in the
container — `databasePath` and `MERCHANT_DB` move it.

```bash
sqlite3 ~/.local/state/merchant/merchant.db 'SELECT * FROM subscriptions'
cp ~/.local/state/merchant/merchant.db backup.db     # while stopped, or use: .backup
```

Losing it is annoying, not destructive: merchant reposts whatever each feed currently offers, once,
and carries on. There is nothing in it worth protecting except the subscriptions, which are one
`/merchant add` each to recreate.

Posted items are forgotten after 60 days, so the file stays small on its own. Unposted ones are
never pruned, however long a weekly channel has been waiting.

**Upgrades bring the schema up on their own.** Merchant stamps a version into the file and applies
whatever is missing when it starts, inside a transaction, so an interrupted upgrade leaves a version
that was fully applied. Nothing has to be run by hand and no state is lost.

Going *backwards* is the one case it refuses: a file written by a newer merchant is reported at
startup rather than opened, because an older build would otherwise meet the change one query at a
time, hours later, in the middle of a sweep.

```
Could not open the ledger at /home/…/merchant.db: its schema is version 3, and this merchant
knows version 2. A newer merchant wrote it: upgrade this one, or point databasePath at a
different file.
```

## Development

Nothing here departs from an ordinary .NET repository: `build`, `test`, `format`, `publish`.

```bash
dotnet build
dotnet test                                  # no network: every feed in them is a captured file
dotnet format                                # the house style, enforced from .editorconfig
dotnet run --project src/Merchant -- --check
```

To run the bot itself against a real server, point it at one guild — commands registered to a guild
appear immediately, where global ones take up to an hour:

```bash
MERCHANT_TOKEN=… MERCHANT_DEV_GUILD=… dotnet run --project src/Merchant
```

CI runs exactly the four commands above plus `docker build`, so a green local run is a green build.

The parser tests run against captured documents from the three formats that actually arrive —
Steam's RSS 1.0, IsThereAnyDeal's RSS 2.0 and Reddit's Atom — under `tests/Merchant.Tests/Fixtures`.
Refresh them when a source changes shape.

Changing the schema of the ledger, and adding a kind of source, are the two things that are not
just an edit to a settings file: both are in `CLAUDE.md`.

## Editing the feeds

Everything merchant can post is in `~/.config/merchant/appsettings.json` under `"feeds"`. Edit it and
restart — nothing is rebuilt, and the menu picks the change up on its own. The copy merchant seeds
carries this same reference in its comments.

```jsonc
"indie-picks": {                          // the key: lower-case, hyphenated, max 100 chars
  "label": "Indie Picks",
  "description": "Small games worth a look.",
  "cadence": "Daily",
  "colour": "#8B5CF6",
  "source": { "type": "rss", "urls": [ "https://example.test/indies.rss" ] }
}
```

| Key           | Required | Value                | Default  | Meaning                                    |
| ------------- | -------- | -------------------- | -------- | ------------------------------------------ |
| `label`       | yes      | string, ≤ 100        | —        | Shown in the menu and on every embed.      |
| `description` | yes      | string, ≤ 400        | —        | One line, in the menu and `/merchant help`. |
| `source`      | yes      | object               | —        | Where the items come from — see below.     |
| `channel`     | no       | string               | the key  | Suggested channel name.                    |
| `cadence`     | no       | `Live`/`Daily`/`Weekly` | `Daily` | How often the channel hears from it.      |
| `colour`      | no       | `"#RRGGBB"`          | `#5865F2` | Embed accent.                             |
| `enabled`     | no       | `true`/`false`       | `true`   | `false` parks a feed without deleting it.  |

The feed's key — `indie-picks` above — is what the ledger stores, so renaming one orphans the
channels already subscribed to it.

### Sources

`"source": { "type": "rss", … }` — one or more syndication feeds, merged.

| Key    | Required | Value                                     |
| ------ | -------- | ----------------------------------------- |
| `urls` | yes      | Array of one or more full http(s) addresses. RSS 1.0, RSS 2.0 and Atom all work without being told which. A URL may contain `{region}` or `{currency}`, filled in per server from `/merchant region`. |

`"source": { "type": "cheapshark", … }` — a slice of CheapShark's deals API. Structured prices, so
these embeds can strike through a list price. Always quoted in USD, whatever the region is set to.

| Key             | Required | Value                | Default       |
| --------------- | -------- | -------------------- | ------------- |
| `upperPrice`    | no       | number above 0       | no ceiling    |
| `minMetacritic` | no       | number, 0–100        | no floor      |
| `sortBy`        | no       | `DealRating`, `Title`, `Savings`, `Price`, `Metacritic`, `Reviews`, `Release`, `Store` or `Recent`. Spaces are ignored, so `Deal Rating` reads the same. | `DealRating` |

### After an edit

```bash
merchant --check        # validates the file, then fetches every feed
```

A feed with a mistake in it is dropped at startup with a line naming it and what was expected, and
every other feed keeps running — one typo should not take a server's channels offline. A file with
no usable feeds at all stops merchant rather than leaving it idling.

A genuinely new *kind* of upstream — neither a syndication feed nor CheapShark — is the one thing
that still needs code: an `ISourceFactory` in `src/Merchant/Feeds/Factories` and a line in
`Program.cs`. Every feed built on a kind that already exists is config alone.

## Licence

MIT.
