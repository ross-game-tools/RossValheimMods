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
