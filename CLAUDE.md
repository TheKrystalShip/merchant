# CLAUDE.md

Guidance for Claude Code working in this repository.

**The documentation lives in [`docs/`](docs/README.md), and it is the reference — not this file.**
This is orientation: what merchant is, where things are, and what not to break without reading the
document that explains why.

| Before you… | Read |
| ----------- | ---- |
| change the sweep, the ledger, the catalog or anything about Discord | [docs/architecture.md](docs/architecture.md) |
| build, test, add a source kind, or change the schema | [docs/development.md](docs/development.md) |
| touch the settings file's shape | [docs/configuration.md](docs/configuration.md), [docs/feeds.md](docs/feeds.md) |
| touch `deploy/`, the unit or the `Dockerfile` | [docs/deployment.md](docs/deployment.md) |
| answer "why is a channel quiet" | [docs/operations.md](docs/operations.md) |

## What this is

merchant announces game deals in Discord channels. Somebody invites it, runs one command per
channel, and never touches it again. It is not a KGSM component and shares nothing with one: it
lives in the `tks` workspace beside `magpie` and `moviebot`, runs as the owner's user under
`systemd --user` or as a container, and depends on nothing of TheKrystalShip's — Discord.Net and
Microsoft.Data.Sqlite are the whole package surface, and `nuget.config` names only nuget.org so the
repo builds on a machine that has never held a GitHub Packages token.

It was written for somebody else's Discord server, which is the constraint that shapes everything:
**the person setting it up is not the person who wrote it, and will not read this file.** Every
design decision in `docs/architecture.md` follows from that, and so should every new one.

## Where things are

```
src/Merchant/
  Program.cs           composition root; where a new ISourceFactory is registered
  MerchantConfig.cs    where the settings file and the ledger are found
  BotOptions.cs        the bot section, and the check that the file has no unknown keys
  Feeds/               Schema.cs (the settings vocabulary), FeedCatalog, SourceRegistry, Factories/
  Sources/             the drivers: RssSource, CheapSharkSource, Links.Http
  Discord/             Sweeper, Announcer, MerchantModule, FeedAutocomplete, GatewayFailure
  Storage/Ledger.cs    SQLite, and the migration list
tests/Merchant.Tests/  xUnit; captured feed documents under Fixtures/
deploy/                install.sh, merchant.service, appsettings.example.jsonc, merchant.env.example
scripts/               one-line wrappers over dotnet build / test / format / run
docs/                  the manuals
```

## Working on it

```bash
dotnet build                                      # analyzers and style rules run here, as errors
dotnet test                                       # no network: the feeds in them are captured files
dotnet format                                     # the style in .editorconfig, applied
dotnet run --project src/Merchant -- --check      # validate the settings file, fetch every feed
```

`scripts/build.sh`, `test.sh`, `format.sh` and `run.sh` are one-line wrappers over exactly those,
taking the same arguments. Nothing lives only inside one, so use whichever is shorter to type.

Nothing is bespoke: an ordinary .NET solution, and CI runs what a person runs locally, plus a
line-length check, `shellcheck`, `docker build`, and the tests again under `de_DE.UTF-8`.

Set `MERCHANT_DEV_GUILD` when running against a real server: guild commands register instantly,
global ones take up to an hour.

**Style and lint are the build's job, not review's.** `.editorconfig` plus
`EnforceCodeStyleInBuild` and `TreatWarningsAsErrors`. Fix what an analyzer reports rather than
muting it; if a rule genuinely has to go, switch it off beside a written reason, and never lower
`AnalysisLevel`. Details and the three existing exceptions:
[docs/development.md](docs/development.md#style-is-the-builds-job-not-reviews).

## Do not break these

Each is load-bearing and each is explained in
[docs/architecture.md](docs/architecture.md) — read the reasoning before deciding one is in the way.

- **The catalog is data; drivers are code.** Feeds live in `appsettings.json`. A change that makes
  somebody paste a feed URL into a *command* is the wrong change. Resist a generic JSON-mapping
  driver.
- **The settings vocabulary is `Feeds/Schema.cs`.** No key written as a literal anywhere else. A
  schema change lands in three places: `Schema.cs`, the `appsettings.example.jsonc` header, and
  `docs/feeds.md`.
- **One malformed feed is dropped and named; an empty catalog is fatal.** Every rejection names its
  feed and what was expected — that message is the whole interface for somebody with a text editor.
- **Sweeping and posting stay separate.** One fetch interval, per-subscription cadences. It is the
  only reason a weekly channel exists, and a sweep that fetched nothing still posts.
- **Only what reached the channel is marked sent**, and the backlog's two orderings are deliberate.
- **The ledger is one connection behind one gate.** An ungated write from a command lands inside the
  sweep's open transaction; `LedgerConcurrencyTests` reproduces it.
- **Schema changes are appended to `Migrations`, never edited.** The array's length is the version.
  Procedure: [docs/development.md](docs/development.md#changing-the-ledgers-schema).
- **`InvariantGlobalization` stays false**, and every number and date crossing the edge is
  invariant. The rest of the workspace sets it true — do not inherit that.
- **The token comes from `MERCHANT_TOKEN` alone**, never the settings file, never the image.
- **A 401 stops merchant; every other gateway failure is waited out.**
- **No privileged intents.** `GatewayIntents.Guilds` only.
- **Embeds are packed to Discord's 6000-character budget**, including the line that says what was
  left out.
- **Every command reply is ephemeral**, and `/merchant add` checks channel permissions before it
  writes.

## Tests

Parser fixtures are **captured documents**, not hand-written XML — a hand-written fixture agrees
with the parser by construction and proves nothing. `FeedCatalogTests` holds the shipped settings
file to account rather than the code. `LedgerMigrationTests` is the only suite that starts from a
database that already exists, and builds the old shape by hand. What each one pins:
[docs/development.md](docs/development.md#tests).

## When documentation changes

The docs are the reference, so a behaviour change that a person could notice belongs in the
matching document under `docs/` in the same commit. Keep this file short: if something here grows
past a few lines of reasoning, it belongs in `docs/` with a pointer left behind.
