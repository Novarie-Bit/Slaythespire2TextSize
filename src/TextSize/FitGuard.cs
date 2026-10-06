using Godot;

namespace TextSize;

/// <summary>
/// Safety net that keeps enlarged text readable. After layout, each enlarged text control is
/// checked; if its text is cut off, or it now sticks out of the frame it sits in:
///
///  1. a one-line label sitting directly on a frame tall enough for another line is allowed
///     to wrap onto more lines, so it can stay big;
///  2. otherwise its size is stepped down (never below the game's normal size) until it fits.
///     If wrapping didn't help even at normal size, the label goes back to one line and the
///     search runs again, so it never ends up worse than a plain shrink.
///
/// "The frame" is the nearest parent that isn't a layout container. Containers grow with
/// their contents, but plain controls (buttons, panels, banners, card art...) don't, so text
/// sticking out of one is text spilling off the art. Scrollable areas and full-screen
/// parents don't count as frames.
/// </summary>
internal static class FitGuard
{
    /// <summary>How much the text size drops per step, as a fraction of normal size.</summary>
    private const float Step = 0.05f;

    /// <summary>Pixels of slack before counting something as sticking out.</summary>
    private const float Tolerance = 2f;

    /// <summary>
    /// Overflow must be seen this many frames in a row before shrinking, so frames that the
    /// game resizes to fit their text a moment later aren't mistaken for overflow.
    /// </summary>
    private const int ConfirmFrames = 2;

    private static readonly StringName WatchedKey = "textsize_mod_fit_watched";

    /// <summary>Set while the mod has switched a label to wrapping.</summary>
    private static readonly StringName WrappedKey = "textsize_mod_wrapped";

    /// <summary>Set once wrapping was tried and given up on, so it isn't tried again.</summary>
    private static readonly StringName WrapTriedKey = "textsize_mod_wrap_tried";

    private static readonly object Lock = new();
    private static readonly Dictionary<ulong, Control> Pending = new();
    private static readonly Dictionary<ulong, int> Suspect = new();
    private static readonly HashSet<ulong> Confirmed = new();

    public static void Install(SceneTree tree) => tree.ProcessFrame += OnProcessFrame;

    /// <summary>Checks the control after the next layout, and again whenever it resizes.</summary>
    public static void Watch(Control control)
    {
        if (TextSizeConfig.Scale <= 1f)
            return; // smaller-than-normal text always fits

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
            Confirmed.Clear();
        }
    }

    /// <summary>Undoes any wrapping the mod switched on, giving the control a fresh start.</summary>
    public static void Forget(Control control)
    {
        control.RemoveMeta(WrapTriedKey);
        if (control is Label label && label.HasMeta(WrappedKey))
        {
            label.RemoveMeta(WrappedKey);
            label.AutowrapMode = TextServer.AutowrapMode.Off;
        }
    }

    private static void Queue(Control control)
    {
        if (TextSizeConfig.Scale <= 1f)
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
            if (!GodotObject.IsInstanceValid(control) || !control.IsInsideTree() || !control.IsVisibleInTree())
                continue;

            var id = control.GetInstanceId();
            if (!Overflows(control))
            {
                lock (Lock)
                {
                    Suspect.Remove(id);
                    Confirmed.Remove(id);
                }
                continue;
            }

            // The first time, wait for the overflow to persist before reacting; once confirmed,
            // keep adjusting every frame until it fits.
            bool act;
            lock (Lock)
            {
                if (Confirmed.Contains(id))
                {
                    act = true;
                }
                else
                {
                    var seen = Suspect.GetValueOrDefault(id) + 1;
                    Suspect[id] = seen;
                    act = seen >= ConfirmFrames;
                    if (act)
                    {
                        Suspect.Remove(id);
                        Confirmed.Add(id);
                    }
                }
            }

            if (!act)
            {
                Queue(control);
                continue;
            }

            if (Adjust(control))
            {
                Queue(control); // look again next frame
            }
            else
            {
                lock (Lock)
                    Confirmed.Remove(id); // nothing left to try: it's at the game's normal size
            }
        }
    }

    /// <summary>Makes one change towards fitting. Returns false when there's nothing left to try.</summary>
    private static bool Adjust(Control control)
    {
        if (TryWrap(control))
        {
            Redraw.Request(control);
            return true;
        }

        var factor = TextScaler.FactorFor(control);
        if (factor > 1f)
        {
            TextScaler.SetFitFactor(control, Math.Max(1f, factor - Step));
            return true;
        }

        // Even at normal size the wrapped text doesn't fit: back to one line and search again.
        if (control is Label label && label.HasMeta(WrappedKey))
        {
            label.RemoveMeta(WrappedKey);
            label.AutowrapMode = TextServer.AutowrapMode.Off;
            TextScaler.SetFitFactor(control, TextSizeConfig.Scale);
            return true;
        }

        return false;
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

        var frame = FindFrame(label);
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

    internal static bool Overflows(Control control) => IsCutOff(control) || SticksOutOfFrame(control);

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
    private static bool SticksOutOfFrame(Control control)
    {
        var frame = FindFrame(control);
        if (frame is null)
            return false;

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

    private static Control? FindFrame(Control control)
    {
        var viewport = control.GetViewportRect().Size;
        for (var node = control.GetParent(); node is Control parent; node = parent.GetParent())
        {
            if (parent is ScrollContainer)
                return null; // scrollable content isn't constrained

            if (parent is Container)
                continue; // containers grow with their contents

            var rect = parent.GetGlobalRect();
            if (rect.Size.X >= viewport.X * 0.9f && rect.Size.Y >= viewport.Y * 0.9f)
                return null; // full-screen: nothing to spill off

            if (rect.Size.X >= 8f && rect.Size.Y >= 8f)
                return parent;

            // Tiny controls are usually just positioning anchors; keep looking.
        }

        return null;
    }
}
