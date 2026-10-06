using Godot;

namespace TextSize;

/// <summary>
/// The "High-Contrast Card Text" and "High-Contrast Tooltips" options. Only text inside a card
/// (the game's NCard) or a hover tooltip (NHoverTipSet), as switched on, is touched; the rest of
/// the game keeps its normal look.
///
/// That text gets a thick black outline, and its colour is pushed to full brightness: plain
/// white/cream text becomes pure white, while coloured text (a red unaffordable cost, a green
/// upgraded number...) keeps its colour so it still means the same thing, just brighter.
///
/// Before changing a label, the mod snapshots its original colours and outline so turning the
/// option off restores them exactly. Colours the game sets while the option is on are
/// intercepted, remembered as the new originals, and made high-contrast too.
/// </summary>
internal static class HighContrast
{
    /// <summary>Minimum outline thickness in pixels.</summary>
    private const int OutlineSize = 8;

    private static readonly StringName SnapshotKey = "textsize_mod_contrast_base";
    private static readonly StringName OriginalSettingsKey = "textsize_mod_contrast_label_settings";

    private static readonly StringName OutlineColor = "font_outline_color";
    private static readonly StringName OutlineSizeName = "outline_size";
    private static readonly StringName LabelColor = "font_color";
    private static readonly StringName RichTextColor = "default_color";

    [ThreadStatic]
    private static bool _writing;

    /// <summary>Quick check before the per-node work: is either option on?</summary>
    public static bool Enabled => TextSizeConfig.HighContrastCards || TextSizeConfig.HighContrastTooltips;

    /// <summary>Whether this text should be high-contrast with the current settings.</summary>
    public static bool IsTarget(Node node) =>
        (TextSizeConfig.HighContrastCards && GameNodes.IsInCard(node))
        || (TextSizeConfig.HighContrastTooltips && GameNodes.IsInTooltip(node));

    /// <summary>Applies or removes high contrast on one control, as the setting requires.</summary>
    public static void Apply(Control control)
    {
        if (control is not (Label or RichTextLabel))
            return;

        var want = Enabled && IsTarget(control);
        if (want)
        {
            if (!control.HasMeta(SnapshotKey))
                TakeSnapshot(control);
            Write(control);
        }
        else if (control.HasMeta(SnapshotKey))
        {
            Restore(control);
        }
    }

    // Harmony prefix target for Control.AddThemeColorOverride.
    internal static void OnColorOverride(Control control, StringName name, ref Color color)
    {
        if (_writing || !Enabled || control is not (Label or RichTextLabel))
            return;

        var colorName = ColorNameFor(control);
        if (name != colorName && name != OutlineColor)
            return;

        if (!control.HasMeta(SnapshotKey))
        {
            if (!IsTarget(control))
                return;
            TakeSnapshot(control);
        }

        var snapshot = Snapshot(control);
        snapshot[name.ToString()] = color;
        snapshot["has_" + name] = true;

        color = name == OutlineColor ? Colors.Black : Brighten(color);
    }

    // Harmony prefix target for Control.AddThemeConstantOverride.
    internal static void OnConstantOverride(Control control, StringName name, ref int value)
    {
        if (_writing || !Enabled || name != OutlineSizeName || control is not (Label or RichTextLabel))
            return;

        if (!control.HasMeta(SnapshotKey))
        {
            if (!IsTarget(control))
                return;
            TakeSnapshot(control);
        }

        var snapshot = Snapshot(control);
        snapshot[name.ToString()] = value;
        snapshot["has_" + name] = true;

        value = Math.Max(value, OutlineSize);
    }

    /// <summary>Makes text as bright as possible while keeping any real colour it has.</summary>
    internal static Color Brighten(Color color)
    {
        color.ToHsv(out var hue, out var saturation, out _);
        return saturation < 0.25f
            ? new Color(1f, 1f, 1f, color.A)
            : Color.FromHsv(hue, Math.Max(saturation, 0.6f), 1f, color.A);
    }

    private static void TakeSnapshot(Control control)
    {
        var colorName = ColorNameFor(control);
        var snapshot = new Godot.Collections.Dictionary
        {
            [colorName.ToString()] = control.GetThemeColor(colorName),
            ["has_" + colorName] = control.HasThemeColorOverride(colorName),
            [OutlineColor.ToString()] = control.GetThemeColor(OutlineColor),
            ["has_" + OutlineColor] = control.HasThemeColorOverride(OutlineColor),
            [OutlineSizeName.ToString()] = control.GetThemeConstant(OutlineSizeName),
            ["has_" + OutlineSizeName] = control.HasThemeConstantOverride(OutlineSizeName),
        };
        control.SetMeta(SnapshotKey, snapshot);

        if (control is Label { LabelSettings: { } settings })
            control.SetMeta(OriginalSettingsKey, settings);
    }

    private static Godot.Collections.Dictionary Snapshot(Control control) =>
        control.GetMeta(SnapshotKey).AsGodotDictionary();

    private static void Write(Control control)
    {
        var snapshot = Snapshot(control);
        var colorName = ColorNameFor(control);

        _writing = true;
        try
        {
            control.AddThemeColorOverride(colorName, Brighten(snapshot[colorName.ToString()].AsColor()));
            control.AddThemeColorOverride(OutlineColor, Colors.Black);
            control.AddThemeConstantOverride(OutlineSizeName, Math.Max(snapshot[OutlineSizeName.ToString()].AsInt32(), OutlineSize));

            // Labels using LabelSettings ignore theme colours, so give this label its own
            // high-contrast copy of its settings.
            if (control is Label label && control.HasMeta(OriginalSettingsKey))
            {
                var original = control.GetMeta(OriginalSettingsKey).As<LabelSettings>();
                var copy = (LabelSettings)original.Duplicate();
                copy.FontColor = Brighten(original.FontColor);
                copy.OutlineColor = Colors.Black;
                copy.OutlineSize = Math.Max(original.OutlineSize, OutlineSize);
                label.LabelSettings = copy;
            }
        }
        finally
        {
            _writing = false;
        }
    }

    private static void Restore(Control control)
    {
        var snapshot = Snapshot(control);
        var colorName = ColorNameFor(control);

        _writing = true;
        try
        {
            RestoreColor(control, snapshot, colorName);
            RestoreColor(control, snapshot, OutlineColor);

            if (snapshot["has_" + OutlineSizeName].AsBool())
                control.AddThemeConstantOverride(OutlineSizeName, snapshot[OutlineSizeName.ToString()].AsInt32());
            else
                control.RemoveThemeConstantOverride(OutlineSizeName);

            if (control is Label label && control.HasMeta(OriginalSettingsKey))
            {
                label.LabelSettings = control.GetMeta(OriginalSettingsKey).As<LabelSettings>();
                control.RemoveMeta(OriginalSettingsKey);
            }
        }
        finally
        {
            _writing = false;
        }

        control.RemoveMeta(SnapshotKey);
    }

    private static void RestoreColor(Control control, Godot.Collections.Dictionary snapshot, StringName name)
    {
        if (snapshot["has_" + name].AsBool())
            control.AddThemeColorOverride(name, snapshot[name.ToString()].AsColor());
        else
            control.RemoveThemeColorOverride(name);
    }

    private static StringName ColorNameFor(Control control) => control is RichTextLabel ? RichTextColor : LabelColor;
}
