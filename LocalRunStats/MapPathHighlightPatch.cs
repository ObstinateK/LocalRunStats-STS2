using System.Collections.Generic;
using System.Linq;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;

namespace LocalRunStats;

// Adds a small goal-toggle button row to the native map screen and
// highlights the recommended remaining path using the game's own hover-
// highlight animation (NMapPoint.AnimHover/AnimUnhover — the same visual a
// node gets when you mouse over it) rather than drawing anything custom, so
// it looks native. AnimHover/AnimUnhover are private (confirmed via
// reflection), reached the same way as HoverTip's non-public setters
// elsewhere in this mod — Harmony's Traverse.
[HarmonyPatch(typeof(NMapScreen), nameof(NMapScreen.Open))]
internal static class MapPathHighlightPatch
{
    private const string NodeName = "LocalRunStatsPathAdvisor";

    // Persists across screen opens within a session so re-opening the map
    // keeps showing whichever goal you last picked, instead of resetting.
    private static MapPathAdvisor.Goal _selectedGoal = MapPathAdvisor.Goal.Elites;

    private static void Postfix(NMapScreen __instance)
    {
        if (__instance.GetNodeOrNull(NodeName) is not null)
        {
            RefreshHighlight(__instance);
            return;
        }

        var panel = new MapPathAdvisorPanel { Name = NodeName };
        panel.Initialize(__instance, _selectedGoal, goal =>
        {
            _selectedGoal = goal;
            RefreshHighlight(__instance);
        });
        __instance.AddChild(panel);
        RefreshHighlight(__instance);
    }

    private static void RefreshHighlight(NMapScreen screen)
    {
        // Reads NMapScreen's OWN private _runState field directly, instead
        // of GameContext.LocalPlayer?.RunState — the latter is only
        // populated by this mod's OWN combat/reward hooks (AfterDamageGiven,
        // AfterPlayerTurnStart, AfterRewardTaken), and the very first time
        // the map opens in a run (right after the Ancient/Neow-equivalent
        // choice, before any combat) none of those have necessarily fired
        // yet — confirmed live: "map highlights did not work after ancient
        // ... but it worked after i did the first combat." NMapScreen
        // itself already has a valid RunState by the time Open() runs (it
        // uses it internally throughout that method), so reading it
        // directly sidesteps this mod's own hook-timing entirely.
        var runState = Traverse.Create(screen).Field("_runState").GetValue<IRunState>();
        if (runState?.Map == null) return;

        var from = runState.CurrentMapPoint ?? runState.Map.StartingMapPoint;
        var path = MapPathAdvisor.ComputeBestPath(from, _selectedGoal);
        // path[0] is the room the player is already standing in (or the
        // starting point pre-act) — only the REMAINING recommended picks
        // are worth highlighting.
        var toHighlight = new HashSet<MapPoint>(path.Skip(1));

        foreach (var node in FindAllMapPointNodes(screen))
        {
            var point = node.Point;
            if (point == null) continue;
            var traverse = Traverse.Create(node);
            if (toHighlight.Contains(point)) traverse.Method("AnimHover").GetValue();
            else traverse.Method("AnimUnhover").GetValue();
        }
    }

    private static IEnumerable<NMapPoint> FindAllMapPointNodes(Node root)
    {
        foreach (var child in root.GetChildren())
        {
            if (child is NMapPoint mapPoint) yield return mapPoint;
            foreach (var nested in FindAllMapPointNodes(child)) yield return nested;
        }
    }
}

// Clears any lingering highlight when the map screen closes, so a node we
// hover-highlighted doesn't stay stuck in that state after leaving the map.
[HarmonyPatch(typeof(NMapScreen), nameof(NMapScreen.Close))]
internal static class MapPathHighlightClearPatch
{
    private static void Prefix(NMapScreen __instance)
    {
        foreach (var child in __instance.GetChildren())
        {
            RecurseUnhover(child);
        }
    }

    private static void RecurseUnhover(Node node)
    {
        if (node is NMapPoint mapPoint) Traverse.Create(mapPoint).Method("AnimUnhover").GetValue();
        foreach (var child in node.GetChildren()) RecurseUnhover(child);
    }
}
