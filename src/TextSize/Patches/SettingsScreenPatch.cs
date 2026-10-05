using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;
using TextSize.UI;

namespace TextSize.Patches;

/// <summary>Adds the "Text Size" row to the General tab of the native settings screen.</summary>
[HarmonyPatch(typeof(NSettingsScreen), nameof(NSettingsScreen._Ready))]
internal static class SettingsScreenPatch
{
    private static void Postfix(NSettingsScreen __instance)
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
