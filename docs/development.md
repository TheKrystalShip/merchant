# Development

Nothing here departs from an ordinary .NET repository: `build`, `test`, `format`, `publish`. There
is nothing bespoke to install and no script to learn.

```bash
dotnet build                                      # analyzers and style rules run here, as errors
dotnet test                                       # no network: every feed in them is a captured file
dotnet format                                     # apply the house style; --verify-no-changes checks it
dotnet run --project src/Merchant -- --check      # validate the settings file, fetch every feed
dotnet run --project src/Merchant -- --check ES   # …for another region
```

`scripts/` holds a one-line wrapper for each of those, for when the full command is more typing than
the thing it does. They take the same arguments and can be run from anywhere:

```bash
scripts/build.sh            # scripts/build.sh -c Release
scripts/test.sh             # LC_ALL=de_DE.UTF-8 scripts/test.sh reproduces the locale run
scripts/format.sh           # scripts/format.sh --verify-no-changes is what CI runs
scripts/run.sh --check      # everything after the name goes to merchant
```

Those four are wrappers and nothing more — there is no build step that only exists inside one, so
`dotnet build` on its own is always equivalent, and CI runs the underlying commands directly.

`scripts/lint.sh` is the exception: it is not a wrapper, it is the two rules the compiler cannot
report, and CI runs the file itself rather than a copy of it.

```bash
scripts/lint.sh             # the line length .editorconfig states, and shellcheck
```

Build, test, format and lint are the whole gate. Run those four and CI has nothing left to tell
you.

## The layout

```
src/Merchant/
  Program.cs           composition root: the DI graph, and where a new source factory is registered
  MerchantConfig.cs    where the settings file and the ledger are found
  BotOptions.cs        the bot section, and the check that the file has no unknown keys
  Feeds/               the catalog: Schema.cs, FeedCatalog, SourceRegistry, Factories/
  Sources/             the drivers: RssSource, CheapSharkSource, and Links.Http
  Discord/             Sweeper, Announcer, MerchantModule, FeedAutocomplete, GatewayFailure
  Storage/Ledger.cs    SQLite, and the migration list
tests/Merchant.Tests/  xUnit, with captured feed documents under Fixtures/
deploy/                install.sh, uninstall.sh, the unit file, the example settings and env files
scripts/               build, test, format and run wrappers, and lint.sh
compose.yaml           the container deploy, published image and all
```

[Architecture](architecture.md) is how those fit together and which of it is load-bearing. Read it
before changing the sweep, the ledger or the catalog.

## Style is the build's job, not review's

`.editorconfig` holds both the style and the lint rules — explicit types over `var`, file-scoped
namespaces, `_camelCase` instance fields, PascalCase for anything static and readonly, constructors
written out rather than primary. `Directory.Build.props` turns on `EnforceCodeStyleInBuild` and the
.NET analyzers at `latest-Recommended`, on top of `TreatWarningsAsErrors`.

A violation is a failed build, on the machine that made it. An editor picks the same rules up on
its own — `.vscode/` recommends the two extensions that do it, and any EditorConfig-aware editor
needs nothing.

Two rules an analyzer cannot report, and each says so where it is written:

- **Line length.** Roslyn has no line-length rule and the formatter does not wrap, so
  `max_line_length` is checked by `scripts/lint.sh`, which reads the number out of `.editorconfig`
  itself. The limit is written once, under `[*.cs]`, and `[tests/**.cs]` turns it off — a theory row
  is a settings document, and wrapping one hides the case it tests.
- **Primary constructors.** Nothing flags one that is already written, so that rule is honoured by
  hand.

Everything the analyzers report is fixed rather than muted, with three exceptions, each switched off
beside its reason in `.editorconfig`: CA1848 and CA1873 want `LoggerMessage` delegates for logging
that happens a few times per half-hour, and CA1720 objects to `ConfigRead.Int`/`Decimal` being named
after what they read, which is the point of them. CA1707 is off for `tests/` alone, because a test
is named as the sentence it asserts.

When a new rule fires, fix it or turn it off with the reason written down — never turn it off
silently, and never lower `AnalysisLevel` to make one go away.

The one place the formatter is overruled is the storefront table in `CheapSharkSource`, which is a
table and is written as one; `#pragma warning disable format` is what holds it.

## Tests

```bash
dotnet test
LC_ALL=de_DE.UTF-8 dotnet test     # the locale run CI does second
```

No test reaches the network.

**Parser tests run against captured documents**, not hand-written XML, under
`tests/Merchant.Tests/Fixtures`: Steam's RSS 1.0 (items outside `<channel>`, no `guid`), ITAD's RSS
2.0 (CDATA and HTML entities), Reddit's Atom (link in an `href`), and a CheapShark response.
Hand-written fixtures agree with the parser by construction and prove nothing. Refresh them from the
live feeds when a source changes shape.

**`FeedCatalogTests` holds the settings file to account** rather than the code. Its theory data is
the catalog merchant ships — the five feeds, their labels, channels, cadences, colours and source
types — so the shipped example cannot drift from what the bot is meant to post. The rest of it
asserts that every rejection names its feed and what was expected, because that message is the
entire interface for somebody with a text editor and nothing else.

**`LedgerMigrationTests` is the only suite that starts from a database that already exists**, which
is the only interesting case: every other one opens an empty file, and an empty file cannot be
migrated wrong. It builds the old database by hand rather than through `Ledger`, because a fixture
written through the code under test agrees with it by construction.

