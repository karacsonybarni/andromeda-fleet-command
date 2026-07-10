#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
if [ "$#" -ne 1 ]; then
  echo "Usage: $0 windows|linux" >&2
  exit 2
fi
target="$1"
case "$target" in
  windows) preset="Windows Desktop" ;;
  linux) preset="Linux" ;;
  *) echo "Usage: $0 windows|linux" >&2; exit 2 ;;
esac
if command -v godot >/dev/null 2>&1; then
  godot --headless --path . --export-release "$preset"
else
  godot4 --headless --path . --export-release "$preset"
fi
