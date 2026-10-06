using Godot;

namespace TextSize;

/// <summary>
/// The "Easy-to-Read Font" option: swaps the fonts on text controls for Atkinson Hyperlegible,
/// a typeface the Braille Institute designed for low-vision readers. The font files are
/// bundled inside the mod's DLL (SIL Open Font License, see Fonts/OFL.txt).
///
/// Works like the size scaling: every font the game assigns is remembered on the node as its
/// "base" font and the readable replacement is applied in its place, so turning the option off
/// puts the game's own fonts back. Each replacement keeps the original font as a fallback, so
/// characters Atkinson Hyperlegible doesn't have (Chinese, Japanese, Korean, Cyrillic...) still
/// show up in the game's font.
/// </summary>
internal static class ReadableFont
{
    private const string MetaPrefix = "textsize_mod_base_font_";

    private static readonly StringName FontSlot = "font";

    private static readonly StringName[] LabelSlots = [FontSlot];

    // mono_font is left alone: monospaced text usually needs to stay monospaced.
    private static readonly StringName[] RichTextSlots = ["normal_font", "bold_font", "italics_font", "bold_italics_font"];

    private static readonly StringName LabelSettingsBaseKey = MetaPrefix + "label_settings";

    private static readonly object Lock = new();
    private static readonly Dictionary<(ulong, Style), Font> Replacements = new();
    private static readonly Dictionary<ulong, Font> OriginalOf = new();

    private static Font?[]? _faces;

    private enum Style
    {
        Regular,
        Bold,
        Italic,
        BoldItalic,
    }

    public static bool Enabled => TextSizeConfig.ReadableFont && Faces is not null;

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

    /// <summary>Applies (or removes) the readable font on one control.</summary>
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
    }

    private static void Apply(LabelSettings settings)
    {
        Font? baseFont;
        if (settings.HasMeta(LabelSettingsBaseKey))
            baseFont = settings.GetMeta(LabelSettingsBaseKey).As<Font>();
        else if (!Enabled || settings.Font is null)
            return;
        else
            baseFont = settings.Font;

        if (baseFont is not null)
            settings.Font = baseFont; // the prefix swaps it
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

    private static Font Replacement(Font original, Style style)
    {
        var faces = Faces;
        if (faces is null)
            return original;

        lock (Lock)
        {
            var key = (original.GetInstanceId(), style);
            if (Replacements.TryGetValue(key, out var cached) && GodotObject.IsInstanceValid(cached))
                return cached;

            var replacement = new FontVariation
            {
                BaseFont = faces[(int)style] ?? faces[0],
                Fallbacks = new Godot.Collections.Array<Font> { original },
            };

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
                using var stream = typeof(ReadableFont).Assembly.GetManifestResourceStream("Fonts/" + files[i]);
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
