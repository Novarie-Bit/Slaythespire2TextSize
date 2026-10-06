using Godot;

namespace TextSize;

/// <summary>
/// The font options: Easy-to-Read Font, Bold Text and Wider Text Spacing.
///
/// Easy-to-Read Font swaps fonts for Atkinson Hyperlegible, a typeface the Braille Institute
/// designed for low-vision readers (bundled inside the DLL, SIL Open Font License, see
/// Fonts/OFL.txt). Bold Text thickens the strokes. Wider Text Spacing adds space between letters
/// (in the font) and between lines (in the label).
///
/// Works like the size scaling: every font the game assigns is remembered on the node as its
/// "base" font and a tweaked version is applied in its place, so turning the options off puts
/// the game's own fonts back. With the readable font, the original font stays as a fallback, so
/// characters Atkinson Hyperlegible doesn't have (Chinese, Japanese, Korean, Cyrillic...) still
/// show up in the game's font.
/// </summary>
internal static class FontTweaks
{
    private const string MetaPrefix = "textsize_mod_base_font_";

    /// <summary>How much Bold Text thickens strokes (FontVariation embolden strength).</summary>
    private const float Embolden = 0.6f;

    /// <summary>Extra pixels between letters and between words with Wider Text Spacing.</summary>
    private const int LetterSpacing = 2;
    private const int WordSpacing = 4;

    /// <summary>Extra pixels between lines with Wider Text Spacing.</summary>
    private const int LineSpacing = 6;

    private static readonly StringName FontSlot = "font";

    private static readonly StringName[] LabelSlots = [FontSlot];

    // mono_font is left alone: monospaced text usually needs to stay monospaced.
    private static readonly StringName[] RichTextSlots = ["normal_font", "bold_font", "italics_font", "bold_italics_font"];

    private static readonly StringName LabelSettingsBaseKey = MetaPrefix + "label_settings";

    private static readonly StringName LabelLineSpacing = "line_spacing";
    private static readonly StringName RichTextLineSpacing = "line_separation";
    private static readonly StringName LineSnapshotKey = "textsize_mod_base_line_spacing";
    private static readonly StringName SettingsLineBaseKey = "textsize_mod_base_settings_line_spacing";

    private static readonly object Lock = new();
    private static readonly Dictionary<(ulong, Style, bool, bool, bool), Font> Replacements = new();
    private static readonly Dictionary<ulong, Font> OriginalOf = new();

    [ThreadStatic]
    private static bool _writing;

    private static Font?[]? _faces;

    private enum Style
    {
        Regular,
        Bold,
        Italic,
        BoldItalic,
    }

    /// <summary>True when any font option is on (and, for the readable font, the font loaded).</summary>
    public static bool Enabled => TextSizeConfig.BoldText || TextSizeConfig.WideSpacing || UseReadable;

    private static bool UseReadable => TextSizeConfig.ReadableFont && Faces is not null;

    private static Font?[]? Faces
    {
        get
        {
            if (_faces is not null)
                return _faces[0] is null ? null : _faces;

            lock (Lock)
            {
                _faces ??= LoadFaces();
            }

            return _faces[0] is null ? null : _faces;
        }
    }

    /// <summary>Applies (or removes) the font options on one control.</summary>
    public static void Apply(Control control)
    {
        var slots = SlotsFor(control);
        if (slots is null)
            return;

        foreach (var slot in slots)
        {
            var key = MetaKey(slot);
            Font? baseFont;
            if (control.HasMeta(key))
            {
                baseFont = control.GetMeta(key).As<Font>();
            }
            else
            {
                if (!Enabled)
                    continue;

                baseFont = control.GetThemeFont(slot);
            }

            if (baseFont is not null)
                control.AddThemeFontOverride(slot, baseFont); // the prefix swaps it
        }

        if (control is Label { LabelSettings: { } settings })
            Apply(settings);

        ApplyLineSpacing(control);
    }

    private static void Apply(LabelSettings settings)
    {
        Font? baseFont;
        if (settings.HasMeta(LabelSettingsBaseKey))
            baseFont = settings.GetMeta(LabelSettingsBaseKey).As<Font>();
        else if (!Enabled || settings.Font is null)
            baseFont = null;
        else
            baseFont = settings.Font;

        if (baseFont is not null)
            settings.Font = baseFont; // the prefix swaps it

        // Line spacing for labels using LabelSettings lives in the (shared) settings resource.
        if (TextSizeConfig.WideSpacing)
        {
            if (!settings.HasMeta(SettingsLineBaseKey))
                settings.SetMeta(SettingsLineBaseKey, settings.LineSpacing);
            settings.LineSpacing = settings.GetMeta(SettingsLineBaseKey).AsSingle() + LineSpacing;
        }
        else if (settings.HasMeta(SettingsLineBaseKey))
        {
            settings.LineSpacing = settings.GetMeta(SettingsLineBaseKey).AsSingle();
            settings.RemoveMeta(SettingsLineBaseKey);
        }
    }

    // Harmony prefix target for Control.AddThemeFontOverride.
    internal static void OnFontOverride(Control control, StringName name, ref Font font)
    {
        if (font is null || !IsReplaceableSlot(name))
            return;

        var original = Unwrap(font);
        control.SetMeta(MetaKey(name), original);
        font = Enabled ? Replacement(original, StyleFor(name, original)) : original;
    }

    // Harmony prefix target for the LabelSettings.Font setter.
    internal static void OnLabelSettingsFont(LabelSettings settings, ref Font value)
    {
        if (value is null)
            return;

        var original = Unwrap(value);
        settings.SetMeta(LabelSettingsBaseKey, original);
        value = Enabled ? Replacement(original, StyleFor(FontSlot, original)) : original;
    }

