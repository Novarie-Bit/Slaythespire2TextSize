#!/usr/bin/env bash
# Builds the ready-to-ship mod without needing the game installed:
#   1. builds stand-in game assemblies (ci/Sts2Stubs)
#   2. builds the mod against them
#   3. rewrites the mod's references to the game's assemblies to version 0.0.0.0
#      so the DLL loads with whatever versions the installed game ships
#   4. copies the result into workshop/content/ (what gets uploaded) and dist/
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/.." && pwd)"
mod_id="TextSizeSetting"
version="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["version"])' "$root/mod/$mod_id.json")"

dotnet build "$here/Sts2Stubs/Sts2Stubs.csproj" -c Release -o "$here/fake-game-data" -nologo
dotnet build "$root/src/TextSize/TextSize.csproj" -c Release -nologo \
  -p:Sts2DataDir="$here/fake-game-data" -p:Version="$version" -p:TreatWarningsAsErrors=true

dll="$root/src/TextSize/bin/Release/net9.0/$mod_id.dll"
dotnet run --project "$here/RetargetReferences/RetargetReferences.csproj" -c Release -- "$dll"

content="$root/workshop/content"
rm -rf "$content" && mkdir -p "$content"
cp "$root/mod/$mod_id.json" "$dll" "$content/"

stage="$root/dist/$mod_id"
rm -rf "$stage" && mkdir -p "$stage"
cp "$content"/* "$stage/"

echo
echo "Built $mod_id v$version -> $content"
