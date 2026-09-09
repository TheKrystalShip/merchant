# Deployment

Two supported ways to run merchant, both installing the same build. The systemd path is for the
host you control; the container is the portable half — same code, token passed at run time, ledger
on a volume.

Neither bakes in a runtime identifier. A framework-dependent publish runs on whatever architecture
the host is, so the same command installs on an arm64 box as on an x86-64 one. The floor that does
matter is the SDK version in `global.json`, where an old one is refused in a sentence rather than a
resolver error.

## systemd

```bash
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
step to remember.

## Container

```bash
docker build -t merchant .
docker run -d --name merchant \
  -e MERCHANT_TOKEN=... \
  -v merchant-data:/data \
  merchant
```

The image needs nothing installed on the host: it builds and runs inside. It runs unprivileged as
uid 10001, and `/data` is the volume holding both state and configuration:

- `MERCHANT_DB=/data/merchant.db` — the ledger. Mount this to keep it across upgrades.
- `MERCHANT_CONFIG=/data/appsettings.json` — the catalog. Nothing is baked into the image: on a
  first run merchant writes the shipped example here, so an empty volume still produces a working
  bot and leaves behind the file to edit.

**No token is ever baked in.** Pass it at run time.

Upgrading is `docker build` and a replaced container; the volume carries the feeds and the ledger
across, and the schema comes up on its own.

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
