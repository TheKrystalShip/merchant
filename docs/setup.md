# Setting it up

From nothing to a channel that posts. Fifteen minutes, most of it Discord's own web UI.

There are three parts, and only the first cannot be undone from a chat window: a Discord
application, merchant running somewhere, and one slash command per channel.

## 1. The Discord application

<https://discord.com/developers/applications> → **New Application** → name it → **Bot** →
**Reset Token**. Copy the token somewhere; Discord shows it once.

Merchant requests **no privileged intents**, so nothing on that page needs enabling — it reads no
messages and no member lists, which is also why the application never needs intent review.

Invite it with this URL, substituting the application id from the **General Information** page:

```
https://discord.com/api/oauth2/authorize?client_id=YOUR_APP_ID&permissions=19456&scope=bot%20applications.commands
```

`19456` is View Channels + Send Messages + Embed Links, and nothing else. If you would rather not
grant it server-wide, grant nothing here and give merchant those three permissions on the specific
channels it should post in — `/merchant add` checks them and will tell you what is missing.

## 2. Merchant itself

Two supported ways, and they install the same build. Pick one; the [deployment
guide](deployment.md) has the detail behind both.

**On a host with systemd** — needs the .NET SDK to build. `dotnet --version` should report 10.0.100
or newer; the exact floor is in `global.json`, and an older SDK says so rather than failing
obscurely.

```bash
deploy/install.sh                            # publishes, installs the unit, links merchant onto PATH
$EDITOR ~/.config/merchant/merchant.env      # put the token in
systemctl --user enable --now merchant
journalctl --user -u merchant -f
```

**As a container** — needs nothing installed, because it builds and runs inside the image.

```bash
docker build -t merchant .
docker run -d --name merchant \
  -e MERCHANT_TOKEN=... \
  -v merchant-data:/data \
  merchant
```

Either way the token is passed in the environment and never written into the settings file. See
[Configuration](configuration.md) for where everything lands on disk.

## 3. One command per channel

```
/merchant add  feed: Games Under $10  channel: #bargain-bin
```

That is the entire setup. The feed field autocompletes from the catalog the moment it is focused,
so nobody has to know what a feed is called, and `add` verifies merchant can actually post in that
channel **before** it saves anything.

The [command reference](commands.md) covers the other five, and [Feeds](feeds.md) covers changing
what is in the menu.

## What the first hour looks like

The first sweep after `/merchant add` posts a handful of items straight away and files the rest of
the backlog as already seen. That is deliberate: a channel that stays empty for a day reads as a
broken bot, and so does one that receives a month of history.

After that, each channel posts on its own cadence — live as items appear, or one digest a day or a
week. See [Live and digest](commands.md#live-and-digest).

Commands registered globally take up to an hour to appear in Discord's menu. If they are missing,
that is usually all it is; while developing, `MERCHANT_DEV_GUILD` registers them to one server
instantly ([Development](development.md#running-against-a-real-server)).

## If it does not work

Run `merchant --check` first — it validates the settings file and fetches every feed, needs no
token, and touches no Discord. [Operations](operations.md) has the rest of the ladder.
