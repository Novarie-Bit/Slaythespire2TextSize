using Godot;

namespace TextSize;

/// <summary>
/// Safety net that keeps enlarged text readable. After layout, each enlarged text control is
/// checked; if its text is cut off, or it now sticks out of the frame it sits in, its size is
/// stepped back down (never below the game's normal size) until it fits.
///
/// "The frame" is the nearest parent that isn't a layout container. Containers grow with
/// their contents, but plain controls (buttons, panels, banners, card art...) don't, so text
/// sticking out of one is text spilling off the art. Scrollable areas and full-screen
/// parents don't count as frames.
/// </summary>
internal static class FitGuard
{
    /// <summary>How much the text size drops per step, as a fraction of normal size.</summary>
    private const float Step = 0.1f;

    /// <summary>Pixels of slack before counting something as sticking out.</summary>
    private const float Tolerance = 2f;

    /// <summary>
    /// Overflow must be seen this many frames in a row before shrinking, so frames that the
    /// game resizes to fit their text a moment later aren't mistaken for overflow.
    /// </summary>
    private const int ConfirmFrames = 2;

    private static readonly StringName WatchedKey = "textsize_mod_fit_watched";

    private static readonly object Lock = new();
    private static readonly Dictionary<ulong, Control> Pending = new();
    private static readonly Dictionary<ulong, int> Suspect = new();

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
            Suspect.Clear();
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
                    Suspect.Remove(id);
                continue;
            }

            var factor = TextScaler.FactorFor(control);
            if (factor <= 1f)
                continue;

            int seen;
            lock (Lock)
            {
                seen = Suspect.GetValueOrDefault(id) + 1;
                Suspect[id] = seen;
            }

            if (seen >= ConfirmFrames)
            {
                lock (Lock)
                    Suspect.Remove(id);
                TextScaler.SetFitFactor(control, Math.Max(1f, factor - Step));
            }

            Queue(control); // look again next frame
        }
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

        var bounds = frame.GetGlobalRect().Grow(Tolerance);
        for (Node? node = control; node is Control current && node != frame; node = node.GetParent())
        {
            if (!bounds.Encloses(current.GetGlobalRect()))
                return true;
        }

        return false;
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
