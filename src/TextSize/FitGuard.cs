using Godot;

namespace TextSize;

/// <summary>
/// Safety net that keeps enlarged text readable. After layout, each enlarged text control is
/// checked; if its text is cut off, or it now sticks out of the frame it sits in:
///
///  1. a one-line label sitting directly on a frame tall enough for another line is allowed
///     to wrap onto more lines, so it can stay big;
///  2. otherwise its size is stepped down (never below the game's normal size, or just under
///     it with the readable font, which can run a little wider) until it fits.
///     If wrapping didn't help even at normal size, the label goes back to one line and the
///     search runs again, so it never ends up worse than a plain shrink.
///
/// "The frame" is the nearest parent that isn't a layout container and that you can actually
/// see: one that draws a picture, panel, colour or button behind the text, or has a background
/// child covering it. Containers grow with their contents, and invisible holders (like the one
/// the game puts hover tooltips in) don't bound anything visually, so neither counts.
/// Scrollable areas and full-screen parents don't count either.
///
/// Text also mustn't push its layout off the screen: if text inside a stack of containers
/// (like an event's story text above its option buttons) makes that stack run past the bottom
/// or right edge of the screen, the text in it shrinks until the whole stack fits. This only
/// happens once the layout has stopped moving, so panels sliding in from off-screen aren't
/// affected.
///
/// While a size is being searched for, the text is hidden (its own SelfModulate is made
/// transparent), so you never see it appear big and then shrink. The search halves the gap
/// each frame, so it takes only a handful of frames.
/// </summary>
internal static class FitGuard
{
    /// <summary>The search stops once the fitting size is known to within this fraction.</summary>
    private const float Precision = 0.03f;

    /// <summary>A background child must cover this much of a parent for it to count as a frame.</summary>
    private const float BackgroundCoverage = 0.7f;

    /// <summary>Pixels of slack before counting something as sticking out.</summary>
    private const float Tolerance = 2f;

    /// <summary>
    /// Overflow must be seen this many frames in a row before shrinking, so frames that the
    /// game resizes to fit their text a moment later aren't mistaken for overflow. The text is
    /// hidden while waiting, so the wait isn't visible.
    /// </summary>
    private const int ConfirmFrames = 2;

    /// <summary>The text's own SelfModulate, saved while the mod hides it.</summary>
    private static readonly StringName HiddenKey = "textsize_mod_fit_hidden";

    /// <summary>
    /// Set on text whose layout runs off the screen even at normal size: that layout is meant to
    /// scroll or extend, so the screen edge isn't checked for it again.
    /// </summary>
    private static readonly StringName ScreenExemptKey = "textsize_mod_screen_exempt";

    private static readonly StringName WatchedKey = "textsize_mod_fit_watched";

    /// <summary>Set while the mod has switched a label to wrapping.</summary>
    private static readonly StringName WrappedKey = "textsize_mod_wrapped";

    /// <summary>Set once wrapping was tried and given up on, so it isn't tried again.</summary>
    private static readonly StringName WrapTriedKey = "textsize_mod_wrap_tried";

    private static readonly object Lock = new();
    private static readonly Dictionary<ulong, Control> Pending = new();
    private static readonly Dictionary<ulong, int> Suspect = new();
    private static readonly Dictionary<ulong, Search> Searches = new();
    private static readonly Dictionary<ulong, Vector2> SuspectPositions = new();

    private enum Overflow
    {
        None,

        /// <summary>Cut off, or sticking out of its frame.</summary>
        Frame,

        /// <summary>Pushing its layout off the screen.</summary>
        Screen,
    }

    /// <summary>A size search in progress: the text fits at Lo and doesn't at Hi.</summary>
    private sealed class Search
    {
        public float Lo;
        public float Hi;
        public Overflow Kind;
    }

    /// <summary>
    /// Text can only need help fitting when it's bigger than normal, or when the readable font
    /// (which can run a little wider than the game's) is on.
    /// </summary>
    public static bool Active => TextSizeConfig.Scale > 1f || TextSizeConfig.CardScale > 1f || TextSizeConfig.ChangesFont;

    /// <summary>
    /// The smallest scale the guard may step down to: normal size, or slightly under it when a
    /// font option is on (the readable font, bold text and wider spacing can all take more room
    /// than the game's font, so text that just fit before may need to go a bit smaller).
    /// </summary>
    public static float FloorFor(float baseScale) => Math.Min(1f, baseScale) * (TextSizeConfig.ChangesFont ? 0.85f : 1f);

    private static float FloorFor(Control control) => FloorFor(TextScaler.BaseScaleFor(control));

    public static void Install(SceneTree tree) => tree.ProcessFrame += OnProcessFrame;

