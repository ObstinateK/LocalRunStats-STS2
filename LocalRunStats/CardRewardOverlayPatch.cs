using System.Collections.Generic;
using System.Linq;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;

namespace LocalRunStats;

// Adds a small "Pick X% / Impact +Y%" label under each offered card on the
// card-reward screen, computed from HistoryStatsEngine.CardStats (this
// player's own history/*.run files — no community data, no network).
//
// RefreshOptions is the screen's own setup method: it receives exactly the
// offered CardCreationResults and (re)populates the NCardHolder row, including
// on reroll. GetCardHolder(CardModel) is the screen's own lookup for the
// holder Control backing a given card.
//
// Geometry note: the card art is not driven by Godot's normal Control layout
// (holder/CardNode/Body all report Size (0,0) even a frame later), so the
// offsets below were dialed in live with a dev-time slider panel rather than
// measured from Control rects. Re-tune if the card layout changes.
[HarmonyPatch(typeof(NCardRewardSelectionScreen), nameof(NCardRewardSelectionScreen.RefreshOptions))]
internal static class CardRewardOverlayPatch
{
    internal const string LabelName = "LocalRunStatsLabel";
    private const float LabelWidth = 230f;
    private const float LabelHeight = 200f;
    private const float LabelYOffset = 232f;
    private const float LabelXOffset = -119f;

    // NCardHolder is a shared widget class, not exclusive to this screen —
    // NDeckViewScreen and friends (deck viewer, upgrade/transform/enchant
    // select) reuse it too. Our label was a permanent child with no cleanup,
    // so a holder decorated here that later got reused elsewhere kept
    // showing our stale reward-screen text — confirmed live: opening the
    // deck viewer showed Pick/Impact/Synergy on cards there. Track every
    // holder we've decorated so CardRewardOverlayCleanupPatch can strip the
    // label the moment this screen actually closes.
    internal static readonly List<NCardHolder> DecoratedHolders = new();

    private static void Postfix(NCardRewardSelectionScreen __instance, IReadOnlyList<CardCreationResult> options, IReadOnlyList<CardRewardAlternative> extraOptions)
    {
        try
        {
            foreach (var result in options)
            {
                var holder = __instance.GetCardHolder(result.Card);
                if (holder == null) continue;
                AttachOrUpdateLabel(holder, result.Card);
            }
        }
        catch (System.Exception ex)
        {
            Log.Error("[LocalRunStats] Failed to attach card reward stats overlay: " + ex);
        }
    }

    private static void AttachOrUpdateLabel(NCardHolder holder, CardModel card)
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
            DecoratedHolders.Add(holder);
        }

        label.Text = BuildStatsText(card);
    }

    private static string BuildStatsText(CardModel card)
    {
        var cardId = card.Id.ToString();
        string pickImpactLine;
        if (!HistoryStatsEngine.CardStats.TryGetValue(cardId, out var entry) || entry.TimesOffered == 0)
        {
            pickImpactLine = "Pick: --\nImpact: --";
        }
        else
        {
            var impactSign = entry.Impact >= 0 ? "+" : "";
            pickImpactLine = $"Pick {entry.PickRate:P0}  ({entry.TimesOffered}x)\nImpact {impactSign}{entry.Impact:P1}";
        }

        return pickImpactLine + "\n" + BuildSynergyLine(card);
    }

    // "Synergy" combines two signals:
    // - Deck-similarity-weighted win rate with vs. without this card, restricted
    //   to historical runs whose final deck overlapped with the deck you're
    //   currently running (does this help in decks like yours, empirically).
    // - A text-keyword overlap between this card's description and your current
    //   deck's (e.g. Dominate mentions Vulnerable/Strength — if your deck already
    //   plays with those, that's flagged even with thin/no history data for it).
    private static string BuildSynergyLine(CardModel card)
    {
        var deck = GameContext.LocalPlayer?.Deck?.Cards;
        if (deck == null || deck.Count == 0)
        {
            return "Synergy: --";
        }

        var currentDeckIds = deck.Select(c => c.Id.ToString()).ToList();
        var synergy = HistoryStatsEngine.ComputeSynergy(card.Id.ToString(), currentDeckIds);

        string line;
        if (!synergy.HasData)
        {
            line = "Synergy: --";
        }
        else
        {
            var sign = synergy.Synergy >= 0 ? "+" : "";
            line = $"Synergy {sign}{synergy.Synergy:P1} (n={synergy.TotalSimilarityWeight:0.#})";
        }

        var offeredKeywords = SynergyKeywords.ExtractKeywords(card);
        var deckKeywords = SynergyKeywords.ExtractDeckKeywords(deck);
        var matched = SynergyKeywords.Overlap(offeredKeywords, deckKeywords);
        if (matched.Count > 0)
        {
            line += $" [{string.Join(", ", matched)}]";
        }

        return line;
    }
}
