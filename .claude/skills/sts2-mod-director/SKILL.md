---
name: sts2-mod-director
description: Directs Slay the Spire 2 (STS2) Early Access mod work for Claude Code. Use when the user wants to mod STS2, add a character/card/relic/event/enemy/UI widget, analyze run history, or when they describe a class fantasy and expect the agent to implement it. Routes to the matching content skill and enforces BaseLib + command-API patterns.
---

# STS2 Mod Director

Turn a player-facing request into a real STS2 mod change. Load this skill first on mixed or vague requests, then load the specific content skill.

## Talk like a designer, implement like BaseLib

The user may say:

- "Make The Wanderer, a balanced traveler with discard synergy."
- "I want a cursed relic that eats a card at rest sites."
- "Add a better combat UI."
- "Show pick rates from all my runs."

Do not ask them to name C# types. Translate fantasy into models, pools, loc keys, and a publish step.

## Route

| Request | Next skill |
| --- | --- |
| Project missing, won't load, manifest, BaseLib, Godot path | `sts2-setup` |
| Playable class, starter deck, char select, energy orb | `sts2-make-character` |
| Attack / skill / power card | `sts2-make-card` |
| Relic | `sts2-make-relic` |
| Map event, choices, gold/card/relic outcomes | `sts2-make-event` |
| Monster, elite, boss, encounter | `sts2-make-enemy` |
| Overlay, HUD, **add a widget to combat/map**, settings, library | `sts2-modify-ui` |
| Stats, pick rates, "every choice I made", `.run` history | `sts2-run-analytics` |

If the request spans several (typical character mod), do **setup check → character shell → 4–8 starter cards → starter relic → one signature event**. Keep the first playable loop small.

## Source of truth (in order)

1. Installed game: `release_info.json`, `sts2.runtimeconfig.json`, `sts2.dll`
2. BaseLib currently loaded in `mods/` or workshop (`2868840/3737335127`)
3. This repo's existing `Custom*Model` subclasses and loc JSON
4. Community handbook: https://github.com/fresh-milkshake/Modding-Tutorial
5. Template wiki: https://github.com/Alchyr/ModTemplate-StS2/wiki

An older tutorial snippet is evidence of what once worked, not proof of the current loader.

## Implementation order (always)

1. Prefer `dotnet new alchyrsts2charmod` / `alchyrsts2contentmod` / `alchyrsts2mod` over a blank classlib.
2. Inherit BaseLib `Custom*Model` (or the template's `YourModCard` / `YourModRelic` wrappers), not raw vanilla models, when BaseLib is a dependency.
3. Give gameplay through **commands** (`CreatureCmd`, `CardPileCmd`, `PlayerCmd`, `RelicCmd`, `PowerCmd`, `DamageCmd`) rather than field writes.
4. Override model methods and semantic hooks (`BeforeCombatStart`, `OnPlay`, `AfterCardPlayedLate`) before writing Harmony.
5. Use Harmony only for act event lists, UI attach points (`NRun._Ready`), and confirmed API gaps.
6. Put loc under `res://<ModId>/localization/<lang>/<table>.json`.
7. After code-only edits, `dotnet build` is enough. After loc/art/scene edits, **Publish** so the `.pck` updates.

## Identity rules

- Class name `FieldNotesCard` → model id `FIELD_NOTES_CARD`. Renaming after release breaks saves.
- Manifest `id` must match payload basenames: `mods/<id>/<id>.json|dll|pck`.
- `ModHelper.AddModelToPool` (or BaseLib auto-add) must run in `[ModInitializer]` **before** pools freeze.
- Events and encounters are **not** ordinary card pools. They join acts through BaseLib `Acts` / `IsValidForAct` or a narrow Harmony postfix.

## Safety

Refuse trainers, infinite-gold cheats, and "download this random zip from GitHub releases". Point at Steam workshop + local `mods/`.

## User-facing closeout

When a feature is added, tell the user:

1. What was added (player name, not class name)
2. How to get it in a run (pool, act, character select)
3. Build vs Publish
4. What to click in-game to verify