    /// <summary>Checks the control after the next layout, and again whenever it resizes.</summary>
    public static void Watch(Control control)
    {
        if (!Active)
            return;

        if (!control.HasMeta(WatchedKey))
        {
            control.SetMeta(WatchedKey, true);
            control.Resized += () => Queue(control);
            control.VisibilityChanged += () => Queue(control);
        }

        Queue(control);
    }

    /// <summary>Forgets in-progress checks, e.g. when the setting changes.</summary>
    public static void Reset()
    {
        lock (Lock)
        {
            Suspect.Clear();
            Searches.Clear();
            SuspectPositions.Clear();
        }
    }

    /// <summary>Undoes any wrapping the mod switched on, giving the control a fresh start.</summary>
    public static void Forget(Control control)
    {
        Show(control);
        control.RemoveMeta(WrapTriedKey);
        control.RemoveMeta(ScreenExemptKey);
        if (control is Label label && label.HasMeta(WrappedKey))
        {
            label.RemoveMeta(WrappedKey);
            label.AutowrapMode = TextServer.AutowrapMode.Off;
        }
    }

    private static void Queue(Control control)
    {
        if (!Active)
            return;

        lock (Lock)
            Pending[control.GetInstanceId()] = control;
    }

    private static void OnProcessFrame()
    {
        List<Control> batch;
        lock (Lock)
        {
            if (Pending.Count == 0)
                return;

            batch = Pending.Values.ToList();
            Pending.Clear();
        }

        foreach (var control in batch)
        {
            if (!GodotObject.IsInstanceValid(control))
                continue;

            if (!control.IsInsideTree() || !control.IsVisibleInTree())
            {
                Abandon(control);
                continue;
            }

            Step(control);
        }
    }

    private static void Step(Control control)
    {
        var id = control.GetInstanceId();
        var kind = Classify(control);
        var over = kind != Overflow.None;

        Search? search;
        lock (Lock)
            Searches.TryGetValue(id, out search);

        if (search is not null)
        {
            Continue(control, id, search, over);
            return;
        }

        if (!over)
        {
            lock (Lock)
            {
                Suspect.Remove(id);
                SuspectPositions.Remove(id);
            }
            Show(control);
            return;
        }

        int seen;
        if (kind == Overflow.Screen)
        {
            // Only judge a layout that has stopped moving (panels slide in from off-screen),
            // and keep the text visible until then.
            var position = control.GetGlobalTransformWithCanvas().Origin;
            lock (Lock)
            {
                var still = SuspectPositions.TryGetValue(id, out var last) && last.DistanceTo(position) < 1f;
                seen = still ? Suspect.GetValueOrDefault(id) + 1 : 1;
                Suspect[id] = seen;
                SuspectPositions[id] = position;
            }

            if (seen >= ConfirmFrames)
                Hide(control);
        }
        else
        {
            // Hide straight away; wait a frame or two in case the game resizes the frame itself.
            Hide(control);
            lock (Lock)
            {
                seen = Suspect.GetValueOrDefault(id) + 1;
                Suspect[id] = seen;
            }
        }

        if (seen < ConfirmFrames)
        {
            Queue(control);
            return;
        }

        lock (Lock)
        {
            Suspect.Remove(id);
            SuspectPositions.Remove(id);
        }

        if (kind == Overflow.Frame && TryWrap(control))
        {
            Redraw.Request(control);
            Queue(control);
            return;
        }

        StartSearch(control, id, kind);
    }

    /// <summary>Begins a search between the lowest allowed size and the current (too big) one.</summary>
    private static void StartSearch(Control control, ulong id, Overflow kind)
    {
        var factor = TextScaler.FactorFor(control);
        if (factor <= FloorFor(control) + 0.001f)
        {
            GiveUp(control, kind);
            return;
        }

        var search = new Search { Lo = FloorFor(control), Hi = factor, Kind = kind };
        lock (Lock)
            Searches[id] = search;

        Try(control, (search.Lo + search.Hi) / 2f);
    }

    private static void Continue(Control control, ulong id, Search search, bool over)
    {
        var current = TextScaler.FactorFor(control);
        if (over)
            search.Hi = current;
        else
            search.Lo = current;

        if (search.Hi - search.Lo > Precision)
        {
            Try(control, (search.Lo + search.Hi) / 2f);
            return;
        }

        // Close enough: settle on the largest size known to fit.
        if (!over && Mathf.IsEqualApprox(current, search.Lo))
        {
            lock (Lock)
                Searches.Remove(id);
            Show(control);
            return;
        }

        if (over && current <= FloorFor(control) + 0.001f)
        {
            lock (Lock)
                Searches.Remove(id);
            GiveUp(control, search.Kind);
            return;
        }

        Try(control, search.Lo);
    }

