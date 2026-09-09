# Commands

Everything is under `/merchant`, and **every reply is ephemeral** — only the person who ran it sees
the answer, so setting up announcements never itself posts in the server.

The commands default to **Manage Server**. Change that in _Server Settings → Integrations →
Merchant_ if a different role should be allowed to wire channels up.

| Command             | What it does                                                            |
| ------------------- | ----------------------------------------------------------------------- |
| `/merchant add`     | Point a feed at a channel. Optionally set how often, and a role to ping. |
| `/merchant list`    | What is posting where, and how much is waiting.                        |
| `/merchant remove`  | Stop one feed, by the number `/merchant list` shows.                    |
| `/merchant preview` | See what a feed would post, before wiring it up. Only you see it.       |
| `/merchant region`  | Set the country and currency used for prices. Two letters and three.    |
| `/merchant help`    | The catalog, with a suggested channel name for each feed.               |

## add

```
/merchant add  feed: Games Under $10  channel: #bargain-bin  [how-often: …]  [ping: @Deals]
```

The **feed** field is autocompleted from the catalog and answers on the empty keystroke, so the list
appears as soon as the field is focused. It accepts free text too — an unknown name is answered by
naming what does exist, rather than by saving a subscription to nothing.

**how-often** defaults to _Recommended for this feed_ — the cadence the catalog gives it, which is
almost always the right answer. The alternatives are _Every item, as it appears_, _One post a day_
and _One post a week_. **ping** names a role to mention on each post from that subscription.

Before it writes anything, `add` checks that merchant can View Channel, Send Messages and Embed
Links in the target channel, and names the missing one if it cannot. A feed bot that silently fails
on a channel permission is the single most common way this kind of setup goes wrong, and the person
hitting it usually has no access to the logs.

Re-running `add` on a channel that already has that feed re-checks the permissions, which makes it
the fix for a channel that went quiet after somebody edited a role.

## list

Every subscription in the server, numbered, with its channel, cadence and how many items are
waiting to be posted. The waiting count is what separates "nothing new upstream" from "posting is
stuck" — see [when a channel goes quiet](operations.md#when-a-channel-goes-quiet).

## remove

```
/merchant remove  id: 2
```

The id is the number `/merchant list` shows. Removing a subscription stops the posting and forgets
nothing else: adding it back does not repost the backlog.

## preview

Fetches a feed now and shows what it would post, to you alone. Nothing is saved and no channel is
touched, so it is the safe way to look at a feed before committing a channel to it.

## region

```
/merchant region  country: ES  currency: EUR
```

Two letters and three, per server. The country is substituted into feed URLs that carry a
`{region}` placeholder — giveaway feeds vary by storefront region — so it changes what those feeds
return.

**Prices stay in USD.** CheapShark, which is where the priced feeds come from, quotes USD whatever
region is set, and merchant says so rather than converting and being subtly wrong. `{currency}` is
filled in for a future source that prices in a server's own money; no feed that ships uses it.

Anything that is not two letters, or not three, is refused by name rather than fetched — a bad code
would otherwise build a request for a page that does not exist and leave the channel quiet with no
error anywhere.

## help

The whole catalog: every feed, its description, and a suggested channel name. This is generated
from the settings file, so it always matches what `add` will accept.

A very long catalog is shortened rather than dropped — Discord refuses an embed past 6000
characters, so `help` fits what it can and says how many it left out.

## Live and digest

An ordinary RSS bot can only post on discovery, which makes "weekly" impossible. Merchant separates
the two: **every feed is fetched on one interval**, and **each channel posts on its own clock** from
what the sweep filed.

| Cadence  | The channel gets                                                    |
| -------- | ------------------------------------------------------------------- |
| `Live`   | Each item as it appears, one embed each, capped per sweep.          |
| `Daily`  | One digest listing everything found since the last one.             |
| `Weekly` | The same, once a week.                                              |

So a live channel gets each giveaway as it appears, and a weekly channel gets one digest listing
the week — from the same machinery, and without either one holding the other up.

The split also holds on the failing path: a sweep that fetches nothing still posts. A daily channel
whose window opens on the half hour its upstream happens to be down is owed the backlog the *last*
sweep filed.

Cadence windows are shaved slightly below their nominal period — 23 hours, 6.9 days — so a sweep
landing a few minutes late cannot walk the daily post around the clock, and a weekly post cannot
drift past Steam's Tuesday chart and become fortnightly.

## The first sweep

After `/merchant add`, the first sweep posts a handful straight away and files the rest of the
backlog as already seen. A new channel proves it works immediately, without a month of history
landing in it.
