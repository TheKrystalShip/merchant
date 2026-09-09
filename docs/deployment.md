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

Each archive holds the executable and the example files that are the whole interface —
`appsettings.example.jsonc` and `merchant.env.example`, plus `merchant.service` on the Linux
builds. On a first run merchant copies the settings example into its config directory and reads
that copy from then on, so a fresh download already knows about five feeds; edit that copy and
restart to change them. `./merchant --check` fetches every feed without a token and without
touching Discord, and is the right thing to run first.

The settings example has to stay beside the executable — that is where merchant looks for the file
it seeds from — so move the two together, never the binary alone.

`SHA256SUMS` is attached beside the archives:

```bash
sha256sum -c SHA256SUMS --ignore-missing
```

Two things the download does not carry. **Linux still needs ICU** — `libicu` on Debian or Ubuntu,
`icu` on Arch — because merchant reads each guild's locale and [cannot run under invariant
globalization](architecture.md#globalization-is-on-and-every-value-is-invariant). Most desktop and server images already have it. **The macOS builds are not
signed or notarized**, being cross-built on Linux, so Gatekeeper refuses one until it is told
otherwise: `xattr -d com.apple.quarantine ./merchant`.

### Running a downloaded build as a service

`merchant.service` in the archive is the same unit the checkout installs, and it expects the
executable at `~/.local/bin/merchant`. Put it there and it needs no editing:

```bash
mkdir -p ~/.local/share/merchant ~/.local/bin
cp merchant appsettings.example.jsonc ~/.local/share/merchant/
ln -sf ~/.local/share/merchant/merchant ~/.local/bin/merchant

install -Dm644 merchant.service ~/.config/systemd/user/merchant.service
systemctl --user daemon-reload

install -Dm600 merchant.env.example ~/.config/merchant/merchant.env
$EDITOR ~/.config/merchant/merchant.env      # put the token in
systemctl --user enable --now merchant
```

Nothing else has to be created first: the unit makes its own configuration, state and cache
directories, and merchant writes the settings file into the first of them on its first start.

`systemctl --user enable --now` starts the service at login rather than at boot. For a bot on a
headless host, `loginctl enable-linger $USER` is what keeps it running between logins, and it is
needed for the checkout install just the same.

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

Seeding them here rather than leaving merchant to do it on its first start means the token and the
feeds can be edited before anything has been announced.

### What the unit does

`deploy/merchant.service` runs as a **user** unit, starts `~/.local/bin/merchant`, restarts on
failure after 15s, and logs to the journal under `merchant`. It is hardened: `NoNewPrivileges`,
`PrivateTmp`, `ProtectSystem=strict` and `ProtectHome=read-only` among them, which between them
leave nothing on the host writable that the unit has not asked for.

It uses three directories and **creates all three itself**, with one `mkdir` before it starts, so
there is nothing to make by hand before a first start:

| Directory                 | What is in it                     |
| ------------------------- | --------------------------------- |
| `~/.config/merchant`      | The token and the settings file.  |
| `~/.local/state/merchant` | The ledger.                       |
| `~/.cache/merchant`       | What a single-file build unpacks. |

Those are the paths merchant resolves on its own, and `ReadWritePaths` names all three: it is what
exempts them from `ProtectSystem=` and `ProtectHome=`, and a directory the unit does not name is
one the service cannot write. `MERCHANT_CONFIG` and `MERCHANT_DB` are written out beside them for
the same reason — a unit whose paths have to be inferred is a unit nobody can check — and
`merchant.env` is read after them, so a value set there still wins.

The `mkdir` is an `ExecStartPre` rather than `StateDirectory=` and its siblings, which look like
the setting for exactly this. For a user unit systemd treats `~/.config/<name>` as where state used
to live, so `StateDirectory=merchant` beside an existing `~/.config/merchant` makes
`~/.local/state/merchant` a symlink into it, and the ledger ends up among the settings.

A downloaded build is a single file with the .NET runtime inside it, and it unpacks part of itself
before it can run. Left to choose for itself it picks a directory under the home directory, which
this unit mounts read-only, and the service then dies at exec with a bundle error and no gateway
connection ever attempted. `DOTNET_BUNDLE_EXTRACT_BASE_DIR` points it at the cache directory
instead, which survives restarts so the unpacking happens once. Deleting that directory costs one
slower start and nothing else.

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
deploy/uninstall.sh            # stops the unit, removes the install, the cache, the symlink and the unit
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
