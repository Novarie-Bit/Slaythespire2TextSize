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

/// <summary>
/// The game's auto-sizing labels pick a font size between their min and max and apply it
/// through a private SetFontSize(int). If that path ever stops going through
/// AddThemeFontSizeOverride, this re-applies the scaled size afterwards. The game types are
/// looked up by name so the mod doesn't break if they move, and the patch is skipped when the
/// method doesn't exist in the installed game version.
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
        foreach (var typeName in new[] { "MegaCrit.Sts2.addons.mega_text.MegaLabel", "MegaCrit.Sts2.addons.mega_text.MegaRichTextLabel" })
        {
            var type = AccessTools.TypeByName(typeName);
            if (type is null)
                continue;

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
