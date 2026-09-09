# Running it

Day-to-day: checking the feeds, finding out why a channel is quiet, and looking after the one file
that holds any state.

## `--check`

```bash
merchant --check        # or: merchant --check ES
```

Fetches every configured feed and reports counts, timings and the first item of each. It reads and
validates the settings file first, so a mistake in an edit shows up here — named — rather than as a
channel that quietly stops posting.

It needs no token and touches no Discord, which is what makes it the first thing to run when
anything looks wrong: it separates "the feed changed" from "Discord is unhappy" without a token and
without touching a server.

```
ok   top-week         5 items    470 ms  Top Games of the Week
     └ #1 - Counter-Strike 2
ok   under-10        20 items    680 ms  Games Under $10
     └ Suicide Squad: Kill the Justice League  $3.49 (-95%)
```

The optional argument is a country code, two letters and nothing else; anything else is refused by
name rather than fetched. `merchant --help` lists the three things merchant can be told and where
its files are, and anything else that reads like a command is named and refused rather than passed
to the host as configuration and answered by a demand for a token nobody was trying to use.

In a container, where there is no `merchant` on the path:

```bash
docker exec merchant dotnet /app/merchant.dll --check
```

To try an edit against a scratch file, without touching the one the bot is reading:

```bash
MERCHANT_CONFIG=/tmp/try.json merchant --check
```

Reading the settings file fails in two ways and both are sentences. A file the process cannot open
throws `UnauthorizedAccessException`, which is not an `IOException` and has to be caught by name — a
root-owned file in a mounted volume is the ordinary way to meet it. A file that will not parse
throws from the configuration provider, whose own message names the file and nothing else: the line
and the column are on the exception underneath, and both are printed, because somebody is looking at
that file in an editor.

## When a channel goes quiet

In order, because each step rules out the one below it:

```bash
merchant --check                              # is the feed still answering?
systemctl --user status merchant              # is the bot even running?
journalctl --user -u merchant -n 50           # what did it say?
```

`/merchant list` reports how many items are waiting per channel, which separates "nothing new
upstream" from "posting is stuck".

If the answer is still not obvious, turn the log up — add `"logging": { "logLevel": { "default":
"Debug" } }` to the settings file and restart. That reports every sweep, every fetch and every
post, including the ones that decided to do nothing.

**A silent channel with everything else healthy is almost always a permission** that was removed
after `/merchant add` ran. Re-running `add` on the same channel re-checks them and names what is
missing.

## A refused token

If merchant is not running at all and the log ends on a `401 Unauthorized`, the token is wrong or
has been reset. Merchant stops rather than retrying one that cannot start working, so this shows up
as a service that has **failed** rather than one that is quietly doing nothing.

Resetting a bot's token at <https://discord.com/developers> invalidates the previous value — which
is the usual way to arrive here.

Every other gateway failure is waited out: a dropped connection, an outage or a DNS hiccup is
something Discord recovers from, and merchant reconnects.

## The ledger

One SQLite file, and the only state merchant has: which channels want which feeds, and what each
has already been shown. It lives at `~/.local/state/merchant/merchant.db`, or `/data/merchant.db` in
the container — `databasePath` and `MERCHANT_DB` move it.

```bash
sqlite3 ~/.local/state/merchant/merchant.db 'SELECT * FROM subscriptions'
cp ~/.local/state/merchant/merchant.db backup.db     # while stopped, or use: .backup
```

Losing it is annoying, not destructive: merchant reposts whatever each feed currently offers, once,
and carries on. There is nothing in it worth protecting except the subscriptions, which are one
`/merchant add` each to recreate.

Posted items are forgotten after 60 days, so the file stays small on its own. Unposted ones are
never pruned, however long a weekly channel has been waiting.

### Upgrades bring the schema up on their own

Merchant stamps a version into the file and applies whatever is missing when it starts, inside a
transaction, so an interrupted upgrade leaves a version that was fully applied. Nothing has to be
run by hand and no state is lost.

Going *backwards* is the one case it refuses: a file written by a newer merchant is reported at
startup rather than opened, because an older build would otherwise meet the change one query at a
time, hours later, in the middle of a sweep.

```
Could not open the ledger at /home/…/merchant.db: its schema is version 3, and this merchant
knows version 2. A newer merchant wrote it: upgrade this one, or point databasePath at a
different file.
```

## Which version is running

```bash
$ merchant --version
merchant 1.0.0+a1b2c3d4e5f6…
```

The number is the release; what follows the `+` is the commit it was built from, which a build made
from a checkout carries and a released image does not need. It is the first question worth
answering about a bot that is behaving oddly, and it is the same string the startup line in the
journal opens with. Every outbound feed request carries the version too, in the user agent.

## Upgrading

```bash
git pull && deploy/install.sh && systemctl --user restart merchant     # systemd
docker compose pull && docker compose up -d                           # container
```

Re-runnable and safe: `install.sh` seeds the token file and the settings file and overwrites
neither, and the container keeps both on its volume, so an upgrade never touches the feeds or the
ledger. [The CHANGELOG](../CHANGELOG.md) says what is in each version, and
[Deployment](deployment.md) has the detail of both paths.
