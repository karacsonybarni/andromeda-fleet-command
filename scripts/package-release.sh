#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
windows="dist/windows/AndromedaFleetCommand.exe"
linux="dist/linux/AndromedaFleetCommand.x86_64"
for artifact in "$windows" "$linux"; do
  if [ ! -s "$artifact" ]; then
    echo "Missing export: $artifact" >&2
    exit 1
  fi
done
mkdir -p dist/packages
cp README.md LICENSE dist/windows/
cp README.md LICENSE dist/linux/
(cd dist/windows && zip -q -r ../packages/AndromedaFleetCommand-Windows-x64.zip .)
(cd dist/linux && chmod +x AndromedaFleetCommand.x86_64 && tar -czf ../packages/AndromedaFleetCommand-Linux-x64.tar.gz .)
sha256sum dist/packages/* > dist/packages/SHA256SUMS.txt
echo "Packaged desktop demo artifacts:"
cat dist/packages/SHA256SUMS.txt
