#!/usr/bin/env bash
# Build ItemDrawers Release and assemble the Thunderstore zip.
#
# The plugin (ItemDrawers.dll) depends on ItemDrawers.Core.dll at load
# time. A package that ships only the plugin installs with no build
# error, then throws FileNotFoundException the first time it needs
# ItemDrawers.Core.dll -- zero drawers register and nothing in the log
# says why (see deploy.sh's header comment; this is the same bug, at
# package time instead of deploy time). This script checks the staged
# output for both DLLs before zipping and refuses to produce a package
# that is missing either one.
#
# Usage: ./package.sh [version]      (default version: manifest.json's own)
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TS="$HERE/thunderstore"
OUT="$HERE/src/ItemDrawers.Game/bin/Release/netstandard2.1"
BUILD="$TS/build"
REQUIRED_DLLS=(ItemDrawers.dll ItemDrawers.Core.dll)

DOTNET="dotnet"
command -v dotnet >/dev/null 2>&1 || DOTNET="/c/Program Files/dotnet/dotnet.exe"

VERSION="${1:-}"
if [ -z "$VERSION" ]; then
    command -v python3 >/dev/null 2>&1 \
        && VERSION="$(python3 -c "import json,sys; print(json.load(open(sys.argv[1]))['version_number'])" "$TS/manifest.json")" \
        || VERSION="dev"
fi

# The manifest's version and the plugin's own PluginVersion must agree.
# They had already drifted once -- manifest 1.0.0 against a plugin
# reporting 0.1.0 -- which is invisible until a player reports a bug
# against a version string that does not match what Thunderstore served
# them, and BepInEx logs the plugin's number, not the manifest's.
PLUGIN_VERSION=$(grep -E 'PluginVersion = "' "$HERE/src/ItemDrawers.Game/DrawerPlugin.cs" | head -1 | cut -d'"' -f2)
if [ "$PLUGIN_VERSION" != "$VERSION" ]; then
    echo "REFUSING to package: version mismatch." >&2
    echo "  manifest.json version_number = $VERSION" >&2
    echo "  DrawerPlugin.PluginVersion   = $PLUGIN_VERSION" >&2
    echo "Set both to the same value and re-run." >&2
    exit 1
fi
echo "==> Version $VERSION (manifest and plugin agree)"

echo "==> Building Release"
"$DOTNET" build "$HERE/ItemDrawers.sln" -c Release --nologo -v q

echo "==> Staging package contents"
rm -rf "$BUILD"
mkdir -p "$BUILD/plugins"

shopt -s nullglob
for dll in "$OUT"/ItemDrawers*.dll; do
    cp "$dll" "$BUILD/plugins/"
done

for name in manifest.json icon.png README.md CHANGELOG.md; do
    src="$TS/$name"
    if [ ! -f "$src" ]; then
        echo "MISSING  $name  -- required at $src" >&2
        exit 1
    fi
    cp "$src" "$BUILD/"
done

echo "==> Verifying both assemblies are present (this is the one thing this script exists to enforce)"
missing=0
for dll in "${REQUIRED_DLLS[@]}"; do
    if [ ! -f "$BUILD/plugins/$dll" ]; then
        echo "MISSING  plugins/$dll" >&2
        missing=1
    else
        echo "  ok       plugins/$dll"
    fi
done

if [ "$missing" -ne 0 ]; then
    echo >&2
    echo "REFUSING to package: at least one required assembly is missing from the" >&2
    echo "build output above. Shipping ItemDrawers.dll without ItemDrawers.Core.dll" >&2
    echo "installs with no error and then throws FileNotFoundException at piece" >&2
    echo "registration -- zero drawers, nothing obviously wrong. See this script's" >&2
    echo "header comment. Fix the build, do not remove this check." >&2
    exit 1
fi

# Finished packages go in builds/ at the top of the MAIN checkout, even when
# this runs from a worktree: worktrees are temporary, so one fixed place is
# where builds are found. --git-common-dir is the main checkout's .git.
REPO_ROOT="$(dirname "$(git -C "$HERE" rev-parse --path-format=absolute --git-common-dir)")"
BUILDS="$REPO_ROOT/builds"
mkdir -p "$BUILDS"
ZIP="$BUILDS/RossItemDrawers-$VERSION.zip"
rm -f "$ZIP"

# Thunderstore requires manifest.json, icon.png and README.md at the ARCHIVE
# ROOT, not inside a folder -- hence zipping the staging directory's contents
# rather than the directory itself, in both branches below.
#
# `zip` is not present on a stock Windows box, and Git Bash does not ship it,
# so this script died here on the machine it was written for. PowerShell's
# Compress-Archive is always available there and produces an archive
# Thunderstore accepts.
if command -v zip >/dev/null 2>&1; then
    ( cd "$BUILD" && zip -rq "$ZIP" . )
else
    # PowerShell's Compress-Archive was tried here first and is WRONG for
    # this: on Windows it writes entry names with backslash separators
    # ("plugins\ItemDrawers.dll"), but the zip format mandates forward
    # slashes. Extractors that take the name literally create a single file
    # called "plugins\ItemDrawers.dll" at the archive root instead of a
    # plugins/ directory -- which installs cleanly and then loads nothing.
    # Python's zipfile lets us write the names ourselves, so we do.
    python -c "$(cat <<'PY'
import os, sys, zipfile
build, out = sys.argv[1], sys.argv[2]
with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
    for root, _, files in os.walk(build):
        for f in sorted(files):
            full = os.path.join(root, f)
            z.write(full, os.path.relpath(full, build).replace(os.sep, "/"))
PY
)" "$BUILD" "$ZIP" || { echo "packaging failed: could not build $ZIP" >&2; exit 1; }
fi

[ -f "$ZIP" ] || { echo "packaging failed: $ZIP was not created" >&2; exit 1; }

echo "==> Packaged $ZIP"
if command -v unzip >/dev/null 2>&1; then
    unzip -l "$ZIP"
else
    python -c "import sys,zipfile; [print(' ',n) for n in zipfile.ZipFile(sys.argv[1]).namelist()]" "$ZIP"
fi
