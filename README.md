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

Needs a Discord application: <https://discord.com/developers/applications> → **New Application** →
**Bot** → **Reset Token**. Merchant requests no privileged intents, so nothing there needs enabling.

Invite it with this URL, substituting the application id — the permissions integer is
View Channels + Send Messages + Embed Links, and nothing else:

```
https://discord.com/api/oauth2/authorize?client_id=YOUR_APP_ID&permissions=19456&scope=bot%20applications.commands
```

### On a host with systemd

```bash
deploy/install.sh                          # publishes to ~/.local/share/merchant, installs the unit
$EDITOR ~/.config/merchant/merchant.env          # put the token in
systemctl --user enable --now merchant
journalctl --user -u merchant -f
```

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
    "databasePath": "merchant.db",   // where the ledger lives
    "sweepMinutes": 30,              // how often every feed is fetched, 5–720
    "userAgent": "merchant/1.0 (…)"  // CheapShark rejects a generic one
    // "devGuildId": 123…            // register commands to one server; instant, vs an hour
  },
  "feeds": { /* … */ }
}
```

The **token is never read from this file** — it comes from `MERCHANT_TOKEN` and nowhere else, so the
settings file can be copied around, pasted into a chat window or committed without leaking anything.

Every setting above can still be overridden by the environment variable that configured it before
the file existed (`MERCHANT_DB`, `MERCHANT_SWEEP_MINUTES`, `MERCHANT_USER_AGENT`,
`MERCHANT_DEV_GUILD`), which is how the unit file and the container pass them.

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

## Development

```bash
dotnet test        # 113 tests, no network
dotnet run --project src/Merchant -- --check
```

The parser tests run against captured documents from the three formats that actually arrive —
Steam's RSS 1.0, IsThereAnyDeal's RSS 2.0 and Reddit's Atom — under `tests/Merchant.Tests/Fixtures`.
Refresh them when a source changes shape.

## Adding a feed

Edit `~/.config/merchant/appsettings.json` and restart. Nothing is rebuilt, and the menu picks the
new feed up on its own — the feed list is resolved when somebody opens it, not registered with
Discord in advance.

```jsonc
"indie-picks": {
  "label": "Indie Picks",              // what the menu and the embeds show
  "description": "Small games worth a look.",
  "channel": "indie-picks",            // suggested channel name; defaults to the key
  "cadence": "Daily",                  // Live, Daily or Weekly. Default: Daily
  "colour": "#8B5CF6",                 // embed accent. Default: #5865F2
  // "enabled": false,                 // park a feed without deleting it
  "source": {
    "type": "rss",
    "urls": [ "https://example.test/indies.rss" ]
  }
}
```

The key (`indie-picks`) is what the ledger stores, so renaming one orphans the channels already
using it. Sources come in two kinds:

| `type`       | Options                                        | Notes                                                |
| ------------ | ---------------------------------------------- | ---------------------------------------------------- |
| `rss`        | `urls` — one or more, merged                    | RSS 1.0, RSS 2.0 and Atom, no format flag needed. A URL may contain `{region}` or `{currency}`, filled in per server from `/merchant region`. |
| `cheapshark` | `upperPrice`, `minMetacritic`, `sortBy`         | Structured prices, so the embeds can strike through a list price. Always quoted in USD. |

Run `merchant --check` after an edit: it validates the file and fetches everything. A feed with a
mistake in it is dropped with an explanation naming the feed, and the others keep running — one typo
should not take a server's channels offline.

A genuinely new *kind* of upstream — something that is neither a syndication feed nor CheapShark —
is the one thing that still needs code: an `ISourceFactory` in `src/Merchant/Feeds/Factories` and a
line in `Program.cs`. Every feed built on a kind that already exists is config alone.

## Licence

MIT.
