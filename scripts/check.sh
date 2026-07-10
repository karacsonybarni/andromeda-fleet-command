#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet run --project tests/AndromedaFleetCommand.Core.Tests
dotnet build AndromedaFleetCommand.Game.csproj --configuration Release
if command -v godot >/dev/null 2>&1; then
  godot --headless --path . --editor --quit
elif command -v godot4 >/dev/null 2>&1; then
  godot4 --headless --path . --editor --quit
else
  echo "Core passed; skipped Godot import because Godot 4.7 .NET is unavailable." >&2
fi
