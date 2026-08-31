# Local Run Stats — Mod Spec

Run statistics overlay for Slay the Spire 2, in the spirit of
[No Rogues](https://github.com/sebastientromp/no-rogues-releases) (card pick
win rates, combat damage tracking, run history) but as a self-contained mod —
no companion app, no backend of our own to run/host. Everything lives in this
mod, reading from disk next to the game plus one read-only community API call.

Edit this file to steer direction; treat it as the source of truth for scope.

## Non-goals

- No server/backend of our own, no accounts, no uploading this player's data
  anywhere.
- **Revised 2026-08-28**: originally "no network calls of any kind" — changed
  on request to pull card/relic stats from sts2runs.com's public community
  API (thousands of runs, vs. a few dozen local ones) instead of this
  player's own local history. See "Data sources" below for exactly what's
  fetched and why. Still no calls that send this player's own data out —
  only GETs against public/read-only endpoints.

## Data sources (as of 2026-08-28)

Three independent things feed the reward-screen overlay, and they answer
different questions — see "What each overlay line means" below for how they
show up together on one card/relic:

1. **Pick / Impact** — sts2runs.com's aggregate stats endpoint
   (`/api/runs/community?...&mode=stats&include_elo=1`), one GET, refreshed
   once per game launch. ~9,225 community runs as of last check. Gives
   pick-rate and win-rate-with-vs-without-in-deck, per card/relic, across the
   whole community. See `HistoryStatsEngine.RefreshCommunityStatsAsync`.
2. **Synergy** — needs full per-run deck lists to compare against *this run's
   current deck*, which the aggregate endpoint above doesn't expose. Two
   sources are pooled together:
   - This player's own local `history/*.run` files (vanilla + modded
     profiles) — free, no network.
   - Individual community runs, each fetched from sts2runs.com's per-run
     detail endpoint (`/api/runs/{id}` — same raw schema as our local `.run`
     files) and cached to `mods/local-run-stats/community_run_cache.jsonl`.
     **Revised 2026-08-28**: originally capped at a 300-run sample; changed
     on request to target the *entire* community corpus (9,000+ runs and
     growing) instead. Since that's one HTTP request per run, fetching it
     all in one burst would hammer a small site's API, so each launch pulls
     up to `CommunityRunFetchBudgetPerSession` (1,500) new-to-us runs and
     stops — a full backfill takes several launches, and once caught up,
     each launch just tops up with whatever's new since last time (a page
     of already-known ids ends the scan early, so a fully-synced launch
     costs one request, not a full re-scan). See
     `HistoryStatsEngine.RefreshCommunityRunDetailsAsync`.
3. **Keyword tag** (the `[Vulnerable]`-style bracket) — not from either API.
   Plain-text keyword matching between this card/relic's description and
   your current deck/relics' descriptions, against STS2's confirmed mechanic
   list (Vulnerable, Weak, Poison, Strength, Dexterity, Block, Exhaust,
   Discard, Retain, Draw, Energy, Orb/Lightning/Frost/Dark/Plasma,
   Osty/Summon, Ethereal, Wound/Burn/Dazed/Void — sourced from
   sts2companion.com/synergies, corrected from an earlier guessed list that
   wrongly included STS1-only terms). Purely thematic — it can't tell
   "applies X" from "benefits from X", just that both mention it.

## What each overlay line means

```
Pick 45% (11x)        <- community: picked 5 of 11 times it was offered
Impact +42.4%         <- community: win rate with it in the final deck
                          minus win rate without (approximated from
                          community totals — see ParseCardEloRatings)
Synergy -20.8% (n=4.9) <- local + cached community runs: deck-similarity-
                          weighted win rate with vs. without THIS card,
                          restricted to past runs whose deck resembled
                          your CURRENT run's deck. n = effective sample
                          size (similarity-weighted, not a raw count) —
                          low n means take the % with a grain of salt.
[Vulnerable]           <- text-keyword overlap between this card's
                          description and your current deck's — thematic
                          flag only, not a value judgement.
```

Pick/Impact and Synergy can disagree, and that's expected: Pick/Impact is
"how does this do across the whole community, in general," while Synergy is
"in decks that looked like MY current one specifically, did this help." A
card can be broadly strong (high community Impact) but a poor fit for your
specific deck (negative Synergy), or vice versa.

