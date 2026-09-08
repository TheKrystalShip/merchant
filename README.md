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
adding, changing or removing a feed is an edit and a restart — see [Feeds](docs/feeds.md).

## Commands

Everything is under `/merchant`, and every reply is ephemeral — setup does not clutter the channel.

| Command             | What it does                                                            |
| ------------------- | ----------------------------------------------------------------------- |
| `/merchant add`     | Point a feed at a channel. Optionally set how often, and a role to ping. |
| `/merchant list`    | What is posting where, and how much is waiting.                        |
| `/merchant remove`  | Stop one feed, by the number `/merchant list` shows.                    |
| `/merchant preview` | See what a feed would post, before wiring it up. Only you see it.       |
| `/merchant region`  | Set the country and currency used for prices. Two letters and three.    |
| `/merchant help`    | The catalog, with a suggested channel name for each feed.               |

Full reference: [Commands](docs/commands.md).

## Getting it running

You need a Discord application and its bot token — <https://discord.com/developers/applications> →
**New Application** → **Bot** → **Reset Token**. Merchant requests no privileged intents, so nothing
there needs enabling.

**On a host with systemd** (needs the .NET 10 SDK to build):

```bash
deploy/install.sh                            # publishes, installs the unit, links merchant onto PATH
$EDITOR ~/.config/merchant/merchant.env      # put the token in
systemctl --user enable --now merchant
```

**As a container** (needs nothing installed):

```bash
docker build -t merchant .
docker run -d --name merchant -e MERCHANT_TOKEN=... -v merchant-data:/data merchant
```

Then run `/merchant add` in the server. The whole of it, including the invite URL and what the first
hour looks like, is in [Setting it up](docs/setup.md).

When something looks wrong, `merchant --check` is the first thing to run: it validates the settings
file and fetches every feed, needs no token, and touches no Discord.

## Documentation

Everything longer than a paragraph is under [`docs/`](docs/README.md).

| Document                                | What is in it                                                       |
| --------------------------------------- | ------------------------------------------------------------------- |
| [Setting it up](docs/setup.md)          | Discord application, invite, install, the first command, the first hour. |
| [Commands](docs/commands.md)            | The six commands in full, and how live channels and digests differ. |
| [Feeds](docs/feeds.md)                  | The catalog: adding, changing and parking feeds, and the source types. |
| [Configuration](docs/configuration.md)  | The settings file, environment variables, the token, where files live. |
| [Running it](docs/operations.md)        | `--check`, why a channel went quiet, the ledger, backups, upgrades.  |
| [Deployment](docs/deployment.md)        | The systemd unit and the container in detail.                        |
| [Development](docs/development.md)      | Build, test, format, style rules, CI, adding a source, schema changes. |
| [Architecture](docs/architecture.md)    | What the pieces are and which parts are load-bearing.                |

## Development

An ordinary .NET solution: `dotnet build`, `dotnet test`, `dotnet format`. The style and lint rules
are `.editorconfig` and are enforced by the build rather than by review, and CI runs everything a
person can run locally.

`scripts/` wraps each of those in a line, if that is less typing:

```bash
scripts/build.sh
scripts/test.sh
scripts/format.sh
scripts/run.sh --check      # everything after the name goes to merchant
```

[Development](docs/development.md) has the rest.

## Licence

MIT.
