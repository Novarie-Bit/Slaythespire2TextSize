using Godot;
using HarmonyLib;

namespace TextSize.UI;

/// <summary>
/// Builds the mod's rows and inserts them into the General tab of the game's settings screen:
///
///   Text Size                    [-]  120%  [+]
///   Easy-to-Read Font            [     Off     ]
///   High-Contrast Card Text      [     On      ]
///
/// Built from plain Godot controls so it doesn't depend on the game's private scenes, but it
/// borrows fonts and colours from the native rows so it blends in.
/// </summary>
internal static class TextSizeSettingRow
{
    private const string RowName = "TextSizeModSetting";
    private const string ReadableFontRowName = "TextSizeModReadableFont";
    private const string HighContrastRowName = "TextSizeModHighContrast";
    private const string DividerName = "TextSizeModDivider";

    private const int DefaultLabelFontSize = 28;
    private const float RowHeight = 64f;
    private const float ButtonSize = 56f;
    private const float ValueWidth = 140f;
    private const int Separation = 12;

    // The On/Off buttons line up with the "[-] 120% [+]" group above them.
    private const float ToggleWidth = ButtonSize * 2 + ValueWidth + Separation * 2;

    // Matches the cream/gold used by the native settings text.
    private static readonly Color TextColor = new(0.91f, 0.86f, 0.75f);
    private static readonly Color DividerColor = new(0.91f, 0.86f, 0.75f, 0.25f);

    // Anchors we try to insert above, in order. Falls back to the end of the list.
    private static readonly string[] InsertBeforeCandidates = ["ModdingDivider", "SendFeedbackDivider", "CreditsDivider"];

    public static void AddTo(Control screen)
    {
        var panel = screen.GetNodeOrNull<Control>("%GeneralSettings")
                    ?? FindFirst<Control>(screen, node => node.GetType().Name == "NSettingsPanel");
        var content = panel is null ? null : GetPanelContent(panel);
        if (panel is null || content is null)
        {
            ModEntry.LogError("Couldn't find the General settings panel.");
            return;
        }

        if (content.GetNodeOrNull(RowName) is not null)
            return;

        KeepPanelSizedToContent(panel, content);

        var nativeLabel = FindFirst<RichTextLabel>(content, _ => true);
        var divider = CreateDivider(content);
        Control[] rows =
        [
            CreateSizeRow(nativeLabel),
            CreateToggleRow(
                nativeLabel,
                ReadableFontRowName,
                "Easy-to-Read Font",
                "Switches the game's text to Atkinson Hyperlegible, a font designed by the Braille Institute to be easy to read. Letters that look alike (like I, l and 1) are made clearly different.",
                () => TextSizeConfig.ReadableFont,
                TextSizeConfig.SetReadableFont),
            CreateToggleRow(
                nativeLabel,
                HighContrastRowName,
                "High-Contrast Card Text",
                "Makes the text on cards bright white with a thick black outline. Coloured card text keeps its colour, just brighter. The rest of the game isn't changed.",
                () => TextSizeConfig.HighContrastCards,
                TextSizeConfig.SetHighContrastCards),
        ];

        // Divider above the block, then the rows separated by their own dividers.
        content.AddChild(divider);
        var nodes = new List<Control> { divider };
        for (var i = 0; i < rows.Length; i++)
        {
            if (i > 0)
            {
                var between = CreateDivider(content);
                between.Name = DividerName + i;
                content.AddChild(between);
                nodes.Add(between);
            }

            content.AddChild(rows[i]);
            nodes.Add(rows[i]);
        }

        var anchor = InsertBeforeCandidates
            .Select(name => content.GetNodeOrNull<Control>(name))
            .FirstOrDefault(node => node is not null);
        if (anchor is not null)
        {
            foreach (var node in nodes)
                content.MoveChild(node, anchor.GetIndex());
        }

        Callable.From(() => WireFocus(content, rows)).CallDeferred();
    }

