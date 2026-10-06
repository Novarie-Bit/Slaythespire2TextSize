using Godot;

namespace TextSize;

/// <summary>
/// Stores the mod's settings. Saved with Godot's ConfigFile under the game's user data folder
/// (not under mods/, where every .json is treated as a mod manifest).
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
    private const string CardPercentKey = "card_percent";
    private const string ReadableFontKey = "readable_font";
    private const string BoldTextKey = "bold_text";
    private const string WideSpacingKey = "wide_spacing";
    private const string HighContrastCardsKey = "high_contrast_cards";
    private const string HighContrastTooltipsKey = "high_contrast_tooltips";

    /// <summary>Text size for everything except cards.</summary>
    public static int Percent { get; private set; } = DefaultPercent;

    /// <summary>Text size for cards.</summary>
    public static int CardPercent { get; private set; } = DefaultPercent;

    public static float Scale => Percent / 100f;

    public static float CardScale => CardPercent / 100f;

    public static bool IsDefault => Percent == DefaultPercent;

    /// <summary>Swap the game's fonts for Atkinson Hyperlegible.</summary>
    public static bool ReadableFont { get; private set; }

    /// <summary>Make all text heavier.</summary>
    public static bool BoldText { get; private set; }

    /// <summary>Extra space between letters and between lines.</summary>
    public static bool WideSpacing { get; private set; }

    /// <summary>White text with a thick black outline, on cards.</summary>
    public static bool HighContrastCards { get; private set; }

    /// <summary>White text with a thick black outline, in hover tooltips.</summary>
    public static bool HighContrastTooltips { get; private set; }

    /// <summary>Any option that changes the font itself (and so can make text take more room).</summary>
    public static bool ChangesFont => ReadableFont || BoldText || WideSpacing;

    /// <summary>True when every option is at the game's normal look.</summary>
    public static bool IsUntouched =>
        IsDefault && CardPercent == DefaultPercent && !ChangesFont && !HighContrastCards && !HighContrastTooltips;

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

        // Before Card Text Size existed, cards followed Text Size; keep them where they were.
        CardPercent = Normalize(config.GetValue(Section, CardPercentKey, Percent).AsInt32());

        ReadableFont = config.GetValue(Section, ReadableFontKey, false).AsBool();
        BoldText = config.GetValue(Section, BoldTextKey, false).AsBool();
        WideSpacing = config.GetValue(Section, WideSpacingKey, false).AsBool();
        HighContrastCards = config.GetValue(Section, HighContrastCardsKey, false).AsBool();
        HighContrastTooltips = config.GetValue(Section, HighContrastTooltipsKey, false).AsBool();
    }

    public static void SetPercent(int percent) => Update(() => Percent = Normalize(percent));

    public static void Step(int direction) => SetPercent(Percent + Math.Sign(direction) * StepPercent);

    public static void SetCardPercent(int percent) => Update(() => CardPercent = Normalize(percent));

    public static void StepCard(int direction) => SetCardPercent(CardPercent + Math.Sign(direction) * StepPercent);

    public static void SetReadableFont(bool on) => Update(() => ReadableFont = on);

    public static void SetBoldText(bool on) => Update(() => BoldText = on);

    public static void SetWideSpacing(bool on) => Update(() => WideSpacing = on);

    public static void SetHighContrastCards(bool on) => Update(() => HighContrastCards = on);

    public static void SetHighContrastTooltips(bool on) => Update(() => HighContrastTooltips = on);

    /// <summary>Applies a change, then saves and notifies only if something actually changed.</summary>
    private static void Update(Action change)
    {
        var before = Snapshot();
        change();
        if (Snapshot() == before)
            return;

        Save();
        Changed?.Invoke();
    }

    private static string Snapshot() =>
        $"{Percent}|{CardPercent}|{ReadableFont}|{BoldText}|{WideSpacing}|{HighContrastCards}|{HighContrastTooltips}";

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
        config.SetValue(Section, CardPercentKey, CardPercent);
        config.SetValue(Section, ReadableFontKey, ReadableFont);
        config.SetValue(Section, BoldTextKey, BoldText);
        config.SetValue(Section, WideSpacingKey, WideSpacing);
        config.SetValue(Section, HighContrastCardsKey, HighContrastCards);
        config.SetValue(Section, HighContrastTooltipsKey, HighContrastTooltips);
        var error = config.Save(ConfigPath);
        if (error != Error.Ok)
            ModEntry.LogError($"Could not save {ConfigPath} ({error}).");
    }
}
