using Godot;
using HarmonyLib;

namespace TextSize;

/// <summary>
/// Recognises where a piece of text lives in the game's UI. The game types are looked up by
/// name so a game update that moves them only disables the card/tooltip-specific options.
/// </summary>
internal static class GameNodes
{
    private static readonly Type? CardType = AccessTools.TypeByName("MegaCrit.Sts2.Core.Nodes.Cards.NCard");
    private static readonly Type? TooltipType = AccessTools.TypeByName("MegaCrit.Sts2.Core.Nodes.HoverTips.NHoverTipSet");

    /// <summary>Inside a card (the game's NCard), including card previews in tooltips.</summary>
    public static bool IsInCard(Node node) => HasAncestor(node, CardType);

    /// <summary>Inside a hover tooltip (the game's NHoverTipSet).</summary>
    public static bool IsInTooltip(Node node) => HasAncestor(node, TooltipType);

    private static bool HasAncestor(Node node, Type? type)
    {
        if (type is null)
            return false;

        for (var parent = node.GetParent(); parent is not null; parent = parent.GetParent())
        {
            if (type.IsInstanceOfType(parent))
                return true;
        }

        return false;
    }
}
