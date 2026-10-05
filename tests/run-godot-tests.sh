#!/usr/bin/env bash
# Builds and runs the headless Godot tests.
# Needs the .NET SDK and Godot 4.5.1 (.NET / "mono" build). Point GODOT at the Godot binary:
#   GODOT=/path/to/Godot_v4.5.1-stable_mono_linux.x86_64 tests/run-godot-tests.sh
set -euo pipefail
here="$(cd "$(dirname "$0")/GodotHeadless" && pwd)"
: "${GODOT:?Set GODOT to the Godot 4.5.1 .NET executable}"

dotnet build "$here/TextSizeTest.csproj" -nologo
# --import first so Godot registers the C# script, then run the test scene.
"$GODOT" --headless --path "$here" --import >/dev/null 2>&1 || true
"$GODOT" --headless --path "$here" 2>&1 | tee "$here/test-output.log"
grep -q "ALL TESTS PASSED" "$here/test-output.log"
