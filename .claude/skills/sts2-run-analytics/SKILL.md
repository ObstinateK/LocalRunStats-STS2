---
name: sts2-run-analytics
description: Analyzes Slay the Spire 2 run history. Use when the user wants stats over all STS2 runs, card pick rates, event rooms, win rate by character/ascension, or to read history/*.run JSON (every floor's choices). Prefer local parsing; do not upload runs unless asked.
---

# STS2 Run Analytics

STS2 already writes a JSON `.run` file after **every finished run**. That file is the analytics source: character, seed, win, floor-by-floor rooms, card picks (`was_picked`), potions, relics, HP/gold, and event/encounter ids.

Do not scrape Steam Cloud or rewrite saves. Read history. Do not rebuild `current_run.save`.

## Where the files are

Windows:

```text
%APPDATA%\SlayTheSpire2\steam\<steam_id>\profile*\saves\history\*.run
%APPDATA%\SlayTheSpire2\steam\<steam_id>\modded\profile*\saves\history\*.run
```

macOS: `~/Library/Application Support/SlayTheSpire2/steam/<id>/...`

Linux: `~/.local/share/SlayTheSpire2/steam/<id>/...` (confirm on the machine)

**Modded runs live under `modded\`.** Vanilla and modded profiles are separate. Always scan both unless the user says otherwise.

In-progress runs are `saves/current_run.save` (and `_mp`). Those are live state, not the finished-history schema. Analytics over "all my runs" = `history/*.run` only.

## First action on a new machine

Run the bundled script (stdlib only):

```bash
python .claude/skills/sts2-run-analytics/scripts/summarize_runs.py
python .claude/skills/sts2-run-analytics/scripts/summarize_runs.py --dump-sample
```

If this skill was copied user-wide, run `scripts/summarize_runs.py` from the skill folder. Pass `--history` to a specific `saves/history` directory.

`--dump-sample` prints top-level keys and one map-point's keys. **Schema version drifts** (`schema_version` in the file). Never assume a field from an old blog post if the sample dump disagrees.

## Known RunHistory shape (community-confirmed)

Top level (names are snake_case JSON):

| Field | Meaning |
| --- | --- |
| `win` | Victory |
| `was_abandoned` | Quit vs killed |
| `ascension` | Ascension |
| `seed` | Run seed |
| `start_time` | Unix time; filename often matches |
| `run_time` | Duration |
| `game_mode` | standard / daily / custom |
| `build_id` / `schema_version` | Game build |
| `acts` | Act ids |
| `modifiers` | Run mods |
| `killed_by_encounter` / `killed_by_event` | Cause of death |
| `players[]` | `character`, `deck`, `relics`, `potions`, `id` |
| `map_point_history` | `Vec<Vec<MapPoint>>` — act floors → points |

Each map point:

- `map_point_type`: `monster` / `elite` / `boss` / `rest_site` / `shop` / `treasure` / `unknown` / `ancient`
- `rooms[]`: encounter (`model_id`, `monster_ids`, `turns_taken`, `room_type`) **or** event (`model_id`, `turns_taken`) **or** other
- `player_stats[]`: HP/gold deltas plus **`card_choices`** and **`potion_choices`**

Each card choice:

```json
{ "card": { "id": "STRIKE", "floor_added_to_deck": 3 }, "was_picked": true }
```

`was_picked: false` is a skip/reject — this is the pick-rate denominator.

Relics on the player object include `floor_added_to_deck`. Event **option text** is not always a dedicated field in older schemas; dump keys on an `Event` room from a real file before claiming "every dialogue click is stored". Room `model_id` is still enough to count which events fired.

## What to compute (default dashboard)

When the user says "analytics on all my runs", produce:

1. Run count, win / kill / abandon, win rate
2. Split by `character`, `ascension`, `game_mode`, vanilla vs `modded` folder
3. Card pick rate: picked / offered (`was_picked`)
4. Relic pickup rate by `floor_added_to_deck` + win rate when held
5. Event frequency by `rooms[].model_id` on event rooms
6. Encounter frequency + `killed_by_encounter`
7. Average `run_time`, floor reached (len of `map_point_history`)
8. Optional: HP/gold per floor from `player_stats`

Write a markdown or CSV next to the repo, not into AppData.

Filter before aggregating (character, min ascension, date via `start_time`).

## Live / extra telemetry (only if history is not enough)

Vanilla history is **per map point**, not per card play in combat. If the user wants "every card I played":

1. Prefer an existing local tool (SpireScope, sts2.gg local parser, slaythestats import) before writing a new uploader
2. Or a **display-only** mod: subscribe combat hooks, append JSONL under the user's documents folder
3. `ModManager.OnMetricsUpload` is Mega Crit telemetry — do not piggyback personal analytics onto that hook unless the user explicitly wants game metrics traffic inspected

Keep extra logs opt-in via BaseLib `SimpleModConfig`. Set `affects_gameplay: false` if the mod only writes stats.

## In-game UI

To show stats **in STS2**, attach a panel (`sts2-modify-ui`) that reads a cached summary JSON the script wrote, or parses `history/` on overlay open (cache; do not parse hundreds of files every frame).

## Privacy

Default: local files only. Do not POST runs to Railway/sts2.gg/slaythestats unless the user names that destination. Steam IDs live in the path — do not put them in committed sample dumps.

## Validation

1. Script finds at least one `history` folder or prints the paths it tried
2. Sample dump shows `map_point_history` and `players`
3. Pick rates use offered counts, not only final deck
4. Modded vs vanilla folders are labeled
5. One known run's win flag matches the in-game run history screen
