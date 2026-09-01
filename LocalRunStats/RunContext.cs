using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Entities.Players;

namespace LocalRunStats;

// Marks when the current run started, so per-run-scoped views (the graph
// overlay) can filter out older runs' rows from the lifetime JSONL logs.
// Set from CombatDamageHudPatch, the same place CombatStatsListener.ResetForNewRun()
// is called — see that method's doc comment for why "NRun._Ready on a fresh
// NRun instance" is being used as the closest thing to a real "run started" event.
public static class RunContext
{
    public static DateTime CurrentRunStartUtc = DateTime.MinValue;

    // Players whose starting gold has already been logged this run — see
    // EnsureBaselineGoldCaptured.
    private static readonly HashSet<ulong> _playersWithBaselineGold = new();

    // Shared "how many fights have ended so far" counter, advanced once per
    // AfterCombatEnd (from CombatStatsListener) — NOT per player. Gold events
    // fire at independent wall-clock moments per player (unlike a fight's
    // PlayerCombatRecord rows, which all share one Timestamp written together
    // in one call), so grouping gold by raw Timestamp never actually aligns
    // two players' events onto the same x-axis tick — each real-world moment
    // gets its own tick, so one player's line always looks "one tick behind"
    // the other even though nothing is actually wrong with the data.
    // Stamping every GoldRecord with this shared stage number instead (see
    // GoldTracker/EnsureBaselineGoldCaptured) buckets both players' gold
    // changes within the same stage of the run onto the same tick, matching
    // how the Damage/Cards charts already group by fight rather than by raw
    // event order.
    public static int CurrentStageIndex { get; private set; }

    public static void ResetForNewRun()
    {
        CurrentRunStartUtc = DateTime.UtcNow;
        _playersWithBaselineGold.Clear();
        CurrentStageIndex = 0;
    }

    public static void AdvanceStage() => CurrentStageIndex++;

    // The gold chart only ever had rows from AfterGoldGained, so a player's
    // starting gold (100, same as vanilla STS) was invisible until their
    // first gold-gain event — and in co-op, players' first gains rarely land
    // on the same fight, so one player's line would sit flat near 0 while the
    // other's already reflected a real balance, looking "desynced" even
    // though both actually started even. Fixed by opportunistically writing
    // one baseline GoldRecord per player (first-seen-this-run only, via the
    // HashSet guard) stamped with CurrentRunStartUtc — earlier than any real
    // event, so it always sorts first — the moment ANY hook first hands us
    // that player, ideally before they've gained any gold at all. Called from
    // several early hooks (RunStateListener.AfterRewardTaken — the very first
    // screen of a run, before combat even exists — plus a couple of
    // combat-hook fallbacks) since there's no dedicated "run started" hook
    // that hands us the player list up front (see MOD_SPEC.md).
    public static void EnsureBaselineGoldCaptured(Player player)
    {
        if (player == null) return;
        if (!_playersWithBaselineGold.Add(player.NetId)) return;

        PlayerStatsLog.AppendJsonLine("gold_log.jsonl", new GoldRecord
        {
            Timestamp = CurrentRunStartUtc.ToString("o"),
            StageIndex = CurrentStageIndex,
            ActIndex = 0,
            PlayerNetId = player.NetId,
            CharacterName = player.Character?.Title?.GetRawText() ?? "?",
            CurrentGold = player.Gold,
        });
    }
}
