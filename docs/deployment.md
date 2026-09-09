# Deployment

Three supported ways to run merchant, all of them the same code. A **downloaded build** is the
shortest — one executable with the runtime inside it, and nothing else to install. The **systemd**
path builds from the checkout and is for a host you control. The **container** is the portable
half — token passed at run time, ledger on a volume.

Only the downloaded build names an architecture, and it names it in the file you pick. Building
from the checkout does not: a framework-dependent publish runs on whatever the host is, so the same
command installs on an arm64 box as on an x86-64 one. The floor that does matter there is the SDK
version in `global.json`, where an old one is refused in a sentence rather than a resolver error.

## A downloaded build

Every release carries one archive per platform, attached to it on [the releases
page](https://github.com/TheKrystalShip/merchant/releases). The .NET runtime is inside the
executable, so the machine needs no SDK, no runtime and no checkout.

| Archive                            | For                                    |
| ---------------------------------- | -------------------------------------- |
| `merchant-<version>-linux-x64`     | Most Linux hosts and VPSes.            |
| `merchant-<version>-linux-arm64`   | A Raspberry Pi, or an arm64 server.    |
| `merchant-<version>-osx-arm64`     | Apple Silicon Macs.                    |
| `merchant-<version>-osx-x64`       | Intel Macs.                            |
| `merchant-<version>-win-x64`       | Windows. This one is a `.zip`.         |

```bash
tar xzf merchant-<version>-linux-x64.tar.gz
cd merchant-<version>-linux-x64
MERCHANT_TOKEN=... ./merchant
```

Each archive holds the executable and the two example files that are the whole interface —
`appsettings.example.jsonc` and `merchant.env.example`. On a first run merchant copies the settings
example into its config directory and reads that copy from then on, so a fresh download already
knows about five feeds; edit that copy and restart to change them. `./merchant --check` fetches
every feed without a token and without touching Discord, and is the right thing to run first.

`SHA256SUMS` is attached beside the archives:

```bash
sha256sum -c SHA256SUMS --ignore-missing
```

Two things the download does not carry. **Linux still needs ICU** — `libicu` on Debian or Ubuntu,
`icu` on Arch — because merchant reads each guild's locale and [cannot run under invariant
globalization](architecture.md#globalization-is-on-and-every-value-is-invariant). Most desktop and server images already have it. **The macOS builds are not
signed or notarized**, being cross-built on Linux, so Gatekeeper refuses one until it is told
otherwise: `xattr -d com.apple.quarantine ./merchant`.

To run it as a service, take the unit from the systemd section below and point `ExecStart` at
wherever the executable was unpacked.

## systemd

Needs the checkout and the .NET SDK; the install it leaves behind needs only the runtime.

```bash
git clone https://github.com/TheKrystalShip/merchant.git
cd merchant
deploy/install.sh
$EDITOR ~/.config/merchant/merchant.env      # put the token in
systemctl --user enable --now merchant
journalctl --user -u merchant -f
```

`deploy/install.sh` publishes to `~/.local/share/merchant`, links the binary into `~/.local/bin` so
`merchant --check` is typeable, and installs the user unit. It needs the .NET SDK; the install it
leaves behind needs only the runtime.

It also seeds two files and **overwrites neither**:

| File                                  | Mode | What it is                            |
| ------------------------------------- | ---- | ------------------------------------- |
| `~/.config/merchant/merchant.env`     | 0600 | The token, and nothing else.          |
| `~/.config/merchant/appsettings.json` | 0644 | The feed catalog and every setting.   |

That matters because install.sh republishes over the whole install directory: anything editable has
to live outside it. It is why an upgrade is safe to re-run.

The unit seeds the settings file rather than leaving merchant to do it because it runs with
`ProtectHome=read-only` — the service can read that file but not write it. The container can, and
does.

### What the unit does

`deploy/merchant.service` runs as a **user** unit, restarts on failure after 15s, and logs to the
journal under `merchant`. It is hardened: `NoNewPrivileges`, `PrivateTmp`, `ProtectSystem=strict`,
`ProtectHome=read-only`, and `ReadWritePaths` granting exactly one directory —
`~/.local/state/merchant`, where the ledger lives.

`MERCHANT_DB` is written out in the unit even though it names the path merchant would resolve on its
own, because `ReadWritePaths` has to grant exactly one directory and a unit that grants a path it
does not name is a unit nobody can check.

If you move the ledger with `databasePath` or `MERCHANT_DB`, **add the new directory to
`ReadWritePaths`** or the service cannot open it.

### Upgrading

```bash
git pull && deploy/install.sh && systemctl --user restart merchant
```

The schema comes up on its own, the settings file and the ledger are untouched, and there is no
step to remember. `merchant --version` says which build is installed.

### Removing it

```bash
deploy/uninstall.sh            # stops the unit, removes the install, the symlink and the unit file
deploy/uninstall.sh --purge    # and the token, the settings file and the ledger
```

Without `--purge` the token, the settings file and the ledger stay where they are, so re-installing
lands on the same subscriptions. With it, the subscriptions go with the ledger.

## Container

The image is published for amd64 and arm64, so a host needs nothing but Docker:

```bash
docker run -d --name merchant \
  --restart unless-stopped \
  -e MERCHANT_TOKEN=... \
  -v merchant-data:/data \
  ghcr.io/thekrystalship/merchant:latest
```

`--restart unless-stopped` is not optional in practice: without it the bot is up until the first
reboot of the host and then quietly is not.

[`compose.yaml`](../compose.yaml) is the same thing with the token in a file rather than in shell
history, and it is the whole of what a host needs — the image is published, so nothing is cloned:

```bash
curl -O https://raw.githubusercontent.com/TheKrystalShip/merchant/main/compose.yaml
printf 'MERCHANT_TOKEN=%s\n' "the-bot-token" > merchant.env && chmod 600 merchant.env
docker compose up -d
```

Pin a version rather than `latest` on anything worth keeping steady:
`ghcr.io/thekrystalship/merchant:1.0.0`. The tags are the released versions, and
[the CHANGELOG](../CHANGELOG.md) says what is in each.

To run the checkout instead — a change being tested, or a fork — `docker build -t merchant .`
builds the same image locally, and `compose.yaml` carries the `build:` line to uncomment.

It runs unprivileged as uid 10001, and `/data` is the volume holding both state and configuration:

- `MERCHANT_DB=/data/merchant.db` — the ledger. Mount this to keep it across upgrades.
- `MERCHANT_CONFIG=/data/appsettings.json` — the catalog. Nothing is baked into the image: on a
  first run merchant writes the shipped example here, so an empty volume still produces a working
  bot and leaves behind the file to edit.

**No token is ever baked in.** Pass it at run time.

Upgrading:

```bash
docker compose pull && docker compose up -d      # or: docker pull … && docker rm -f merchant && …
```

The volume carries the feeds and the ledger across, and the schema comes up on its own.

```bash
docker exec merchant dotnet /app/merchant.dll --check
```

### The one ordering that matters in the Dockerfile

The account and the `chown` of `/data` come **before** `VOLUME /data`. A build step that touches a
path already declared as a volume is discarded by the classic builder, which would leave `/data`
owned by root while merchant runs unprivileged and cannot open its own ledger or seed its own
settings file.

BuildKit keeps the `chown`, so the wrong order builds and passes CI and fails only where the
classic builder runs it. Do not reorder it.

## What to back up

The ledger, and only if you would rather not re-run `/merchant add`. It holds the subscriptions and
what each channel has already been shown; nothing else in merchant is state. See [the
ledger](operations.md#the-ledger).
