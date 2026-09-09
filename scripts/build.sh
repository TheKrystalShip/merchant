#!/usr/bin/env bash
# dotnet build. The analyzers and the style rules in .editorconfig run here, as errors, so a
# violation fails this rather than review. Anything after the script name goes to dotnet:
#   scripts/build.sh -c Release
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."
exec dotnet build "$@"