    private static (MarginContainer Row, HBoxContainer Box) CreateRowShell(RichTextLabel? nativeLabel, string name, string titleText, string tooltip)
    {
        var row = new MarginContainer
        {
            Name = name,
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
        hbox.AddThemeConstantOverride("separation", Separation);
        row.AddChild(hbox);

        var title = CreateLabel(nativeLabel, titleText);
        title.Name = "Label";
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        title.HorizontalAlignment = HorizontalAlignment.Left;
        title.TooltipText = tooltip;
        title.MouseFilter = Control.MouseFilterEnum.Pass;
        hbox.AddChild(title);

        return (row, hbox);
    }

    private static MarginContainer CreateSizeRow(RichTextLabel? nativeLabel)
    {
        var (row, hbox) = CreateRowShell(
            nativeLabel,
            RowName,
            "Text Size",
            "Makes in-game text larger or smaller. Text never spills out of its box: where space is tight it grows as much as fits.");

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

        ListenWhileOnScreen(row, () =>
        {
            if (!GodotObject.IsInstanceValid(value))
                return;

            value.Text = $"{TextSizeConfig.Percent}%";
            decrease.Disabled = TextSizeConfig.Percent <= TextSizeConfig.MinPercent;
            increase.Disabled = TextSizeConfig.Percent >= TextSizeConfig.MaxPercent;
        });

        return row;
    }

    private static MarginContainer CreateToggleRow(
        RichTextLabel? nativeLabel, string name, string titleText, string tooltip, Func<bool> get, Action<bool> set)
    {
        var (row, hbox) = CreateRowShell(nativeLabel, name, titleText, tooltip);

        var toggle = CreateButton(nativeLabel, "", "ToggleButton");
        toggle.CustomMinimumSize = new Vector2(ToggleWidth, ButtonSize);
        hbox.AddChild(toggle);

        toggle.Pressed += () => set(!get());

        ListenWhileOnScreen(row, () =>
        {
            if (GodotObject.IsInstanceValid(toggle))
                toggle.Text = get() ? "On" : "Off";
        });

        return row;
    }

    /// <summary>
    /// Runs <paramref name="refresh"/> now and whenever a setting changes while the row is on
    /// screen. The settings screen can leave and re-enter the tree as menus open and close.
    /// </summary>
    private static void ListenWhileOnScreen(Control row, Action refresh)
    {
        refresh();
        row.TreeEntered += () =>
        {
            TextSizeConfig.Changed -= refresh;
            TextSizeConfig.Changed += refresh;
            refresh();
        };
        row.TreeExiting += () => TextSizeConfig.Changed -= refresh;
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
    /// Hooks the rows into controller / keyboard navigation: left/right within a row, up/down
    /// between rows and the native rows above and below.
    /// </summary>
    private static void WireFocus(VBoxContainer content, Control[] rows)
    {
        var buttons = rows
            .Where(row => GodotObject.IsInstanceValid(row) && row.IsInsideTree())
            .Select(row => row.GetNode("ContentRow").GetChildren().OfType<Button>().ToArray())
            .Where(row => row.Length > 0)
            .ToArray();
        if (buttons.Length == 0)
            return;

        var first = rows[0];
        var last = rows[^1];
        Control? above = null;
        for (var i = first.GetIndex() - 1; i >= 0 && above is null; i--)
            above = LastFocusable(content.GetChild(i));

        Control? below = null;
        for (var i = last.GetIndex() + 1; i < content.GetChildCount() && below is null; i++)
            below = FirstFocusable(content.GetChild(i));

        for (var r = 0; r < buttons.Length; r++)
        {
            var row = buttons[r];
            Control? up = r > 0 ? buttons[r - 1][^1] : above;
            Control? down = r < buttons.Length - 1 ? buttons[r + 1][^1] : below;

            for (var b = 0; b < row.Length; b++)
            {
                if (b > 0)
                    row[b].FocusNeighborLeft = row[b].GetPathTo(row[b - 1]);
                if (b < row.Length - 1)
                    row[b].FocusNeighborRight = row[b].GetPathTo(row[b + 1]);
                if (up is not null)
                    row[b].FocusNeighborTop = row[b].GetPathTo(up);
                if (down is not null)
                    row[b].FocusNeighborBottom = row[b].GetPathTo(down);
            }
        }

        if (above is not null)
            above.FocusNeighborBottom = above.GetPathTo(buttons[0][^1]);
        if (below is not null)
            below.FocusNeighborTop = below.GetPathTo(buttons[^1][^1]);
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

    /// <summary>The panel's rows live in its <c>Content</c> VBoxContainer.</summary>
    private static VBoxContainer? GetPanelContent(Control panel)
    {
        var property = AccessTools.Property(panel.GetType(), "Content");
        return property?.GetValue(panel) as VBoxContainer
               ?? FindFirst<VBoxContainer>(panel, _ => true);
    }

    private static T? FindFirst<T>(Node root, Func<T, bool> predicate) where T : Node
    {
        var queue = new Queue<Node>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (node != root && node is T match && predicate(match))
                return match;

            foreach (var child in node.GetChildren())
                queue.Enqueue(child);
        }

        return null;
    }
}