    private static void Try(Control control, float factor)
    {
        TextScaler.SetFitFactor(control, factor);
        Queue(control);
    }

    /// <summary>
    /// Nothing smaller is allowed. If wrapping was on, go back to one line and search again
    /// (never worse than a plain shrink). If the layout runs off the screen even at normal
    /// size, it's meant to scroll: restore full size and stop checking the screen edge for it.
    /// Otherwise show the text as it is.
    /// </summary>
    private static void GiveUp(Control control, Overflow kind)
    {
        if (kind == Overflow.Screen)
        {
            control.SetMeta(ScreenExemptKey, true);
            TextScaler.SetFitFactor(control, TextScaler.BaseScaleFor(control));
            Show(control);
            return;
        }

        if (control is Label label && label.HasMeta(WrappedKey))
        {
            label.RemoveMeta(WrappedKey);
            label.AutowrapMode = TextServer.AutowrapMode.Off;
            TextScaler.SetFitFactor(control, TextScaler.BaseScaleFor(control));
            Queue(control);
            return;
        }

        Show(control);
    }

    private static void Hide(Control control)
    {
        if (control.HasMeta(HiddenKey))
            return;

        control.SetMeta(HiddenKey, control.SelfModulate);
        control.SelfModulate = control.SelfModulate with { A = 0f };
    }

    private static void Show(Control control)
    {
        if (!control.HasMeta(HiddenKey))
            return;

        control.SelfModulate = control.GetMeta(HiddenKey).AsColor();
        control.RemoveMeta(HiddenKey);
    }

    /// <summary>Drops any in-progress check for a control that left the screen.</summary>
    private static void Abandon(Control control)
    {
        var id = control.GetInstanceId();
        lock (Lock)
        {
            Suspect.Remove(id);
            Searches.Remove(id);
            SuspectPositions.Remove(id);
        }

        Show(control);
    }

    /// <summary>
    /// Lets a one-line label wrap when it sits directly on a frame (not in a layout container,
    /// where wrapping could squash it to one word per line) and the frame has room for at least
    /// two lines.
    /// </summary>
    private static bool TryWrap(Control control)
    {
        if (control is not Label label
            || label.AutowrapMode != TextServer.AutowrapMode.Off
            || label.HasMeta(WrapTriedKey)
            || label.GetParent() is Container
            || !label.Text.Contains(' ')
            || MegaText.UsesAutoSize(label))
            return false;

        label.SetMeta(WrapTriedKey, true);

        var (frame, _) = FindFrame(label);
        if (frame is null)
            return false;

        var font = label.LabelSettings?.Font ?? label.GetThemeFont("font");
        var size = label.LabelSettings?.FontSize ?? label.GetThemeFontSize("font_size");
        if (frame.Size.Y < font.GetHeight(size) * 2f)
            return false;

        label.SetMeta(WrappedKey, true);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        return true;
    }

    internal static bool Overflows(Control control) => Classify(control) != Overflow.None;

    private static Overflow Classify(Control control)
    {
        if (IsCutOff(control))
            return Overflow.Frame;

        var (frame, scrollable) = FindFrame(control);
        if (frame is not null)
            return SticksOutOfFrame(control, frame) ? Overflow.Frame : Overflow.None;

        return !scrollable && !control.HasMeta(ScreenExemptKey) && PushesOffScreen(control)
            ? Overflow.Screen
            : Overflow.None;
    }

    /// <summary>Text that no longer fits inside its own box and gets clipped or trimmed.</summary>
    private static bool IsCutOff(Control control)
    {
        switch (control)
        {
            case Label label:
                if (string.IsNullOrEmpty(label.Text))
                    return false;

                if (label.GetLineCount() > label.GetVisibleLineCount())
                    return true;

                if (label.AutowrapMode == TextServer.AutowrapMode.Off
                    && (label.ClipText || label.TextOverrunBehavior != TextServer.OverrunBehavior.NoTrimming))
                {
                    var font = label.LabelSettings?.Font ?? label.GetThemeFont("font");
                    var size = label.LabelSettings?.FontSize ?? label.GetThemeFontSize("font_size");
                    var width = font.GetMultilineStringSize(label.Text, label.HorizontalAlignment, -1, size).X;
                    return width > label.Size.X + Tolerance;
                }

                return false;

            case RichTextLabel rich:
                return !string.IsNullOrEmpty(rich.Text)
                       && rich.IsFinished()
                       && (rich.GetContentHeight() > rich.Size.Y + Tolerance
                           || rich.GetContentWidth() > rich.Size.X + Tolerance);

            default:
                return false;
        }
    }

