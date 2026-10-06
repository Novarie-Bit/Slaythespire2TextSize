#!/usr/bin/env bash
# Runs the headless Godot tests against the built mod (run ci/build-release.sh first).
# Needs the .NET SDK and Godot 4.5.1 (.NET / "mono" build). Point GODOT at the Godot binary:
#   GODOT=/path/to/Godot_v4.5.1-stable_mono_linux.x86_64 tests/run-godot-tests.sh
set -euo pipefail
here="$(cd "$(dirname "$0")/GodotHeadless" && pwd)"
: "${GODOT:?Set GODOT to the Godot 4.5.1 .NET executable}"

dotnet build "$here/TextSizeTest.csproj" -nologo

# Harmony's patcher (MonoMod) loads a helper library that needs _Unwind_RaiseException from
# libgcc_s. The official Godot Linux build doesn't load libgcc_s itself, so depending on the
# .NET runtime every Harmony patch can fail with "undefined symbol: _Unwind_RaiseException".
# Preloading libgcc_s makes the tests behave the same everywhere. (Test setup only: the game
# itself ships its own runtime and Harmony.)
if [[ "$(uname -s)" == "Linux" ]]; then
  for lib in /lib/x86_64-linux-gnu/libgcc_s.so.1 /usr/lib/x86_64-linux-gnu/libgcc_s.so.1 /lib64/libgcc_s.so.1; do
    if [[ -f "$lib" ]]; then
      export LD_PRELOAD="${LD_PRELOAD:+$LD_PRELOAD:}$lib"
      break
    fi
  done
fi

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
