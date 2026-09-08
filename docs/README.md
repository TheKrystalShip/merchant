# Merchant documentation

Everything about merchant that is longer than a paragraph lives here. The
[repository README](../README.md) is the introduction; these are the manuals.

## Using it

| Document                            | For                                                                  |
| ----------------------------------- | -------------------------------------------------------------------- |
| [Setting it up](setup.md)           | From nothing to a channel that posts: Discord application, invite, install, first command. |
| [Commands](commands.md)             | The six slash commands, and how live channels and digests differ.     |
| [Feeds](feeds.md)                   | The catalog: what ships, how to add, change or park a feed, and the source types. |
| [Configuration](configuration.md)   | The settings file, the environment variables, the token, and where every file lives. |

## Running it

| Document                        | For                                                                      |
| ------------------------------- | ------------------------------------------------------------------------ |
| [Running it](operations.md)     | `--check`, why a channel went quiet, the ledger, backups and upgrades.   |
| [Deployment](deployment.md)     | The systemd unit and the container, in detail, and what to back up.      |

## Working on it

| Document                          | For                                                                    |
| --------------------------------- | ---------------------------------------------------------------------- |
| [Development](development.md)     | Build, test, format, the style rules, CI, adding a source, changing the schema. |
| [Architecture](architecture.md)   | What the pieces are and which parts of the arrangement are load-bearing. |

`CLAUDE.md` at the repository root is the same ground for an agent working in the tree: a short
orientation that points back here.

## Where else the schema is written down

The settings schema is stated in three places, each for a different reader, and a change belongs in
all three:

1. `src/Merchant/Feeds/Schema.cs` — the code.
2. The comment header of `deploy/appsettings.example.jsonc` — whoever has the file open.
3. [`feeds.md`](feeds.md) and [`configuration.md`](configuration.md) — whoever has not opened it yet.
