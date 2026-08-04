#!/usr/bin/env bash
set -euo pipefail

# Cross-platform build script for SameGuidPathDedup.
# Works on macOS (Intel/ARM), Debian/Ubuntu, Windows (via Git Bash/WSL).

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$HERE/SameGuidPathDedup"

if ! command -v dotnet >/dev/null 2>&1; then
  echo "✗ dotnet SDK not found in PATH."
  echo "  Install: https://dotnet.microsoft.com/download"
  echo "  macOS:   brew install --cask dotnet-sdk"
  exit 1
fi

dotnet --version

echo "→ Restoring packages..."
dotnet restore

echo "→ Building netstandard2.0..."
dotnet build -c Release -p:TargetFrameworks=netstandard2.0 --no-restore

ARTIFACT_DIR="$HERE/artifacts/1.0.0.0"
mkdir -p "$ARTIFACT_DIR"

# Some SDK builds emit directly into bin/Release/, others into bin/Release/netstandard2.0/.
DLL=$(find bin/Release -name 'SameGuidPathDedup.dll' -path '*netstandard2.0*' | head -n 1)
if [[ -z "$DLL" ]]; then
  DLL=$(find bin/Release -name 'SameGuidPathDedup.dll' | head -n 1)
fi

if [[ -z "$DLL" ]]; then
  echo "✗ SameGuidPathDedup.dll not found after build."
  exit 1
fi

cp -f "$DLL" "$ARTIFACT_DIR/SameGuidPathDedup.dll"
cp -f bin/Release/netstandard2.0/SameGuidPathDedup.pdb "$ARTIFACT_DIR/" 2>/dev/null || true

echo ""
echo "✓ Build complete."
echo "  Artifact: $ARTIFACT_DIR/SameGuidPathDedup.dll"
echo "  Size:     $(wc -c < "$ARTIFACT_DIR/SameGuidPathDedup.dll") bytes"