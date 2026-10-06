# Text Size Setting: a Slay the Spire 2 mod

Adds a **Text Size** option to the game's own settings menu so text is easier to read.

```
Settings > General

  Text Size                                 [ - ]   120%   [ + ]
```

- Anywhere from **70% to 200%**. Changes apply instantly and are remembered.
- Works on its own: no other mods needed. Doesn't change gameplay or saves.

**Players:** subscribe on the Steam Workshop, start the game, and say yes if it asks about loading mods. That's all.

---

## Uploading to the Steam Workshop (mod owner only)

Only you, the owner, upload this. Players never need anything from this page. If someone else ran the upload, it would create a separate copy on *their* account. Nobody else can change your Workshop item.

You don't need to install or build anything. The finished mod is already in `workshop/content/`.

### 1. Get the files

On this repository's GitHub page, click the green **Code** button, then **Download ZIP**. Unzip it somewhere you'll keep, such as your Documents folder. Keep this folder; you'll need it for future updates.

### 2. Try it in your game (optional, recommended)

1. Double-click **`Test in my game.bat`**. It finds your game and copies the mod into its `mods` folder.
2. Start Slay the Spire 2, open **Settings > General**, and try the **Text Size** row.
3. Close the game and double-click **`Remove test copy from my game.bat`**. Otherwise the game sees the mod twice once you subscribe to it on the Workshop.

### 3. Upload

1. Make sure **Steam is running** and you're logged in.
2. Double-click **`Upload to Steam Workshop.bat`**.
3. When asked who can see it, type **1** (only you) the first time. Subscribe to it in Steam and check it works in game.
4. Double-click **`Upload to Steam Workshop.bat`** again and type **2** to make it public for everyone.

The first time, the script downloads MegaCrit's official uploader ([megacrit/sts2-mod-uploader](https://github.com/megacrit/sts2-mod-uploader)) for you. If it can't, it opens the download page and tells you where to unzip it.

**If Windows shows "Windows protected your PC":** click **More info**, then **Run anyway**. Windows shows this for any script downloaded from the internet.

**If it's your first Workshop upload ever:** Steam hides the item until you accept the Workshop agreement. The item's page has a notice linking to it.

### After the first upload

The upload creates `workshop/mod_id.txt`, which holds your Workshop item's number. It's how later uploads update the same item instead of making a duplicate. Don't delete it. Also add it to this repository (or ask Claude to) so a fresh download keeps it.

### Updating the mod later

Get the new version of this repository, make sure `workshop/mod_id.txt` is in it, then double-click **`Upload to Steam Workshop.bat`** and pick **2**.

---

## For developers

### Layout

| Path | What it is |
| --- | --- |
| `src/TextSize/` | Mod source (C#, Harmony) |
| `mod/TextSizeSetting.json` | Mod manifest read by the game's loader |
| `workshop/` | Steam Workshop uploader workspace: `workshop.json`, `image.png`, `content/` (the built mod) |
| `*.bat`, `scripts/windows/EasyTools.ps1` | Double-click test, remove and upload helpers |
| `ci/` | Stand-in game assemblies and the release build |
| `tests/GodotHeadless/` | Tests that run the built DLL inside headless Godot 4.5.1 |

### Building

`ci/build-release.sh` rebuilds `workshop/content/` without the game installed (needs the .NET 9 SDK). It compiles against small stand-ins for the game's types. It then rewrites the DLL's references to `sts2`, `GodotSharp` and `0Harmony` to version 0.0.0.0, so the same DLL loads whichever versions the installed game ships.

The mod looks up every game type it touches by name at runtime. The only compile-time dependency on `sts2.dll` is the `[ModInitializer]` attribute, so game updates that move things around disable a single feature at worst. Problems are logged with a `[TextSizeSetting]` prefix.

To build against a real install instead, use `scripts/package.ps1 -GameDir "...\Slay the Spire 2" -Install` (or `scripts/package.sh`).

### How it works

| File | Role |
| --- | --- |
| `ModEntry.cs` | `[ModInitializer]` entry point. Loads the setting and applies each Harmony patch independently |
| `TextScaler.cs` | Scales font sizes across the UI and re-applies them when the setting changes |
| `MegaText.cs` | Raises the maximum size of the game's self-fitting labels instead of enlarging them past their box |
| `FitGuard.cs` | Shrinks any enlarged text that gets cut off or spills out of its frame until it fits |
| `Patches/FontSizePatches.cs` | Intercepts font sizes set from code: `AddThemeFontSizeOverride` and `LabelSettings.FontSize` |
| `Patches/SettingsScreenPatch.cs` | Hooks `NSettingsScreen._Ready` to add the row |
| `UI/TextSizeSettingRow.cs` | The row itself, styled after the native rows, with controller and keyboard focus |
| `TextSizeConfig.cs` | Saves the percentage to `user://TextSizeMod/settings.cfg` |

Every font size the game sets is stored on its node as the base size and replaced with the scaled size. Sizes baked into scenes and themes are picked up when a node enters the scene tree. Changing the setting re-applies everything from the stored base sizes, so changes never compound.

**Keeping text inside its frame:** the game's self-fitting labels get a higher maximum size, and the game keeps them inside their box. Any other text that gets cut off or sticks out of its frame (the nearest parent that isn't a layout container) is stepped back down by `FitGuard.cs`, never below normal size, until it fits. Where space is tight, text grows only as much as fits.

### Testing

```bash
ci/build-release.sh
GODOT=/path/to/Godot_v4.5.1-stable_mono_linux.x86_64 tests/run-godot-tests.sh
```

The tests load the exact DLL from `workshop/content/` into a real Godot 4.5.1 (.NET) engine, the version the game uses. They check scaling, restoring, persistence, clamping, row placement, focus and live updates. GitHub Actions runs them on every push. They don't replace trying the mod in the real game.
