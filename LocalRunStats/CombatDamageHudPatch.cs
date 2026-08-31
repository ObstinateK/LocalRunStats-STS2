using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes;

namespace LocalRunStats;

// NRun is the run-scene root — it's the "always-visible run widget" host per
// the modding notes (persists across map, combat, and reward screens, unlike
// NCombatUi which only exists during a fight). Name-guarded like the other
// patches in this mod, since _Ready can fire again across scene reloads.
[HarmonyPatch(typeof(NRun), nameof(NRun._Ready))]
internal static class CombatDamageHudPatch
{
    private const string NodeName = "LocalRunStatsCombatDamageHud";

    private static void Postfix(NRun __instance)
    {
        try
        {
            if (__instance.GetNodeOrNull(NodeName) is not null) return;
            Log.Info("[LocalRunStats] NRun._Ready fired, attaching combat damage HUD.");
            CombatStatsListener.Instance.ResetForNewRun();
            RunContext.CurrentRunStartUtc = System.DateTime.UtcNow;
            var hud = new CombatDamageHud { Name = NodeName };
            __instance.AddChild(hud);
            HudTuningPanel.EnsureAttached(__instance);
            StatsGraphOverlay.EnsureAttached(__instance);
        }
        catch (System.Exception ex)
        {
            Log.Error("[LocalRunStats] Failed to attach combat damage HUD: " + ex);
        }
    }
}
