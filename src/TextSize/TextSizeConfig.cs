using Godot;

namespace TextSize;

/// <summary>
/// Stores the chosen text size. Saved with Godot's ConfigFile under the game's user data
/// folder (not under mods/, where every .json is treated as a mod manifest).
/// </summary>
internal static class TextSizeConfig
{
    public const int MinPercent = 70;
    public const int MaxPercent = 200;
    public const int StepPercent = 10;
    public const int DefaultPercent = 100;

    private const string ConfigDir = "user://TextSizeMod";
    private const string ConfigPath = ConfigDir + "/settings.cfg";
    private const string Section = "text";
    private const string PercentKey = "percent";

    public static int Percent { get; private set; } = DefaultPercent;

    public static float Scale => Percent / 100f;

    public static bool IsDefault => Percent == DefaultPercent;

    /// <summary>Raised on the main thread after the text size changes.</summary>
    public static event Action? Changed;

    public static void Load()
    {
        var config = new ConfigFile();
        var error = config.Load(ConfigPath);
        if (error == Error.FileNotFound)
            return;

        if (error != Error.Ok)
        {
            ModEntry.LogError($"Could not read {ConfigPath} ({error}); using {DefaultPercent}%.");
            return;
        }

        Percent = Normalize(config.GetValue(Section, PercentKey, DefaultPercent).AsInt32());
    }

    public static void SetPercent(int percent)
    {
        percent = Normalize(percent);
        if (percent == Percent)
            return;

        Percent = percent;
        Save();
        Changed?.Invoke();
    }

    public static void Step(int direction) => SetPercent(Percent + Math.Sign(direction) * StepPercent);

    private static int Normalize(int percent)
    {
        var snapped = (int)MathF.Round(percent / (float)StepPercent) * StepPercent;
        return Math.Clamp(snapped, MinPercent, MaxPercent);
    }

    private static void Save()
    {
        DirAccess.MakeDirRecursiveAbsolute(ConfigDir);

        var config = new ConfigFile();
        config.SetValue(Section, PercentKey, Percent);
        var error = config.Save(ConfigPath);
        if (error != Error.Ok)
            ModEntry.LogError($"Could not save {ConfigPath} ({error}).");
    }
}
