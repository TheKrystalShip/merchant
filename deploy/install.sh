#!/usr/bin/env bash
# Publishes merchant into ~/.local/share/merchant and installs the user unit.
# Does not touch the token: write ~/.config/merchant/merchant.env yourself first.
set -euo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
prefix="${HOME}/.local/share/merchant"
state="${HOME}/.local/state/merchant"
config="${HOME}/.config/merchant"

mkdir -p "$prefix" "$state" "$config" "${HOME}/.config/systemd/user"

if [[ ! -f "${config}/merchant.env" ]]; then
  install -m 600 "${repo}/deploy/merchant.env.example" "${config}/merchant.env"
  echo "Wrote ${config}/merchant.env — put the bot token in it before starting." >&2
fi

dotnet publish "${repo}/src/Merchant/Merchant.csproj" \
  -c Release -r linux-x64 --self-contained false -o "$prefix"

install -m 644 "${repo}/deploy/merchant.service" "${HOME}/.config/systemd/user/merchant.service"
systemctl --user daemon-reload

echo "Installed. Start it with: systemctl --user enable --now merchant"
echo "Check the feeds without starting it: ${prefix}/merchant --check"
