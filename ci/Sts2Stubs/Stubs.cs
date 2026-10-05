// Signatures mirror the members of sts2.dll that the mod uses. Bodies are empty on purpose.
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
        public VBoxContainer Content { get; } = null!;
    }
}

namespace MegaCrit.Sts2.addons.mega_text
{
    public partial class MegaLabel : Label
    {
        private void SetFontSize(int size)
        {
        }
    }

    public partial class MegaRichTextLabel : RichTextLabel
    {
        private void SetFontSize(int size)
        {
        }
    }
}
