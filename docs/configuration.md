# Configuration

One file and six environment variables. The file is the catalog and everything about how merchant
runs; the environment carries the one secret and whatever a unit file or a container has to name.

## The settings file

`~/.config/merchant/appsettings.json` — or wherever `MERCHANT_CONFIG` points, which the container
sets to `/data/appsettings.json`.

If it is not there, merchant writes the shipped example to it on first run and uses that, so an
empty volume still produces a working bot and leaves behind a file to edit. `deploy/install.sh`
seeds it too, because the systemd unit runs with the home directory read-only and cannot write it
itself.

It takes **comments and trailing commas**, and the copy merchant seeds is annotated throughout.

```jsonc
{
  "bot": {
    "sweepMinutes": 30,              // how often every feed is fetched, 5–720
    "userAgent": "merchant/<version> (…)"  // the default; CheapShark rejects a generic one
    // "databasePath": "…"           // default: ~/.local/state/merchant/merchant.db
    // "devGuildId": 123…            // register commands to one server; instant, vs an hour
  },
  "feeds": { /* … */ },
  "logging": { "logLevel": { "default": "Debug" } }   // optional; read by the host
}
```

### `bot`

Every key optional.

| Key            | Value           | Default                                | Meaning                                   |
| -------------- | --------------- | -------------------------------------- | ----------------------------------------- |
| `sweepMinutes` | number, 5–720   | `30`                                   | How often every feed is fetched.          |
| `userAgent`    | string          | `merchant/<version> (…)`               | Sent on every request.                    |
| `databasePath` | string          | `~/.local/state/merchant/merchant.db`  | Where the ledger lives.                   |
| `devGuildId`   | number          | register globally                      | Register commands to one server instead.  |

Name `databasePath` only to move the ledger somewhere else. A **relative** path resolves against
the working directory, which means the same install reads a different database depending on where
it was started — one under the unit, one in the checkout under `dotnet run` — and presents as a bot
that has forgotten every subscription.

### `feeds`

The catalog: every feed merchant can post. It has [its own document](feeds.md).

### `logging`

Read by the host rather than by merchant, and optional. Turning the log up is an edit to the same
file as everything else:

```jsonc
"logging": { "logLevel": { "default": "Debug" } }
```

That reports every sweep, every fetch and every post, including the ones that decided to do
nothing. It is the second thing to reach for when a channel goes quiet.

### Unknown keys are named

Every level of the file is held to the schema, **including its top**. A catalog written under
`"feed"` rather than `"feeds"` is named at startup, rather than producing a bot that starts, reports
itself healthy and announces nothing.

## The token

The token comes from `MERCHANT_TOKEN` and nowhere else. It is **never read from the settings file**
— a `bot.token` key there is reported and ignored — so the settings file can be copied around,
pasted into a chat window or committed without leaking anything.

- **systemd:** `~/.config/merchant/merchant.env`, mode 0600, loaded by the unit's `EnvironmentFile`.
- **container:** `docker run -e MERCHANT_TOKEN=…`. Nothing is ever baked into the image.

A token Discord refuses stops merchant with a non-zero exit rather than being retried forever — see
[a refused token](operations.md#a-refused-token).

## Environment variables

A handful of settings can also come from the environment, and **the environment wins over the
file**. That is how the unit file and the container pass them; the feeds are the catalog and live
only in the file.

| Variable                 | Overrides             |
| ------------------------ | --------------------- |
| `MERCHANT_TOKEN`         | — (the only source)   |
| `MERCHANT_CONFIG`        | where the file is read from |
| `MERCHANT_DB`            | `bot.databasePath`    |
| `MERCHANT_SWEEP_MINUTES` | `bot.sweepMinutes`    |
| `MERCHANT_USER_AGENT`    | `bot.userAgent`       |
| `MERCHANT_DEV_GUILD`     | `bot.devGuildId`      |

## Where everything lives

| What            | On a host                             | In the container         |
| --------------- | ------------------------------------- | ------------------------ |
| Settings / feeds | `~/.config/merchant/appsettings.json` | `/data/appsettings.json` |
| Token           | `~/.config/merchant/merchant.env`     | `-e MERCHANT_TOKEN`      |
| Ledger          | `~/.local/state/merchant/merchant.db` | `/data/merchant.db`      |
| The program     | `~/.local/share/merchant`             | `/app`                   |

Everything editable lives outside the install directory, because `install.sh` republishes over the
whole of it on an upgrade.

## Checking an edit

```bash
merchant --check
```

Validates the file, names anything wrong in it, then fetches every feed. To try an edit against a
scratch file without touching the one the bot is reading:

```bash
MERCHANT_CONFIG=/tmp/try.json merchant --check
```

See [Operations](operations.md#--check) for what it prints.
