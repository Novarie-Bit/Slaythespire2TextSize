#!/usr/bin/env bash
# Compile-checks the mod against stub game assemblies (no game install needed).
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/.." && pwd)"

dotnet build "$here/Sts2Stubs/Sts2Stubs.csproj" -c Release -o "$here/fake-game-data" -nologo
dotnet build "$root/src/TextSize/TextSize.csproj" -c Release -nologo \
  -p:Sts2DataDir="$here/fake-game-data" -p:TreatWarningsAsErrors=true
