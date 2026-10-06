using System.Collections.Concurrent;
using System.Reflection;
using Godot;
using HarmonyLib;

namespace TextSize;

/// <summary>
/// The game's own text controls (MegaLabel / MegaRichTextLabel) can auto-size: they pick the
/// largest font between MinFontSize and MaxFontSize that fits their box. Scaling the size they
/// pick would push text out of the box, so for these the mod raises MaxFontSize instead and
/// lets the game's auto-sizer keep the text inside its frame.
///
/// Everything is looked up by name, so if the game renames these members the mod falls back to
/// treating the controls like ordinary labels.
/// </summary>
internal static class MegaText
{
    private static readonly string[] TypeNames =
    [
        "MegaCrit.Sts2.addons.mega_text.MegaLabel",
        "MegaCrit.Sts2.addons.mega_text.MegaRichTextLabel",
    ];

    private static readonly Type[] MegaTypes = TypeNames
        .Select(AccessTools.TypeByName)
        .Where(type => type is not null)
        .ToArray()!;

    private static readonly ConcurrentDictionary<Type, Accessors?> Cache = new();

    private static readonly StringName BaseMaxKey = "textsize_mod_base_max_font_size";

    /// <summary>True for game labels that size their own text to fit.</summary>
    public static bool UsesAutoSize(Control control)
    {
        var accessors = For(control.GetType());
        return accessors is not null && accessors.AutoSizeEnabled(control);
    }

    /// <summary>
    /// Sets MaxFontSize to the scaled base value. With <paramref name="reflow"/>, also asks the
    /// label to auto-size again so the change shows immediately.
    /// </summary>
    public static void ApplyMaxFontSize(Control control, float factor, bool reflow)
    {
        var accessors = For(control.GetType());
        if (accessors is null)
            return;

        int baseMax;
        if (control.HasMeta(BaseMaxKey))
        {
            baseMax = control.GetMeta(BaseMaxKey).AsInt32();
        }
        else
        {
            if (factor <= 1f)
                return;

            baseMax = accessors.GetMax(control);
            if (baseMax <= 0)
                return;

            control.SetMeta(BaseMaxKey, baseMax);
        }

        var scaled = TextScaler.Scale(baseMax, factor);
        if (accessors.GetMax(control) != scaled)
            accessors.SetMax(control, scaled);

        if (reflow)
            accessors.Reflow(control);
    }

    private static Accessors? For(Type type)
    {
        if (!MegaTypes.Any(mega => mega.IsAssignableFrom(type)))
            return null;

        return Cache.GetOrAdd(type, Accessors.Create);
    }

    private sealed class Accessors
    {
        private Func<object, int> _getMax = null!;
        private Action<object, int> _setMax = null!;
        private Func<object, bool>? _getAutoSize;
        private MethodInfo? _setTextAutoSize;

        public static Accessors? Create(Type type)
        {
            var accessors = new Accessors();

            var maxProperty = AccessTools.Property(type, "MaxFontSize");
            var maxField = maxProperty is null ? AccessTools.Field(type, "MaxFontSize") : null;
            if (maxProperty is { CanRead: true, CanWrite: true } && maxProperty.PropertyType == typeof(int))
            {
                accessors._getMax = target => (int)maxProperty.GetValue(target)!;
                accessors._setMax = (target, value) => maxProperty.SetValue(target, value);
            }
            else if (maxField is not null && maxField.FieldType == typeof(int))
            {
                accessors._getMax = target => (int)maxField.GetValue(target)!;
                accessors._setMax = (target, value) => maxField.SetValue(target, value);
            }
            else
            {
                ModEntry.LogError($"{type.Name} has no MaxFontSize; treating it as a plain label.");
                return null;
            }

            var autoProperty = AccessTools.Property(type, "AutoSizeEnabled");
            var autoField = autoProperty is null ? AccessTools.Field(type, "AutoSizeEnabled") : null;
            if (autoProperty is { CanRead: true } && autoProperty.PropertyType == typeof(bool))
                accessors._getAutoSize = target => (bool)autoProperty.GetValue(target)!;
            else if (autoField is not null && autoField.FieldType == typeof(bool))
                accessors._getAutoSize = target => (bool)autoField.GetValue(target)!;

            accessors._setTextAutoSize = AccessTools.Method(type, "SetTextAutoSize", [typeof(string)]);
            return accessors;
        }

        // Labels without an AutoSizeEnabled switch are assumed to always auto-size.
        public bool AutoSizeEnabled(Control control) => _getAutoSize?.Invoke(control) ?? true;

        public int GetMax(Control control) => _getMax(control);

        public void SetMax(Control control, int value) => _setMax(control, value);

        public void Reflow(Control control)
        {
            if (_setTextAutoSize is null || !control.IsInsideTree())
                return;

            var text = control switch
            {
                Label label => label.Text,
                RichTextLabel rich => rich.Text,
                _ => null,
            };
            if (text is not null)
                _setTextAutoSize.Invoke(control, [text]);
        }
    }
}
