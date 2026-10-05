// Test-only stand-ins for the game types the mod touches (see ci/Sts2Stubs for the compile-only versions).
using Godot;
namespace MegaCrit.Sts2.Core.Modding
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class ModInitializerAttribute(string m) : Attribute { public string M { get; } = m; }
}
namespace MegaCrit.Sts2.Core.Nodes.Screens.Settings
{
    public partial class NSettingsScreen : Control { public override void _Ready() { } }
    public partial class NSettingsPanel : Control { public VBoxContainer Content { get; set; } = null!; }
}
namespace MegaCrit.Sts2.addons.mega_text
{
    public partial class MegaLabel : Label { private void SetFontSize(int size) => AddThemeFontSizeOverride("font_size", size); }
    public partial class MegaRichTextLabel : RichTextLabel { private void SetFontSize(int size) { } }
}