## Resolved decisions

- **Display**: in-game overlay panel, directly on the card/relic reward
  screens (not a post-run summary or external viewer).
- **Scope of stats**: Pick rate ✓, Win-rate Impact ✓ (both card and relic),
  Synergy vs. current deck ✓, combat damage dealt/taken/blocked ✓, keyword
  overlap tag ✓. Card acquisition timeline done but superseded in practice
  by community Pick/Impact for reward-screen purposes.
- **Data retention**: unbounded — `runs.jsonl`/`combats.jsonl`/`card_picks.jsonl`
  are append-only local logs; `community_run_cache.jsonl` grows toward the
  300-run sample cap and then only adds newer runs going forward.

## Open questions

- **Multiplayer**: this game supports co-op (saw two player IDs in the log
  during testing) — stats currently track only the local player
  (`localPlayerId` in `OnMetricsUpload`, `Creature.IsPlayer` in combat
  hooks). Not revisited since — fine for solo play, untested in co-op.

## Technical notes (from reflecting the game's own sts2.dll)

- Native mod loader lives in `MegaCrit.Sts2.Core.Modding` — no BepInEx.
- Hook points available via `ModHelper`:
  - `SubscribeForRunStateHooks(string id, RunHookSubscriptionDelegate del)`
  - `SubscribeForCombatStateHooks(string id, CombatHookSubscriptionDelegate del)`
- `ModManager.OnMetricsUpload` / `CallMetricsHooks(SerializableRun run, bool isVictory, ulong localPlayerId)`
  looks like the hook the game itself uses for its own end-of-run stats —
  likely the cleanest place to capture a finished run's data.
- Entry point: `[ModInitializer("MethodName")]` on the class, matching
  static method inside. See `ModEntry.cs` for the working example.
- Manifest schema (`LocalRunStats.json`) verified against `ModManifest`:
  fields are `id`, `name`, `author`, `description`, `version`, `dependencies`
  as-is, but `has_dll`, `has_pck`, `affects_gameplay`, `min_game_version` are
  **snake_case** — `ModManifest` deserializes via `System.Text.Json` with
  explicit `[JsonPropertyName]` attributes on those four fields. camelCase
  silently deserializes to false/null with no error (this bit us once — see
  git history / session log if resurrecting that bug).
- The compiled DLL **must be named exactly `<manifest id>.dll`**
  (`ModManager.TryLoadMod` does `Path.Combine(mod.path, modId + ".dll")`),
  not the mod's display name. Set `<AssemblyName>` in the csproj accordingly.
  Same rule applies to `.pck` if `has_pck` is ever set.
- `MegaCrit.Sts2.Core.Hooks.Hook` is a static class with ~90 dispatch points
  (`AfterDamageGiven`, `AfterCombatEnd`, `AfterCombatVictory`,
  `AfterRewardTaken`, `AfterModifyingCardRewardOptions`, etc.) — this is the
  game's internal event bus for powers/relics/mods. `ModHelper`'s
  `SubscribeForRunStateHooks` / `SubscribeForCombatStateHooks` are the
  intended way for a mod to listen in, without needing Harmony at all.
  Worth using for live per-combat/per-card-pick detail later.

## Status

- [x] Dev environment set up (.NET 9 SDK, Godot 4.5.1 Mono)
- [x] Mod builds, deploys, and loads cleanly in-game (verified via Steam launch,
      `RUNNING MODDED!` + ModInitializer log lines confirmed, zero crashes)
- [x] Per-run history recorded via `ModManager.OnMetricsUpload` (no Harmony
      needed): outcome, deck, relics, gold, damage dealt →
      `mods\local-run-stats\runs.jsonl`. Raw local record; no longer the
      source for the overlay's Pick/Impact numbers (see Data sources) but
      still the only record of *your own* damage-dealt stat.
- [x] Live per-combat damage (dealt/taken/blocked) via a `SingletonModel`
      combat-hook listener → `combats.jsonl`.
- [x] Card acquisition log (cards added to deck mid-run, not the starting
      deck) via a `SingletonModel` run-hook listener → `card_picks.jsonl`.
