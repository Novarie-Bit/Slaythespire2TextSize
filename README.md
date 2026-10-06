# Text Size Setting: a Slay the Spire 2 mod

Adds readability options to the game's own settings menu.

```
Settings > General

  Text Size                                 [ - ]   120%   [ + ]
  Easy-to-Read Font                         [       Off       ]
  High-Contrast Card Text                   [       On        ]
```

- **Text Size:** anywhere from **70% to 200%**. Text never spills out of its box.
- **Easy-to-Read Font:** switches the game's text to [Atkinson Hyperlegible](https://www.brailleinstitute.org/freefont/), designed by the Braille Institute for low-vision readers. Languages it doesn't cover keep the game's own font.
- **High-Contrast Card Text:** text on cards becomes bright white with a thick black outline. Coloured card text keeps its colour, just brighter. Only cards change; the rest of the game looks normal.
- Changes apply instantly and are remembered. Works on its own: no other mods needed. Doesn't change gameplay or saves.

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

### Your Workshop item

`workshop/mod_id.txt` holds your Workshop item's number (3814426525) and is saved in this repository. It's how uploads update your existing item instead of making a duplicate, so don't delete it.

### Updating the mod later

Download the latest version of this repository (**Code > Download ZIP**), then double-click **`Upload to Steam Workshop.bat`** and pick **2**. It updates your existing Workshop item.

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
| `FitGuard.cs` | Wraps or shrinks any enlarged text that gets cut off or spills out of its frame, until it fits |
| `Patches/FontSizePatches.cs` | Intercepts font sizes set from code: `AddThemeFontSizeOverride` and `LabelSettings.FontSize` |
| `Patches/SettingsScreenPatch.cs` | Hooks `NSettingsScreen._Ready` to add the row |
| `UI/TextSizeSettingRow.cs` | The row itself, styled after the native rows, with controller and keyboard focus |
| `ReadableFont.cs` | Easy-to-Read Font: swaps fonts for Atkinson Hyperlegible (bundled in the DLL), keeping the game's font as a fallback |
| `CardContrast.cs` | High-Contrast Card Text: brightens and outlines text inside cards (`NCard`) only, and restores it exactly when turned off |
| `Redraw.cs` | Asks cached off-screen pictures (like cards) to redraw after text changes |
| `TextSizeConfig.cs` | Saves the settings to `user://TextSizeMod/settings.cfg` |

Every font size the game sets is stored on its node as the base size and replaced with the scaled size. Sizes baked into scenes and themes are picked up when a node enters the scene tree. Changing the setting re-applies everything from the stored base sizes, so changes never compound.

**Keeping text inside its frame:** the game's self-fitting labels get a higher maximum size, and the game keeps them inside their box. Any other text that gets cut off, sticks out of a *visible* frame (a parent that draws a picture, panel, colour or button behind it), or pushes its layout off the screen (like an event's story text pushing its options down) is handled by `FitGuard.cs`. A one-line label sitting directly on a frame with room for another line wraps. Otherwise the largest size that fits is found in a few frames while the text is hidden, so it never flashes big and then shrinks. Layouts that run off-screen even at normal size are treated as scrolling by design and left at full size.

### Testing

```bash
ci/build-release.sh
GODOT=/path/to/Godot_v4.5.1-stable_mono_linux.x86_64 tests/run-godot-tests.sh
```

The tests load the exact DLL from `workshop/content/` into a real Godot 4.5.1 (.NET) engine, the version the game uses. They check scaling, restoring, persistence, clamping, row placement, focus and live updates. GitHub Actions runs them on every push. They don't replace trying the mod in the real game.

## Credits

The Easy-to-Read Font option uses **Atkinson Hyperlegible**, Copyright 2020 Braille Institute of America, Inc., licensed under the SIL Open Font License 1.1 (see `src/TextSize/Fonts/OFL.txt`; a copy ships with the mod as `AtkinsonHyperlegible-OFL.txt`).
