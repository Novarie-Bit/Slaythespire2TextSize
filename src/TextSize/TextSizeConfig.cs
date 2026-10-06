using Godot;

namespace TextSize;

/// <summary>
/// Stores the mod's settings: text size, easy-to-read font, and high-contrast card text. Saved with Godot's ConfigFile under the game's user data
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
    private const string ReadableFontKey = "readable_font";
    private const string HighContrastCardsKey = "high_contrast_cards";

    public static int Percent { get; private set; } = DefaultPercent;

    public static float Scale => Percent / 100f;

    public static bool IsDefault => Percent == DefaultPercent;

    /// <summary>Swap the game's fonts for Atkinson Hyperlegible.</summary>
    public static bool ReadableFont { get; private set; }

    /// <summary>White text with a thick black outline, on cards only.</summary>
    public static bool HighContrastCards { get; private set; }

    /// <summary>True when every option is at the game's normal look.</summary>
    public static bool IsUntouched => IsDefault && !ReadableFont && !HighContrastCards;

    /// <summary>Raised on the main thread after any setting changes.</summary>
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
        ReadableFont = config.GetValue(Section, ReadableFontKey, false).AsBool();
        HighContrastCards = config.GetValue(Section, HighContrastCardsKey, false).AsBool();
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

    public static void SetReadableFont(bool on)
    {
        if (on == ReadableFont)
            return;

        ReadableFont = on;
        Save();
        Changed?.Invoke();
    }

    public static void SetHighContrastCards(bool on)
    {
        if (on == HighContrastCards)
            return;

        HighContrastCards = on;
        Save();
        Changed?.Invoke();
    }

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
        config.SetValue(Section, ReadableFontKey, ReadableFont);
        config.SetValue(Section, HighContrastCardsKey, HighContrastCards);
        var error = config.Save(ConfigPath);
        if (error != Error.Ok)
            ModEntry.LogError($"Could not save {ConfigPath} ({error}).");
    }
}
