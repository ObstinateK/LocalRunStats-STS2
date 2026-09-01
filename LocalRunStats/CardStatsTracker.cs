using System.Collections.Generic;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace LocalRunStats;

// Times Played / Times Drawn per card, for the current run — inspired by
// https://github.com/rmac-silva/CardTracker, adapted to this mod's
// SingletonModel/AbstractModel-override convention instead of that mod's
// Harmony-patching Hook directly (both work; this one avoids Harmony for
// the two hooks that already have a clean virtual override — see
// CardStatsTooltipPatch.cs for the one part that still needs Harmony, since
// there's no AbstractModel hook for "a card's tooltip is being built").
//
// Keyed by card id + upgraded state (an upgraded Strike is tracked
// separately from a base Strike) — not enchantment/"generated this combat"
// variants like the reference mod distinguishes; simpler scope, can be
// extended later if that granularity turns out to matter.
public sealed class CardStatsTracker : SingletonModel
{
    public static CardStatsTracker Instance => ModelDb.Singleton<CardStatsTracker>();

    public override bool ShouldReceiveCombatHooks => true;

    private sealed class Stats
    {
        public int Played;
        public int Drawn;
    }

    private readonly Dictionary<string, Stats> _stats = new();

    public void ResetForNewRun() => _stats.Clear();

    public override System.Threading.Tasks.Task AfterCardPlayed(PlayerChoiceContext context, CardPlay cardPlay)
    {
        if (cardPlay?.Card != null) GetOrCreate(cardPlay.Card).Played++;
        return System.Threading.Tasks.Task.CompletedTask;
    }

    // Signature confirmed by decompiling Hook.AfterCardDrawn: single
    // unambiguous CardModel param, no swap risk.
    public override System.Threading.Tasks.Task AfterCardDrawn(PlayerChoiceContext context, CardModel card, bool fromHandDraw)
    {
        if (card != null) GetOrCreate(card).Drawn++;
        return System.Threading.Tasks.Task.CompletedTask;
    }

    private Stats GetOrCreate(CardModel card)
    {
        var key = GetKey(card);
        if (!_stats.TryGetValue(key, out var stats))
        {
            stats = new Stats();
            _stats[key] = stats;
        }
        return stats;
    }

    private static string GetKey(CardModel card) => card.IsUpgraded ? card.Id.Entry + "_UPGRADED" : card.Id.Entry;

    // Returns null if we have no data for this card yet, so the tooltip
    // patch can fall back to the game's own default tooltip untouched.
    public string GetStatsText(CardModel card)
    {
        if (!_stats.TryGetValue(GetKey(card), out var stats)) return null;
        var playRate = stats.Drawn > 0 ? (stats.Played / (float)stats.Drawn * 100f).ToString("0") + "%" : "N/A";
        return $"Times Played: {stats.Played}\nTimes Drawn: {stats.Drawn}\nPlay Rate: {playRate}";
    }
}