    /// <summary>Text (or the containers it pushed bigger) poking outside its frame.</summary>
    private static bool SticksOutOfFrame(Control control, Control frame)
    {
        // Compare in the frame's own coordinates, so tilted or scaled things (like the fanned-out
        // cards in your hand) are measured correctly.
        var bounds = new Rect2(Vector2.Zero, frame.Size).Grow(Tolerance);
        var toFrame = frame.GetGlobalTransform().AffineInverse();
        for (Node? node = control; node is Control current && node != frame; node = node.GetParent())
        {
            if (!bounds.Encloses(RectIn(toFrame, current)))
                return true;
        }

        return false;
    }

    /// <summary>The control's rectangle, as seen from the frame (axis-aligned bounds).</summary>
    private static Rect2 RectIn(Transform2D toFrame, Control control)
    {
        var transform = toFrame * control.GetGlobalTransform();
        var size = control.Size;
        Vector2[] corners = [Vector2.Zero, new(size.X, 0f), new(0f, size.Y), size];

        var first = transform * corners[0];
        var rect = new Rect2(first, Vector2.Zero);
        foreach (var corner in corners)
            rect = rect.Expand(transform * corner);

        return rect;
    }

    /// <summary>
    /// Text in a stack of layout containers that runs past the bottom or right edge of the
    /// screen while its top-left corner is on screen: the stack grew off the screen, for
    /// example story text pushing an event's options down out of view.
    /// </summary>
    private static bool PushesOffScreen(Control control)
    {
        if (control.GetParent() is not Container)
            return false; // only stacks of containers grow into each other

        var screen = control.GetViewportRect().Grow(Tolerance);
        for (Node? node = control; node is Control current; node = current.GetParent())
        {
            var rect = RectOnScreen(current);
            var topLeftOnScreen = screen.HasPoint(rect.Position);
            if (topLeftOnScreen && (rect.End.X > screen.End.X || rect.End.Y > screen.End.Y))
                return true;

            if (current.GetParent() is not Container || current.GetParent() is ScrollContainer)
                break; // reached the top of the stack
        }

        return false;
    }

    private static Rect2 RectOnScreen(Control control)
    {
        var transform = control.GetGlobalTransformWithCanvas();
        var size = control.Size;
        Vector2[] corners = [Vector2.Zero, new(size.X, 0f), new(0f, size.Y), size];

        var rect = new Rect2(transform * corners[0], Vector2.Zero);
        foreach (var corner in corners)
            rect = rect.Expand(transform * corner);

        return rect;
    }

    /// <summary>
    /// Finds the visible frame bounding the text, if any. Scrollable is true when the text is in a
    /// scroll area, where neither frames nor the screen edge constrain it.
    /// </summary>
    private static (Control? Frame, bool Scrollable) FindFrame(Control control)
    {
        var viewport = control.GetViewportRect().Size;
        Node branch = control;
        for (var node = control.GetParent(); node is Control parent; branch = parent, node = parent.GetParent())
        {
            if (parent is ScrollContainer)
                return (null, true); // scrollable content isn't constrained

            if (parent is Container)
                continue; // containers grow with their contents

            var rect = parent.GetGlobalRect();
            if (rect.Size.X >= viewport.X * 0.9f && rect.Size.Y >= viewport.Y * 0.9f)
                return (null, false); // full-screen: only the screen edge bounds it

            if (rect.Size.X >= 8f && rect.Size.Y >= 8f && IsVisibleFrame(parent, branch))
                return (parent, false);

            // Invisible holders and tiny positioning anchors don't bound anything; keep looking.
        }

        return (null, false);
    }

    /// <summary>
    /// True when the parent visibly bounds the text: it draws something itself, or it has a
    /// background child (outside the text's own branch) covering most of it.
    /// </summary>
    private static bool IsVisibleFrame(Control parent, Node textBranch)
    {
        if (DrawsBackground(parent))
            return true;

        var area = parent.Size.X * parent.Size.Y;
        if (area <= 0f)
            return false;

        foreach (var child in parent.GetChildren())
        {
            if (child == textBranch || child is not Control { Visible: true } background || !DrawsBackground(background))
                continue;

            var covered = new Rect2(background.Position, background.Size * background.Scale)
                .Intersection(new Rect2(Vector2.Zero, parent.Size));
            if (covered.Size.X * covered.Size.Y >= area * BackgroundCoverage)
                return true;
        }

        return false;
    }

    private static bool DrawsBackground(Control control) => control switch
    {
        TextureRect texture => texture.Texture is not null,
        NinePatchRect patch => patch.Texture is not null,
        ColorRect color => color.Color.A > 0.05f,
        Panel or PanelContainer or BaseButton => true,
        _ => false,
    };
}
