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
