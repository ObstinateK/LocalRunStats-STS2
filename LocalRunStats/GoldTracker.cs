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
            var record = new GoldRecord
            {
                Timestamp = DateTime.UtcNow.ToString("o"),
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
