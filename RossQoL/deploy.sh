#!/usr/bin/env bash
# Build RossQoL and deploy EVERY produced assembly to a r2modman profile.
#
# The plugin depends on RossQoL.Core.dll. Copying only
# RossQoL.dll produces a FileNotFoundException the first time the
# plugin needs it -- with no error in the build and nothing obviously wrong
# in the plugin. Copy the whole output, never a named file.
#
# STANDING RULE: this deploys to the r2modman `dev` profile only. Never
# point it at `Default` -- r2modman owns that profile, and hand-copying
# into it desynchronises what is installed from what r2modman believes is
# installed.
#
# Usage: ./deploy.sh [profile]      (default profile: dev)
set -euo pipefail

PROFILE="${1:-dev}"
if [ "$PROFILE" = "Default" ]; then
    echo "REFUSING to deploy to the 'Default' profile -- r2modman owns it." >&2
    echo "Deploy to 'dev' (or another non-Default profile) instead." >&2
    exit 1
fi

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
OUT="$HERE/src/RossQoL.Game/bin/Release/netstandard2.1"
DEST="$HOME/AppData/Roaming/r2modmanPlus-local/Valheim/profiles/$PROFILE/BepInEx/plugins/RossQoL"

DOTNET="dotnet"
command -v dotnet >/dev/null 2>&1 || DOTNET="/c/Program Files/dotnet/dotnet.exe"

"$DOTNET" build "$HERE/RossQoL.sln" -c Release --nologo -v q

[ -d "$DEST" ] || mkdir -p "$DEST"

shopt -s nullglob
copied=0
for dll in "$OUT"/RossQoL*.dll; do
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
