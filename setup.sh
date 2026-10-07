#!/usr/bin/env bash
# Acorn setup for macOS and Linux.
#  - Checks for the .NET SDK (does NOT download or install anything).
#  - Restores packages and publishes a Release build into dist/.
#  - macOS: wraps it in dist/Acorn.app. Linux: adds a desktop entry (skip with --no-shortcuts).
# Safe to run again. Never touches your vault (~/Library/Application Support/Acorn or ~/.local/share/Acorn).
set -euo pipefail
cd "$(dirname "$0")"

REQUIRED_MAJOR=10
CREATE_SHORTCUTS=1
if [ "${1:-}" = "--no-shortcuts" ]; then CREATE_SHORTCUTS=0; fi

echo
echo "=== Acorn setup ($(uname -s)) ==="
echo

no_dotnet() {
    echo "The .NET SDK ${REQUIRED_MAJOR} or newer was not found."
    echo "Download it from the official site, install it, then run ./setup.sh again:"
    echo "    https://dotnet.microsoft.com/download/dotnet/${REQUIRED_MAJOR}.0"
    echo "(This script never downloads or runs installers by itself.)"
    exit 1
}

command -v dotnet >/dev/null 2>&1 || no_dotnet
have_major=$(dotnet --list-sdks 2>/dev/null | awk -F. '{print $1}' | sort -n | tail -1)
if [ -z "${have_major}" ] || [ "${have_major}" -lt "${REQUIRED_MAJOR}" ]; then no_dotnet; fi

os=$(uname -s)
arch=$(uname -m)
case "${os}" in
    Darwin) case "${arch}" in arm64) rid=osx-arm64 ;; *) rid=osx-x64 ;; esac ;;
    Linux)  case "${arch}" in aarch64|arm64) rid=linux-arm64 ;; *) rid=linux-x64 ;; esac ;;
    *) echo "Unsupported OS: ${os}"; exit 1 ;;
esac
out="dist/${rid}"

echo "[1/3] Restoring NuGet packages (needs internet the first time only)..."
dotnet restore src/Acorn.App/Acorn.App.csproj -r "${rid}"

echo "[2/3] Building Release into ${out} ..."
dotnet publish src/Acorn.App/Acorn.App.csproj -c Release -r "${rid}" --self-contained false --no-restore -o "${out}"
chmod +x "${out}/Acorn"

if [ "${os}" = "Darwin" ]; then
    echo "[3/3] Creating dist/Acorn.app ..."
    app="dist/Acorn.app"
    rm -rf "${app}"                      # only the generated bundle inside dist/
    mkdir -p "${app}/Contents/MacOS" "${app}/Contents/Resources"
    cp -R "${out}/." "${app}/Contents/MacOS/"
    cat > "${app}/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key><string>Acorn</string>
    <key>CFBundleDisplayName</key><string>Acorn</string>
    <key>CFBundleIdentifier</key><string>local.acorn.vault</string>
    <key>CFBundleExecutable</key><string>Acorn</string>
    <key>CFBundlePackageType</key><string>APPL</string>
    <key>CFBundleShortVersionString</key><string>0.1.0</string>
    <key>CFBundleVersion</key><string>1</string>
    <key>LSMinimumSystemVersion</key><string>11.0</string>
    <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
PLIST
    chmod +x "${app}/Contents/MacOS/Acorn"
    # Ad-hoc signature so Apple Silicon will run the unsigned bundle.
    if command -v codesign >/dev/null 2>&1; then
        codesign --force --deep --sign - "${app}" >/dev/null 2>&1 || echo "    (ad-hoc codesign failed; the app may still run)"
    fi
    echo
    echo "Done. Drag dist/Acorn.app into /Applications (or run it from dist/)."
    echo "The app is not signed by an Apple developer ID. On first launch: right-click > Open,"
    echo "or run: xattr -dr com.apple.quarantine /Applications/Acorn.app"
    echo "Your vault will be stored in: ~/Library/Application Support/Acorn"
else
    if [ "${CREATE_SHORTCUTS}" = "1" ]; then
        echo "[3/3] Adding a desktop entry..."
        apps="${XDG_DATA_HOME:-$HOME/.local/share}/applications"
        mkdir -p "${apps}"
        cat > "${apps}/acorn.desktop" <<DESKTOP
[Desktop Entry]
Type=Application
Name=Acorn
Comment=Offline password and server vault
Exec="$(pwd)/${out}/Acorn"
Icon=$(pwd)/${out}/wwwroot/favicon.ico
Terminal=false
Categories=Utility;Security;
DESKTOP
    else
        echo "[3/3] Skipping desktop entry (--no-shortcuts)."
    fi
    echo
    echo "Done. Run: $(pwd)/${out}/Acorn"
    echo "Linux needs WebKitGTK (e.g. libwebkit2gtk-4.1) installed for the window to open."
    echo "Your vault will be stored in: \${XDG_DATA_HOME:-~/.local/share}/Acorn"
fi
echo "After this first setup Acorn works fully offline."
