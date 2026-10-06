using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;

namespace TextSize;

/// <summary>
/// Entry point called by the Slay the Spire 2 mod loader.
/// </summary>
[ModInitializer(nameof(Initialize))]
public static class ModEntry
{
    public const string ModId = "TextSizeSetting";
    public const string HarmonyId = "novarie.sts2.textsize";

    public static void Initialize()
    {
        TextSizeConfig.Load();

        ApplyPatches(new Harmony(HarmonyId));

        TextScaler.Install();
        TextSizeConfig.Changed += TextScaler.RefreshAll;

        Log($"Loaded. Text size is {TextSizeConfig.Percent}%.");
    }

    /// <summary>
    /// Patches one class at a time so that if a game update breaks one patch (for example the
    /// settings screen), the rest of the mod keeps working.
    /// </summary>
    private static void ApplyPatches(Harmony harmony)
    {
        foreach (var type in AccessTools.GetTypesFromAssembly(typeof(ModEntry).Assembly))
        {
            if (!type.IsDefined(typeof(HarmonyPatch), inherit: false))
                continue;

            try
            {
                harmony.CreateClassProcessor(type).Patch();
            }
            catch (Exception e)
            {
                LogError($"Patch {type.Name} failed and was skipped: {e.Message}");
            }
        }
    }

    internal static void Log(string message) => GD.Print($"[{ModId}] {message}");

    internal static void LogError(string message) => GD.PushError($"[{ModId}] {message}");
}
