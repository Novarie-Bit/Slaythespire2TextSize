using System.Collections.Concurrent;
using Godot;

namespace TextSize;

/// <summary>
/// Scales font sizes across the whole UI.
///
/// Every font size the game sets is treated as a "base" size: it is remembered on the node
/// (as metadata) and the scaled value is applied instead. Font sizes come from three places,
/// and each one is covered:
///
///  1. Code calling <see cref="Control.AddThemeFontSizeOverride"/> - intercepted by a Harmony prefix.
///  2. Code setting <see cref="LabelSettings.FontSize"/> - intercepted by a Harmony prefix.
///  3. Sizes baked into scenes or themes - picked up when the node enters the scene tree.
///
/// The game's self-fitting labels are handled by <see cref="MegaText"/> instead (their maximum
/// size is raised, and the game keeps them inside their box), and <see cref="FitGuard"/> steps
/// any text that still doesn't fit back down, per control, until it does.
///
/// When the setting changes, every tracked node is re-applied from its base size, so the
/// change is visible immediately without restarting.
/// </summary>
internal static class TextScaler
{
    private const string MetaPrefix = "textsize_mod_base_";

    private static readonly StringName FontSize = "font_size";

    private static readonly StringName[] LabelFontSizes = [FontSize];

    private static readonly StringName[] RichTextFontSizes =
    [
        "normal_font_size",
        "bold_font_size",
        "italics_font_size",
        "bold_italics_font_size",
        "mono_font_size",
    ];

    private static readonly StringName LabelSettingsBaseKey = MetaPrefix + "label_settings";

    /// <summary>Per-node cap on the scale, set by <see cref="FitGuard"/> when text doesn't fit.</summary>
    private static readonly StringName FitKey = "textsize_mod_fit";

    // Font sizes can be set while scenes are built on loader threads.
    private static readonly ConcurrentDictionary<string, StringName> MetaKeys = new();

    private static bool _installed;

    public static int Scale(int baseSize) => Scale(baseSize, TextSizeConfig.Scale);

    public static int Scale(int baseSize, float factor)
    {
        if (baseSize <= 0 || Mathf.IsEqualApprox(factor, 1f))
            return baseSize;

        return Math.Max(1, (int)MathF.Round(baseSize * factor));
    }

    /// <summary>The scale to use for this node: the setting, unless the fit guard capped it.</summary>
    public static float FactorFor(GodotObject node)
    {
        var factor = TextSizeConfig.Scale;
        if (node.HasMeta(FitKey))
            factor = Math.Min(factor, node.GetMeta(FitKey).AsSingle());

        return Math.Max(factor, Math.Min(1f, TextSizeConfig.Scale));
    }

    /// <summary>Caps the scale for one control and re-applies its sizes.</summary>
    public static void SetFitFactor(Control control, float factor)
    {
        control.SetMeta(FitKey, factor);

        // LabelSettings resources are shared between labels, so give this label its own copy.
        if (control is Label { LabelSettings: { } shared } label)
        {
            var own = (LabelSettings)shared.Duplicate();
            own.SetMeta(FitKey, factor);
            label.LabelSettings = own;
        }

        Apply(control, reflow: true);
    }

    public static void Install()
    {
        if (_installed)
            return;

        if (Engine.GetMainLoop() is not SceneTree tree)
        {
            ModEntry.LogError("No SceneTree available; text that comes from scenes will not be resized.");
            return;
        }

        _installed = true;
        tree.NodeAdded += OnNodeAdded;
        FitGuard.Install(tree);

        if (!TextSizeConfig.IsDefault)
            RefreshAll();
    }

