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
- Local install: `pull-and-install.ps1` fast-forwards `main` and `dotnet build`s
  Release. The csproj copies `local-run-stats.dll` + `LocalRunStats.json` into
  `<STS2>/mods/LocalRunStats` when it can see a Steam install (`Sts2Path`, or
  the common `SteamLibrary` / `Program Files (x86)\Steam` paths). Close the
  game first — STS2 locks the loaded DLL. Override with
  `/p:Sts2Path="C:\path\to\Slay the Spire 2"` if autodetection misses.
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

### Fixed 2026-08-31: gold chart looked desynced because starting gold was invisible

Reported live: a co-op Gold chart showed The Necrobinder's line already
elevated from the first point while The Silent's stayed flat near 0 until
partway through, then jumped sharply — looked desynced even though nothing
was actually wrong with the event ordering (that class of bug was already
fixed earlier — see "Fix cumulative chart desync in co-op" below). Root
cause: `gold_log.jsonl` only ever got a row from `GoldTracker.AfterGoldGained`
— every player starts a run with real gold already in hand (matches vanilla
STS's 99/100 starting gold), but that pre-`AfterGoldGained` balance was never
recorded anywhere, so each player's line effectively started from an assumed
0 until their own first gold-gain event fired. In co-op the two players'
first gains rarely land on the same fight, so one player's baseline showed up
"late" relative to the other, reading as a desync.
Fixed with `RunContext.EnsureBaselineGoldCaptured(Player)`: writes one
GoldRecord per player (guarded by a per-run `HashSet<ulong>` so it only ever
fires once per player), stamped with `RunContext.CurrentRunStartUtc` — always
earlier than any real event, so it sorts first regardless of which hook
happens to capture it. Called from three points, earliest-first:
`RunStateListener.AfterRewardTaken` (the very first screen of any run —
Neow's/the ancient blessing choice — fires before combat even exists, so
`player.Gold` there should still be the untouched starting amount),
`CombatStatsListener.AfterPlayerTurnStart` (fallback — first turn of the
first fight, before any card is played), and `GoldTracker.AfterGoldGained`
itself (last-resort fallback, so at least *something* gets recorded even if
neither earlier hook fired first for some reason — though by definition
that fallback's value is already post-gain, not pristine).
`RunContext` also gained `ResetForNewRun()` (clears the per-run HashSet
alongside setting `CurrentRunStartUtc`), replacing the old direct field
assignment in `CombatDamageHudPatch`.

### Fixed 2026-08-31: two players on the same character silently collided, not just mislabeled

Reported as a labeling request ("if there's multiples of the same character,
add a number after — Silent 1, Silent 2") but turned out to be a real
data-correctness bug once traced through: `PlayerStatsLog`'s chart/table
builders grouped and keyed every per-player series by `CharacterName`
(a string) instead of `PlayerNetId` (every record type already carries
both). Two players both on The Silent would collide — e.g.
`fight.FirstOrDefault(r => r.CharacterName == "Silent")` only ever returns
ONE of the two Silents' rows per fight, so the second player's data was
silently dropped from that fight's bar/point entirely, not just displayed
under a shared label. `CombatDamageHud`'s live table was safe from the
data-loss (it already reads `CombatStatsListener.DamageByPlayer`, a
`Dictionary<ulong, PlayerDamageTracker>` genuinely keyed by NetId), but two
same-character rows there would still have shown identical, ambiguous
labels.
Fixed by adding `PlayerDamageTracker.NetId` (set once in `GetOrCreateTracker`)
and a shared `PlayerStatsLog.DisambiguateCharacterNames(IEnumerable<(ulong
NetId, string CharacterName)>)` helper: collapses to one entry per distinct
NetId (first occurrence), counts how many distinct NetIds share each raw
CharacterName, and only appends " 1"/" 2"/... for names shared by more than
one — a lone player keeps their plain name. Every `PlayerStatsLog` Build*
method (`BuildPerStageFightMetric`, `BuildCumulativeFightMetric`,
`BuildGoldChartData`, `BuildOverallCardPlayCounts`, `BuildCardPlayCountsByFight`)
now groups/keys by `PlayerNetId` internally and only resolves through this
helper for display strings (chart legend keys, BBCode table headers).
`CombatDamageHud.AppendTable` does the same for its name column. Numbering
is recomputed fresh from whatever data is available each time it's called
(never persisted) — so early in a run, before a second same-character player
has logged anything, the first one may briefly show unsuffixed, then both
gain numbers once there's enough data to detect the collision. That's
accepted as fine, same "best-effort, self-correcting" spirit as the
Doom/poison Applier approximations above.

### Added 2026-08-31: auto-install on build

`Sid-creates` (co-op collaborator) contributed `pull-and-install.ps1` plus an
`InstallToGame` MSBuild target (`AfterTargets="Build"`) that copies the built
DLL + manifest straight into `<Sts2Path>\mods\LocalRunStats` — no more manual
copy after every build. `Sts2Path` auto-detection originally only covered
`D:\SteamLibrary\...` and `C:\Program Files (x86)\Steam\...`, missing this
project's actual dev machine (`C:\SteamLibrary\...`); expanded the candidate
list to also cover `C:\SteamLibrary`, `D:`/`E:`/`F:` drives with either
`SteamLibrary` or `Steam` as the folder name, and `C:\Program Files\Steam`
(non-x86). Override with `/p:Sts2Path="..."` if autodetection ever misses.
Merged via a normal `git fetch` + `git merge` after a push conflict (the PR
landed on GitHub via `gh`/a web PR while this session's local `master` had
already diverged) — no conflicts, since the PR's `MOD_SPEC.md` edit and this
session's edits landed in different, non-overlapping sections of the file.

### Fixed 2026-08-31: Doom-kill damage was the enemy's full MaxHp, not its remaining HP

Reported live: "right now its adding the total health of the enemy to the
damage dealt." Correct — the original approximation used `creature.MaxHp` for
every Doom kill, which double-counts: an enemy already whittled down by
normal attacks/poison before Doom finishes it off already had that damage
counted once via those hits' own `AfterDamageGiven` calls, then got its FULL
max health added a second time here.
The fix needs the enemy's HP immediately before the kill, but that can't be
read at `AfterDiedToDoom` time (or even in the `BeforeDeath` hook added
earlier for the Applier fix) — re-decompiling
`CreatureCmd.KillWithoutCheckingWinCondition` confirmed the order is: capture
`currentHp` into a local, drain it to 0 via `LoseHpInternal`, fire
`Hook.AfterCurrentHpChanged`, THEN fire `Hook.BeforeDeath` — so by the time
any of our hooks see the creature, `CurrentHp` already reads 0. Fixed instead
by opportunistically caching `target.CurrentHp` in `_lastKnownHp` every time
`AfterDamageGiven` fires (which already covers every real hit, including
poison ticks, since those go through the same `CreatureCmd.Damage`/hook
path) — so by the time Doom triggers, the last cached value is exactly the
creature's HP right before the kill, with no HP-changing event possible in
between under normal circumstances. `AfterDiedToDoom` now uses this cache
(falling back to `MaxHp` only if the creature took no tracked damage at all
this fight, i.e. it's reasonable to assume it was still full) and skips
crediting anything if the cached HP is 0 (already fully accounted for via
normal damage). Caveat noted in code: a heal landing between the last hit and
the Doom kill would make the cached value stale (an undercount, not an
overcount) — considered an acceptable edge case, same "best-effort" spirit as
the Applier lookups.

### Changed 2026-08-31: per-fight card counts now flow in a grid, not a long list

The "By Fight" card-play breakdown (added earlier this session) was one long
vertical list of `[b]Fight N[/b]` blocks — changed on request to lay fights
out side-by-side, wrapping to a new row once a row runs out of width, "like a
table." BBCode's `[table=N]` forces a fixed column count, not width-based
wrapping, so this isn't rendered as BBCode text at all: `CardPlayCountsPanel`
was restructured around a `ScrollContainer` > `VBoxContainer` containing the
overall-totals `RichTextLabel` (unchanged) followed by an `HFlowContainer` —
Godot's native flex-wrap container — holding one small `RichTextLabel` child
per fight (`CustomMinimumSize` ~150px wide), which Godot lays out and wraps
automatically. `PlayerStatsLog.BuildCardPlayCountsByFightBlocks()` replaces
the old single-string `BuildCardPlayCountsByFight()`, returning
`List<string>` (one self-contained BBCode block per fight) instead of one
combined string, so the panel can create one widget per block rather than
concatenating text.

### Fixed 2026-08-31: HFlowContainer still rendered as a vertical list

Reported live: "the card play layout is still one long list" — the
`HFlowContainer` change above compiled and ran, but visually did nothing.
Root cause: a `ScrollContainer`'s default `HorizontalScrollMode` is `Auto`,
which lets its child grow as wide as it wants (scrolling horizontally to
match) instead of clamping the child's width to the ScrollContainer's own
viewport. With no fixed width to wrap within, a `FlowContainer`'s minimum
size collapses to fit just its single widest child — so `_fightFlow` wrapped
after every one fight block, which looks identical to a plain vertical list.
Fixed by setting `HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled`
(clamps content width to the viewport — the standard Godot technique for
"vertical-scroll-only content that wraps to available width") plus
`SizeFlagsHorizontal = Control.SizeFlags.ExpandFill` on both the inner
`VBoxContainer` and the `HFlowContainer` itself, so each actually claims the
full available width instead of shrinking to its minimum. Lesson: a
FlowContainer needs an ANCESTOR that assigns it a real width to wrap
against — nesting it inside a ScrollContainer alone isn't enough without also
disabling that ScrollContainer's horizontal scroll.

### Fixed 2026-08-31: gold chart still desynced after the baseline fix

Reported live, after the baseline fix above: "initial gold looks good but
the gold amounts are still desynced. its counting gold for another player
one turn later." Root cause: `BuildGoldChartData` was grouping by raw
`Timestamp`, same as the damage/cards charts — but unlike those, gold events
don't have a natural shared moment to group by. A fight's `PlayerCombatRecord`
rows are written together in one method call (`WritePerPlayerRecords`), so
every player's row for that fight literally shares one Timestamp string. Gold
events fire independently per player, at whatever real-world instant each
player's `AfterGoldGained` happens to fire — those timestamps essentially
never coincide, so every single gold event got its OWN x-axis tick, and the
other player's line just carried its last value forward flat until their own
next event. That reads exactly like "one turn behind," even though no data
was actually lost or mislabeled.
Fixed by bucketing on a new shared `RunContext.CurrentStageIndex` counter
instead of `Timestamp` — advanced once per finished fight (`AdvanceStage()`,
called from `CombatStatsListener.AfterCombatEnd`), stamped onto every
`GoldRecord` (`GoldRecord.StageIndex`) at write time by both `GoldTracker`
and `EnsureBaselineGoldCaptured`. `BuildGoldChartData` now groups by
`StageIndex`: every gold change within the same stage of the run — including
ones that happen outside combat (map rewards, shops, rest sites), which get
attributed to "the stage since the last fight ended" — lands on the same
tick for both players. Per-stage mode changed meaning along with this: a bar
is now "total gold gained during this stage" (sum of that player's gains) —
still a "raw value, not running total" like the other per-stage charts, just
resolved per stage instead of per individual pickup event.

### Changed 2026-08-31: Turns chart no longer splits by player

Requested after noticing the per-player Turns lines were always identical:
"you can remove turns taken tracker graphs as it will be the same for each
player in multiplayer. replace this with turns taken by each combat
disregarding multiplayer." Confirmed by decompiling `Hook.AfterSideTurnStart`
— it fires once per SIDE's turn (`AbstractModel.AfterSideTurnStart(CombatSide
side, IReadOnlyList<Creature> participants, ICombatState combatState)`), not
once per player on that side, which is exactly the shared "how many turns
has this fight had" signal needed. `CombatStatsListener` now tracks a single
`_currentFightTurns` counter via this hook (incremented only when
`side == CombatSide.Player`) instead of the old per-player counting in
`AfterPlayerTurnStart` (which still exists, just for its other two side
effects: `GameContext.LocalPlayer` and `EnsureBaselineGoldCaptured`).
`PlayerCombatRecord.TurnsTaken` keeps its per-row shape for now (every
player's row for a fight just carries the same shared value, so the existing
act-filter/fight-grouping plumbing didn't need touching), but
`PlayerStatsLog.BuildTurnsChartData` no longer uses the generic per-player
`BuildFightMetric` helper — it reads one representative row per fight and
renders a single "Turns" series instead of one line per player.

### Changed 2026-08-31: encounter labels use real enemy names, not internal ids

Reported live: "enemy names are currently shown as what its like in the
code... can they be replaced to the actual in-game name like 'Shrinker
Beetle', 'Ceremonial Beast'." `EncounterId` (used for the "Fight N: ___"
labels on the per-fight card panel and chart x-axis labels) was
`combatRoom.ModelId.ToString()` — the internal id, e.g.
"ENCOUNTER.SHRINKER_BEETLE_WEAK". Confirmed via reflection that
`CombatRoom.Encounter` (an `EncounterModel`) has a `Title` property of type
`LocString` — same type/pattern already used for character names elsewhere
in this mod (`player.Character?.Title?.GetRawText()`). Added
`CombatStatsListener.GetEncounterDisplayName(CombatRoom)` —
`combatRoom.Encounter?.Title?.GetRawText() ?? combatRoom.ModelId.ToString()`
(falls back to the raw id only if the title is somehow unpopulated) — and
used it at all three write sites that previously called
`combatRoom.ModelId.ToString()` directly (`WriteAggregateRecord`,
`WritePerPlayerRecords`, `WriteCardPlayCountsByFight`). `ShortenEncounterId`
no longer needs its dot-prefix-stripping logic (there's no more
"CATEGORY.NAME" namespacing to strip since the stored value is already a
real display name) — simplified to a plain truncate, bumped from 10 to 16
chars now that labels are real words instead of underscored codes.

### Added 2026-08-31: browser-based run summary report

Requested: "make a layout like this [sts2runs.com's /run/{id} floor-by-floor
table] that i can see at the end of a run." Asked the user two design
questions up front (co-op scope, trigger mechanism) since the source layout
is inherently single-player-shaped:
- **Co-op scope**: toggle between players (tabs at the top of the report,
  not a combined multi-column view).
- **Trigger**: a button ("Run Report") on the existing StatsGraphOverlay,
  next to the close button — not auto-opened on run end.
Chose to generate a static HTML file and open it via `OS.ShellOpen` rather
than building this as native Godot UI: Godot has no table/grid layout
primitive, and hand-rolling badges/pills/multi-column alignment via `_Draw()`
(as ChartCanvas already does for charts) would be far more work than HTML/
CSS for a design this table-heavy, for no real benefit since it only needs
to open once per run, not live-update.

New `RunSummaryReport.cs`: `OpenLatest()` finds the most-recently-modified
`.run` file across all profiles (reuses `HistoryStatsEngine.FindAllRunFiles`,
now `internal` instead of `private`) — local `.run` files are only written
once a run actually ends (win/death/abandon), so "most recent file" always
means "the run that just finished," with no dedicated run-end hook needed.
Writes `mods/local-run-stats/run_summary.html` and opens it via
`OS.ShellOpen`.

Card/relic/potion/monster/event/character ids in the JSON (e.g.
"CARD.REAVE") are resolved to real display names via
`ModelDb.GetByIdOrNull<T>(new ModelId(category, entry))` against the live
game's model registry — confirmed via reflection that `CardModel.Title` is
a plain `String` (the one exception) while `RelicModel`/`PotionModel`/
`MonsterModel`/`EncounterModel`/`EventModel`/`CharacterModel` all expose
`LocString Title` (same pattern as `GetEncounterDisplayName`). This works
even for cards/relics this player never picked, since ModelDb holds every
canonical definition. Falls back to a humanized id (`SHRINKER_BEETLE` ->
`Shrinker Beetle`) if a lookup fails.

**Pitfall found via a standalone smoke test (not live in-game) before this
ever reached the user**: `map_point_history` is NOT a flat list of floors —
it's nested one sub-array PER ACT (confirmed against real local `.run`
files: a run that reached Act 3 has 3 outer entries, one that stayed in Act
1 has 1). A naive flat `foreach` over the outer array crashed immediately
(`InvalidOperationException: The node must be of type 'JsonObject'`) since
each outer element is itself a `JsonArray`, not a floor object. Fixed by
flattening across acts before assigning floor numbers, so they run
continuously across the whole run (matching vanilla Slay the Spire's own
numbering) rather than restarting at 1 each act.
Also verified via the same smoke test: the deck-size column's reconciliation
trick (`startingDeckSize = finalDeckCount - totalGained + totalRemoved`,
running total incremented per floor by `cardsGained - cardsRemoved`) lands
exactly on each player's true final deck count on both a solo run and a
multi-act run — confirms the approach doesn't need to know/guess how
`floor_added_to_deck` tags the very first floor.

Scope deliberately cut for v1: event/ancient-choice flavor text (the
`{"key":..., "table":...}` LocKey objects in `event_choices`/
`ancient_choice`) isn't resolved — those rows just show whatever
relics/cards/potions/gold changed, not the narrative text, to avoid a
second localization-resolution system on top of the model-id one. Multiple
`rooms[]` entries per map point (never observed, but the schema allows it)
only render the first.

### Fixed 2026-08-31: run report showed a previous run instead of the in-progress one

Reported live: "make this live track? right now its showing me info from a
previous run probably because im in the middle of a run." Correct diagnosis
— `RunSummaryReport` originally parsed the most-recently-modified
history/*.run FILE, and those are only written once a run actually ends
(win/death/abandon), so it could never reflect a run still in progress.
Found a better source via reflection: `RunManager` is a static singleton
(`RunManager.Instance`) with a live `RunHistory History` property the game
itself keeps up to date throughout the run — `RunSummaryReport.OpenCurrent()`
(renamed from `OpenLatest()`) now reads `RunManager.Instance.History`
directly instead of finding/parsing a file. Bonus: strongly-typed C# objects
instead of JsonNode walking, and access to fields the raw file's schema
didn't cleanly expose — `AncientChoiceHistoryEntry.Title` and
`EventOptionHistoryEntry.Title` are already-resolved `LocString`s, so event/
ancient-choice flavor text (explicitly cut from v1 as a scope decision) is
now shown directly, no separate localization-table lookup needed.
**Pitfall, caught by the compiler this time, not a live report**:
`RunHistory.MapPointHistory` is `List<List<MapPointHistoryEntry>>` — nested
one sub-list per act, same as the file format apparently regardless of
whether it's read live or after serialization. `IRunState.MapPointHistory`
has the identical nested shape (`IReadOnlyList<IReadOnlyList<...>>`). Fixed
the same way as the file-based version: flatten via `SelectMany` before
assigning continuous floor numbers.
`HistoryStatsEngine.FindAllRunFiles` reverted back to `private` (was
temporarily `internal` for the file-based version of this feature, no longer
needed now that nothing outside HistoryStatsEngine calls it).

### Added 2026-08-31: seed-unlocks.ps1 — copy main-save unlocks into the modded profile

Requested: "can this be implemented to my main save, the save before the
mods where i have everything unlocked?" then "yes do that and make it so
that it works for other players as well." Confirmed via a raw user-string
heap scan of sts2.dll (not just decompiled method bodies — grepped for the
literal "modded" across the whole assembly) that the native mod loader
unconditionally routes ANY modded session's saves to `steam/<id>/modded/
profileN/...` instead of the normal `steam/<id>/profileN/...` — an
`isModded`/`is_modded`-gated path branch, confirmed against this player's
own folders (`profile1/saves/progress.save` vs `modded/profile1/saves/
progress.save`, both present). This is a deliberate engine-level safeguard
so mods can never touch the real save; there's no manifest flag or config to
disable it, and it shouldn't be defeated even if there were one.

Added `seed-unlocks.ps1` (repo root, alongside `pull-and-install.ps1`) —
copies `progress.save` (unlock state only, not run history/prefs) FROM the
vanilla profile INTO the matching modded profile, never the reverse. Backs
up the modded profile's existing `progress.save` first with a timestamped
suffix (`.pre-seed-yyyyMMdd-HHmmss`) before overwriting, so it's always
reversible. Needs no configuration/parameters (unlike `pull-and-install.ps1`'s
`-Sts2Path`) since save data lives at a fixed OS-level location
(`%APPDATA%\SlayTheSpire2\steam`), not the game's variable install path.
Loops every Steam account folder and every `profileN` slot found, so it
works unmodified for any other player who pulls the repo and runs it
locally — same game-running guard pattern as `pull-and-install.ps1` (refuses
to run if `SlayTheSpire2.exe` is active, since the game may overwrite
`progress.save` with its own in-memory state on exit/autosave, undoing the
copy).
Run live for this player: found a real profile1 pair, backed up the modded
copy, seeded it, verified via MD5 that the modded profile's `progress.save`
now byte-for-byte matches the main save's (183,585 bytes, matching hash).

### Found 2026-08-31: Steam Cloud sync silently reverted the seeded unlocks

Reported live: "i booted the game and my things are not unlocked (like
timeline)" — right after the MD5-verified seed above. Re-checked the file
after the user's session: `progress.save` had reverted to its OLD pre-seed
content (85,578 bytes, MD5 matching both `progress.save.backup` and this
script's own `.pre-seed-*` backup exactly), with no error logged anywhere.
Investigated `ProgressSaveManager.LoadProgress()` first (suspected a
validation/checksum rejection falling back to a hardcoded default on parse
failure — decompiled and ruled out: on failure it resets to a bare
Ironclad-only `ProgressState.CreateDefault()`, which would've been a much
smaller file, not one matching the exact old byte-for-byte content) and
`GameModeExtension.AreAchievementsAndEpochsLocked()` (checks
`gameMode != GameMode.Standard` — Daily/Custom-mode gating, unrelated to
mods, ruled out). The actual cause was simpler and outside the decompiled
code entirely: this player had Steam Cloud sync ON for this game (confirmed
`SteamRemoteSaveStore`/`CloudSaveStore` exist in sts2.dll, so Cloud saves are
real here) — editing `progress.save` locally, then launching, let Steam pull
the last cloud-synced version back down before the game ever read the seeded
file, silently reverting it with no error shown anywhere. Fixed by turning
Steam Cloud sync OFF for Slay the Spire 2 (Steam Library -> right-click ->
Properties -> General) BEFORE re-running `seed-unlocks.ps1` — re-seeded,
launched, confirmed live: Timeline unlocks now show correctly and persisted.
`seed-unlocks.ps1` updated with a loud warning (both in the header comment
and printed at runtime) about turning Cloud sync off first, since this is
exactly the kind of failure that looks like the script silently did nothing,
when actually neither the script nor the game logged anything wrong.

### Fixed 2026-09-01: run report went from "shows an old run" to "does nothing" — RunManager.Instance.History isn't actually live

Reported live, after the RunManager.Instance.History rewrite above: "font
still is unchanged. also run report button isnt doing anything" — then,
after adding diagnostics, confirmed via log: `RunManager.Instance` was
present but `.History` was consistently null throughout real mid-run
gameplay (cards being played, combats completing). The earlier assumption —
that `RunHistory History` on `RunManager` is a continuously-live object —
was wrong. Decompiled `RunManager` itself to check: `History` is a plain
nullable auto-property (`public RunHistory? History { get; set; }`) with no
default; it's only ever ASSIGNED at specific points tied to saving/
uploading a finished run (`RunHistoryUtilities.CreateRunHistoryEntry(...)`
appears right where `History` gets built). It is NOT kept in sync during
normal play — it's essentially the same "only populated near run-end"
problem the original file-based version had, just one layer further in.
The actual continuously-live source, confirmed by finding the game's OWN
code writing to it during normal play
(`UpdatePlayerStatsInMapPointHistory` -> `State.CurrentMapPointHistoryEntry
?.GetEntry(player.NetId)`, called from a normal per-update path, not a
save/upload path), is `IRunState.MapPointHistory` — accessible via
`Player.RunState`, already used elsewhere in this mod (`GoldTracker` reads
`player.RunState?.CurrentActIndex`). That same `GetEntry(player.NetId)` call
also confirms `PlayerMapPointHistoryEntry.PlayerId` is keyed by **NetId**
during live play, not the SteamID seen in the on-disk `.run` file's
`player_id` field (that translation apparently happens only at
save/serialize time) — matters because it's the join key against
`CombatStatsListener.DamageByPlayer`.
`RunSummaryReport` rewritten again: `OpenCurrent()` now reads
`GameContext.LocalPlayer?.RunState` instead of `RunManager.Instance.History`.
`IRunState` has no player-roster/deck-list API the way `RunHistory` did, so
two things changed: the player list is now built from whoever actually
appears in the map point history (in first-seen order) with names resolved
via `CombatStatsListener.DamageByPlayer` instead of `RunHistory.Players`,
and the **Deck size column was dropped entirely** rather than guess at
another possibly-wrong live "current deck count" source — no obvious
equivalent to `RunHistoryPlayer.Deck` was found on `IRunState` in the time
spent on this bug already. The report header also dropped
Win/WasAbandoned/Seed/RunTime/KilledByEncounter (only meaningful for a
finished run, and not on `IRunState` either) in favor of current
Ascension/Act/Floor, which fits "in progress" better anyway.
**Debugging note worth keeping**: two separate `Log.Info` calls added
earlier to narrow this down (RunManager.Instance null-check,
.History null-check) never once appeared in the log across many button
presses, despite being simple unconditional straight-line code sitting
between two OTHER log lines (one Info, one Warn) that both printed
reliably every single time. Never root-caused — worked around by folding
the same diagnostic into the reliable Warn call instead. If a future
Log.Info mysteriously "disappears" again, don't assume the code path wasn't
reached — reach for Warn/Error or fold the detail into an existing reliable
log line rather than trusting a new bare Log.Info to show up.
**Also reverted this session, per explicit request ("revert all font
changes and shelf it for now")**: the ChartCanvas/CardPlayCountsPanel
in-game-font work (GetThemeFont/FontHelper attempts) — back to
`ThemeDB.FallbackFont`, `FontHelper.cs` deleted. Two guesses
(`ThemeDB.GetProjectTheme()?.DefaultFont`, then `GetThemeFont("font",
"Label")`) both reportedly changed nothing visible; unclear which
assumption was wrong (that STS2 uses "Label" as its themed type, that
`GetThemeFont` was resolving correctly at all, or something else) since no
further diagnostics were run on it before it was shelved. Revisit by adding
logging similar to what the Doom/gold/turns bugs needed — guessing font
theme types blind clearly wasn't working.

### Added 2026-09-01: per-card Times Played / Times Drawn hover tooltip

Requested with a reference implementation:
https://github.com/rmac-silva/CardTracker — a separate STS2 mod that shows
Times Played/Drawn (and Power uptime) on card hover tooltips. Read its
source directly (CardRegistrar.cs, the Patches/ files) to understand its
approach before adapting rather than guessing from the feature name alone.
Confirmed via decompile that its two Hook patches
(`Hook.AfterCardPlayed`/`Hook.ModifyCardBeingAddedToDeck`) match hooks this
mod already uses in a cleaner form (`CombatStatsListener.AfterCardPlayed`,
`RunStateListener.ShouldAddToDeck`) — no need to Harmony-patch those. The
one genuinely new hook, `Hook.AfterCardDrawn`, dispatches to
`AbstractModel.AfterCardDrawn(PlayerChoiceContext, CardModel, bool
fromHandDraw)` — a normal override, same as everywhere else in this mod, no
Harmony needed there either.
Added `CardStatsTracker` (new `SingletonModel`): `Dictionary<string card-id
[+ "_UPGRADED"], Stats{Played,Drawn}>`, reset per run alongside
`CombatStatsListener`/`RunContext` in `CombatDamageHudPatch`. Simpler key
than the reference mod's (which also distinguishes enchanted/"generated
this combat" variants) — scoped down deliberately, can extend later.
The display side DOES still need Harmony, since there's no AbstractModel
hook for "a card's hover tooltip is being built": `CardStatsTooltipPatch`
patches `NCardHolder.CreateHoverTips` (`protected virtual`, decompiled body
confirmed as just `NHoverTipSet.CreateAndShow(this,
CardNode.Model.HoverTips)`), Prefix-returning false after replicating that
call with one extra `HoverTip` appended to a copy of the card's own
`HoverTips` list — appending to (not replacing) that list means all of the
card's normal keyword tooltips stay intact.
**Improved on the reference implementation, not just copied it**:
`HoverTip.Id`/`IsSmart` turned out to have PUBLIC setters (reflection
initially reported all of Title/Description/Id/IsSmart/Icon as
"set=True" — that flag doesn't distinguish public from non-public setters,
which the compiler then caught for Title/Description/Icon specifically:
CS0200 "cannot be assigned to -- it is read only"). So only those three
need the reference mod's Harmony `Traverse` workaround; `Id`/`IsSmart` are
set directly via a normal object initializer.
Only patches `NCardHolder.CreateHoverTips` (hand/rewards/shop — anywhere a
card sits in a "holder"), NOT `NInspectCardScreen.UpdateCardDisplay` (the
dedicated deck-viewer inspect screen) the way the reference mod's second
patch does — deliberately out of scope for this pass, since that second
patch leans on four private-field `AccessTools.Field` reflections
(`_card`/`_cards`/`_index`/`_hoverTipRect`) that add real fragility for
what's a secondary surface (deck viewer) vs. the primary one (hand hover
during combat, which is covered). Revisit if the deck-viewer view turns out
to matter enough to be worth it.

### Fixed 2026-09-01: card stats tooltip showed nothing — new hook listeners aren't auto-registered

Two separate bugs found in sequence testing the feature above, both fixed
live before landing:

1. **"hovering isnt showing any stats", no errors logged** — root cause and
   fix documented above (the NCardHolder/NPreviewCardHolder/
   NSelectedHandCardHolder three-way patch).
2. **Still nothing after that fix, tooltip patches confirmed firing via
   diagnostic logging but always with `stats=NONE`, even for cards played
   dozens of times** — this mod does NOT auto-register new
   SingletonModel/AbstractModel subclasses for combat/run-state hooks the
   way ModelDb.Init() auto-CONSTRUCTS them. `RunStatsRecorder.Initialize()`
   explicitly lists which model instances receive hooks:
   `ModHelper.SubscribeForCombatStateHooks("local-run-stats", _ => new[] {
   CombatStatsListener.Instance })` — `CardStatsTracker.Instance` was never
   added to that array, so `ShouldReceiveCombatHooks => true` on the new
   class did nothing on its own; `AfterCardPlayed`/`AfterCardDrawn` were
   simply never invoked on it. Fixed by adding `CardStatsTracker.Instance`
   to that same array. **Lesson for any future new hook-listening
   SingletonModel**: `ShouldReceiveCombatHooks`/`ShouldReceiveRunStateHooks`
   alone are NOT sufficient — the instance also has to be added to the
   corresponding `ModHelper.Subscribe...` call in `RunStatsRecorder.
   Initialize()`, or its hook overrides silently never fire, with no error
   anywhere.

### Added 2026-09-01: map path advisor — highlights the best remaining path for a chosen goal

Requested: "is there a way to calculate ideal map path to take? like best
path for most upgrades or most elites or most question marks." Asked two
clarifying questions first (single goal with a toggle vs. multiple goals
shown at once; highlighted on the actual map vs. a separate panel/report) —
user chose single-goal-with-toggle, highlighted directly on the in-game map.

`MapPathAdvisor.ComputeBestPath(MapPoint from, Goal goal)` — the map is a
DAG (`MapPoint.Children` only ever points to the next row up, confirmed via
reflection), so this is longest-path-in-a-DAG: BFS out from `from` to
collect every reachable node, process rows in DESCENDING order (boss-ward
first) so each node's children are already scored, `bestScore[node] =
weight(node) + max(children's bestScore)`, reconstruct forward via a
recorded best-child pointer. `Weight` is 1 for the goal's target
`MapPointType` (Elite/Unknown["Events"]/RestSite["Upgrades" — a rest site
only OFFERS the choice to upgrade, doesn't guarantee it, but it's the
closest available proxy]/Shop/Treasure) and 0 otherwise.

Display avoids any custom drawing: `NMapPoint` already has private
`AnimHover()`/`AnimUnhover()` methods — the exact same visual a node gets
from a normal mouse hover — found by decompiling `NNormalMapPoint.
OnHighlightPointType`, which the game itself uses for the map LEGEND's
"highlight all nodes of this type" feature
(`NMapScreen.HighlightPointType`/`PointTypeHighlighted` event). Calling
those (via Harmony `Traverse`, since they're private) on exactly the
recommended path's nodes gets a fully native-looking highlight for free.
`MapPathHighlightPatch` Postfixes `NMapScreen.Open` (adds a small
`MapPathAdvisorPanel` goal-toggle button row on first open, re-highlights
on every open thereafter so it reflects wherever the player currently is)
and Prefixes `NMapScreen.Close` (clears any lingering highlight). Finds
`NMapPoint` nodes via a generic recursive `GetChildren()` walk rather than
reaching into `NMapScreen`'s private `_mapPointDictionary` — avoids one
more private-field dependency for something a plain tree walk already
gets for free.
`GameContext.LocalPlayer.RunState.Map`/`.CurrentMapPoint` supply the live
map graph and current position — same `IRunState` access pattern already
established for `RunSummaryReport`. Falls back to `Map.StartingMapPoint`
when `CurrentMapPoint` is null (very start of an act, before the first
move) — path[0] is always "the room the player is already in," so only
`path[1..]` gets highlighted, not the starting/current node itself.

### Changed 2026-09-01: map path advisor — combat tie-break, locked-in position, fixed missing highlight after Ancient

Three requests handled together:
- **"pick path with minimal combat for all cases except elites"**:
  `MapPathAdvisor.Weight` now returns `primary * PrimaryScale - combatPenalty`
  (PrimaryScale=1000, combatPenalty=1 for Monster rooms when goal !=
  Elites) instead of a bare 0/1. The large scale factor guarantees the
  primary goal always wins the comparison first; among ties on the primary
  goal, the path with fewer Monster rooms wins. Skipped for the Elites goal
  itself since maximizing elites necessarily means more combat by
  definition.
- **Position tuning**: added X/Y sliders + a "Log Position" button to
  `MapPathAdvisorPanel` (same tuning-panel-then-lock-in pattern as the
  Damage HUD earlier in this mod) since the original hardcoded (16, 16)
  sat on top of the relic display. Locked in at (29, 186) once reported;
  tuning controls removed.
- **"map highlights did not work after ancient... but worked after i did
  the first combat"**: `RefreshHighlight` was reading
  `GameContext.LocalPlayer?.RunState` — that static field is only populated
  by this mod's OWN hooks (`AfterDamageGiven`, `AfterPlayerTurnStart`,
  `AfterRewardTaken`), none of which necessarily fire before the very first
  time the map opens in a run (right after the Ancient/Neow-equivalent
  choice, which apparently doesn't route through `AfterRewardTaken` the way
  normal card/relic rewards do). Fixed by reading `NMapScreen`'s own
  private `_runState` field directly via Harmony `Traverse` instead —
  `Open()` already uses it internally throughout its own body, so it's
  guaranteed valid by the time our Postfix runs, sidestepping this mod's
  hook-timing entirely rather than trying to fix the timing.

### Fixed 2026-09-01: combat-avoidance tie-break never penalized Elite rooms

Reported live with a screenshot: for the Events goal, two candidate paths
tied on event count and total fight count, but one routed through an Elite
and the other through a regular fight — the advisor picked the Elite one.
Root cause: the tie-break penalty from the fix above only checked
`MapPointType.Monster`, never `MapPointType.Elite` — an Elite room scored
identically to an EMPTY room (0 penalty) under "avoid combat," so it could
never lose to a path through a real fight. Fixed with a third scoring tier:
primary goal count (dominant) -> avoid Elites (for every goal except
Elites) -> avoid regular Monster fights (lowest priority). See
`MapPathAdvisor.Weight`.

### Not yet verified live (2026-08-31 batch, continued)

None of the following have been tested in-game yet as of this note — all
built and deployed (auto-install confirmed the DLL landed) pending the next
run:
- Chart x-axis label centering fix.
- Doom-kill Applier attribution via the `BeforeDeath` cache, AND the
  MaxHp -> `_lastKnownHp` sizing fix on top of it (both landed before either
  was tested live).
- Osty dealt/taken asymmetric attribution (including the "Unleash"-style
  card-dealt-through-Osty path).
- Per-fight card play breakdown, now as a wrapping HFlowContainer grid
  instead of a long list.
- Poison-damage-dealt Applier fix.
- Gold chart StageIndex-bucketing fix (second attempt, after the Timestamp-
  grouping approach didn't actually fix the reported desync).
- Same-character NetId-based disambiguation ("Silent 1"/"Silent 2") across
  all charts, card-count panels, and the live Damage HUD table.
- Turns chart collapsed to a single shared series instead of per-player.
- Real enemy display names (via combatRoom.Encounter.Title) replacing internal encounter ids.
- Browser-based run summary report, now reading RunManager.Instance.History live instead of parsing a file (superseded the file-based version below before it was ever tested live) — the flattening fix was caught by the compiler, not a live test; the ModelDb-based title resolution, the "Run Report" button, and the generated page's actual appearance/live-updating in a browser have not been exercised live yet.
