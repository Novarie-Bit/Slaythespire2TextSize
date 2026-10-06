// Stand-ins for the sts2.dll types the mod and its tests touch. The mod itself only needs
// ModInitializerAttribute at compile time; the rest it finds by name at runtime, and the
// headless tests use these to exercise those lookups.
using Godot;

namespace MegaCrit.Sts2.Core.Modding
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class ModInitializerAttribute(string initializerMethod) : Attribute
    {
        public string InitializerMethod { get; } = initializerMethod;
    }
}

namespace MegaCrit.Sts2.Core.Nodes.Screens.Settings
{
    public partial class NSettingsScreen : Control
    {
        public override void _Ready()
        {
        }
    }

    public partial class NSettingsPanel : Control
    {
        public VBoxContainer Content { get; set; } = null!;
    }
}

namespace MegaCrit.Sts2.Core.Nodes.Cards
{
    public partial class NCard : Control
    {
    }
}

namespace MegaCrit.Sts2.addons.mega_text
{
    // Mimic the game's auto-sizing: try sizes from MaxFontSize down and apply the first that
    // fits the box. Like the real thing, it measures candidate sizes rather than reading the
    // applied size back, so enlarging its result afterwards would overflow the box.
    public partial class MegaLabel : Label
    {
        public bool AutoSizeEnabled { get; set; } = true;
        public int MinFontSize { get; set; } = 8;
        public int MaxFontSize { get; set; } = 32;

        public void SetTextAutoSize(string text)
        {
            Text = text;
            if (!AutoSizeEnabled)
                return;

            var font = GetThemeFont("font");
            for (var size = MaxFontSize; size >= MinFontSize; size--)
            {
                if (size == MinFontSize || font.GetStringSize(Text, HorizontalAlignment.Left, -1, size).X <= Size.X)
                {
                    SetFontSize(size);
                    return;
                }
            }
        }

        private void SetFontSize(int size) => AddThemeFontSizeOverride("font_size", size);
    }

    public partial class MegaRichTextLabel : RichTextLabel
    {
        public bool AutoSizeEnabled { get; set; }
        public int MinFontSize { get; set; } = 8;
        public int MaxFontSize { get; set; } = 32;

        public void SetTextAutoSize(string text) => Text = text;
    }
}
