using System;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;

namespace LocalRunStats;

// Same ModelDb.Singleton<T> pattern as the other listeners in this mod — see
// CombatStatsListener's doc comment for why `new` is never used on model
// types. Registered via ModHelper.SubscribeForRunStateHooks alongside
// RunStateListener.
//
// AbstractModel.AfterGoldGained(Player) signature confirmed by decompiling
// the real caller (Hook.AfterGoldGained) — single unambiguous Player param,
// no risk of the parameter-order mixup that bit AfterDamageGiven.
public sealed class GoldTracker : SingletonModel
{
    public static GoldTracker Instance => ModelDb.Singleton<GoldTracker>();

    public override bool ShouldReceiveCombatHooks => false;

    public override System.Threading.Tasks.Task AfterGoldGained(Player player)
    {
        try
        {
            // Last-resort fallback: normally RunStateListener.AfterRewardTaken
            // (or CombatStatsListener.AfterPlayerTurnStart) already captured
            // this player's starting gold before their first gain, but if
            // neither fired first for some reason, at least the baseline gets
            // written (even though by definition it's already post-gain at
            // this point) rather than never existing at all.
            RunContext.EnsureBaselineGoldCaptured(player);

            var record = new GoldRecord
            {
                Timestamp = DateTime.UtcNow.ToString("o"),
                StageIndex = RunContext.CurrentStageIndex,
                ActIndex = player.RunState?.CurrentActIndex ?? 0,
                PlayerNetId = player.NetId,
                CharacterName = player.Character?.Title?.GetRawText() ?? "?",
                CurrentGold = player.Gold,
            };
            PlayerStatsLog.AppendJsonLine("gold_log.jsonl", record);
        }
        catch (Exception ex)
        {
            Log.Error("[LocalRunStats] Failed to record gold gain: " + ex);
        }
        return System.Threading.Tasks.Task.CompletedTask;
    }
}
