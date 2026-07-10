#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
if command -v godot >/dev/null 2>&1; then
  exec godot --path .
elif command -v godot4 >/dev/null 2>&1; then
  exec godot4 --path .
else
  echo "Godot 4.7 .NET is required: https://godotengine.org/download/" >&2
  exit 1
fi
