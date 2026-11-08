#!/bin/bash
# Builds the self-contained package and packs it as ../release/DashX360-Linux-x64.tar.gz for GitHub Releases.
set -e
cd "$(dirname "$0")/.."
./tools/build-release.sh
ROOT="$(cd .. && pwd)"
STAGE="$ROOT/release/DashX360-Linux"
rm -rf "$ROOT/release"
mkdir -p "$STAGE"
cp -a "$ROOT/dist/DashX360" "$ROOT/dist/install.sh" "$ROOT/dist/uninstall.sh" "$ROOT/dist/LEIA-ME.md" "$STAGE/"
tar -C "$ROOT/release" -czf "$ROOT/release/DashX360-Linux-x64.tar.gz" DashX360-Linux
(cd "$ROOT/release" && sha256sum DashX360-Linux-x64.tar.gz > DashX360-Linux-x64.tar.gz.sha256)
rm -rf "$STAGE"
ls -lh "$ROOT/release"