**`GlobalizationTests`** pins dates and prices across `de-DE`, `es-ES` and `fa-IR`, and fails under
`-p:InvariantGlobalization=true`. That flag must stay false — see
[globalization](architecture.md#globalization-is-on-and-every-value-is-invariant).

**`SettingsFileTests`** pins that a `logging` level in the settings file actually reaches the
logger, because "turn the log up" is the first instruction anybody debugging a quiet channel is
given.

**`LedgerConcurrencyTests`** reproduces the failure that the single-connection gate exists to
prevent; **`CadenceTests`** pins the shaved windows; **`GatewayFailureTests`** pins both directions
of the 401 check, since being trigger-happy there would mean quitting over an outage Discord
recovers from in a minute.

## CI

`.github/workflows/ci.yml` runs `build`, `test` and `format`, plus `scripts/lint.sh`, `docker
build`, and the test suite a second time under a comma-decimal locale. Everything in it is a command
that runs locally, so `scripts/build.sh && scripts/test.sh && scripts/format.sh --verify-no-changes
&& scripts/lint.sh` passing means CI passes.

`--check` is the one command CI does not run: it is the only one that reaches the network, and a
feed having a quiet afternoon is not a broken commit.

The locale run is not ceremony. Merchant runs with globalization on, posts USD prices and parses
English feed dates, so anything formatted or parsed against the host's culture is a bug that only
appears on a machine set to another language: an embed reading `$3,49`, a review count as `7.716`,
or a feed whose dates the parser refuses.

## Versioning and releases

The version is `<Version>` in `Directory.Build.props`, and that is the only place it is written.
`merchant --version` reports it, the outbound user agent carries it, and a build made from a
checkout appends the commit it came from:

```
$ merchant --version
merchant 1.0.0+a1b2c3d4e5f6…
```

Semantic versioning, read from the point of view of somebody running it rather than somebody
calling it: the patch digit is a fix nothing has to be done about, the minor digit is a feed, a
command or a setting worth knowing about, and the major digit is a thing that has to be done by
hand on the way up — a setting whose meaning changes, a removed command, a ledger that cannot be
read by the version before it.

Cutting one is four steps and a push:

```bash
# 1. Bump <Version> in Directory.Build.props.
# 2. Write the section for it in CHANGELOG.md, headed "## 1.2.0 — YYYY-MM-DD".
git commit -am "Release 1.2.0"
git tag -a v1.2.0 -m "merchant v1.2.0"
git push --follow-tags
```

The pushed tag is the release. `.github/workflows/release.yml` refuses the build outright if the
tag and `<Version>` disagree, or if the CHANGELOG has no section named after it — a release that
lies about its own version, or that arrives with nothing said about it, is worse than a late one.
What it then does:

- Runs the tests.
- Builds the image for amd64 and arm64 and pushes it to `ghcr.io/thekrystalship/merchant`, tagged
  with the version and with `latest`.
- Creates the GitHub release, with the CHANGELOG section as its notes.

Nothing is published by pushing to `main`, and nothing needs publishing by hand.

## Running against a real server

```bash
MERCHANT_TOKEN=… MERCHANT_DEV_GUILD=… dotnet run --project src/Merchant
```

Commands registered to a guild appear immediately, where global ones take up to an hour — which
makes iterating on a command signature unbearable otherwise.

## Adding a feed

Edit the settings file and restart. No code, no rebuild, no command re-registration. See
[Feeds](feeds.md).

The schema is written down in **three places, each for a different reader**, and a new field or a
new value for an existing one belongs in all three:

1. `src/Merchant/Feeds/Schema.cs` — for the code. Every key, bound and default is a constant here,
   and the reader, the validation and the error messages all name their fields from it.
2. The comment header of `deploy/appsettings.example.jsonc` — for whoever has the file open.
3. [`docs/feeds.md`](feeds.md) — for whoever has not opened it yet.

## Adding a kind of source

A class in `src/Merchant/Feeds/Factories` implementing `ISourceFactory`, and an
`AddSingleton<ISourceFactory, …>()` in `Program.cs`. There is no switch anywhere to keep in step.

The factory owns both what its options mean and what a bad value looks like. It returns an
`ISourceBlueprint` — a source definition that has already been validated — so everything that can
be wrong about a feed has been said out loud at startup and nothing can fail for the first time
during a sweep. Bind options into a typed record (`RssOptions`, `CheapSharkOptions`), and make
anything with a closed set of values an enum — `CheapSharkSort` rather than a string — so the
accepted values are visible in code and a misspelling is caught at startup rather than silently
reordering a feed.

Resist a generic JSON-mapping driver until something actually needs it: it trades a rebuild for a
mini-language living in a settings file.

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

Three rules, all of them about databases that already exist and hold somebody's subscriptions:

- **Never edit or renumber an entry that has shipped.** Every database already carries its effects
  and will never run it again, so an edit only changes what a *fresh* install gets — which is how
  two installs of the same version end up with different schemas.
- **Every column added to an existing table needs a default**, or the migration fails on the rows
  that are already there.
- **Write it so it survives running against a database that has partly seen it.** The transaction
  makes a whole migration atomic, so this mostly means not depending on state a previous migration
  left in flight.

Then add a case to `LedgerMigrationTests`: build the old shape by hand, put a row in it, open the
`Ledger`, and assert the row is still there and reads correctly. **Data surviving is the assertion
that matters** — a migration that runs cleanly and empties a table is the failure worth catching.

Nothing needs to be run by hand on a deploy, and no operator step exists to forget. The one thing
merchant will not do is go backwards: a file stamped past `SchemaVersion` is refused by name at
startup.
