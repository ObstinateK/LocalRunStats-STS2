using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.HoverTips;

namespace LocalRunStats;

// CreateHoverTips is what actually builds the hover tooltip shown for a
// card sitting in a "holder" — but it's `protected virtual` on the base
// NCardHolder AND overridden separately by two subclasses (confirmed via
// reflection: NGridCardHolder/NHandCardHolder inherit the base unchanged,
// but NPreviewCardHolder and NSelectedHandCardHolder both declare their own
// CreateHoverTips). Patching only the base method silently misses whichever
// holder type is actually used at the moment you hover — confirmed live:
// "hovering isnt showing any stats" even with the base patch working
// correctly and card stats being tracked fine underneath. Most notably,
// NSelectedHandCardHolder.CreateHoverTips() — almost certainly what's
// active while hovering a card in hand during combat — decompiles to a
// completely EMPTY override (shows nothing at all, by design), so all
// three call sites need their own patch.
internal static class CardStatsTooltipHelper
{
    // Returns null if we have no data for this card yet (caller should let
    // the original method run untouched in that case).
    public static HoverTip? BuildTip(CardModel model)
    {
        var statsText = CardStatsTracker.Instance.GetStatsText(model);
        if (statsText == null) return null;

        // Id/IsSmart have public setters (confirmed via reflection AND the
        // compiler), but Title/Description/Icon's setters exist but aren't
        // public — reflection alone didn't reveal that (GetSetMethod(true)
        // finds non-public setters too), the compiler did (CS0200). Same
        // Harmony Traverse technique as
        // https://github.com/rmac-silva/CardTracker's CardTooltipPatch,
        // which this feature is adapted from, for those three; HoverTip is
        // a value type, so the mutated copy has to be read back out via
        // GetValue<HoverTip>() at the end rather than mutated in place.
        var tip = new HoverTip(ModelDb.Power<StrengthPower>(), "", false)
        {
            Id = "LocalRunStats_CardStats",
            IsSmart = false,
        };
        var traverse = Traverse.Create(tip);
        traverse.Property("Title").SetValue("Card Stats");
        traverse.Property("Description").SetValue(statsText);
        traverse.Property("Icon").SetValue(null);
        return traverse.GetValue<HoverTip>();
    }
}

// Base holder — used as-is by NGridCardHolder/NHandCardHolder (neither
// overrides CreateHoverTips). Original body:
// `NHoverTipSet.CreateAndShow(this, CardNode.Model.HoverTips)?...`.
// Appends to a copy of the card's own HoverTips list rather than replacing
// it, so its normal keyword tooltips stay intact.
[HarmonyPatch(typeof(NCardHolder), "CreateHoverTips")]
internal static class CardStatsTooltipPatch
{
    private static bool Prefix(NCardHolder __instance)
    {
        var model = __instance.CardNode?.Model;
        if (model == null) return true;
        var tip = CardStatsTooltipHelper.BuildTip(model);
        if (tip == null) return true;

        var combinedTips = model.HoverTips.ToList();
        combinedTips.Add(tip.Value);
        NHoverTipSet.CreateAndShow(__instance, combinedTips)?.SetAlignmentForCardHolder(__instance);
        return false;
    }
}

// NPreviewCardHolder's own override mirrors the base exactly (same logic,
// just referencing `this` instead of an inherited call) — same fix.
[HarmonyPatch(typeof(NPreviewCardHolder), "CreateHoverTips")]
internal static class PreviewCardStatsTooltipPatch
{
    private static bool Prefix(NPreviewCardHolder __instance)
    {
        var model = __instance.CardNode?.Model;
        if (model == null) return true;
        var tip = CardStatsTooltipHelper.BuildTip(model);
        if (tip == null) return true;

        var combinedTips = model.HoverTips.ToList();
        combinedTips.Add(tip.Value);
        NHoverTipSet.CreateAndShow(__instance, combinedTips)?.SetAlignmentForCardHolder(__instance);
        return false;
    }
}

// NSelectedHandCardHolder's own override is EMPTY — shows nothing at all by
// design. When we have stats, show a tooltip with JUST the stats tip
// (deliberately not reviving the normal keyword tooltips the game chose to
// suppress here) rather than guessing why it's suppressed and overstepping;
// when we have no data, preserve the original no-op behavior exactly.
[HarmonyPatch(typeof(NSelectedHandCardHolder), "CreateHoverTips")]
internal static class SelectedHandCardStatsTooltipPatch
{
    private static bool Prefix(NSelectedHandCardHolder __instance)
    {
        var model = __instance.CardNode?.Model;
        if (model == null) return true;
        var tip = CardStatsTooltipHelper.BuildTip(model);
        if (tip == null) return true;

        NHoverTipSet.CreateAndShow(__instance, new List<IHoverTip> { tip.Value })?.SetAlignmentForCardHolder(__instance);
        return false;
    }
}
