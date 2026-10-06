#!/usr/bin/env bash
# Runs the headless Godot tests against the built mod (run ci/build-release.sh first).
# Needs the .NET SDK and Godot 4.5.1 (.NET / "mono" build). Point GODOT at the Godot binary:
#   GODOT=/path/to/Godot_v4.5.1-stable_mono_linux.x86_64 tests/run-godot-tests.sh
set -euo pipefail
here="$(cd "$(dirname "$0")/GodotHeadless" && pwd)"
: "${GODOT:?Set GODOT to the Godot 4.5.1 .NET executable}"

dotnet build "$here/TextSizeTest.csproj" -nologo

# --import first so Godot registers the C# script, then run the test scene.
# Both are time-limited so a stuck engine fails the run instead of hanging it.
echo "== Importing test project"
timeout 300 "$GODOT" --headless --path "$here" --import 2>&1 | tail -n 40 || echo "(import exited with $?)"

echo "== Running tests"
set +e
timeout 600 "$GODOT" --headless --path "$here" 2>&1 | tee "$here/test-output.log"
status=${PIPESTATUS[0]}
set -e
if [[ $status -eq 124 ]]; then
  echo "Godot timed out after 10 minutes." >&2
fi

grep -q "ALL TESTS PASSED" "$here/test-output.log"
