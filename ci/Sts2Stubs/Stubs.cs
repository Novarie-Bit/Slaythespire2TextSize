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

namespace MegaCrit.Sts2.addons.mega_text
{
    // One auto-size path that goes through AddThemeFontSizeOverride and one that doesn't.
    public partial class MegaLabel : Label
    {
        private void SetFontSize(int size) => AddThemeFontSizeOverride("font_size", size);
    }

    public partial class MegaRichTextLabel : RichTextLabel
    {
        private void SetFontSize(int size)
        {
        }
    }
}
