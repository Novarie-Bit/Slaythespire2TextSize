// Runs inside headless Godot. Each Check() prints PASS/FAIL; the process exits non-zero on any failure.
using System.Reflection;
using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;
using TextSize;

public partial class TestRunner : Node
{
    int _failures;

    void Check(string what, object? actual, object? expected)
    {
        var ok = Equals(actual, expected);
        if (!ok) _failures++;
        GD.Print($"{(ok ? "PASS" : "FAIL")}: {what} (got {actual}, expected {expected})");
    }

    public override void _Ready() => Callable.From(Run).CallDeferred();

    async void Run()
    {
        DirAccess.RemoveAbsolute("user://TextSizeMod/settings.cfg");
        ModEntry.Initialize();
        var root = GetTree().Root;
        Check("default percent", TextSizeConfig.Percent, 100);

        // Pre-existing label created before any scale change.
        var codeLabel = new Label { Text = "code" };
        codeLabel.AddThemeFontSizeOverride("font_size", 20);
        root.AddChild(codeLabel);
        Check("code override at 100%", codeLabel.GetThemeFontSize("font_size"), 20);

        var untouched = new Label { Text = "untouched" };
        root.AddChild(untouched);
        var themeDefault = untouched.GetThemeFontSize("font_size");
        Check("untouched label at 100% has no override", untouched.HasThemeFontSizeOverride("font_size"), false);

        TextSizeConfig.SetPercent(150);
        Check("code override rescaled at 150%", codeLabel.GetThemeFontSize("font_size"), 30);
        Check("theme-default label rescaled at 150%", untouched.GetThemeFontSize("font_size"), (int)MathF.Round(themeDefault * 1.5f));

        // Scene-style override set natively (bypasses the C# method), then enters the tree.
        var sceneLabel = new Label { Text = "scene" };
        sceneLabel.Set("theme_override_font_sizes/font_size", 24);
        root.AddChild(sceneLabel);
        Check("scene override scaled on enter tree", sceneLabel.GetThemeFontSize("font_size"), 36);

        // Re-entering the tree must not double-scale.
        root.RemoveChild(sceneLabel); root.AddChild(sceneLabel);
        Check("re-enter tree does not double scale", sceneLabel.GetThemeFontSize("font_size"), 36);

        // Code changing the size later.
        codeLabel.AddThemeFontSizeOverride("font_size", 10);
        Check("later code override scaled", codeLabel.GetThemeFontSize("font_size"), 15);

        // LabelSettings set from code and natively.
        var settingsA = new LabelSettings();
        settingsA.FontSize = 10;
        Check("LabelSettings code setter scaled", settingsA.FontSize, 15);
        var settingsB = new LabelSettings();
        settingsB.Set("font_size", 12);
        var lsLabel = new Label { Text = "ls", LabelSettings = settingsB };
        root.AddChild(lsLabel);
        Check("LabelSettings scene value scaled on enter", settingsB.FontSize, 18);

        // RichTextLabel theme defaults.
        var rtl = new RichTextLabel { Text = "rich" };
        root.AddChild(rtl);
        Check("RichTextLabel normal size scaled", rtl.GetThemeFontSize("normal_font_size"), (int)MathF.Round(rtl.GetThemeFontSize("normal_font_size") / 1.5f * 1.5f));
        Check("RichTextLabel tracked", rtl.HasMeta("textsize_mod_base_normal_font_size"), true);
        var rtlBase = rtl.GetMeta("textsize_mod_base_normal_font_size").AsInt32();

        // Self-fitting game labels: the cap is raised, the game's own sizing keeps text in its box.
        var mega = new MegaLabel { ClipText = true, MaxFontSize = 20, MinFontSize = 8 };
        root.AddChild(mega);
        mega.Size = new Vector2(300, 40);
        Check("MegaLabel max font raised", mega.MaxFontSize, 30);
        mega.SetTextAutoSize("Hello");
        Check("MegaLabel short text uses raised max", mega.GetThemeFontSize("font_size"), 30);
        var megaTight = new MegaLabel { ClipText = true, MaxFontSize = 20, MinFontSize = 8 };
        root.AddChild(megaTight);
        megaTight.Size = new Vector2(220, 40);
        megaTight.SetTextAutoSize("A much longer line of text");
        var tightSize = megaTight.GetThemeFontSize("font_size");
        var tightWidth = megaTight.GetThemeFont("font").GetStringSize(megaTight.Text, HorizontalAlignment.Left, -1, tightSize).X;
        Check("MegaLabel long text still fits its box", tightWidth <= 220, true);
        Check("MegaLabel size was not enlarged after fitting", tightSize <= 30, true);
        var megaRich = new MegaRichTextLabel { Text = "mega rich" };
        root.AddChild(megaRich);
        megaRich.AddThemeFontSizeOverride("normal_font_size", 20);
        Check("MegaRichTextLabel without auto-size scaled like a normal label", megaRich.GetThemeFontSize("normal_font_size"), 30);

        // Back to 100%: everything restored to base.
        TextSizeConfig.SetPercent(100);
        Check("code label restored", codeLabel.GetThemeFontSize("font_size"), 10);
        Check("untouched label restored", untouched.GetThemeFontSize("font_size"), themeDefault);
        Check("scene label restored", sceneLabel.GetThemeFontSize("font_size"), 24);
        Check("LabelSettings restored", settingsB.FontSize, 12);
        Check("rtl restored", rtl.GetThemeFontSize("normal_font_size"), rtlBase);
        Check("mega max restored", mega.MaxFontSize, 20);
        Check("mega re-fitted at normal size", mega.GetThemeFontSize("font_size"), 20);

        // Clamp/step behaviour.
        TextSizeConfig.SetPercent(999); Check("clamped max", TextSizeConfig.Percent, 200);
        TextSizeConfig.SetPercent(1); Check("clamped min", TextSizeConfig.Percent, 70);
        TextSizeConfig.SetPercent(124); Check("snapped to step", TextSizeConfig.Percent, 120);
        TextSizeConfig.SetPercent(100);

        // Persistence.
        TextSizeConfig.SetPercent(130);
        var cfg = new ConfigFile(); cfg.Load("user://TextSizeMod/settings.cfg");
        Check("saved to disk", cfg.GetValue("text", "percent", 0).AsInt32(), 130);
        TextSizeConfig.SetPercent(100);

        // Text that would spill out of its frame at 200% is stepped back until it fits.
        TextSizeConfig.SetPercent(200);
        Label MakeLabel(string text, int size) { var l = new Label { Text = text }; l.AddThemeFontSizeOverride("font_size", size); return l; }
        Control MakeFrame(float x, float y, float w, float h) { var f = new Control { Position = new Vector2(x, y), Size = new Vector2(w, h) }; root.AddChild(f); return f; }

        var frame = MakeFrame(100, 100, 220, 40);
        var framed = MakeLabel("Proceed to Map", 20);
        framed.HorizontalAlignment = HorizontalAlignment.Center;
        frame.AddChild(framed);
        framed.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        var free = MakeLabel("Proceed to Map", 20);
        root.AddChild(free);

        var scrollArea = new ScrollContainer { Position = new Vector2(400, 100), Size = new Vector2(100, 40) };
        root.AddChild(scrollArea);
        var scrolled = MakeLabel("Proceed to Map", 20);
        scrollArea.AddChild(scrolled);

        var clipFrame = MakeFrame(100, 200, 150, 40);
        var hbox = new HBoxContainer();
        clipFrame.AddChild(hbox);
        hbox.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var clipped = MakeLabel("Proceed to Map", 20);
        clipped.ClipText = true;
        clipped.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        hbox.AddChild(clipped);

        var richFrame = MakeFrame(100, 300, 200, 60);
        var boxed = new RichTextLabel { Text = "Gain 5 Block. Draw 1 card.", AutowrapMode = TextServer.AutowrapMode.Word, ScrollActive = false };
        boxed.AddThemeFontSizeOverride("normal_font_size", 12);
        richFrame.AddChild(boxed);
        boxed.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        var shared = new LabelSettings();
        shared.FontSize = 20;
        var lsFrame = MakeFrame(100, 400, 220, 40);
        var lsFramed = new Label { Text = "Proceed to Map", LabelSettings = shared };
        lsFrame.AddChild(lsFramed);
        lsFramed.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var lsFree = new Label { Text = "x", LabelSettings = shared };
        root.AddChild(lsFree);

        for (var i = 0; i < 60; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        var framedSize = framed.GetThemeFontSize("font_size");
        Check("framed label no longer spills out", FitGuard.Overflows(framed), false);
        Check("framed label shrank below 200%", framedSize < 40, true);
        Check("framed label still bigger than normal", framedSize > 20, true);
        Check("free label keeps full 200%", free.GetThemeFontSize("font_size"), 40);
        Check("label in scroll area keeps full 200%", scrolled.GetThemeFontSize("font_size"), 40);
        Check("clipped label no longer cut off", FitGuard.Overflows(clipped), false);
        Check("clipped label shrank below 200%", clipped.GetThemeFontSize("font_size") < 40, true);
        var boxedSize = boxed.GetThemeFontSize("normal_font_size");
        Check("fixed-size rich text fits its box", FitGuard.Overflows(boxed), false);
        Check("fixed-size rich text within normal..200%", boxedSize >= 12 && boxedSize < 24, true);
        Check("shared LabelSettings copied for the cramped label", lsFramed.LabelSettings != shared, true);
        Check("cramped LabelSettings label fits", FitGuard.Overflows(lsFramed), false);
        Check("free label keeps shared LabelSettings at 200%", shared.FontSize, 40);

        TextSizeConfig.SetPercent(100);
        Check("framed label back to normal", framed.GetThemeFontSize("font_size"), 20);
        Check("rich text back to normal", boxed.GetThemeFontSize("normal_font_size"), 12);
        Check("copied LabelSettings back to normal", lsFramed.LabelSettings.FontSize, 20);

        TextSizeConfig.SetPercent(200);
        Check("framed label gets full size again right after the change", framed.GetThemeFontSize("font_size"), 40);
        for (var i = 0; i < 60; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check("framed label re-fitted after the change", framed.GetThemeFontSize("font_size"), framedSize);
        foreach (var node in new Node[] { frame, free, scrollArea, clipFrame, richFrame, lsFrame, lsFree }) node.QueueFree();
        TextSizeConfig.SetPercent(100);

        // Settings screen injection.
        var screen = new NSettingsScreen { Name = "Settings" };
        var scroll = new Control { Name = "Scroll", Size = new Vector2(1000, 600) };
        var panel = new NSettingsPanel { Name = "GeneralSettings", UniqueNameInOwner = true };
        var content = new VBoxContainer { Name = "VBox" };
        panel.Content = content;
        screen.AddChild(scroll); scroll.AddChild(panel); panel.AddChild(content);
        panel.Owner = screen;
        var nativeRow = new MarginContainer { Name = "Fullscreen", CustomMinimumSize = new Vector2(0, 64) };
        var nativeLabel = new RichTextLabel { Name = "Label", Text = "Fullscreen", FitContent = true, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(400, 0) };
        nativeLabel.AddThemeFontSizeOverride("normal_font_size", 28);
        var nativeButton = new Button { Name = "Tick", Text = "x", FocusMode = Control.FocusModeEnum.All };
        var hb = new HBoxContainer(); hb.AddChild(nativeLabel); hb.AddChild(nativeButton); nativeRow.AddChild(hb);
        content.AddChild(nativeRow);
        content.AddChild(new ColorRect { Name = "CreditsDivider", CustomMinimumSize = new Vector2(0, 2) });
        var credits = new Button { Name = "Credits", Text = "Credits", FocusMode = Control.FocusModeEnum.All };
        content.AddChild(credits);
        panel.Size = new Vector2(1000, 64 + 2 + 40 + 8);
        var before = panel.Size.Y;
        root.AddChild(screen); // Godot calls _Ready -> Harmony postfix injects the row
        screen._Ready(); // a second call must not duplicate it
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        var names = content.GetChildren().Select(c => c.Name.ToString()).ToArray();
        Check("row order", string.Join(",", names), "Fullscreen,TextSizeModDivider,TextSizeModSetting,CreditsDivider,Credits");
        Check("panel grew", panel.Size.Y > before, true);
        var row = content.GetNode<Control>("TextSizeModSetting");
        var inc = row.GetNode<Button>("ContentRow/IncreaseButton");
        var dec = row.GetNode<Button>("ContentRow/DecreaseButton");
        var value = row.GetNode<Label>("ContentRow/Value");
        Check("value text", value.Text, "100%");
        Check("title font size copied from native", row.GetNode<Label>("ContentRow/Label").GetThemeFontSize("font_size"), 28);
        Check("focus up from row", inc.GetNodeOrNull(inc.FocusNeighborTop) == nativeButton, true);
        Check("focus down from row", dec.GetNodeOrNull(dec.FocusNeighborBottom) == credits, true);
        Check("native above points down to row", nativeButton.GetNodeOrNull(nativeButton.FocusNeighborBottom) == inc, true);

        var h0 = panel.Size.Y;
        inc.EmitSignal(BaseButton.SignalName.Pressed);
        inc.EmitSignal(BaseButton.SignalName.Pressed);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check("percent after 2 clicks", TextSizeConfig.Percent, 120);
        Check("value label updated", value.Text, "120%");
        Check("native label scaled live", nativeLabel.GetThemeFontSize("normal_font_size"), 34);
        Check("panel grew with text", panel.Size.Y > h0, true);

        // Leave and re-enter the tree, then click again.
        root.RemoveChild(screen); root.AddChild(screen);
        dec.EmitSignal(BaseButton.SignalName.Pressed);
        Check("value label updates after re-entering tree", value.Text, "110%");
        for (int i = 0; i < 10; i++) dec.EmitSignal(BaseButton.SignalName.Pressed);
        Check("decrease disabled at min", dec.Disabled, true);
        Check("value at min", value.Text, "70%");

        GD.Print(_failures == 0 ? "ALL TESTS PASSED" : $"{_failures} TEST(S) FAILED");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }
}