- [x] **Card reward screen overlay** (Harmony patch on
      `NCardRewardSelectionScreen.RefreshOptions`): Pick/Impact/Synergy/keyword
      label under each offered card, geometry hand-tuned live. Working,
      confirmed no crashes across multiple play sessions.
- [x] **Relic reward overlay**, two host UIs, same stats:
      `NChooseARelicSelection` (horizontal relic picker) and
      `NEventOptionButton` (Neow's-choice / Ancient blessing vertical list —
      patched on the `Option` property setter, not `_Ready`, since `Option`
      is assigned after `_Ready` fires).
- [x] Community data integration (sts2runs.com) — see "Data sources" above.
      Confirmed live: 9,225 community runs loaded for Pick/Impact; individual
      community run details fetched and cached for Synergy, growing toward
      the full corpus a bounded batch per launch rather than a 300-run cap.
- [x] **Combat damage HUD** (`CombatDamageHud`, patched onto `NRun._Ready`) —
      always-visible top-right panel, two tables ("COMBAT DAMAGE TAKEN" above
      "COMBAT DAMAGE DEALT", matching the reference image's layout), one row
      per player (multiplayer-aware, keyed by `Player.NetId`), columns for
      current fight / each act reached / running total. Confirmed live.
      Position uses `TopLevel = true` on the Control — confirmed live that
      this makes `Position` an **absolute global coordinate**, not relative to
      anchors or the parent's transform (first attempt anchored top-right and
      landed off-screen because of this). A `HudTuningPanel` slider panel
      (still present, not yet removed) lets position/size be adjusted live —
      see `HudTuning.cs`.
