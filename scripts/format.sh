#!/usr/bin/env bash
# dotnet format: the style in .editorconfig, applied. CI runs the checking half of it:
#   scripts/format.sh --verify-no-changes
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."
exec dotnet format "$@"
