using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;

namespace LocalRunStats;

// Strips our stat labels the moment the card reward screen closes — see
// CardRewardOverlayPatch.DecoratedHolders for why this is needed (NCardHolder
// gets reused by other screens like the deck viewer, and without this our
// leftover label text showed up there too).
[HarmonyPatch(typeof(NCardRewardSelectionScreen), "_ExitTree")]
internal static class CardRewardOverlayCleanupPatch
{
    private static void Postfix()
    {
        try
        {
            foreach (var holder in CardRewardOverlayPatch.DecoratedHolders)
            {
                if (!GodotObject.IsInstanceValid(holder)) continue;
                var label = holder.GetNodeOrNull(CardRewardOverlayPatch.LabelName);
                label?.QueueFree();
            }
            CardRewardOverlayPatch.DecoratedHolders.Clear();
        }
        catch (System.Exception ex)
        {
            Log.Error("[LocalRunStats] Failed to clean up card reward stats overlay: " + ex);
        }
    }
}
