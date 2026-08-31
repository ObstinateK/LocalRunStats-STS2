using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.ValueProps;

namespace LocalRunStats;

// Registered via ModHelper.SubscribeForCombatStateHooks as a lightweight custom
// SingletonModel that only exists to listen for combat hooks (per ModHelper's own
// doc comment, this is the intended pattern for mods providing custom listeners).
//
// IMPORTANT: never construct this with `new` — ModelDb.Init() auto-discovers every
// AbstractModel subtype across loaded assemblies (mod assemblies included) via
// reflection and constructs the one canonical instance itself at startup. Calling
// the constructor ourselves throws DuplicateModelException ("Don't call
// constructors on models! Use ModelDb instead."). Always fetch the existing
// instance through ModelDb.Singleton<T>() instead.
public sealed class CombatStatsListener : SingletonModel
{
    public static CombatStatsListener Instance => ModelDb.Singleton<CombatStatsListener>();

    private int _damageDealt;
    private int _damageTaken;
    private int _damageBlocked;

    // Keyed by Player.NetId — multiplayer-aware (co-op has multiple distinct
    // player Creatures, each dealing/taking damage independently). Read by
    // CombatDamageHud for the live panels and persisted per-fight below.
    private readonly Dictionary<ulong, PlayerDamageTracker> _damageByPlayer = new();

    public IReadOnlyDictionary<ulong, PlayerDamageTracker> DamageByPlayer => _damageByPlayer;

    public override bool ShouldReceiveCombatHooks => true;

    // This SingletonModel lives for the whole game process, not per-run — the
    // AbstractModel hook API has no dedicated "run started" event, so this is
    // called from CombatDamageHudPatch instead, which already fires exactly
    // once per new run (NRun._Ready on a fresh NRun instance). Without this,
    // starting a new run after abandoning/finishing one leaves the previous
    // run's per-player/per-act damage numbers showing in the new run's HUD —
    // and inflates the table's act-column count too, since that's derived
    // from the max act index across all cached data.
    public void ResetForNewRun() => _damageByPlayer.Clear();

    // Parameter names/order verified by decompiling the real caller,
    // Hook.AfterDamageGiven: `model.AfterDamageGiven(choiceContext, dealer,
    // results, props, target, cardSource)`. An earlier version of this method
    // had the two Creature parameters swapped (named "target" for position 2
    // and "source" for position 5) — meaning "damage dealt" was actually
    // measuring damage taken, since position 2 is really the attacker and
    // position 5 is really the one being hit. Fixed; keep the real names here
    // so this doesn't happen again.
    public override Task AfterDamageGiven(PlayerChoiceContext context, Creature dealer, DamageResult damageResult, ValueProp props, Creature target, CardModel card)
    {
        if (dealer != null && dealer.IsPlayer)
        {
            // UnblockedDamage, not TotalDamage: TotalDamage is the attack's raw
            // output before the target's own Block absorbs part of it. "Damage
            // dealt"/"taken" both mean HP actually removed, not raw attack value.
            _damageDealt += damageResult.UnblockedDamage;
            if (dealer.Player != null)
            {
                GameContext.LocalPlayer = dealer.Player;
                GetOrCreateTracker(dealer.Player).CurrentFightDealt += damageResult.UnblockedDamage;
                CombatDamageHud.RefreshAll();
            }
        }
        if (target != null && target.IsPlayer)
        {
            _damageTaken += damageResult.UnblockedDamage;
            _damageBlocked += damageResult.BlockedDamage;
            if (target.Player != null)
            {
                GameContext.LocalPlayer = target.Player;
                GetOrCreateTracker(target.Player).CurrentFightTaken += damageResult.UnblockedDamage;
                CombatDamageHud.RefreshAll();
            }
        }
        return Task.CompletedTask;
    }

    private PlayerDamageTracker GetOrCreateTracker(MegaCrit.Sts2.Core.Entities.Players.Player player)
    {
        if (!_damageByPlayer.TryGetValue(player.NetId, out var tracker))
        {
            tracker = new PlayerDamageTracker();
            _damageByPlayer[player.NetId] = tracker;
        }
        // Character can only be known once the player has loaded in, but
        // that's already true by the time they've dealt/taken damage — cheap
        // to just keep it in sync rather than caching it wrong on first sight.
        tracker.CharacterName = player.Character?.Title?.GetRawText() ?? "?";
        return tracker;
    }

    public override Task AfterCombatEnd(CombatRoom combatRoom)
    {
        try
        {
            WriteAggregateRecord(combatRoom);
            WritePerPlayerRecords(combatRoom);
            FoldCurrentFightIntoActTotals(combatRoom);
        }
        catch (Exception ex)
        {
            Log.Error("[LocalRunStats] Failed to record combat: " + ex);
        }
        finally
        {
            _damageDealt = 0;
            _damageTaken = 0;
            _damageBlocked = 0;
        }
        return Task.CompletedTask;
    }

    private void FoldCurrentFightIntoActTotals(CombatRoom combatRoom)
    {
        var actIndex = combatRoom.Act?.Index ?? 0;
        foreach (var tracker in _damageByPlayer.Values)
        {
            if (tracker.CurrentFightDealt != 0)
            {
                tracker.DealtByActIndex.TryGetValue(actIndex, out var existingDealt);
                tracker.DealtByActIndex[actIndex] = existingDealt + tracker.CurrentFightDealt;
                tracker.CurrentFightDealt = 0;
            }
            if (tracker.CurrentFightTaken != 0)
            {
                tracker.TakenByActIndex.TryGetValue(actIndex, out var existingTaken);
                tracker.TakenByActIndex[actIndex] = existingTaken + tracker.CurrentFightTaken;
                tracker.CurrentFightTaken = 0;
            }
        }
        CombatDamageHud.RefreshAll();
    }

    private static void WriteAggregateRecord(CombatRoom combatRoom)
    {
        var record = new CombatRecord
        {
            Timestamp = DateTime.UtcNow.ToString("o"),
            EncounterId = combatRoom.ModelId.ToString(),
            DamageDealt = Instance._damageDealt,
            DamageTaken = Instance._damageTaken,
            DamageBlocked = Instance._damageBlocked,
        };

        PlayerStatsLog.AppendJsonLine("combats.jsonl", record);
        Log.Info($"[LocalRunStats] Recorded combat: {record.EncounterId} dealt={record.DamageDealt} taken={record.DamageTaken} blocked={record.DamageBlocked}");
    }

    // Per-player breakdown of the fight that just ended — this is what feeds
    // the graph overlay's "Damage Dealt"/"Damage Taken" charts. Only players
    // who did something this fight get a row (mirrors the live HUD).
    private static void WritePerPlayerRecords(CombatRoom combatRoom)
    {
        var actIndex = combatRoom.Act?.Index ?? 0;
        var timestamp = DateTime.UtcNow.ToString("o");
        foreach (var (netId, tracker) in Instance._damageByPlayer)
        {
            if (tracker.CurrentFightDealt == 0 && tracker.CurrentFightTaken == 0) continue;
            var record = new PlayerCombatRecord
            {
                Timestamp = timestamp,
                ActIndex = actIndex,
                EncounterId = combatRoom.ModelId.ToString(),
                PlayerNetId = netId,
                CharacterName = tracker.CharacterName,
                DamageDealt = tracker.CurrentFightDealt,
                DamageTaken = tracker.CurrentFightTaken,
            };
            PlayerStatsLog.AppendJsonLine("player_combat_stats.jsonl", record);
        }
    }
}
