using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Events;

namespace LocalRunStats;

// The "Ancient blessing" / Neow's-choice-style screens (vertical list of
// options with icon + title + description) render each option as an
// NEventOptionButton — used for every event choice, not just relics, but
// Option.Relic is non-null specifically when that row grants a relic. This
// is a different widget entirely from NChooseARelicSelection (the horizontal
// scaled-card relic picker) — same underlying relic stats, different host UI.
//
// Patch set_Option, not _Ready: _Ready fires on scene-tree entry, but Option
// is assigned separately by the parent afterwards (confirmed live — only the
// first button of three had Option populated by the time _Ready ran).
//
// Geometry dialed in live with the (now-retired) OverlayTuning slider panel.
[HarmonyPatch(typeof(NEventOptionButton), nameof(NEventOptionButton.Option), MethodType.Setter)]
internal static class RelicEventOptionOverlayPatch
{
    private const string LabelName = "LocalRunStatsRelicRowLabel";
    private const float LabelWidth = 346f;
    private const float LabelHeight = 64f;
    private const float LabelYOffset = 23f;
    private const float LabelXOffset = -197f;

    private static void Postfix(NEventOptionButton __instance)
    {
        try
        {
            var relic = __instance.Option?.Relic;
            if (relic == null) return;

            AttachOrUpdateLabel(__instance, relic);
        }
        catch (System.Exception ex)
        {
            Log.Error("[LocalRunStats] Failed to attach relic event option overlay: " + ex);
        }
    }

    private static void AttachOrUpdateLabel(NEventOptionButton button, RelicModel relic)
    {
        var label = button.GetNodeOrNull<Label>(LabelName);
        if (label == null)
        {
            label = new Label
            {
                Name = LabelName,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                HorizontalAlignment = HorizontalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            };
            label.AddThemeColorOverride("font_color", new Color(0.75f, 0.9f, 1f));
            label.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.9f));
            label.AddThemeConstantOverride("shadow_offset_x", 1);
            label.AddThemeConstantOverride("shadow_offset_y", 1);
            label.AddThemeFontSizeOverride("font_size", 11);
            label.AnchorLeft = 0f;
            label.AnchorRight = 0f;
            label.AnchorTop = 0f;
            label.AnchorBottom = 0f;
            label.Position = new Vector2(LabelXOffset, LabelYOffset);
            label.Size = new Vector2(LabelWidth, LabelHeight);
            label.CustomMinimumSize = label.Size;
            button.AddChild(label);
        }

        label.Text = RelicStatsText.BuildStatsText(relic);
    }
}
