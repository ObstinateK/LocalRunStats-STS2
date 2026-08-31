using System.Linq;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Relics;
using MegaCrit.Sts2.Core.Nodes.Screens;

namespace LocalRunStats;

// Same idea as CardRewardOverlayPatch but for the "choose a relic" screen.
// NChooseARelicSelection has no public RefreshOptions/GetHolder equivalent —
// its _Ready() builds one NRelicBasicHolder per offered RelicModel directly
// into the private _relicRow field, so we patch _Ready (Postfix) and read
// the offered relics straight off the holders it just created via reflection
// (NRelicBasicHolder._model is private; there's no public RelicModel getter).
[HarmonyPatch(typeof(NChooseARelicSelection), nameof(NChooseARelicSelection._Ready))]
internal static class RelicRewardOverlayPatch
{
    private const string LabelName = "LocalRunStatsRelicLabel";
    private const float LabelWidth = 230f;
    private const float LabelHeight = 200f;
    private const float LabelYOffset = 232f;
    private const float LabelXOffset = -119f;

    private static void Postfix(NChooseARelicSelection __instance)
    {
        try
        {
            var relicRow = Traverse.Create(__instance).Field("_relicRow").GetValue<Control>();
            if (relicRow == null) return;

            foreach (var holder in relicRow.GetChildren().OfType<NRelicBasicHolder>())
            {
                var model = Traverse.Create(holder).Field("_model").GetValue<RelicModel>();
                if (model == null) continue;
                AttachOrUpdateLabel(holder, model);
            }
        }
        catch (System.Exception ex)
        {
            Log.Error("[LocalRunStats] Failed to attach relic reward stats overlay: " + ex);
        }
    }

    private static void AttachOrUpdateLabel(NRelicBasicHolder holder, RelicModel relic)
    {
        var label = holder.GetNodeOrNull<Label>(LabelName);
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
            label.AnchorLeft = 0f;
            label.AnchorRight = 0f;
            label.AnchorTop = 0f;
            label.AnchorBottom = 0f;
            label.Position = new Vector2(LabelXOffset, LabelYOffset);
            label.Size = new Vector2(LabelWidth, LabelHeight);
            label.CustomMinimumSize = label.Size;
            holder.AddChild(label);
        }

        label.Text = RelicStatsText.BuildStatsText(relic);
    }
}
