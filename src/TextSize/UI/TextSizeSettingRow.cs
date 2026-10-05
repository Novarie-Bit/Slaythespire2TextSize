using Godot;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;

namespace TextSize.UI;

/// <summary>
/// Builds the "Text Size   [-] 120% [+]" row and inserts it into the General tab of the
/// game's settings screen. Built from plain Godot controls so it doesn't depend on the
/// game's private scenes, but it borrows fonts and colours from the native rows so it
/// blends in.
/// </summary>
internal static class TextSizeSettingRow
{
    private const string RowName = "TextSizeModSetting";
    private const string DividerName = "TextSizeModDivider";

    private const int DefaultLabelFontSize = 28;
    private const float RowHeight = 64f;
    private const float ButtonSize = 56f;
    private const float ValueWidth = 140f;

    // Matches the cream/gold used by the native settings text.
    private static readonly Color TextColor = new(0.91f, 0.86f, 0.75f);
    private static readonly Color DividerColor = new(0.91f, 0.86f, 0.75f, 0.25f);

    // Anchors we try to insert above, in order. Falls back to the end of the list.
    private static readonly string[] InsertBeforeCandidates = ["ModdingDivider", "SendFeedbackDivider", "CreditsDivider"];

    public static void AddTo(NSettingsScreen screen)
    {
        var panel = screen.GetNodeOrNull<NSettingsPanel>("%GeneralSettings") ?? FindFirst<NSettingsPanel>(screen);
        var content = panel?.Content;
        if (panel is null || content is null)
        {
            ModEntry.LogError("Couldn't find the General settings panel.");
            return;
        }

        if (content.GetNodeOrNull(RowName) is not null)
            return;

        KeepPanelSizedToContent(panel, content);

        var nativeLabel = FindFirst<RichTextLabel>(content);
        var divider = CreateDivider(content);
        var row = CreateRow(nativeLabel);

        content.AddChild(divider);
        content.AddChild(row);

        var anchor = InsertBeforeCandidates
            .Select(name => content.GetNodeOrNull<Control>(name))
            .FirstOrDefault(node => node is not null);
        if (anchor is not null)
        {
            content.MoveChild(divider, anchor.GetIndex());
            content.MoveChild(row, anchor.GetIndex());
        }

        Callable.From(() => WireFocus(content, row)).CallDeferred();
    }

