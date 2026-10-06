using System.Reflection;
using Godot;
using HarmonyLib;

namespace TextSize.Patches;

// Same approach as FontSizePatches.cs: targets are resolved in code, not typeof() in attributes.

/// <summary>Swaps fonts set from code for the tweaked font when a font option is on.</summary>
[HarmonyPatch]
internal static class AddThemeFontOverridePatch
{
    private static MethodBase TargetMethod() =>
        AccessTools.DeclaredMethod(typeof(Control), nameof(Control.AddThemeFontOverride));

    private static void Prefix(Control __instance, StringName name, ref Font font)
    {
        FontTweaks.OnFontOverride(__instance, name, ref font);
    }
}

/// <summary>Swaps fonts assigned to LabelSettings resources from code.</summary>
[HarmonyPatch]
internal static class LabelSettingsFontPatch
{
    private static MethodBase TargetMethod() =>
        AccessTools.DeclaredPropertySetter(typeof(LabelSettings), nameof(LabelSettings.Font));

    private static void Prefix(LabelSettings __instance, ref Font value)
    {
        FontTweaks.OnLabelSettingsFont(__instance, ref value);
    }
}

/// <summary>Keeps card/tooltip text high-contrast when the game recolours it.</summary>
[HarmonyPatch]
internal static class AddThemeColorOverridePatch
{
    private static MethodBase TargetMethod() =>
        AccessTools.DeclaredMethod(typeof(Control), nameof(Control.AddThemeColorOverride));

    private static void Prefix(Control __instance, StringName name, ref Color color)
    {
        HighContrast.OnColorOverride(__instance, name, ref color);
    }
}

/// <summary>Keeps outlines thick and wider line spacing when the game changes them.</summary>
[HarmonyPatch]
internal static class AddThemeConstantOverridePatch
{
    private static MethodBase TargetMethod() =>
        AccessTools.DeclaredMethod(typeof(Control), nameof(Control.AddThemeConstantOverride));

    private static void Prefix(Control __instance, StringName name, ref int constant)
    {
        FontTweaks.OnConstantOverride(__instance, name, ref constant);
        HighContrast.OnConstantOverride(__instance, name, ref constant);
    }
}
