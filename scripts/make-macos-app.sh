#!/usr/bin/env bash
# Builds BinaryViewer.app, and optionally a drag-and-drop DMG installer.
#
#   ./scripts/make-macos-app.sh                 # .app for Apple Silicon
#   ./scripts/make-macos-app.sh osx-x64         # .app for Intel
#   ./scripts/make-macos-app.sh osx-arm64 --dmg # .app + BinaryViewer.dmg (needs macOS)
#
# The bundle is self contained: no .NET runtime required on the target Mac.
set -euo pipefail

RID="osx-arm64"
OUTDIR="out"
MAKE_DMG=0

for arg in "$@"; do
  case "$arg" in
    osx-arm64|osx-x64) RID="$arg" ;;
    --dmg)             MAKE_DMG=1 ;;
    --out=*)           OUTDIR="${arg#--out=}" ;;
    *) echo "unknown argument: $arg" >&2; exit 2 ;;
  esac
done

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
APP="$OUTDIR/BinaryViewer.app"

rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

dotnet publish "$ROOT/Gui/BinaryViewer.Gui.csproj" \
  -c Release -r "$RID" --self-contained true \
  -o "$APP/Contents/MacOS"

cat > "$APP/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key>                 <string>Binary Viewer</string>
  <key>CFBundleDisplayName</key>          <string>Binary Viewer</string>
  <key>CFBundleIdentifier</key>           <string>dev.binaryviewer.app</string>
  <key>CFBundleVersion</key>              <string>1.0.0</string>
  <key>CFBundleShortVersionString</key>   <string>1.0.0</string>
  <key>CFBundlePackageType</key>          <string>APPL</string>
  <key>CFBundleExecutable</key>           <string>BinaryViewer</string>
  <key>NSHighResolutionCapable</key>      <true/>
  <key>LSMinimumSystemVersion</key>       <string>11.0</string>
  <key>CFBundleDocumentTypes</key>
  <array>
    <dict>
      <key>CFBundleTypeName</key>         <string>Binary file</string>
      <key>CFBundleTypeRole</key>         <string>Viewer</string>
      <key>LSHandlerRank</key>            <string>Alternate</string>
      <key>LSItemContentTypes</key>       <array><string>public.data</string></array>
    </dict>
  </array>
</dict>
</plist>
PLIST

chmod +x "$APP/Contents/MacOS/BinaryViewer"

# ad-hoc signature: enough for a locally built app, not for downloads (see the note below)
if command -v codesign >/dev/null 2>&1; then
  codesign --force --deep --sign - "$APP" >/dev/null 2>&1 \
    && echo "ad-hoc signed" \
    || echo "codesign failed (still runnable via right click > Open)"
fi

echo "built: $APP"

if [ "$MAKE_DMG" = "1" ]; then
  if ! command -v hdiutil >/dev/null 2>&1; then
    echo "hdiutil not found - DMG creation only works on macOS" >&2
    exit 1
  fi

  STAGE="$(mktemp -d)"
  cp -R "$APP" "$STAGE/"
  ln -s /Applications "$STAGE/Applications"        # the drag target in the installer window

  DMG="$OUTDIR/BinaryViewer-$RID.dmg"
  rm -f "$DMG"
  hdiutil create -volname "Binary Viewer" -srcfolder "$STAGE" -ov -format UDZO "$DMG"
  rm -rf "$STAGE"
  echo "built: $DMG"
fi

cat <<'NOTE'

설치 / Install
  .app 을 Applications 폴더로 드래그하면 끝입니다 (DMG를 만들었다면 그 창에서 바로).

  이 앱은 Apple Developer ID 서명 / 공증(notarization)이 되어 있지 않습니다.
  인터넷으로 받은 경우 처음 실행할 때 Gatekeeper가 막을 수 있는데, 둘 중 하나로 해결됩니다.
    - Finder에서 앱을 우클릭 > "열기" > 다시 "열기"
    - 또는  xattr -dr com.apple.quarantine /Applications/BinaryViewer.app

  Drag the .app into Applications. It is ad-hoc signed only, so a downloaded copy may be
  blocked by Gatekeeper on first launch: right click > Open, or strip the quarantine attribute.
NOTE
