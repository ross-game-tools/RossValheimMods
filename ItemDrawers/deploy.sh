#!/usr/bin/env bash
# Build ItemDrawers and deploy EVERY produced assembly to a r2modman profile.
#
# The plugin depends on ItemDrawers.Core.dll. Copying only ItemDrawers.dll
# produces a FileNotFoundException at piece registration and no drawers appear
# in the build menu -- with no error in the build and nothing obviously wrong
# in the plugin. Copy the whole output, never a named file.
#
# Usage: ./deploy.sh [profile]      (default profile: dev)
set -euo pipefail

PROFILE="${1:-dev}"

# r2modman owns the Default profile; deploying a dev build there
# overwrites what r2modman installed and is never what we want.
if [ "$(printf '%s' "$PROFILE" | tr '[:upper:]' '[:lower:]')" = "default" ]; then
    echo "REFUSING to deploy to the Default profile -- use the dev profile." >&2
    exit 1
fi
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
OUT="$HERE/src/ItemDrawers.Game/bin/Release/netstandard2.1"
DEST="$HOME/AppData/Roaming/r2modmanPlus-local/Valheim/profiles/$PROFILE/BepInEx/plugins/ItemDrawers"

DOTNET="dotnet"
command -v dotnet >/dev/null 2>&1 || DOTNET="/c/Program Files/dotnet/dotnet.exe"

"$DOTNET" build "$HERE/ItemDrawers.sln" -c Release --nologo -v q

[ -d "$DEST" ] || mkdir -p "$DEST"

shopt -s nullglob
copied=0
for dll in "$OUT"/ItemDrawers*.dll; do
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

# A profile can end up with TWO copies of this plugin: the folder above,
# written by this script, and an r2modman-installed "Ross-RossItemDrawers"
# of a released version. BepInEx loads whichever declares the higher
# version and logs only a mild "Skipping [ItemDrawers x] because a newer
# version exists" -- so a test can silently exercise the released build
# instead of the one just deployed, and nothing about the game says so.
# That happened, and cost a session's worth of confusing results.
#
# Warn rather than delete: the other copy is r2modman's to manage, not
# this script's.
PLUGINS_ROOT="$(dirname "$DEST")"
others=$(find "$PLUGINS_ROOT" -name "ItemDrawers.dll" -not -path "$DEST/*" 2>/dev/null)
if [ -n "$others" ]; then
    echo >&2
    echo "WARNING: another copy of ItemDrawers.dll is installed in this profile:" >&2
    echo "$others" | sed "s|^|  |" >&2
    echo "BepInEx will load only the highest version, which may not be the one just deployed." >&2
    echo "Remove it in r2modman (or disable that mod) before testing." >&2
fi
