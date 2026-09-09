# Feeds

Everything merchant can post is in `~/.config/merchant/appsettings.json` under `"feeds"`. **Edit it
and restart** — nothing is rebuilt, and the command menu picks the change up on its own, because
the feed field is autocompleted from the catalog rather than registered with Discord.

The copy merchant seeds carries this same reference in its comments, so the file explains itself to
whoever has it open.

## What ships

| Feed                       | Where it comes from                              | Default cadence |
| -------------------------- | ------------------------------------------------ | --------------- |
| **Top Games of the Week**  | Steam's own weekly top-sellers chart             | once a week     |
| **Games Under $10**        | CheapShark, price-capped, best deals first       | once a day      |
| **Best Game Deals**        | CheapShark, 75+ Metacritic, largest discounts    | once a day      |
| **Worth Checking Out**     | Rock Paper Shotgun, PC Gamer, top of r/GameDeals | once a day      |
| **Free Games & Giveaways** | IsThereAnyDeal giveaways                         | as they appear  |

Those five are what merchant ships with, not what it can do. Around five is the right size for the
menu — a curation guideline for whoever edits the file, not something the code enforces.

## An entry

```jsonc
"indie-picks": {                          // the key: lower-case, hyphenated, max 100 chars
  "label": "Indie Picks",
  "description": "Small games worth a look.",
  "cadence": "Daily",
  "colour": "#8B5CF6",
  "source": { "type": "rss", "urls": [ "https://example.test/indies.rss" ] }
}
```

| Key           | Required | Value                   | Default   | Meaning                                     |
| ------------- | -------- | ----------------------- | --------- | ------------------------------------------- |
| `label`       | yes      | string, ≤ 100           | —         | Shown in the menu and on every embed.       |
| `description` | yes      | string, ≤ 400           | —         | One line, in the menu and `/merchant help`. |
| `source`      | yes      | object                  | —         | Where the items come from — see below.      |
| `channel`     | no       | string                  | the key   | Suggested channel name.                     |
| `cadence`     | no       | `Live`/`Daily`/`Weekly` | `Daily`   | How often the channel hears from it.        |
| `colour`      | no       | `"#RRGGBB"`             | `#5865F2` | Embed accent.                               |
| `enabled`     | no       | `true`/`false`          | `true`    | `false` parks a feed without deleting it.   |

Each feed posts in its own colour, so channels read apart at a glance.

**The key is what the ledger stores.** Renaming `indie-picks` orphans the channels already
subscribed to it — they stop posting, and `/merchant list` no longer recognises them. Change the
`label` freely; change the key only for a feed nobody has subscribed to.

The key is taken exactly as the file writes it and is not trimmed: `"free-games "` and
`"free-games"` would otherwise be one feed with the second quietly replacing the first. A stray
space is refused by name, with the name quoted so the space is visible.

## Sources

### `rss`

One or more syndication feeds, merged into one.

| Key    | Required | Value                                                                          |
| ------ | -------- | ------------------------------------------------------------------------------ |
| `urls` | yes      | Array of one or more full http(s) addresses. RSS 1.0, RSS 2.0 and Atom all work without being told which. |

A URL may contain `{region}`, filled in per server from `/merchant region` — giveaway feeds vary by
storefront region. `{currency}` is filled in the same way and no shipped feed needs it: neither
source prices in anything but USD, so it is there for a source that one day does.

```jsonc
"source": {
  "type": "rss",
  "urls": [ "https://isthereanydeal.com/feeds/{region}/giveaways.rss" ]
}
```

### `cheapshark`

A slice of CheapShark's deals API. Structured prices, so these embeds can strike through a list
price. **Always quoted in USD**, whatever the region is set to.

| Key             | Required | Value           | Default      |
| --------------- | -------- | --------------- | ------------ |
| `upperPrice`    | no       | number above 0  | no ceiling   |
| `minMetacritic` | no       | number, 0–100   | no floor     |
| `sortBy`        | no       | `DealRating`, `Title`, `Savings`, `Price`, `Metacritic`, `Reviews`, `Release`, `Store` or `Recent`. Spaces are ignored, so `Deal Rating` reads the same. | `DealRating` |

```jsonc
"source": { "type": "cheapshark", "upperPrice": 10, "sortBy": "Deal Rating" }
```

CheapShark rather than IsThereAnyDeal for the priced feeds, because ITAD encodes its filters into an
opaque token minted by its own web UI — "under ten" cannot be expressed in a URL merchant builds.
ITAD is still right for giveaways, which need no filter.

CheapShark also **requires a descriptive `User-Agent`** or refuses the request outright, and Reddit
throttles a generic one harder. That is the `bot.userAgent` setting; leave it recognisable.

## After an edit

```bash
merchant --check
```

Validates the file, then fetches every feed and reports counts, timings and the first item of each.
Run it before restarting, and an edit that would have taken a channel offline shows up here — named
— instead of as a channel that quietly stops posting.

A feed with a mistake in it is **dropped at startup** with a line naming it and what was expected,
and every other feed keeps running: one typo should not take a server's channels offline. A file
with **no usable feeds at all** stops merchant rather than leaving it idling.

## A new kind of source

A genuinely new *kind* of upstream — neither a syndication feed nor CheapShark — is the one thing
that still needs code: an `ISourceFactory` in `src/Merchant/Feeds/Factories` and a line in
`Program.cs`. Every feed built on a kind that already exists is config alone. See
[Development](development.md#adding-a-kind-of-source).
