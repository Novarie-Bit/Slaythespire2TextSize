using Godot;

namespace TextSize;

/// <summary>
/// Some UI (cards, for example) can be drawn into an off-screen SubViewport that the game only
/// re-renders when it decides something changed. When the mod changes text inside one of
/// those, this asks for a re-render for the next few frames (while layout and auto-sizing
/// settle) so the new size actually shows up. Viewports that already redraw every frame are
/// left alone.
/// </summary>
internal static class Redraw
{
    private const int Frames = 3;

    private static readonly Dictionary<ulong, (SubViewport Viewport, int FramesLeft)> Pending = new();

    public static void Install(SceneTree tree) => tree.ProcessFrame += OnProcessFrame;

    /// <summary>Call from the main thread after changing text inside <paramref name="control"/>.</summary>
    public static void Request(Control control)
    {
        if (!control.IsInsideTree() || control.GetViewport() is not SubViewport viewport)
            return;

        Pending[viewport.GetInstanceId()] = (viewport, Frames);
    }

    private static void OnProcessFrame()
    {
        if (Pending.Count == 0)
            return;

        foreach (var (id, (viewport, framesLeft)) in Pending.ToList())
        {
            if (!GodotObject.IsInstanceValid(viewport) || framesLeft <= 0)
            {
                Pending.Remove(id);
                continue;
            }

            if (viewport.RenderTargetUpdateMode is SubViewport.UpdateMode.Disabled or SubViewport.UpdateMode.Once)
                viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once; // renders once, then back to Disabled

            Pending[id] = (viewport, framesLeft - 1);
        }
    }
}
