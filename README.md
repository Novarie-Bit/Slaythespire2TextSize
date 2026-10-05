# Text Size Setting: a Slay the Spire 2 mod

Adds a **Text Size** option to the game's own settings menu so you can make text bigger (or smaller).

```
Settings > General

  Text Size                                 [ - ]   120%   [ + ]
```

- Anywhere from **70% to 200%** in 10% steps.
- The change applies immediately, including on the settings screen itself, and is remembered between sessions.
- Standalone: no BaseLib or other mods needed. Doesn't change gameplay or saves (`affects_gameplay: false`).

## Installing

**From the Steam Workshop:** subscribe, launch the game, and accept the mod-loading prompt if it appears.

**Manually:** unzip `TextSizeSetting-v<version>.zip` into your game folder so you end up with:

```
Slay the Spire 2/
  mods/
    TextSizeSetting/
      TextSizeSetting.json
      TextSizeSetting.dll
```

Your setting is saved in the game's user data folder (`user://TextSizeMod/settings.cfg`), not in `mods/`.

## Building

Requirements: the [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) and Slay the Spire 2 installed. The mod compiles against the `sts2.dll`, `GodotSharp.dll` and `0Harmony.dll` that ship with the game.

```powershell
# Windows (auto-detects the default Steam library)
./scripts/package.ps1

# Game in another library? Point at it, and optionally install straight into the game:
./scripts/package.ps1 -GameDir "D:\SteamLibrary\steamapps\common\Slay the Spire 2" -Install
```

```bash
# Linux
scripts/package.sh --game-dir "$HOME/.local/share/Steam/steamapps/common/Slay the Spire 2" --install
```

This produces:

| Output | What it's for |
| --- | --- |
| `dist/TextSizeSetting/` | The mod folder (manifest + DLL) |
| `dist/TextSizeSetting-v1.0.0.zip` | Install-ready zip for Nexus or GitHub releases |
| `workshop/content/` | Exactly what gets uploaded to the Steam Workshop |

## Uploading to the Steam Workshop

MegaCrit's official uploader is [megacrit/sts2-mod-uploader](https://github.com/megacrit/sts2-mod-uploader). This repo's `workshop/` folder is already laid out as an uploader workspace:

```
workshop/
  workshop.json   title, description, visibility, change notes
  image.png       Workshop preview image (must be under 1 MB)
  content/        filled in by scripts/package.*
```

1. Run `./scripts/package.ps1` so `workshop/content/` holds a fresh build.
2. Download `ModUploader` from the uploader's [releases page](https://github.com/megacrit/sts2-mod-uploader/releases).
3. Make sure Steam is running and you're logged in to the account that owns the game.
4. From the folder with `ModUploader.exe`, run:
   ```powershell
   .\ModUploader.exe upload -w "C:\path\to\Slaythespire2TextSize\workshop"
   ```
5. On the first upload the tool creates a Workshop item and writes `workshop/mod_id.txt`. **Commit that file**, because later uploads use it to update the same item instead of creating a new one.
6. `workshop.json` starts with `"visibility": "private"`. Subscribe to your own item and check it works in game, then change it to `"public"` and upload again (or switch visibility on the Workshop page).

**Updating:** bump `version` in `mod/TextSizeSetting.json`, write a `changeNote` in `workshop/workshop.json`, run the package script, then upload again.

## How it works

| File | Role |
| --- | --- |
| `src/TextSize/ModEntry.cs` | `[ModInitializer]` entry point: loads the setting, applies Harmony patches, starts the scaler |
| `src/TextSize/TextScaler.cs` | Scales font sizes across the UI and re-applies them when the setting changes |
| `src/TextSize/Patches/FontSizePatches.cs` | Intercepts font sizes set from code (`AddThemeFontSizeOverride`, `LabelSettings.FontSize`, the game's auto-sizing `MegaLabel`/`MegaRichTextLabel`) |
| `src/TextSize/Patches/SettingsScreenPatch.cs` | Hooks `NSettingsScreen._Ready` to add the row |
| `src/TextSize/UI/TextSizeSettingRow.cs` | Builds the row, styled after the native settings rows, with controller/keyboard focus |
| `src/TextSize/TextSizeConfig.cs` | Saves and loads the chosen percentage |

Every font size the game sets is treated as the base size. It's stored on the node and replaced with the scaled size. Sizes baked into scenes and themes get picked up when a node enters the scene tree. Changing the setting re-applies the new scale to everything on screen from those stored base sizes, so repeated changes never compound.

**Known limitation:** the game auto-shrinks some text to fit a fixed box (card descriptions, for example). That text can only get as big as its box allows.

## Testing

- `ci/build-with-stubs.sh` compile-checks the mod against small stand-ins for the game's types, so no game install is needed.
- `tests/run-godot-tests.sh` runs the scaler and the settings row inside a real headless **Godot 4.5.1 (.NET)**, the engine version the game uses. It checks scaling, restoring, persistence, clamping, row placement, focus wiring and live updates. Set `GODOT` to the Godot .NET binary first.

GitHub Actions runs both on every push (`.github/workflows/build.yml`). Neither one replaces trying the mod in the real game.

## Compatibility notes

- Built against STS2's modding API as of Major Update 2: an external `<id>.json` manifest next to `<id>.dll`, with `[ModInitializer]` and Harmony.
- If a game update renames the settings screen internals, the Text Size row might not appear, but the game won't crash. Errors are logged with a `[TextSizeSetting]` prefix in the game log.