    // Harmony prefix target for Control.AddThemeConstantOverride (line spacing only).
    internal static void OnConstantOverride(Control control, StringName name, ref int value)
    {
        if (_writing || !TextSizeConfig.WideSpacing || name != LineSpacingName(control))
            return;

        if (!control.HasMeta(LineSnapshotKey))
            TakeLineSnapshot(control);

        var snapshot = control.GetMeta(LineSnapshotKey).AsGodotDictionary();
        snapshot["value"] = value;
        snapshot["has"] = true;
        value += LineSpacing;
    }

    private static void ApplyLineSpacing(Control control)
    {
        var name = LineSpacingName(control);
        if (name is null)
            return;

        _writing = true;
        try
        {
            if (TextSizeConfig.WideSpacing)
            {
                if (!control.HasMeta(LineSnapshotKey))
                    TakeLineSnapshot(control);

                var snapshot = control.GetMeta(LineSnapshotKey).AsGodotDictionary();
                control.AddThemeConstantOverride(name, snapshot["value"].AsInt32() + LineSpacing);
            }
            else if (control.HasMeta(LineSnapshotKey))
            {
                var snapshot = control.GetMeta(LineSnapshotKey).AsGodotDictionary();
                if (snapshot["has"].AsBool())
                    control.AddThemeConstantOverride(name, snapshot["value"].AsInt32());
                else
                    control.RemoveThemeConstantOverride(name);
                control.RemoveMeta(LineSnapshotKey);
            }
        }
        finally
        {
            _writing = false;
        }
    }

    private static void TakeLineSnapshot(Control control)
    {
        var name = LineSpacingName(control)!;
        control.SetMeta(LineSnapshotKey, new Godot.Collections.Dictionary
        {
            ["value"] = control.GetThemeConstant(name),
            ["has"] = control.HasThemeConstantOverride(name),
        });
    }

    private static StringName? LineSpacingName(Control control) => control switch
    {
        RichTextLabel => RichTextLineSpacing,
        Label => LabelLineSpacing,
        _ => null,
    };

    private static Font Replacement(Font original, Style style)
    {
        var readable = UseReadable;
        var bold = TextSizeConfig.BoldText;
        var spacing = TextSizeConfig.WideSpacing;

        lock (Lock)
        {
            var key = (original.GetInstanceId(), style, readable, bold, spacing);
            if (Replacements.TryGetValue(key, out var cached) && GodotObject.IsInstanceValid(cached))
                return cached;

            var replacement = new FontVariation();
            if (readable)
            {
                var faces = Faces!;
                replacement.BaseFont = faces[(int)style] ?? faces[0];
                replacement.Fallbacks = new Godot.Collections.Array<Font> { original };
            }
            else
            {
                replacement.BaseFont = original;
            }

            if (bold)
            {
                // Faces that are already bold need less extra weight.
                var alreadyBold = style is Style.Bold or Style.BoldItalic;
                replacement.VariationEmbolden = alreadyBold ? Embolden / 2f : Embolden;
            }

            if (spacing)
            {
                replacement.SpacingGlyph = LetterSpacing;
                replacement.SpacingSpace = WordSpacing;
            }

            Replacements[key] = replacement;
            OriginalOf[replacement.GetInstanceId()] = original;
            return replacement;
        }
    }

    /// <summary>If game code hands back one of our replacement fonts, use the font it replaced.</summary>
    private static Font Unwrap(Font font)
    {
        lock (Lock)
        {
            return OriginalOf.TryGetValue(font.GetInstanceId(), out var original) && GodotObject.IsInstanceValid(original)
                ? original
                : font;
        }
    }

    private static Style StyleFor(StringName slot, Font original)
    {
        var name = slot.ToString();
        if (name == "bold_font")
            return Style.Bold;
        if (name == "italics_font")
            return Style.Italic;
        if (name == "bold_italics_font")
            return Style.BoldItalic;

        var style = original.GetFontStyle();
        var bold = (style & TextServer.FontStyle.Bold) != 0 || original.GetFontWeight() >= 600;
        var italic = (style & TextServer.FontStyle.Italic) != 0;
        return (bold, italic) switch
        {
            (true, true) => Style.BoldItalic,
            (true, false) => Style.Bold,
            (false, true) => Style.Italic,
            _ => Style.Regular,
        };
    }

    private static Font?[] LoadFaces()
    {
        string[] files =
        [
            "AtkinsonHyperlegible-Regular.ttf",
            "AtkinsonHyperlegible-Bold.ttf",
            "AtkinsonHyperlegible-Italic.ttf",
            "AtkinsonHyperlegible-BoldItalic.ttf",
        ];

        var faces = new Font?[files.Length];
        for (var i = 0; i < files.Length; i++)
        {
            try
            {
                using var stream = typeof(FontTweaks).Assembly.GetManifestResourceStream("Fonts/" + files[i]);
                if (stream is null)
                {
                    ModEntry.LogError($"Bundled font {files[i]} is missing.");
                    continue;
                }

                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                faces[i] = new FontFile { Data = memory.ToArray() };
            }
            catch (Exception e)
            {
                ModEntry.LogError($"Could not load {files[i]}: {e.Message}");
            }
        }

        return faces;
    }

    private static StringName[]? SlotsFor(Control control) => control switch
    {
        RichTextLabel => RichTextSlots,
        Label or Button or LineEdit or TextEdit or ItemList or Tree or TabBar or TabContainer => LabelSlots,
        _ => null,
    };

    private static bool IsReplaceableSlot(StringName name)
    {
        var text = name.ToString();
        return text is "font" or "normal_font" or "bold_font" or "italics_font" or "bold_italics_font";
    }

    private static StringName MetaKey(StringName slot) => MetaPrefix + slot.ToString();
}