    /// <summary>Re-applies the current scale to every text node in the scene tree.</summary>
    public static void RefreshAll()
    {
        if (Engine.GetMainLoop() is not SceneTree tree || tree.Root is null)
            return;

        FitGuard.Reset();

        var stack = new Stack<Node>();
        stack.Push(tree.Root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node is Control control)
            {
                // A new setting gets a fresh chance to fit at full size.
                control.RemoveMeta(FitKey);
                if (control is Label { LabelSettings: { } settings })
                    settings.RemoveMeta(FitKey);

                Apply(control, reflow: true);
            }

            foreach (var child in node.GetChildren(includeInternal: true))
                stack.Push(child);
        }
    }

    /// <summary>
    /// Returns the unscaled size the game asked for, or the current size if the node is not tracked.
    /// </summary>
    public static int GetBaseFontSize(Control control, StringName name)
    {
        var key = MetaKey(name);
        return control.HasMeta(key) ? control.GetMeta(key).AsInt32() : control.GetThemeFontSize(name);
    }

    // Harmony prefix target for Control.AddThemeFontSizeOverride.
    internal static void OnFontSizeOverride(Control control, StringName name, ref int fontSize)
    {
        if (fontSize <= 0 || !IsFontSizeName(name))
            return;

        // Self-fitting labels: the size comes from the game's auto-sizer and already fits.
        if (MegaText.UsesAutoSize(control))
            return;

        control.SetMeta(MetaKey(name), fontSize);
        fontSize = Scale(fontSize, FactorFor(control));
        FitGuard.Watch(control);
    }

    // Harmony prefix target for the LabelSettings.FontSize setter.
    internal static void OnLabelSettingsFontSize(LabelSettings settings, ref int value)
    {
        if (value <= 0)
            return;

        settings.SetMeta(LabelSettingsBaseKey, value);
        value = Scale(value, FactorFor(settings));
    }

    private static void OnNodeAdded(Node node)
    {
        if (node is Control control)
            Apply(control, reflow: false);
    }

    /// <summary>
    /// Applies the scale to one control. Tracked nodes are re-applied from their base size;
    /// nodes the mod has never touched are left alone while the setting is at 100%.
    /// </summary>
    private static void Apply(Control control, bool reflow)
    {
        var names = FontSizeNamesFor(control);
        if (names is null)
            return;

        if (MegaText.UsesAutoSize(control))
        {
            if (reflow)
            {
                MegaText.ApplyMaxFontSize(control, FactorFor(control), reflow: true);
            }
            else
            {
                // Entering the tree: raise the cap now, and auto-size again once the label is
                // set up, in case it already sized itself before it was added.
                MegaText.ApplyMaxFontSize(control, FactorFor(control), reflow: false);
                if (!TextSizeConfig.IsDefault)
                {
                    Callable.From(() =>
                    {
                        if (GodotObject.IsInstanceValid(control))
                            MegaText.ApplyMaxFontSize(control, FactorFor(control), reflow: true);
                    }).CallDeferred();
                }
            }

            FitGuard.Watch(control);
            return;
        }

        foreach (var name in names)
        {
            var key = MetaKey(name);
            int baseSize;
            if (control.HasMeta(key))
            {
                baseSize = control.GetMeta(key).AsInt32();
            }
            else
            {
                if (TextSizeConfig.IsDefault)
                    continue;

                baseSize = control.GetThemeFontSize(name);
            }

            if (baseSize > 0)
                control.AddThemeFontSizeOverride(name, baseSize); // the prefix scales it
        }

        if (control is Label { LabelSettings: { } settings })
            Apply(settings);

        FitGuard.Watch(control);
    }

    private static void Apply(LabelSettings settings)
    {
        int baseSize;
        if (settings.HasMeta(LabelSettingsBaseKey))
            baseSize = settings.GetMeta(LabelSettingsBaseKey).AsInt32();
        else if (TextSizeConfig.IsDefault)
            return;
        else
            baseSize = settings.FontSize;

        if (baseSize > 0)
            settings.FontSize = baseSize; // the prefix scales it
    }

    private static StringName[]? FontSizeNamesFor(Control control) => control switch
    {
        RichTextLabel => RichTextFontSizes,
        Label or Button or LineEdit or TextEdit or ItemList or Tree or TabBar or TabContainer => LabelFontSizes,
        _ => null,
    };

    private static bool IsFontSizeName(StringName name)
    {
        var text = name.ToString();
        return text.EndsWith("font_size", StringComparison.Ordinal);
    }

    private static StringName MetaKey(StringName name)
    {
        return MetaKeys.GetOrAdd(name.ToString(), static text => MetaPrefix + text);
    }
}
