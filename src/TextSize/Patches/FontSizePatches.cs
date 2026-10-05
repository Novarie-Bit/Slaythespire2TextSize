using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;

namespace TextSize.Patches;

/// <summary>Scales every font size override set from code.</summary>
[HarmonyPatch(typeof(Control), nameof(Control.AddThemeFontSizeOverride))]
internal static class AddThemeFontSizeOverridePatch
{
    private static void Prefix(Control __instance, StringName name, ref int fontSize)
    {
        TextScaler.OnFontSizeOverride(__instance, name, ref fontSize);
    }
}

/// <summary>Scales font sizes assigned to LabelSettings resources from code.</summary>
[HarmonyPatch(typeof(LabelSettings), nameof(LabelSettings.FontSize), MethodType.Setter)]
internal static class LabelSettingsFontSizePatch
{
    private static void Prefix(LabelSettings __instance, ref int value)
    {
        TextScaler.OnLabelSettingsFontSize(__instance, ref value);
    }
}

/// <summary>
/// The game's auto-sizing labels pick a font size between their min and max and apply it
/// through a private SetFontSize(int). If that path ever stops going through
/// AddThemeFontSizeOverride, this re-applies the scaled size afterwards. The patch is skipped
/// when the method doesn't exist in the installed game version.
/// </summary>
[HarmonyPatch]
internal static class MegaTextSetFontSizePatch
{
    private static readonly StringName LabelFontSize = "font_size";

    private static readonly StringName[] RichTextFontSizes =
    [
        "normal_font_size",
        "bold_font_size",
        "italics_font_size",
        "bold_italics_font_size",
        "mono_font_size",
    ];

    private static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (var type in new[] { typeof(MegaLabel), typeof(MegaRichTextLabel) })
        {
            var method = AccessTools.DeclaredMethod(type, "SetFontSize", [typeof(int)]);
            if (method is not null)
                yield return method;
        }
    }

    private static bool Prepare() => TargetMethods().Any();

    private static void Postfix(Control __instance, int __0)
    {
        if (__0 <= 0)
            return;

        StringName[] names = __instance is RichTextLabel ? RichTextFontSizes : [LabelFontSize];
        foreach (var name in names)
        {
            // Only fix it up if the override wasn't already scaled by the prefix above.
            if (__instance.GetThemeFontSize(name) != TextScaler.Scale(__0))
                __instance.AddThemeFontSizeOverride(name, __0);
        }
    }
}
