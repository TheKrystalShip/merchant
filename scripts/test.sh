#!/usr/bin/env bash
# dotnet test. Nothing here reaches the network: every feed in the tests is a captured file.
# The comma-decimal run CI does second is this with a locale in front of it:
#   LC_ALL=de_DE.UTF-8 scripts/test.sh
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."
exec dotnet test "$@"