    private static MarginContainer CreateRow(RichTextLabel? nativeLabel)
    {
        var row = new MarginContainer
        {
            Name = RowName,
            CustomMinimumSize = new Vector2(0f, RowHeight),
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        row.AddThemeConstantOverride("margin_left", 12);
        row.AddThemeConstantOverride("margin_right", 12);

        var hbox = new HBoxContainer
        {
            Name = "ContentRow",
            Alignment = BoxContainer.AlignmentMode.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        hbox.AddThemeConstantOverride("separation", 12);
        row.AddChild(hbox);

        var title = CreateLabel(nativeLabel, "Text Size");
        title.Name = "Label";
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        title.HorizontalAlignment = HorizontalAlignment.Left;
        title.TooltipText = "Makes in-game text larger or smaller. Some text that has to fit inside a fixed box (like card descriptions) may only grow as far as the box allows.";
        title.MouseFilter = Control.MouseFilterEnum.Pass;
        hbox.AddChild(title);

        var decrease = CreateButton(nativeLabel, "-", "DecreaseButton");
        var value = CreateLabel(nativeLabel, "");
        value.Name = "Value";
        value.HorizontalAlignment = HorizontalAlignment.Center;
        value.CustomMinimumSize = new Vector2(ValueWidth, 0f);
        var increase = CreateButton(nativeLabel, "+", "IncreaseButton");

        hbox.AddChild(decrease);
        hbox.AddChild(value);
        hbox.AddChild(increase);

        decrease.Pressed += () => TextSizeConfig.Step(-1);
        increase.Pressed += () => TextSizeConfig.Step(+1);

        void Refresh()
        {
            if (!GodotObject.IsInstanceValid(value))
                return;

            value.Text = $"{TextSizeConfig.Percent}%";
            decrease.Disabled = TextSizeConfig.Percent <= TextSizeConfig.MinPercent;
            increase.Disabled = TextSizeConfig.Percent >= TextSizeConfig.MaxPercent;
        }

        // The settings screen can leave and re-enter the tree as menus open and close,
        // so only listen for changes while the row is actually on screen.
        Refresh();
        row.TreeEntered += () =>
        {
            TextSizeConfig.Changed -= Refresh;
            TextSizeConfig.Changed += Refresh;
            Refresh();
        };
        row.TreeExiting += () => TextSizeConfig.Changed -= Refresh;

        return row;
    }

    private static Label CreateLabel(RichTextLabel? native, string text)
    {
        var label = new Label
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        ApplyNativeTextStyle(label, native);
        return label;
    }

    private static Button CreateButton(RichTextLabel? native, string text, string name)
    {
        var button = new Button
        {
            Name = name,
            Text = text,
            CustomMinimumSize = new Vector2(ButtonSize, ButtonSize),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            FocusMode = Control.FocusModeEnum.All,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
        };
        ApplyNativeTextStyle(button, native);

        button.AddThemeStyleboxOverride("normal", CreateBox(new Color(0.10f, 0.13f, 0.16f, 0.85f), DividerColor));
        button.AddThemeStyleboxOverride("hover", CreateBox(new Color(0.16f, 0.20f, 0.24f, 0.95f), TextColor));
        button.AddThemeStyleboxOverride("pressed", CreateBox(new Color(0.06f, 0.08f, 0.10f, 0.95f), TextColor));
        button.AddThemeStyleboxOverride("focus", CreateBox(new Color(0f, 0f, 0f, 0f), new Color(1f, 0.85f, 0.4f), 3));
        button.AddThemeStyleboxOverride("disabled", CreateBox(new Color(0.10f, 0.13f, 0.16f, 0.4f), new Color(0.5f, 0.5f, 0.5f, 0.25f)));
        button.AddThemeColorOverride("font_hover_color", Colors.White);
        button.AddThemeColorOverride("font_pressed_color", TextColor);
        button.AddThemeColorOverride("font_focus_color", Colors.White);
        button.AddThemeColorOverride("font_disabled_color", new Color(0.6f, 0.6f, 0.6f, 0.5f));
        return button;
    }

    private static StyleBoxFlat CreateBox(Color background, Color border, int borderWidth = 2)
    {
        var box = new StyleBoxFlat
        {
            BgColor = background,
            BorderColor = border,
        };
        box.SetBorderWidthAll(borderWidth);
        box.SetCornerRadiusAll(8);
        box.SetContentMarginAll(4);
        return box;
    }

    /// <summary>Copies font, size and colours from a native settings label onto a plain control.</summary>
    private static void ApplyNativeTextStyle(Control target, RichTextLabel? native)
    {
        var fontSize = DefaultLabelFontSize;
        var fontColor = TextColor;

        if (native is not null)
        {
            if (native.GetThemeFont("normal_font") is { } font)
                target.AddThemeFontOverride("font", font);

            var nativeSize = TextScaler.GetBaseFontSize(native, "normal_font_size");
            if (nativeSize > 0)
                fontSize = nativeSize;

            fontColor = native.GetThemeColor("default_color");
            target.AddThemeColorOverride("font_outline_color", native.GetThemeColor("font_outline_color"));
            target.AddThemeConstantOverride("outline_size", native.GetThemeConstant("outline_size"));
            target.AddThemeColorOverride("font_shadow_color", native.GetThemeColor("font_shadow_color"));
            target.AddThemeConstantOverride("shadow_offset_x", native.GetThemeConstant("shadow_offset_x"));
            target.AddThemeConstantOverride("shadow_offset_y", native.GetThemeConstant("shadow_offset_y"));
        }

        target.AddThemeColorOverride("font_color", fontColor);
        target.AddThemeFontSizeOverride("font_size", fontSize); // scaled by TextScaler like any other text
    }

    private static Control CreateDivider(VBoxContainer content)
    {
        var native = InsertBeforeCandidates
            .Select(name => content.GetNodeOrNull<ColorRect>(name))
            .FirstOrDefault(node => node is not null);

        return new ColorRect
        {
            Name = DividerName,
            Color = native?.Color ?? DividerColor,
            CustomMinimumSize = native?.CustomMinimumSize ?? new Vector2(0f, 2f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
    }

    /// <summary>
    /// The settings panel is sized by the game when it opens, so grow (or shrink) it by however
    /// much the content's minimum height changes - when our row is added, and whenever the
    /// text size changes afterwards.
    /// </summary>
    private static void KeepPanelSizedToContent(Control panel, Control content)
    {
        var lastHeight = content.GetCombinedMinimumSize().Y;

        content.MinimumSizeChanged += () =>
        {
            if (!GodotObject.IsInstanceValid(panel) || !GodotObject.IsInstanceValid(content))
                return;

            var height = content.GetCombinedMinimumSize().Y;
            var delta = height - lastHeight;
            lastHeight = height;
            if (Mathf.IsZeroApprox(delta))
                return;

            panel.Size = new Vector2(panel.Size.X, Mathf.Max(height, panel.Size.Y + delta));
        };
    }

    /// <summary>
    /// Hooks the row into controller / keyboard navigation between the native rows above and below.
    /// </summary>
    private static void WireFocus(VBoxContainer content, Control row)
    {
        if (!GodotObject.IsInstanceValid(row) || !row.IsInsideTree())
            return;

        var decrease = row.GetNodeOrNull<Button>("ContentRow/DecreaseButton");
        var increase = row.GetNodeOrNull<Button>("ContentRow/IncreaseButton");
        if (decrease is null || increase is null)
            return;

        decrease.FocusNeighborRight = decrease.GetPathTo(increase);
        increase.FocusNeighborLeft = increase.GetPathTo(decrease);

        var index = row.GetIndex();
        Control? above = null;
        for (var i = index - 1; i >= 0 && above is null; i--)
            above = LastFocusable(content.GetChild(i));

        Control? below = null;
        for (var i = index + 1; i < content.GetChildCount() && below is null; i++)
            below = FirstFocusable(content.GetChild(i));

        if (above is not null)
        {
            decrease.FocusNeighborTop = decrease.GetPathTo(above);
            increase.FocusNeighborTop = increase.GetPathTo(above);
            above.FocusNeighborBottom = above.GetPathTo(increase);
        }

        if (below is not null)
        {
            decrease.FocusNeighborBottom = decrease.GetPathTo(below);
            increase.FocusNeighborBottom = increase.GetPathTo(below);
            below.FocusNeighborTop = below.GetPathTo(increase);
        }
    }

    private static Control? FirstFocusable(Node node)
    {
        if (node is Control { Visible: true, FocusMode: Control.FocusModeEnum.All } control)
            return control;

        foreach (var child in node.GetChildren())
        {
            if (FirstFocusable(child) is { } found)
                return found;
        }

        return null;
    }

    private static Control? LastFocusable(Node node)
    {
        var children = node.GetChildren();
        for (var i = children.Count - 1; i >= 0; i--)
        {
            if (LastFocusable(children[i]) is { } found)
                return found;
        }

        return node is Control { Visible: true, FocusMode: Control.FocusModeEnum.All } control ? control : null;
    }

    private static T? FindFirst<T>(Node root) where T : Node
    {
        var queue = new Queue<Node>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (node != root && node is T match)
                return match;

            foreach (var child in node.GetChildren())
                queue.Enqueue(child);
        }

        return null;
    }
}
