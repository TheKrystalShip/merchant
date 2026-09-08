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

All of it is environment variables; there is no settings file.

| Variable                 | Default          | Meaning                                                                  |
| ------------------------ | ---------------- | ------------------------------------------------------------------------ |
| `MERCHANT_TOKEN`         | —                | **Required.** The bot token.                                             |
| `MERCHANT_DB`            | `merchant.db`    | Where the ledger lives.                                                  |
| `MERCHANT_SWEEP_MINUTES` | `30`             | How often feeds are fetched. Clamped to 5–720.                           |
| `MERCHANT_USER_AGENT`    | `merchant/1.0 …` | Sent on every fetch. CheapShark rejects a generic one.                   |
| `MERCHANT_DEV_GUILD`     | unset            | Register commands to one server, which is instant. Global takes an hour. |

## Checking the feeds

```bash
merchant --check        # or: merchant --check ES
```

Fetches all five feeds and reports counts, timings and the first item of each. Needs no token and
touches no Discord, so it is the first thing to run when a channel goes quiet.

```
ok   top-week         5 items    470 ms  Top Games of the Week
     └ #1 - Counter-Strike 2
ok   under-10        20 items    680 ms  Games Under $10
     └ Suicide Squad: Kill the Justice League  $3.49 (-95%)
```

## Development

```bash
dotnet test        # 61 tests, no network
dotnet run --project src/Merchant -- --check
```

The parser tests run against captured documents from the three formats that actually arrive —
Steam's RSS 1.0, IsThereAnyDeal's RSS 2.0 and Reddit's Atom — under `tests/Merchant.Tests/Fixtures`.
Refresh them when a source changes shape.

## Adding a feed

A row in `Catalog.All`, a case in `Catalog.SourceFor`, and a member on `FeedChoice`. Storage,
scheduling, digests and commands need no change; a test asserts the catalog and the menu agree.

## Licence

MIT.
