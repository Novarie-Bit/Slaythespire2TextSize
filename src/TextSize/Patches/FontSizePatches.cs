using System.Reflection;
using Godot;
using HarmonyLib;

namespace TextSize.Patches;

// Targets are resolved in code rather than with typeof() in the attribute: attribute
// arguments embed the exact GodotSharp version, code references don't (see ci/RetargetReferences).

/// <summary>Scales every font size override set from code.</summary>
[HarmonyPatch]
internal static class AddThemeFontSizeOverridePatch
{
    private static MethodBase TargetMethod() =>
        AccessTools.DeclaredMethod(typeof(Control), nameof(Control.AddThemeFontSizeOverride));

    private static void Prefix(Control __instance, StringName name, ref int fontSize)
    {
        TextScaler.OnFontSizeOverride(__instance, name, ref fontSize);
    }
}

/// <summary>Scales font sizes assigned to LabelSettings resources from code.</summary>
[HarmonyPatch]
internal static class LabelSettingsFontSizePatch
{
    private static MethodBase TargetMethod() =>
        AccessTools.DeclaredPropertySetter(typeof(LabelSettings), nameof(LabelSettings.FontSize));

    private static void Prefix(LabelSettings __instance, ref int value)
    {
        TextScaler.OnLabelSettingsFontSize(__instance, ref value);
    }
}
