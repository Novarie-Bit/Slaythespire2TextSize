using System.Reflection;
using Godot;
using HarmonyLib;
using TextSize.UI;

namespace TextSize.Patches;

/// <summary>
/// Adds the "Text Size" row to the General tab of the native settings screen. The screen type
/// is looked up by name so a game update that moves it only disables the row, not the mod.
/// </summary>
[HarmonyPatch]
internal static class SettingsScreenPatch
{
    private const string SettingsScreenTypeName = "MegaCrit.Sts2.Core.Nodes.Screens.Settings.NSettingsScreen";

    private static MethodBase? TargetMethod()
    {
        var type = AccessTools.TypeByName(SettingsScreenTypeName);
        return type is null ? null : AccessTools.DeclaredMethod(type, "_Ready");
    }

    private static bool Prepare(MethodBase? original)
    {
        if (original is not null || TargetMethod() is not null)
            return true;

        ModEntry.LogError("Couldn't find the settings screen; the Text Size row won't be shown.");
        return false;
    }

    private static void Postfix(Control __instance)
    {
        try
        {
            TextSizeSettingRow.AddTo(__instance);
        }
        catch (Exception e)
        {
            ModEntry.LogError($"Could not add the Text Size setting: {e}");
        }
    }
}
