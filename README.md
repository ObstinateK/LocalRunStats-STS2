# Local Run Stats

A self-contained companion analytics mod for **Slay the Spire 2**, in the spirit of
[No Rogues](https://github.com/sebastientromp/no-rogues-releases) — but with no backend or
companion app of its own. Everything runs inside the game via the native mod loader; the only
network calls are read-only GETs against [sts2runs.com](https://sts2runs.com)'s public community
API, used to enrich reward-screen stats with community-wide pick/win rates.

This is local-only, single-player-and-co-op-friendly, and never uploads or shares your own run
data anywhere.

## Features

- **Combat Damage HUD** — a persistent top-right panel showing damage dealt/taken per player
  (co-op aware), broken down per fight, per act, and for the whole run.
- **Card & relic reward overlays** — on the reward screen, each card/relic shows:
  - **Pick rate** and **Impact** (win-rate delta), pulled from sts2runs.com's community data
  - **Synergy**, a deck-similarity-weighted win rate comparing this run's current deck against
    past runs (local history + cached community runs)
  - A thematic keyword tag (e.g. `[Vulnerable]`) when the card/relic shares a mechanic with your
    current deck
- **Graph overlay** (via the 📈 button on the HUD) — full-screen charts for the current run,
  styled to match the game's own native tooltip look (same background, same gold section titles):
  - Damage Dealt / Damage Taken / Gold, per-stage (bar) or cumulative (line), filterable by act
  - Turns per fight and a per-fight card-play breakdown grid
- **Card hover stats** — hovering any card shows **Times Played**, **Times Drawn**, and **Play
  Rate** for the current run, right in the game's own tooltip. Inspired by
  [rmac-silva/CardTracker](https://github.com/rmac-silva/CardTracker).
- **Run Summary Report** — a "Run Report" button on the graph overlay opens a floor-by-floor
  browser report (styled after sts2runs.com's run-detail page) for your **current, in-progress**
  run — HP/gold per floor, cards/relics/potions gained, monster names, and more. Co-op runs get a
  tab per player.
- **Map Path Advisor** — a small "Best Path For:" panel on the map screen highlights the
  recommended remaining route (using the game's own native hover highlight) for a goal you choose:
  most Elites, most Events, most Upgrade opportunities (rest sites), most Shops, or most Treasure.
  Ties on the chosen goal are broken by avoiding Elites, then avoiding regular fights — except when
  the goal is Elites itself, where more Elite fights is the point, but it still prefers fewer
  regular fights along the way.

## Installing

You need the compiled DLL, not just this source — either build it yourself (see below) or grab
a build someone already made.

1. Close Slay the Spire 2 if it's running.
2. Copy `LocalRunStats/local-run-stats.dll` and `LocalRunStats/LocalRunStats.json` into:
   - Windows: `<Slay the Spire 2 install>\mods\LocalRunStats\`
   - macOS: `<Slay the Spire 2 install>/mods/LocalRunStats/` (inside the `.app`'s Contents/MacOS)
3. Launch the game. Check `godot.log` for `RUNNING MODDED!` to confirm it loaded.

## Building

Requires the .NET 9 SDK. From the repo root:

**Windows:**
```powershell
powershell -File .\pull-and-install.ps1
```

**macOS:**
```bash
./pull-and-install.sh
```

Both scripts `git pull`, build in Release, and copy the result into your local `mods/LocalRunStats`
folder automatically (auto-detecting common Steam install locations — pass a path explicitly if
yours isn't found). Close the game first; it locks the DLL while running.

## Bringing your unlock progress into a modded save

Slay the Spire 2's mod loader always routes modded sessions to a separate save profile, so a
modded save starts with nothing unlocked even if your main save has everything. Run
`seed-unlocks.ps1` (Windows) or `seed-unlocks.sh` (macOS) to copy your main save's unlock progress
(`progress.save`) into your modded profile — one-time, with an automatic backup first:

```powershell
powershell -File .\seed-unlocks.ps1
```
```bash
./seed-unlocks.sh
```

**Turn off Steam Cloud sync for the game first** (Steam Library → right-click the game →
Properties → General) — otherwise Steam will silently pull the old cloud-synced save back down on
your next launch and undo the copy.

## Technical notes

See [`LocalRunStats/MOD_SPEC.md`](LocalRunStats/MOD_SPEC.md) for the full technical history: hook
signatures verified by decompiling `sts2.dll`, known pitfalls, and the reasoning behind every
non-obvious decision in the code.
