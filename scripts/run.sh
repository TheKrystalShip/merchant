#!/usr/bin/env bash
# Runs merchant out of the checkout. Everything after the script name goes to merchant itself:
#   scripts/run.sh --check        validate the settings file and fetch every feed; needs no token
#   scripts/run.sh --check ES     …as another region would see it
#   scripts/run.sh                the bot proper, which needs MERCHANT_TOKEN in the environment
#
# Set MERCHANT_DEV_GUILD alongside the token when working on a command: commands registered to one
# guild appear immediately, where global ones take up to an hour.
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."
exec dotnet run --project src/Merchant -- "$@"
