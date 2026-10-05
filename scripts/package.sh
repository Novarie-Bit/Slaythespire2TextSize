#!/usr/bin/env bash
# Builds the mod and stages everything needed to install it or upload it to the Steam Workshop.
#
#   scripts/package.sh [--game-dir "/path/to/Slay the Spire 2"] [--install]
#
# Outputs:
#   dist/TextSizeSetting/                 the mod folder (manifest + DLL)
#   dist/TextSizeSetting-v<version>.zip   install-ready archive (contains mods/TextSizeSetting/...)
#   workshop/content/                     what the Steam Workshop uploader will upload
# --install also copies the mod into <game dir>/mods/TextSizeSetting for local testing.
# Extra arguments after -- are passed to dotnet build (e.g. -- -p:Sts2DataDir=...).
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
mod_id="TextSizeSetting"
manifest="$root/mod/$mod_id.json"
version="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["version"])' "$manifest")"

game_dir=""
install=false
extra=()
while [[ $# -gt 0 ]]; do
  case "$1" in
    --game-dir) game_dir="$2"; shift 2 ;;
    --install) install=true; shift ;;
    --) shift; extra=("$@"); break ;;
    *) echo "Unknown argument: $1" >&2; exit 1 ;;
  esac
done

args=(build "$root/src/TextSize/TextSize.csproj" -c Release -nologo "-p:Version=$version")
[[ -n "$game_dir" ]] && args+=("-p:GameDir=$game_dir")
dotnet "${args[@]}" "${extra[@]}"

dll="$root/src/TextSize/bin/Release/net9.0/$mod_id.dll"
[[ -f "$dll" ]] || { echo "Build output not found: $dll" >&2; exit 1; }

# Make the DLL accept any version of the game's assemblies (see ci/RetargetReferences).
dotnet run --project "$root/ci/RetargetReferences/RetargetReferences.csproj" -c Release -- "$dll"

stage="$root/dist/$mod_id"
rm -rf "$stage" && mkdir -p "$stage"
cp "$manifest" "$dll" "$stage/"

zip_root="$root/dist/zip"
rm -rf "$zip_root" && mkdir -p "$zip_root/mods"
cp -r "$stage" "$zip_root/mods/"
zip_path="$root/dist/$mod_id-v$version.zip"
rm -f "$zip_path"
(cd "$zip_root" && python3 -m zipfile -c "$zip_path" mods)
rm -rf "$zip_root"

content="$root/workshop/content"
rm -rf "$content" && mkdir -p "$content"
cp "$stage"/* "$content/"

echo
echo "Staged $mod_id v$version"
echo "  Mod folder:        $stage"
echo "  Zip:               $zip_path"
echo "  Workshop content:  $content"

if $install; then
  [[ -n "$game_dir" ]] || { echo "Pass --game-dir to use --install." >&2; exit 1; }
  target="$game_dir/mods/$mod_id"
  rm -rf "$target" && mkdir -p "$target"
  cp "$stage"/* "$target/"
  echo "  Installed to:      $target"
fi
