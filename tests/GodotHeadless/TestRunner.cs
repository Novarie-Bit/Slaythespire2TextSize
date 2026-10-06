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

    public override void _Ready()
    {
        // Watchdog: never let a stuck test keep the engine running.
        GetTree().CreateTimer(300).Timeout += () =>
        {
            GD.PrintErr("Tests did not finish within 5 minutes.");
            GetTree().Quit(2);
        };
        Callable.From(Run).CallDeferred();
    }

    async void Run()
    {
        try
        {
            await RunTests();
        }
        catch (Exception e)
        {
            GD.PrintErr($"Test run crashed: {e}");
            GetTree().Quit(3);
        }
    }

    async Task RunTests()
    {
        DirAccess.RemoveAbsolute("user://TextSizeMod/settings.cfg");
        GetTree().Root.Size = new Vector2I(1920, 1080); // headless defaults to a 64x64 window
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

        // Tall enough for two lines: wraps instead of shrinking.
        var tallFrame = MakeFrame(500, 300, 220, 170);
        var wrapper = MakeLabel("Proceed to the Map Screen", 20);
        wrapper.HorizontalAlignment = HorizontalAlignment.Center;
        wrapper.VerticalAlignment = VerticalAlignment.Center;
        tallFrame.AddChild(wrapper);
        wrapper.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        for (var i = 0; i < 150; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        Check("label in a tall frame wrapped instead of shrinking", wrapper.AutowrapMode != TextServer.AutowrapMode.Off, true);
        Check("wrapped label keeps full 200%", wrapper.GetThemeFontSize("font_size"), 40);
        Check("wrapped label fits its frame", FitGuard.Overflows(wrapper), false);
        Check("short frame label stayed on one line", framed.AutowrapMode, TextServer.AutowrapMode.Off);
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
        Check("wrapping undone at 100%", wrapper.AutowrapMode, TextServer.AutowrapMode.Off);
        Check("rich text back to normal", boxed.GetThemeFontSize("normal_font_size"), 12);
        Check("copied LabelSettings back to normal", lsFramed.LabelSettings.FontSize, 20);

        TextSizeConfig.SetPercent(200);
        Check("framed label gets full size again right after the change", framed.GetThemeFontSize("font_size"), 40);
        for (var i = 0; i < 150; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check("framed label re-fitted after the change", framed.GetThemeFontSize("font_size"), framedSize);
        foreach (var node in new Node[] { frame, free, scrollArea, clipFrame, richFrame, lsFrame, lsFree, tallFrame }) node.QueueFree();
        TextSizeConfig.SetPercent(100);

        // A card in hand: tilted, scaled, drawn into a cached off-screen viewport, and the
        // setting is changed while it's on screen (like changing it mid-battle).
        var cardViewport = new SubViewport { Size = new Vector2I(800, 800), RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled };
        root.AddChild(cardViewport);
        var card = new Control { Position = new Vector2(300, 300), Size = new Vector2(300, 420), Rotation = 0.35f, Scale = new Vector2(0.8f, 0.8f) };
        cardViewport.AddChild(card);
        var cardText = MakeLabel("Block 5", 20);
        card.AddChild(cardText);
        cardText.Position = new Vector2(20, 330);
        for (var i = 0; i < 5; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        TextSizeConfig.SetPercent(200);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check("cached card picture asked to redraw", cardViewport.RenderTargetUpdateMode, SubViewport.UpdateMode.Once);
        for (var i = 0; i < 150; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check("tilted card text not mistaken for spilling over", FitGuard.Overflows(cardText), false);
        Check("tilted card text grows to 200% mid-battle", cardText.GetThemeFontSize("font_size"), 40);
        cardViewport.QueueFree();
        TextSizeConfig.SetPercent(100);

        // Easy-to-read font.
        var fontLabel = new Label { Text = "Readable" };
        root.AddChild(fontLabel);
        var gameFont = fontLabel.GetThemeFont("font");
        var rich = new RichTextLabel { Text = "[b]Bold[/b] text", BbcodeEnabled = true };
        root.AddChild(rich);
        var richOriginal = rich.GetThemeFont("normal_font");

        TextSizeConfig.SetReadableFont(true);
        var applied = fontLabel.GetThemeFont("font") as FontVariation;
        Check("label switched to Atkinson Hyperlegible", applied?.BaseFont?.GetFontName(), "Atkinson Hyperlegible");
        Check("readable font falls back to the game's font", applied?.Fallbacks.Contains(gameFont), true);
        Check("missing characters still come from the game's font", applied?.HasChar('Ж'), gameFont.HasChar('Ж'));
        var boldFace = (rich.GetThemeFont("bold_font") as FontVariation)?.BaseFont;
        Check("bold text uses the bold face", boldFace is not null && (boldFace.GetFontStyle() & TextServer.FontStyle.Bold) != 0, true);
        var custom = new FontVariation { BaseFont = gameFont, VariationEmbolden = 0.5f };
        fontLabel.AddThemeFontOverride("font", custom);
        Check("font the game sets later is swapped too", fontLabel.GetThemeFont("font") is FontVariation swapped && swapped != custom && swapped.Fallbacks.Contains(custom), true);
        var lateLabel = new Label { Text = "Late" };
        root.AddChild(lateLabel);
        Check("new labels get the readable font", (lateLabel.GetThemeFont("font") as FontVariation)?.BaseFont?.GetFontName(), "Atkinson Hyperlegible");

        TextSizeConfig.SetReadableFont(false);
        Check("off restores the font the game set", fontLabel.GetThemeFont("font") == custom, true);
        Check("off restores rich text fonts", rich.GetThemeFont("normal_font") == richOriginal, true);
        Check("off restores new labels", lateLabel.GetThemeFont("font") == gameFont, true);
        foreach (var node in new Node[] { fontLabel, rich, lateLabel }) node.QueueFree();

        // High-contrast card text: cards only.
        var cardNode = new MegaCrit.Sts2.Core.Nodes.Cards.NCard { Size = new Vector2(300, 420) };
        root.AddChild(cardNode);
        var cardTitle = new Label { Text = "Strike" };
        cardNode.AddChild(cardTitle);
        var cardCost = new Label { Text = "1" };
        cardCost.AddThemeColorOverride("font_color", new Color(0.9f, 0.2f, 0.2f));
        cardNode.AddChild(cardCost);
        var cardDesc = new RichTextLabel { Text = "Deal 6 damage." };
        cardNode.AddChild(cardDesc);
        var lsOriginal = new LabelSettings { FontColor = new Color(0.8f, 0.8f, 0.7f), OutlineSize = 2 };
        var lsCard = new Label { Text = "x", LabelSettings = lsOriginal };
        cardNode.AddChild(lsCard);
        var outside = new Label { Text = "Map" };
        root.AddChild(outside);
        var outsideColor = outside.GetThemeColor("font_color");

        TextSizeConfig.SetHighContrastCards(true);
        Check("card title is pure white", cardTitle.GetThemeColor("font_color"), Colors.White);
        Check("card title has a black outline", cardTitle.GetThemeColor("font_outline_color"), Colors.Black);
        Check("card title outline is thick", cardTitle.GetThemeConstant("outline_size") >= 8, true);
        Check("card description is pure white", cardDesc.GetThemeColor("default_color"), Colors.White);
        var cost = cardCost.GetThemeColor("font_color");
        Check("red card text stays red, at full brightness", cost.R > 0.99f && cost.G < 0.6f && cost.B < 0.6f, true);
        Check("LabelSettings card text gets its own high-contrast copy", lsCard.LabelSettings != lsOriginal && lsCard.LabelSettings.FontColor == Colors.White && lsCard.LabelSettings.OutlineSize >= 8, true);
        Check("shared LabelSettings left untouched", lsOriginal.FontColor, new Color(0.8f, 0.8f, 0.7f));
        Check("text outside cards is not changed", outside.GetThemeColor("font_color") == outsideColor && !outside.HasThemeColorOverride("font_outline_color"), true);
        cardCost.AddThemeColorOverride("font_color", new Color(0.2f, 0.7f, 0.2f));
        var green = cardCost.GetThemeColor("font_color");
        Check("colour the game sets later is made high-contrast", green.G > 0.99f && green.R < 0.6f, true);
        var lateCard = new MegaCrit.Sts2.Core.Nodes.Cards.NCard();
        var lateCardText = new Label { Text = "Drawn" };
        lateCard.AddChild(lateCardText);
        root.AddChild(lateCard);
        Check("cards created later are high-contrast", lateCardText.GetThemeColor("font_color"), Colors.White);

        TextSizeConfig.SetHighContrastCards(false);
        Check("off removes the title colour override", cardTitle.HasThemeColorOverride("font_color"), false);
        Check("off removes the title outline override", cardTitle.HasThemeConstantOverride("outline_size"), false);
        Check("off restores the latest colour the game set", cardCost.GetThemeColor("font_color"), new Color(0.2f, 0.7f, 0.2f));
        Check("off restores the original LabelSettings", lsCard.LabelSettings == lsOriginal, true);
        foreach (var node in new Node[] { cardNode, outside, lateCard }) node.QueueFree();

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
        Check("row order", string.Join(",", names), "Fullscreen,TextSizeModDivider,TextSizeModSetting,TextSizeModDivider1,TextSizeModReadableFont,TextSizeModDivider2,TextSizeModHighContrast,CreditsDivider,Credits");
        Check("panel grew", panel.Size.Y > before, true);
        var row = content.GetNode<Control>("TextSizeModSetting");
        var inc = row.GetNode<Button>("ContentRow/IncreaseButton");
        var dec = row.GetNode<Button>("ContentRow/DecreaseButton");
        var value = row.GetNode<Label>("ContentRow/Value");
        Check("value text", value.Text, "100%");
        Check("title font size copied from native", row.GetNode<Label>("ContentRow/Label").GetThemeFontSize("font_size"), 28);
        var fontToggle = content.GetNode<Button>("TextSizeModReadableFont/ContentRow/ToggleButton");
        var contrastToggle = content.GetNode<Button>("TextSizeModHighContrast/ContentRow/ToggleButton");
        Check("font toggle starts Off", fontToggle.Text, "Off");
        Check("contrast toggle starts Off", contrastToggle.Text, "Off");
        Check("focus up from size row", inc.GetNodeOrNull(inc.FocusNeighborTop) == nativeButton, true);
        Check("focus down from size row", dec.GetNodeOrNull(dec.FocusNeighborBottom) == fontToggle, true);
        Check("focus down from font row", fontToggle.GetNodeOrNull(fontToggle.FocusNeighborBottom) == contrastToggle, true);
        Check("focus down from contrast row", contrastToggle.GetNodeOrNull(contrastToggle.FocusNeighborBottom) == credits, true);
        Check("native above points down to rows", nativeButton.GetNodeOrNull(nativeButton.FocusNeighborBottom) == inc, true);
        Check("native below points up to rows", credits.GetNodeOrNull(credits.FocusNeighborTop) == contrastToggle, true);

        fontToggle.EmitSignal(BaseButton.SignalName.Pressed);
        Check("font toggle turns the setting on", TextSizeConfig.ReadableFont, true);
        Check("font toggle shows On", fontToggle.Text, "On");
        fontToggle.EmitSignal(BaseButton.SignalName.Pressed);
        Check("font toggle turns it back off", TextSizeConfig.ReadableFont, false);
        contrastToggle.EmitSignal(BaseButton.SignalName.Pressed);
        Check("contrast toggle turns the setting on", TextSizeConfig.HighContrastCards, true);
        contrastToggle.EmitSignal(BaseButton.SignalName.Pressed);
        Check("contrast toggle shows Off again", contrastToggle.Text, "Off");

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
