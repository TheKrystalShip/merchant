#!/usr/bin/env bash
# The two rules no compiler reports: the line length .editorconfig states, and shellcheck over the
# shell scripts. Everything else in the house style is an analyzer, and `dotnet build` fails on it.
# CI runs this file rather than a copy of it, so a green run here is a green run there.
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."

status=0

# Read from .editorconfig rather than repeated here: [*.cs] sets the limit and [tests/**.cs] turns
# it off, so one number in one place cannot drift from another written somewhere else.
limit="$(awk '/^\[/ { section = $0 }
              section == "[*.cs]" && /^max_line_length/ { print $3; exit }' .editorconfig)"

if [[ -z "$limit" ]]; then
  echo "No max_line_length under [*.cs] in .editorconfig, so there is nothing to check." >&2
  exit 1
fi

over="$(find src -name '*.cs' -not -path '*/obj/*' -not -path '*/bin/*' -print0 |
  xargs -0 awk -v limit="$limit" \
    'length > limit { printf "%s:%d is %d characters\n", FILENAME, FNR, length }')"

if [[ -n "$over" ]]; then
  echo "Longer than the ${limit} characters .editorconfig allows:"
  echo "$over"
  status=1
fi

if command -v shellcheck >/dev/null 2>&1; then
  shellcheck deploy/*.sh scripts/*.sh || status=1
else
  echo "shellcheck is not installed here, so the shell scripts went unchecked; CI checks them." >&2
fi

exit "$status"
