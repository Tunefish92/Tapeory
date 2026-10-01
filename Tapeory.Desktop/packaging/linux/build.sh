#!/usr/bin/env bash
# Builds the Linux desktop app into OUT (default: dist):
#   Tapeory-VERSION-linux-x86_64.tar.gz   unpack and run ./tapeory
#   Tapeory-VERSION-linux-x86_64.AppImage one file; updates itself from Settings → About
# Everything it needs is inside: the app and the engine with its own .NET runtime.
#
# Usage: packaging/linux/build.sh VERSION [OUT]
set -euo pipefail

VERSION=${1:?usage: build.sh VERSION [OUT]}
OUT=$(realpath -m "${2:-dist}")
DESKTOP=$(cd "$(dirname "$0")/../.." && pwd)
ROOT=$(dirname "$DESKTOP")
NAME="Tapeory-$VERSION-linux-x86_64"
WORK=$(mktemp -d)
trap 'rm -rf "$WORK"' EXIT
mkdir -p "$OUT"

echo "== App"
TAPEORY_VERSION="$VERSION" cargo build --release --locked --manifest-path "$DESKTOP/Cargo.toml"

echo "== Engine"
# Invariant globalization: the engine then doesn't need the system's ICU libraries.
dotnet publish "$ROOT/Tapeory.Api" -c Release -r linux-x64 --self-contained \
  -p:Version="$VERSION" -p:DebugType=none -p:InvariantGlobalization=true -o "$WORK/engine"
find "$WORK/engine" -name '*.pdb' -delete
rm -rf "$WORK/engine/wwwroot"

echo "== tar.gz"
mkdir -p "$WORK/$NAME"
cp "$DESKTOP/target/release/tapeory" "$WORK/$NAME/"
cp -r "$WORK/engine" "$WORK/$NAME/engine"
cp "$ROOT/unraid/tapeory.png" "$DESKTOP/packaging/linux/tapeory.desktop" "$WORK/$NAME/"
tar -C "$WORK" -czf "$OUT/$NAME.tar.gz" "$NAME"

echo "== AppImage"
APPDIR="$WORK/AppDir"
mkdir -p "$APPDIR/usr/bin" "$APPDIR/usr/share/icons/hicolor/512x512/apps" "$APPDIR/usr/share/applications"
cp "$DESKTOP/target/release/tapeory" "$APPDIR/usr/bin/"
cp -r "$WORK/engine" "$APPDIR/usr/bin/engine"
cp "$ROOT/unraid/tapeory.png" "$APPDIR/tapeory.png"
cp "$ROOT/unraid/tapeory.png" "$APPDIR/usr/share/icons/hicolor/512x512/apps/tapeory.png"
cp "$DESKTOP/packaging/linux/tapeory.desktop" "$APPDIR/tapeory.desktop"
cp "$DESKTOP/packaging/linux/tapeory.desktop" "$APPDIR/usr/share/applications/"
cat > "$APPDIR/AppRun" <<'EOF'
#!/bin/sh
HERE="$(dirname "$(readlink -f "$0")")"
exec "$HERE/usr/bin/tapeory" "$@"
EOF
chmod +x "$APPDIR/AppRun"

TOOL=${APPIMAGETOOL:-$WORK/appimagetool}
if [ ! -x "$TOOL" ]; then
  curl -fsSL -o "$TOOL" https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage
  chmod +x "$TOOL"
fi
# Extract-and-run: works without FUSE, e.g. in containers and CI.
ARCH=x86_64 APPIMAGE_EXTRACT_AND_RUN=1 "$TOOL" --no-appstream "$APPDIR" "$OUT/$NAME.AppImage"

ls -lh "$OUT/$NAME.tar.gz" "$OUT/$NAME.AppImage"
