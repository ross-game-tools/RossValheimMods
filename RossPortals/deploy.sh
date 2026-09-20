#!/usr/bin/env bash
# Build RossPortals and deploy EVERY produced assembly to a r2modman profile.
#
# The plugin depends on RossPortals.Core.dll at load time. Copying only
# RossPortals.dll installs with no build error, then throws
# FileNotFoundException the first time the panel builds a portal list -- and
# nothing in the log says why. Copy the whole output, never a named file.
#
# Usage: ./deploy.sh [profile]      (default profile: dev)
set -euo pipefail

PROFILE="${1:-dev}"

# r2modman owns the Default profile; deploying a dev build there overwrites
# what r2modman installed and is never what we want.
if [ "$(printf '%s' "$PROFILE" | tr '[:upper:]' '[:lower:]')" = "default" ]; then
    echo "REFUSING to deploy to the Default profile -- use the dev profile." >&2
    exit 1
fi
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
OUT="$HERE/src/RossPortals.Game/bin/Release/netstandard2.1"
DEST="$HOME/AppData/Roaming/r2modmanPlus-local/Valheim/profiles/$PROFILE/BepInEx/plugins/RossPortals"

DOTNET="dotnet"
command -v dotnet >/dev/null 2>&1 || DOTNET="/c/Program Files/dotnet/dotnet.exe"

"$DOTNET" build "$HERE/RossPortals.sln" -c Release --nologo -v q

[ -d "$DEST" ] || mkdir -p "$DEST"

shopt -s nullglob
copied=0
for dll in "$OUT"/RossPortals*.dll; do
    if cp "$dll" "$DEST/" 2>/dev/null; then
        echo "  deployed $(basename "$dll")"
        copied=$((copied + 1))
    else
        echo "  LOCKED   $(basename "$dll")  -- Valheim is running; quit it and re-run" >&2
        exit 1
    fi
done

[ "$copied" -gt 0 ] || { echo "nothing to deploy -- did the build produce output?" >&2; exit 1; }
echo "deployed $copied assemblies to profile '$PROFILE'"

# RossPortals REPLACES XPortal. If this profile still has XPortal installed
# (via r2modman), both mods patch the same portal hover/interact path and
# fight over the UI. RossPortals declares a BepInEx incompatibility, so BepInEx
# will refuse to load one of them -- warn here so the cause isn't a mystery.
PLUGINS_ROOT="$(dirname "$DEST")"
xportal=$(find "$PLUGINS_ROOT" -iname "*XPortal*" 2>/dev/null || true)
if [ -n "$xportal" ]; then
    echo >&2
    echo "WARNING: XPortal appears to be installed in this profile:" >&2
    echo "$xportal" | sed "s|^|  |" >&2
    echo "RossPortals replaces it and is incompatible with it. Disable XPortal in" >&2
    echo "r2modman before testing, or BepInEx will refuse to load one of them." >&2
fi

# A profile can also end up with two copies of THIS plugin: the folder above,
# and an r2modman-installed release. BepInEx loads only the higher version and
# logs a mild "Skipping ... because a newer version exists", so a test can
# silently exercise the wrong build.
others=$(find "$PLUGINS_ROOT" -name "RossPortals.dll" -not -path "$DEST/*" 2>/dev/null || true)
if [ -n "$others" ]; then
    echo >&2
    echo "WARNING: another copy of RossPortals.dll is installed in this profile:" >&2
    echo "$others" | sed "s|^|  |" >&2
    echo "BepInEx will load only the highest version, which may not be the one just deployed." >&2
fi
