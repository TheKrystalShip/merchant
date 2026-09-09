# Changelog

Every released version, newest first, and what changed in it. The version is `<Version>` in
`Directory.Build.props`; `merchant --version` reports the one a running build was compiled with.

## 1.0.0 — 2026-09-09

The first release.

### What it does

- Announces game deals in Discord channels from five feeds: Steam's weekly top sellers, two
  CheapShark queries, an RSS merge of Rock Paper Shotgun, PC Gamer and r/GameDeals, and
  IsThereAnyDeal's giveaways.
- Six commands under `/merchant` — `add`, `list`, `remove`, `preview`, `region` and `help` — every
  reply ephemeral, and `add` checks it can post in the channel before it writes anything.
- Per-subscription cadences: live as items appear, or one digest a day or a week, on one shared
  fetch interval.
- A per-server region and currency, filled into feed URLs that accept one.

### How it runs

- A `systemd --user` unit, installed by `deploy/install.sh` and removed by `deploy/uninstall.sh`.
- A container image at `ghcr.io/thekrystalship/merchant`, for amd64 and arm64, holding its settings
  file and its ledger on one volume.
- `merchant --check [COUNTRY]` validates the settings file and fetches every feed without a token
  and without touching Discord.

### What it holds

- The feed catalog is the settings file, so adding, changing or removing a feed is an edit and a
  restart rather than a build.
- One SQLite ledger holds the subscriptions and what each channel has been shown. Its schema is
  brought up on start, inside a transaction, and a file written by a newer merchant is refused by
  name rather than opened.