- [ ] **Per-fight/gold background logging + graph overlay** — built
      2026-08-29, **not verified live yet** (built without launching the game,
      by request). Adds:
      - `player_combat_stats.jsonl` — one row per player per finished fight
        (dealt/taken), written in `CombatStatsListener.WritePerPlayerRecords`.
      - `gold_log.jsonl` — one row per `AfterGoldGained` fire via a new
        `GoldTracker` `SingletonModel` (signature `AfterGoldGained(Player)`
        confirmed by decompiling `Hook.AfterGoldGained` first — no parameter
        ambiguity this time, single Player param).
      - A 📈 button on `CombatDamageHud` (left of the "COMBAT DAMAGE TAKEN"
        header) toggles `StatsGraphOverlay`: a custom hand-drawn (`_Draw()`)
        chart (`ChartCanvas`) for Damage Dealt / Damage Taken / Gold, each
        switchable between "Per Stage" (bucketed by act, grouped bars — one
        per player) and "Cumulative" (running total over time, a connected
        line per player — bars don't read well for a continuous running
        total across a whole run's worth of fights).
      - Uses the same `TopLevel`-Control-toggled-by-`Visible` pattern as the
        rest of this mod, not `NOverlayStack`/`IOverlayScreen` (the more
        "native" way per the modding notes) — untested territory deferred to
        avoid guessing at an unfamiliar API without being able to verify it.
      Confirmed live and iterated on:
      - Fixed stale-data-across-runs: `CombatStatsListener`/background JSONL
        logs are lifetime state, not per-run — added `RunContext.CurrentRunStartUtc`
        (set from the same `NRun._Ready` spot as `ResetForNewRun()`) and filter
        all graph reads to it.
      - Added hover tooltips on both chart modes (`ChartCanvas._GuiInput` +
        cached hitboxes rebuilt every `_Draw()`).
      - Redesigned "Per Stage": originally bucketed by act (one bar per act);
        changed on request to one bar per individual fight/gold-event (raw
        value, not summed) — "stage" means a single fight, not an act. Added
        a separate act-filter button row (All/A1/A2/...) as the orthogonal
        way to narrow by act, built dynamically from `PlayerStatsLog.GetAvailableActs()`.
      - Graph panel position/size locked at the tuned 820x680 centered
        default (now a `const` in `StatsGraphOverlay`).
      - Damage HUD position/size locked in 2026-08-31 at
        MarginRight=189 MarginTop=93 Width=300 Height=308 (now `const` in
        `CombatDamageHud`). Both panels are now fully tuned — `HudTuning.cs`
        and `HudTuningPanel.cs` (the dev-only slider tool) were deleted.

### Turns & Cards tab (2026-08-29)

Extended `PlayerCombatRecord` with `TurnsTaken`/`CardsPlayed` (same file,
`player_combat_stats.jsonl` — not a separate log), tracked live via two new
`CombatStatsListener` hooks:
- `AfterPlayerTurnStart(Player)` — unambiguous single-param signature,
  incremented per player turn start.
- `AfterCardPlayed(PlayerChoiceContext, CardPlay)` — `CardPlay` itself has no
  Player/Owner reference (checked its full property list), but
  `CardPlay.Card.Owner` does — attributes the play to whoever's deck the
  card belongs to.

`PlayerStatsLog`'s per-stage/cumulative builders were generalized from
damage-specific (`bool dealt`) to a `Func<PlayerCombatRecord, float>`
selector, so Turns/Cards reuse the exact same fight-grouping logic (and the
same co-op desync fix) as Damage, rather than duplicating it.

`StatsGraphOverlay` gained a tab row ("Damage & Gold" / "Turns & Cards")
above the existing Per Stage/Cumulative mode buttons — mode and act filter
are shared state across both tabs, only which `ChartCanvas` set is
positioned/visible changes. Chart layout height is now `/ activeCount`
instead of a hardcoded `/ 3`, so a 2-chart tab and a 3-chart tab both fill
the available space correctly. **Not yet verified live.**

### Damage source coverage: what's counted and what's approximated

Investigated on request ("does the tracker count poison/doom/special
damage?"). `Creature.LoseHpInternal` — the low-level HP-reduction primitive —
has an explicit doc comment: "Hooks and everything are all done in
CreatureCmd.Damage... there needs to be a really good reason to want to
avoid them." Confirmed by decompiling `PoisonPower.AfterSideTurnStart`: it
calls `CreatureCmd.Damage(...)`, the same hooked path as ordinary attacks. So:

- **Already counted, no changes needed**: Poison, Thorns, self-damage cards,
  and (per that doc comment) essentially anything that reduces HP as a
  "damage" concept — all confirmed or strongly implied to route through
  `CreatureCmd.Damage`, which is what fires `Hook.AfterDamageGiven`.
- **Doom is fundamentally different, not a damage source**: `DoomPower.DoomKill`
  calls `CreatureCmd.Kill()` directly — an instant execute, never
  `CreatureCmd.Damage()`. There's no damage *amount* to observe; it fires a
  separate hook, `AfterDiedToDoom(PlayerChoiceContext, IReadOnlyList<Creature>)`.
  Added an override for it that approximates "how much this execute was
  worth" as the creature's `MaxHp`. Multiplayer attribution for the *dealt*
  side is also approximate — Doom carries no "who applied it" reference the
  way an attack's dealer/target does, so an enemy dying to Doom is credited
  to `GameContext.LocalPlayer` rather than whichever player's card/relic
  actually caused it. **Not yet verified live** (no Doom interaction tested).

### Known pitfall: NCardHolder is a shared widget, not reward-screen-exclusive

Our card reward overlay attaches a Label as a permanent child of `NCardHolder`
with no cleanup. `NCardHolder` is reused by `NDeckViewScreen` and the
upgrade/transform/enchant deck-select screens too, not just the reward
screen — so a holder we decorated kept showing stale Pick/Impact/Synergy
text wherever it got reused next. Confirmed live: opening the deck viewer
showed reward-screen stats on cards there. Fixed by tracking every holder we
decorate (`CardRewardOverlayPatch.DecoratedHolders`) and stripping the label
in a new patch on `NCardRewardSelectionScreen._ExitTree` (confirmed via
reflection that method is actually declared on this class, not just
inherited, before trusting the patch would bind). **Not yet re-verified
live** — game was mid co-op session when this was built.

### Known pitfall: GameContext.LocalPlayer starts null until the first combat hit

Synergy needs the current deck/relics, sourced from `GameContext.LocalPlayer`
— which was only ever set from `CombatStatsListener.AfterDamageGiven`, i.e.
the first hit of the first fight. Every run's very first screen (Neow's /
the ancient blessing) happens *before* any combat, so Synergy legitimately
showed `--` there (not a calculation bug — `HasData` was correctly false
because there was no player reference to build `currentDeckIds` from at
all). Narrowed by also populating `GameContext.LocalPlayer` from
`RunStateListener.AfterRewardTaken` (fires the moment any reward, including
Neow's own relic choice, is claimed) — doesn't fully solve the very first
render of that very first screen, but stops `--` from persisting through
every subsequent reward screen before the first fight. No dedicated "run
started" hook exists to do this properly from the start (see the other
known pitfall on this).

### Known pitfall: co-op writes one record per player per fight — group by Timestamp, not by record

First real co-op test (2026-08-31) found the Cumulative damage-dealt line
chart desynced between players: fight 1 showed one player did 0 damage, but
it "caught up" a fight late. Root cause: `CombatStatsListener.WritePerPlayerRecords`
writes one `PlayerCombatRecord` per player per fight, and all of a fight's
records share the same `Timestamp`. `BuildPerStageDamage` already grouped by
that shared `Timestamp` correctly (one bucket per *fight*), but
`BuildCumulativeDamage` didn't — it advanced the x-axis once per *record*, so
a 2-player fight ate two x-axis slots for one real moment, and whichever
player's record sorted second landed one slot late. Fixed by grouping by
`Timestamp` first in `BuildCumulativeDamage` too (and applied the same fix to
gold's cumulative mode, which had the analogous bug: it aligned by each
player's own event index rather than actual chronological moment — two
players' independent gold pickups aren't the same "moment" just because
they're both each player's 3rd pickup). **Fixed but not yet re-verified live**
— built and compiled clean, not deployed/tested this round since the game
was mid-session and the user asked not to relaunch it. Re-check the
Cumulative charts in the next co-op session.

### Known limitation: `OnMetricsUpload` gating

Decompiled `MetricUtilities.UploadRunMetricsInternal` — `ModManager.CallMetricsHooks`
(which fires our `OnMetricsUpload`) is only reached if ALL of:
- `ReleaseInfoManager.ReleaseInfo != null` (release build — true for us)
- `PrefsSave.UploadData == true` (player's own "share anonymous data" opt-in
  toggle — true on this machine, but a privacy-conscious player disabling
  telemetry would silently break this mod too, which is bad for a mod whose
  whole point is local-only/no-telemetry. Worth revisiting via a direct
  `ModHelper` hook instead if this turns out to be a real problem.)
- `!SettingsSave.FullConsole`
- run not abandoned
- `SaveManager.Instance.Progress.NumberOfRuns > 1` — **this is why the first
  test run recorded nothing**: mods force an isolated "modded" save profile,
  which starts fresh, so run #1 in that profile is always "our first run
  ever" and is explicitly skipped by the base game. Needs a *second*
  completed run in the modded profile before the hook fires at all.

### Known limitation: no true offered-vs-skipped tracking for cards

`card_picks.jsonl` (via `ShouldAddToDeck`) logs cards *added* to the deck
mid-run, not "offered but skipped." `AfterRewardTaken` doesn't expose which
option was chosen either (`CardReward.Cards` retains the full offered list
even after pick; the selection is an index resolved through a
`TaskCompletionSource<int>` local to `NCardRewardSelectionScreen.SelectCard`,
not surfaced to hooks). Real local "pick rate when offered" would need deeper
UI-layer correlation — moot now anyway, since Pick/Impact come from
sts2runs.com's community data instead (see Data sources).

### Known pitfall: `AfterDamageGiven`'s two `Creature` params are easy to swap

`AbstractModel.AfterDamageGiven(PlayerChoiceContext, Creature, DamageResult, ValueProp, Creature, CardModel)`
has two same-typed `Creature` params with no naming hint from the override
signature alone. Decompiling the real caller (`Hook.AfterDamageGiven`) is the
only way to know which is which:
`model.AfterDamageGiven(choiceContext, dealer, results, props, target, cardSource)`
— **position 2 is the attacker, position 5 is the one being hit.** An earlier
version of `CombatStatsListener` guessed backwards (named position 2
"target", position 5 "source"), so "damage dealt" was silently measuring
damage taken instead — `_damageTaken`/`_damageBlocked` stayed correct only
by accident, since those used `damageResult.Receiver` directly rather than
the mislabeled parameter. Caught via live testing: the HUD only ever showed
numbers matching damage received, never damage the player dealt. Fixed by
renaming to `dealer`/`target` to match the real signature. If touching this
method again, don't trust the parameter names in an IDE's own signature
help for an override — decompile the actual call site.

### Known pitfall: never `new` an `AbstractModel`/`SingletonModel` subtype

`ModelDb.Init()` reflects over every loaded assembly (mod DLLs included) at
startup, finds every `AbstractModel` subtype, and constructs the one canonical
instance itself via `Activator.CreateInstance`. If your own code also calls
`new YourModelType()` — even lazily, e.g. via a `static readonly` field first
touched later — you get
`DuplicateModelException: ... Don't call constructors on models! Use ModelDb instead.`
This crashed the game outright the first time (black screen on entering
combat, since the exception fired inside the `ModHelper.SubscribeForCombatStateHooks`
delegate, which runs at combat-hook-iteration time, not at mod-init time).
Fix: derive custom listener models from `SingletonModel` and fetch the
already-registered instance via `ModelDb.Singleton<T>()` instead of `new`.

### Fixed 2026-08-31: chart x-axis labels didn't line up with the data

Reported live: a co-op "Damage Dealt" cumulative chart showed a line that
looked shifted right relative to its "1"/"2" x-axis labels. Root cause in
`ChartCanvas._Draw`: line points (`DrawLines`) and bar groups (`DrawBars`)
are positioned at each group's horizontal **center**
(`g * groupWidth + groupWidth * 0.5`), but the x-axis label loop drew each
label **left-aligned** starting at the group's edge (`g * groupWidth`) — so a
label sat under the *start* of its group instead of under the point/bars it
was labeling, and the mismatch got more visible the fewer groups (fights)
there were. Fixed by drawing labels with `HorizontalAlignment.Center` over
the same `groupWidth`-wide box instead of `Left`, so they align with both
bar groups and line points without needing separate logic per chart type.

### Fixed 2026-08-31: Doom-kill damage credited to the wrong co-op player

Reported live: "the amount is given to another character." Root cause:
`AfterDiedToDoom` had no dealer reference for who applied the Doom (Doom
kills go through `DoomPower.DoomKill` → `CreatureCmd.Kill`, never
`CreatureCmd.Damage`, so there's no `dealer` param at all), so it fell back
to crediting `GameContext.LocalPlayer` — a single shared static that gets
overwritten by *whichever player most recently dealt/took damage or started
a turn*. In co-op that's frequently not the player whose Doom stacks
actually killed the creature.
Fix: `PowerModel.Applier` (set at `PowerCmd.Apply` time) records which
Creature applied a power, including `DoomPower`. The catch: by the time
`Hook.AfterDiedToDoom` fires for the whole killed batch,
`CreatureCmd.KillWithoutCheckingWinCondition` has already called
`creature.RemoveAllPowersAfterDeath()` on every one of them, so their
`Powers` list (and therefore the `DoomPower.Applier`) is already gone.
Confirmed via decompile that `Hook.BeforeDeath(creature)` fires earlier in
the same method, *before* powers are stripped — so `CombatStatsListener` now
overrides `BeforeDeath`, checks for a live `DoomPower` on the dying creature,
and caches its `Applier` in `_pendingDoomApplier` (keyed by `Creature`,
cleared at combat end/run reset so a prevented death can't leak a stale
entry). `AfterDiedToDoom` consumes that cache first and only falls back to
`GameContext.LocalPlayer` if no Applier was captured (e.g. a relic-sourced
Doom with no Creature applier).

### Fixed 2026-08-31: Osty's damage was silently dropped, then made asymmetric on purpose

Decompiling `CreatureCmd.Damage` showed a hit absorbed by Osty (via the
`DieForYou` redirect) fires `AfterDamageGiven` with `target` = the Osty
Creature itself, not the player — `Osty.IsPlayer` is `false` (it's a
`Monster`-backed pet, see `Creature.IsPet`/`Creature.PetOwner`), so a plain
`target.IsPlayer` check silently dropped 100% of Osty's damage taken. Fixed
attribution is **intentionally asymmetric** per explicit user direction:
- **Dealt**: counts for the pet owner. Confirmed via a real card
  ("Unleash": *"Osty deals 6 damage. Deals additional damage equal to
  Osty's current HP."*) that Osty CAN deal damage through player-cast cards,
  even though its own `MonsterMoveStateMachine` is a do-nothing state (it
  never attacks on its own AI turn) — that damage should still count as the
  player's.
- **Taken**: explicitly excluded. Osty soaking a hit is meant to behave like
  extra Block, not like the player getting hit — per the user: "damage osty
  takes [should] essentially act as additional block, since that is not
  damage my character has taken."

`CombatStatsListener` has two resolvers reflecting this:
`ResolveDealerPlayer` (`IsPlayer` OR `IsPet → PetOwner`) and
`ResolveTakenPlayer` (`IsPlayer` only). `AfterDiedToDoom` mirrors this too:
a pet dying to Doom is skipped entirely (`if (creature.IsPet) continue;`) —
neither taken (per the above) nor an "enemy died" dealt-credit.

### Added 2026-08-31: per-fight card play breakdown

The existing whole-run "which cards, how many times" panel
(`card_plays.jsonl`, one row per raw play event) was working well, but the
user wanted it split out per individual fight too ("separate lists per fight
so I can see how many of which card I have used in each fight"). Rather than
reconstruct fight boundaries from `card_plays.jsonl`'s scattered per-play
timestamps, `CombatStatsListener` now also tracks an in-memory
`Dictionary<ulong netId, Dictionary<string cardName, int count>>` per fight
(`_cardPlayCountsThisFight`, incremented in `AfterCardPlayed` alongside the
existing raw-event log), folded into a new `card_play_fights.jsonl` at
`AfterCombatEnd` — one row per (player, card) pair, same
Timestamp/ActIndex/EncounterId shape as `PlayerCombatRecord`. Both files
coexist: `PlayerStatsLog.BuildCardPlayCountsBbcode()` now renders the overall
totals first, then a "--- By Fight ---" section grouping
`card_play_fights.jsonl` by Timestamp (same convention as
`BuildPerStageFightMetric`) with one card table per player per fight.

### Fixed 2026-08-31: poison damage dealt to enemies wasn't counted

Reported live, after the batch below shipped: "poison damage was not being
accounted for." Decompiling `PoisonPower.AfterSideTurnStart` explained why:
a poison tick calls `CreatureCmd.Damage(ctx, base.Owner, base.Amount, ...,
dealer: null, cardSource: null)` — always `dealer: null`, since the tick is
self-inflicted by the power on its own owner each side-turn-start, with no
reference to whoever originally stacked the poison. The "taken" side of
`AfterDamageGiven` still worked fine (it's `target`-based and never needed a
dealer), but the "dealt" side does need one, so 100% of poison damage a
player applied to an enemy was silently dropped. Fixed the same way as the
Doom-applier fix below: read `PowerModel.Applier` — here, straight off the
ticking creature's still-live `PoisonPower` (no death/`BeforeDeath` caching
needed this time, since the creature doesn't die from a single tick and
`PowerCmd.Decrement` — which can remove the power at 0 stacks — only runs
*after* `CreatureCmd.Damage` and this hook complete). Gated strictly on
`dealer == null` so a normal attack from an enemy that also happens to be
poisoned can't get misattributed to whoever applied that poison. Same
multi-source caveat as Doom: if two co-op players poison the same enemy,
`Applier` only reflects the most recent stacker.

### Not yet verified live (2026-08-31 batch)

None of the following have been tested in-game yet as of this note — all
built and deployed pending the user closing/reopening the game:
- Chart x-axis label centering fix.
- Doom-kill Applier attribution via the new `BeforeDeath` cache.
- Osty dealt/taken asymmetric attribution (including the "Unleash"-style
  card-dealt-through-Osty path).
- Per-fight card play breakdown (`card_play_fights.jsonl` + the "By Fight"
  BBCode section).
- Poison-damage-dealt Applier fix (just added, above).
