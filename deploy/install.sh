#!/usr/bin/env bash
# Publishes merchant into ~/.local/share/merchant and installs the user unit.
# Does not touch the token: write ~/.config/merchant/merchant.env yourself first.
#
# Needs the .NET SDK to run, and leaves behind an install that needs only the .NET runtime.
set -euo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
prefix="${HOME}/.local/share/merchant"
state="${HOME}/.local/state/merchant"
config="${HOME}/.config/merchant"
bin="${HOME}/.local/bin"

if ! command -v dotnet >/dev/null 2>&1; then
  echo "dotnet is not on PATH. Install the .NET SDK first: https://dotnet.microsoft.com/download" >&2
  exit 1
fi

mkdir -p "$prefix" "$state" "$config" "$bin" "${HOME}/.config/systemd/user"

if [[ ! -f "${config}/merchant.env" ]]; then
  install -m 600 "${repo}/deploy/merchant.env.example" "${config}/merchant.env"
  echo "Wrote ${config}/merchant.env — put the bot token in it before starting." >&2
fi

# The feed catalog. merchant would seed this itself on a first run, but the unit runs with the home
# directory read-only, and seeding here means the feeds can be edited before it ever starts.
if [[ ! -f "${config}/appsettings.json" ]]; then
  install -m 644 "${repo}/deploy/appsettings.example.jsonc" "${config}/appsettings.json"
  echo "Wrote ${config}/appsettings.json — the feeds live there; edit and restart to change them." >&2
fi

# No runtime identifier: a framework-dependent publish is architecture-neutral, so the same command
# installs on an arm64 box as on an x86-64 one.
dotnet publish "${repo}/src/Merchant/Merchant.csproj" -c Release -o "$prefix"

# --check is the first thing to run when a channel goes quiet, and it is worth being able to type.
ln -sf "${prefix}/merchant" "${bin}/merchant"

install -m 644 "${repo}/deploy/merchant.service" "${HOME}/.config/systemd/user/merchant.service"
systemctl --user daemon-reload

echo
echo "Installed. Start it with: systemctl --user enable --now merchant"
echo "Feeds:     ${config}/appsettings.json"
echo "Ledger:    ${state}/merchant.db"
echo "Check the feeds without starting it: merchant --check"

case ":${PATH}:" in
  *":${bin}:"*) ;;
  *) echo "Note: ${bin} is not on your PATH, so run ${prefix}/merchant --check instead." >&2 ;;
esac
