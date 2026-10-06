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

        // Mega labels: one path goes through AddThemeFontSizeOverride, one doesn't.
        var mega = new MegaLabel { Text = "mega" };
        root.AddChild(mega);
        typeof(MegaLabel).GetMethod("SetFontSize", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(mega, [22]);
        Check("MegaLabel SetFontSize scaled once", mega.GetThemeFontSize("font_size"), 33);
        var megaRich = new MegaRichTextLabel { Text = "mega rich" };
        root.AddChild(megaRich);
        typeof(MegaRichTextLabel).GetMethod("SetFontSize", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(megaRich, [20]);
        Check("MegaRichTextLabel SetFontSize (non-override path) scaled", megaRich.GetThemeFontSize("normal_font_size"), 30);

        // Back to 100%: everything restored to base.
        TextSizeConfig.SetPercent(100);
        Check("code label restored", codeLabel.GetThemeFontSize("font_size"), 10);
        Check("untouched label restored", untouched.GetThemeFontSize("font_size"), themeDefault);
        Check("scene label restored", sceneLabel.GetThemeFontSize("font_size"), 24);
        Check("LabelSettings restored", settingsB.FontSize, 12);
        Check("rtl restored", rtl.GetThemeFontSize("normal_font_size"), rtlBase);
        Check("mega restored", mega.GetThemeFontSize("font_size"), 22);

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

        // Settings screen injection.
        var screen = new NSettingsScreen { Name = "Settings" };
        var scroll = new Control { Name = "Scroll", Size = new Vector2(1000, 600) };
        var panel = new NSettingsPanel { Name = "GeneralSettings", UniqueNameInOwner = true };
        var content = new VBoxContainer { Name = "VBox" };
        panel.Content = content;
        screen.AddChild(scroll); scroll.AddChild(panel); panel.AddChild(content);
        panel.Owner = screen;
        var nativeRow = new MarginContainer { Name = "Fullscreen", CustomMinimumSize = new Vector2(0, 64) };
        var nativeLabel = new RichTextLabel { Name = "Label", Text = "Fullscreen", FitContent = true };
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
