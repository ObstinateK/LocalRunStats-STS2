using System.Linq;
using MegaCrit.Sts2.Core.Models;

namespace LocalRunStats;

// Shared by RelicRewardOverlayPatch (NChooseARelicSelection) and
// RelicEventOptionOverlayPatch (NEventOptionButton) — same relic, same stats,
// two different host UIs.
public static class RelicStatsText
{
    public static string BuildStatsText(RelicModel relic)
    {
        var relicId = relic.Id.ToString();
        string pickImpactLine;
        if (!HistoryStatsEngine.RelicStats.TryGetValue(relicId, out var entry) || entry.TimesOffered == 0)
        {
            pickImpactLine = "Pick: --\nImpact: --";
        }
        else
        {
            var impactSign = entry.Impact >= 0 ? "+" : "";
            pickImpactLine = $"Pick {entry.PickRate:P0}  ({entry.TimesOffered}x)\nImpact {impactSign}{entry.Impact:P1}";
        }

        return pickImpactLine + "\n" + BuildSynergyLine(relic);
    }

    // Same two-signal approach as CardRewardOverlayPatch.BuildSynergyLine:
    // empirical win-rate synergy plus a text-keyword overlap check — for
    // relics, checked against both the current deck's cards and current
    // relics, since relic themes (e.g. Vulnerable-focused) usually pair with
    // cards rather than other relics.
    private static string BuildSynergyLine(RelicModel relic)
    {
        var player = GameContext.LocalPlayer;
        var relics = player?.Relics;
        var deck = player?.Deck?.Cards;

        string line;
        if (relics == null || relics.Count == 0)
        {
            line = "Synergy: --";
        }
        else
        {
            var currentRelicIds = relics.Select(r => r.Id.ToString()).ToList();
            var synergy = HistoryStatsEngine.ComputeRelicSynergy(relic.Id.ToString(), currentRelicIds);
            if (!synergy.HasData)
            {
                line = "Synergy: --";
            }
            else
            {
                var sign = synergy.Synergy >= 0 ? "+" : "";
                line = $"Synergy {sign}{synergy.Synergy:P1} (n={synergy.TotalSimilarityWeight:0.#})";
            }
        }

        var offeredKeywords = SynergyKeywords.ExtractKeywords(relic);
        var themeKeywords = SynergyKeywords.ExtractRelicKeywords(relics ?? System.Array.Empty<RelicModel>());
        if (deck != null) themeKeywords.UnionWith(SynergyKeywords.ExtractDeckKeywords(deck));
        var matched = SynergyKeywords.Overlap(offeredKeywords, themeKeywords);
        if (matched.Count > 0)
        {
            line += $" [{string.Join(", ", matched)}]";
        }

        return line;
    }
}
