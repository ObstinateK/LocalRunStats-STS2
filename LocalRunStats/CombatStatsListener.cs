using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
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

    // Doom kills carry no dealer reference (see AfterDiedToDoom below), so the
    // Applier of the creature's DoomPower is captured here in BeforeDeath —
    // the last point at which the dying creature's Powers list is still
    // intact, before CreatureCmd.KillWithoutCheckingWinCondition strips it via
    // RemoveAllPowersAfterDeath() (confirmed by decompile: BeforeDeath fires,
    // then powers are removed, then eventually Hook.AfterDiedToDoom fires for
    // the whole batch). Keyed by the dying Creature; consumed and removed in
    // AfterDiedToDoom, and cleared defensively at combat end/run reset so a
    // prevented death (Fairy in a Bottle, etc.) can't leak a stale entry.
    private readonly Dictionary<Creature, Creature> _pendingDoomApplier = new();

    // Last-observed CurrentHp per creature, refreshed on every real damage hit
    // (see AfterDamageGiven). Used to size a Doom kill's "damage dealt" value
    // accurately — see AfterDiedToDoom for why creature.CurrentHp/MaxHp can't
    // be read directly at that point.
    private readonly Dictionary<Creature, int> _lastKnownHp = new();

    // Per-fight card play counts, keyed by Player.NetId then card name.
    // Folded into card_play_fights.jsonl at AfterCombatEnd (one row per
    // player-card pair, mirroring WritePerPlayerRecords) so the graph
    // overlay can show a per-fight breakdown alongside the existing
    // whole-run card_plays.jsonl aggregate.
    private readonly Dictionary<ulong, Dictionary<string, int>> _cardPlayCountsThisFight = new();

    public override bool ShouldReceiveCombatHooks => true;

    // This SingletonModel lives for the whole game process, not per-run — the
    // AbstractModel hook API has no dedicated "run started" event, so this is
    // called from CombatDamageHudPatch instead, which already fires exactly
    // once per new run (NRun._Ready on a fresh NRun instance). Without this,
    // starting a new run after abandoning/finishing one leaves the previous
    // run's per-player/per-act damage numbers showing in the new run's HUD —
    // and inflates the table's act-column count too, since that's derived
    // from the max act index across all cached data.
    public void ResetForNewRun()
    {
        _damageByPlayer.Clear();
        _pendingDoomApplier.Clear();
        _cardPlayCountsThisFight.Clear();
        _lastKnownHp.Clear();
    }

    // Attribution is deliberately ASYMMETRIC between dealing and taking:
    //
    // Dealt: a pet's (IsPet, e.g. Necrobinder's Osty) damage counts toward its
    // owner's Damage Dealt. Osty has no attack move of its own (its
    // MonsterMoveStateMachine is a do-nothing state), but player-cast cards
    // can still make it deal damage directly (e.g. "Unleash": "Osty deals 6
    // damage...") — that's still damage the player caused and should count as
    // theirs.
    //
    // Taken: only the player's own combat Creature counts. Confirmed via
    // decompiling CreatureCmd.Damage that when Osty absorbs a hit via
    // DieForYou, the resulting DamageResult's Receiver (and therefore
    // AfterDamageGiven's `target`) is Osty itself, not the player — but
    // that's explicitly NOT wanted here: Osty soaking a hit should behave
    // like extra Block, not like the player taking damage, so it's excluded.
    private static Player ResolveDealerPlayer(Creature creature)
    {
        if (creature == null) return null;
        if (creature.IsPlayer) return creature.Player;
        if (creature.IsPet) return creature.PetOwner;
        return null;
    }

    private static Player ResolveTakenPlayer(Creature creature)
    {
        if (creature == null || !creature.IsPlayer) return null;
        return creature.Player;
    }

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
        // Track the target's CurrentHp after every real hit lands — see
        // AfterDiedToDoom, which needs to know a creature's HP immediately
        // before it dies to Doom, at a point where creature.CurrentHp has
        // already been zeroed out by the game itself.
        if (target != null) _lastKnownHp[target] = target.CurrentHp;

        // Poison ticks pass dealer=null — confirmed by decompiling
        // PoisonPower.AfterSideTurnStart: `CreatureCmd.Damage(ctx, base.Owner,
        // base.Amount, ..., dealer: null, cardSource: null)`. A poison tick is
        // self-inflicted by the power on its own owner each side-turn-start,
        // independent of who originally stacked it, so there's no dealer
        // reference at all here — which silently dropped every bit of poison
        // damage from "damage dealt" (the "taken" side still worked, since
        // that's target-based and doesn't need a dealer). Recovered by reading
        // the ticking creature's own PoisonPower.Applier, which is still set
        // at this point: PowerCmd.Decrement (which can remove the power once
        // it hits 0) only runs AFTER CreatureCmd.Damage/this hook completes.
        // Approximation, same caveat as Doom's Applier lookup: if two players
        // both poison the same enemy, only the most recent stacker's Applier
        // is on record. Gated strictly on `dealer == null` (not just a failed
        // ResolveDealerPlayer) so a normal enemy attack on an already-poisoned
        // player can't get misattributed to whoever applied that poison.
        var dealerPlayer = dealer != null
            ? ResolveDealerPlayer(dealer)
            : ResolveDealerPlayer(target?.Powers?.OfType<PoisonPower>().FirstOrDefault()?.Applier);
        if (dealerPlayer != null)
        {
            // UnblockedDamage, not TotalDamage: TotalDamage is the attack's raw
            // output before the target's own Block absorbs part of it. "Damage
            // dealt"/"taken" both mean HP actually removed, not raw attack value.
            _damageDealt += damageResult.UnblockedDamage;
            GameContext.LocalPlayer = dealerPlayer;
            GetOrCreateTracker(dealerPlayer).CurrentFightDealt += damageResult.UnblockedDamage;
            CombatDamageHud.RefreshAll();
        }
        var targetPlayer = ResolveTakenPlayer(target);
        if (targetPlayer != null)
        {
            _damageTaken += damageResult.UnblockedDamage;
            _damageBlocked += damageResult.BlockedDamage;
            GameContext.LocalPlayer = targetPlayer;
            GetOrCreateTracker(targetPlayer).CurrentFightTaken += damageResult.UnblockedDamage;
            CombatDamageHud.RefreshAll();
        }
        return Task.CompletedTask;
    }

    // Captures the Applier of a dying creature's DoomPower (if it has one)
    // before RemoveAllPowersAfterDeath strips it — see _pendingDoomApplier.
    public override Task BeforeDeath(Creature creature)
    {
        var doom = creature?.Powers?.OfType<DoomPower>().FirstOrDefault();
        if (doom != null) _pendingDoomApplier[creature] = doom.Applier;
        return Task.CompletedTask;
    }

    // Doom is NOT damage — confirmed by decompiling DoomPower.DoomKill, which
    // calls CreatureCmd.Kill() directly, never CreatureCmd.Damage() (the
    // shared path that fires AfterDamageGiven and that ordinary attacks,
    // Poison, Thorns, self-damage cards, etc. all already go through — so
    // those are already counted with no changes needed). Doom is an instant
    // execute with no damage number attached to it at all, so this
    // approximates "how much this execute was worth" as the creature's
    // remaining HP right before the kill (via _lastKnownHp — see
    // AfterDamageGiven), NOT its MaxHp. Using MaxHp was a real overcounting
    // bug: a creature already whittled down by normal attacks/poison before
    // Doom finishes it off would have that already-counted damage added a
    // SECOND time (once via the original hits' AfterDamageGiven, again here
    // via the full MaxHp). creature.CurrentHp/MaxHp can't be read directly at
    // this point either way — confirmed by decompiling
    // CreatureCmd.KillWithoutCheckingWinCondition: it drains CurrentHp to 0
    // via LoseHpInternal BEFORE Hook.BeforeDeath (and therefore well before
    // Hook.AfterDiedToDoom) ever fires, so by the time any of our hooks see
    // this creature, CurrentHp already reads 0. Falls back to MaxHp only if
    // the creature never took any tracked damage this fight (e.g. its Doom
    // stacks alone reached the kill threshold with no other hits landing),
    // in which case it's reasonable to assume it was still at full health.
    // Multiplayer attribution for the *dealt* side is also an approximation:
    // Doom doesn't carry a "who applied it" reference the way an attack's
    // dealer/target does, so an enemy dying to Doom is credited to
    // GameContext.LocalPlayer rather than whichever player's card/relic
    // actually caused it.
    public override Task AfterDiedToDoom(PlayerChoiceContext context, IReadOnlyList<Creature> creatures)
    {
        foreach (var creature in creatures)
        {
            if (creature == null) continue;

            _pendingDoomApplier.Remove(creature, out var applier);
            var amount = _lastKnownHp.Remove(creature, out var hp) ? hp : creature.MaxHp;
            if (amount <= 0) continue; // already fully accounted for via normal damage

            // Pets (Osty) are out of scope entirely, same as in
            // AfterDamageGiven — Osty dying to Doom is neither the player
            // taking damage nor an enemy dying, so it's not attributed
            // anywhere.
            if (creature.IsPet) continue;

            // Player's own Creature dying to Doom counts as damage taken.
            var owningPlayer = ResolveTakenPlayer(creature);
            if (owningPlayer != null)
            {
                _damageTaken += amount;
                GetOrCreateTracker(owningPlayer).CurrentFightTaken += amount;
                continue;
            }

            // Enemy died to Doom -> credited as damage dealt. Prefer the
            // creature that actually applied the killing Doom stacks (captured
            // in BeforeDeath) over GameContext.LocalPlayer: that static is just
            // "whichever player most recently dealt/took damage or started a
            // turn," which in co-op is frequently the WRONG player — confirmed
            // live ("doom damage... the amount is given to another
            // character"). Falls back to GameContext.LocalPlayer only if no
            // DoomPower/Applier could be found (e.g. Doom applied by a source
            // with no Creature applier, like some relics).
            _damageDealt += amount;
            var creditedPlayer = ResolveDealerPlayer(applier) ?? GameContext.LocalPlayer;
            if (creditedPlayer != null) GetOrCreateTracker(creditedPlayer).CurrentFightDealt += amount;
        }
        CombatDamageHud.RefreshAll();
        return Task.CompletedTask;
    }

    // Signature confirmed via reflection: single unambiguous Player param, no
    // dealer/target-style swap risk.
    public override Task AfterPlayerTurnStart(PlayerChoiceContext context, Player player)
    {
        if (player != null)
        {
            GameContext.LocalPlayer = player;
            RunContext.EnsureBaselineGoldCaptured(player);
            GetOrCreateTracker(player).CurrentFightTurns++;
        }
        return Task.CompletedTask;
    }

    // CardPlay itself carries no Player/Owner reference (checked its full
    // property list), but CardModel.Owner does — the card knows whose deck
    // it belongs to, which is what we actually want here anyway (who played
    // it, not who it's currently targeting).
    public override Task AfterCardPlayed(PlayerChoiceContext context, CardPlay cardPlay)
    {
        var owner = cardPlay?.Card?.Owner;
        if (owner != null)
        {
            GetOrCreateTracker(owner).CurrentFightCardsPlayed++;
            PlayerStatsLog.AppendJsonLine("card_plays.jsonl", new CardPlayRecord
            {
                Timestamp = DateTime.UtcNow.ToString("o"),
                PlayerNetId = owner.NetId,
                CharacterName = owner.Character?.Title?.GetRawText() ?? "?",
                CardName = cardPlay.Card.Title,
            });

            if (!_cardPlayCountsThisFight.TryGetValue(owner.NetId, out var counts))
            {
                counts = new Dictionary<string, int>();
                _cardPlayCountsThisFight[owner.NetId] = counts;
            }
            counts.TryGetValue(cardPlay.Card.Title, out var existing);
            counts[cardPlay.Card.Title] = existing + 1;
        }
        return Task.CompletedTask;
    }

    private PlayerDamageTracker GetOrCreateTracker(Player player)
    {
        if (!_damageByPlayer.TryGetValue(player.NetId, out var tracker))
        {
            tracker = new PlayerDamageTracker { NetId = player.NetId };
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
            WriteCardPlayCountsByFight(combatRoom);
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
            // Any entries left here belong to deaths that were prevented
            // (Fairy in a Bottle, etc.) and never reached AfterDiedToDoom to
            // consume them — drop them rather than let them leak into the
            // next fight.
            _pendingDoomApplier.Clear();
            _cardPlayCountsThisFight.Clear();
            // Creature instances don't outlive their fight, so any entries
            // here belong to creatures that took damage but survived — drop
            // them rather than let the dictionary grow unbounded over a run.
            _lastKnownHp.Clear();
        }
        return Task.CompletedTask;
    }

    // One row per (player, card) pair played during the fight that just
    // ended, feeding the graph overlay's per-fight card breakdown. Shares the
    // CharacterName already populated in _damageByPlayer by GetOrCreateTracker
    // (called for every card play, so it's guaranteed set for anyone in here).
    private static void WriteCardPlayCountsByFight(CombatRoom combatRoom)
    {
        var actIndex = combatRoom.Act?.Index ?? 0;
        var timestamp = DateTime.UtcNow.ToString("o");
        foreach (var (netId, counts) in Instance._cardPlayCountsThisFight)
        {
            Instance._damageByPlayer.TryGetValue(netId, out var tracker);
            var characterName = tracker?.CharacterName ?? "?";
            foreach (var (cardName, count) in counts)
            {
                PlayerStatsLog.AppendJsonLine("card_play_fights.jsonl", new CardPlayCountRecord
                {
                    Timestamp = timestamp,
                    ActIndex = actIndex,
                    EncounterId = combatRoom.ModelId.ToString(),
                    PlayerNetId = netId,
                    CharacterName = characterName,
                    CardName = cardName,
                    Count = count,
                });
            }
        }
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
            // Turns/cards-played have no ByAct tracking (not shown on the live
            // HUD) — just reset for the next fight, after WritePerPlayerRecords
            // already persisted this fight's values.
            tracker.CurrentFightTurns = 0;
            tracker.CurrentFightCardsPlayed = 0;
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
            if (tracker.CurrentFightDealt == 0 && tracker.CurrentFightTaken == 0
                && tracker.CurrentFightTurns == 0 && tracker.CurrentFightCardsPlayed == 0) continue;
            var record = new PlayerCombatRecord
            {
                Timestamp = timestamp,
                ActIndex = actIndex,
                EncounterId = combatRoom.ModelId.ToString(),
                PlayerNetId = netId,
                CharacterName = tracker.CharacterName,
                DamageDealt = tracker.CurrentFightDealt,
                DamageTaken = tracker.CurrentFightTaken,
                TurnsTaken = tracker.CurrentFightTurns,
                CardsPlayed = tracker.CurrentFightCardsPlayed,
            };
            PlayerStatsLog.AppendJsonLine("player_combat_stats.jsonl", record);
        }
    }
}
