#!/usr/bin/env bash
# Undoes deploy/install.sh: stops the unit, and removes the published install, whatever a
# single-file build unpacked into the cache directory, the symlink on PATH and the unit file.
# Needs no sudo, because install.sh needed none.
#
# The token, the settings file and the ledger are left alone, so re-installing lands on the same
# subscriptions. --purge deletes those too, and the subscriptions with them.
set -euo pipefail

prefix="${HOME}/.local/share/merchant"
state="${HOME}/.local/state/merchant"
config="${HOME}/.config/merchant"
cache="${HOME}/.cache/merchant"
bin="${HOME}/.local/bin/merchant"
unit="${HOME}/.config/systemd/user/merchant.service"

purge=false

case "${1-}" in
  --purge) purge=true ;;
  "") ;;
  *)
    echo "deploy/uninstall.sh takes --purge, or nothing at all." >&2
    exit 1
    ;;
esac

if [[ -f "$unit" ]]; then
  systemctl --user disable --now merchant.service >/dev/null 2>&1 || true
  rm -f "$unit"
  systemctl --user daemon-reload
  echo "Stopped merchant and removed its unit."
fi

# Only if it is this install's: a symlink pointing somewhere else belongs to somebody else.
if [[ -L "$bin" && "$(readlink -f "$bin")" == "${prefix}/"* ]]; then
  rm -f "$bin"
fi

rm -rf "$prefix" "$cache"
echo "Removed ${prefix} and ${cache}."

if [[ "$purge" == true ]]; then
  rm -rf "$config" "$state"
  echo "Removed ${config} and ${state}. The subscriptions are gone with the ledger."
else
  echo
  echo "Kept, so a re-install picks up where this left off:"
  echo "  ${config}   the token and the settings file"
  echo "  ${state}    the ledger, holding every subscription"
  echo "Run with --purge to delete those as well."
fi
