# LocalRunStats — Product Requirements

**Status:** Shipped · **Platform:** Slay the Spire 2, native mod loader · **Updated:** 2026-09-01

> A fully designed version of this document is published as an artifact:
> [LocalRunStats PRD](https://claude.ai/code/artifact/2aac6b49-5182-4006-b199-284ad1f6bd98).
> This file is the plain-text copy kept in the repo for anyone browsing on GitHub.

## Overview

Slay the Spire 2 gives a player no way to see how a card or relic is actually performing, across
their own history or the wider community, while they're deciding whether to take it. LocalRunStats
answers that question in place — on the reward screen, on the map, and in a live report — without
asking the player to leave the game or trust a third-party service with their save data.

It runs entirely inside the game via STS2's native mod loader. The only network calls it makes are
read-only GETs against [sts2runs.com](https://sts2runs.com)'s public community API, used to enrich
reward-screen stats with pick rates and win rates drawn from thousands of other players' runs.
Nothing the player does locally is ever uploaded.

## Goals

- **Decide with data, in the moment** — pick/impact/synergy on the reward screen, not a
  spreadsheet after the run ends.
- **Work in co-op** — every panel (damage, gold, card stats, map advisor) is multiplayer-aware
  from the start, not bolted on.
- **Never leave the player worse off** — local-only by default, and the one save-side tool
  (unlock seeding) always backs up before it writes.
- **Match the game's own presentation** — native fonts, native hover tooltips, native
  hover-highlight animation. It should look like it shipped with the game.

## Non-goals

- No server or backend of our own — no accounts, no hosting, nothing to keep running.
- No uploading this player's own run data anywhere, ever.
- No new game content — no cards, relics, or characters. This is an analytics layer, not a
  content mod.
- No Steam Workshop distribution at this time — install is a manual DLL drop or a build script.

## Target users

- **The player running it** — wants reward-screen stats and a live run report while playing solo
  or in co-op, without standing up any infrastructure themselves.
- **Their co-op party** — needs the same mod installed locally to see their own panels; nothing
  is shared between players' clients besides the game's own multiplayer state.

## Feature requirements

All seven features below are shipped and verified against live gameplay.

| Feature | What it does |
|---|---|
| **Combat Damage HUD** | Persistent top-right panel — damage dealt/taken per player, per fight, per act, and for the run so far. |
| **Card & relic reward overlays** | Pick rate, Impact (community win-rate delta), Synergy (deck-similarity-weighted win rate vs. local + cached community runs), and a thematic keyword tag. |
| **Graph overlay** | Full-screen charts — Damage/Gold per-stage or cumulative, filterable by act; a second tab for turns-per-fight and a per-fight card-play grid. Styled to match the game's native tooltip look. |
| **Card hover stats** | Times Played, Times Drawn, Play Rate for the current run, inside the game's own native tooltip. |
| **Run summary report** | Browser report, floor by floor, for the run *in progress* — reads live run state, so it's accurate mid-run. Co-op runs get a tab per player. |
| **Map path advisor** | Highlights the best remaining route on the map for a chosen goal (Elites / Events / Upgrades / Shops / Treasure), using the game's own native hover-highlight. |
| **Unlock seeding** | Copies unlock progress from the main save into the modded profile, one-time, with an automatic backup first. |

## Technical foundation

Every hook signature and UI extension point was confirmed by decompiling the game's own assembly
before being relied on — never assumed from an IDE's signature help.

- **Mod loader** — STS2's native loader (`MegaCrit.Sts2.Core.Modding`), no BepInEx. Entry via
  `[ModInitializer]`.
- **UI extension** — Harmony patches for card/relic/tooltip/map screens; `AbstractModel` overrides
  for combat/run-state hooks.
- **Hook registration** — not automatic; every hook-listening model must be added explicitly in
  `RunStatsRecorder.Initialize()`, or it silently never fires.
- **External dependencies** — none. Evaluated BaseLib and RitsuLib (shared STS2 modding
  frameworks); declined — neither solves an open problem here, and both would mean every co-op
  player also installing them.
- **Data retention** — unbounded local JSONL logs plus a capped community-run cache.

## Distribution & setup

1. **Build** — `pull-and-install.ps1` / `.sh` pulls, builds in Release, and copies the DLL into
   the local `mods/LocalRunStats` folder, auto-detecting common Steam install locations.
2. **Seed unlocks** — `seed-unlocks.ps1` / `.sh` copies the main save's unlock progress into the
   modded profile. Requires Steam Cloud sync off for the game first.
3. **Launch** — the game's own log confirms load with `RUNNING MODDED!`.

## Open questions

- **Resolved** — Can the mod preview an encounter before you travel to it? No: confirmed by
  decompiling the room-creation path, the specific encounter is pulled from a pool at the exact
  moment of travel. Ruled out an "elite/boss preview" feature on this basis.
- **Under consideration** — A danger warning when current HP is unusually low for this point in
  the run, compared to how past runs at similar HP/floor turned out. The only open idea that would
  change a decision mid-run rather than just inform after the fact.
- **Under consideration** — Cross-run trends (career win rate, most-played characters/cards over
  time) — every run is already logged, so this is aggregation, not new collection.
- **Under consideration** — Potion usage tracking, to match the Times Played/Drawn cards already
  get.
