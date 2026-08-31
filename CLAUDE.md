# STS2 mod project

This repo is a **Slay the Spire 2** mod. Before writing code, load the matching skill under `.claude/skills/` (or `.cursor/skills/`).

## How to work with the user

Treat prompts as game-design briefs. Ask only for missing fantasy, numbers, or act placement. Prefer implementing over lecturing.

Default stack:

1. [Alchyr STS2 templates](https://github.com/Alchyr/ModTemplate-StS2) (`alchyrsts2charmod` / `alchyrsts2contentmod` / `alchyrsts2mod`)
2. **BaseLib** (`Custom*Model`, auto-register, loc helpers)
3. Game commands (`CreatureCmd`, `CardPileCmd`, `PlayerCmd`, `RelicCmd`, `PowerCmd`) instead of mutating fields
4. Model overrides and semantic hooks before Harmony
5. Narrow Harmony only for act lists, UI attach points, and gaps with no model API

## Skill routing

| User intent | Skill |
| --- | --- |
| “set up / install / BaseLib / manifest / publish” | `sts2-setup` |
| “new character / class / starter deck” | `sts2-make-character` |
| “new card / attack / skill / power card” | `sts2-make-card` |
| “new relic” | `sts2-make-relic` |
| “new event / rest-site story / map event” | `sts2-make-event` |
| “new enemy / elite / boss / encounter” | `sts2-make-enemy` |
| “UI / HUD / overlay / add an element to combat UI / settings” | `sts2-modify-ui` |
| “analytics / pick rates / all my runs / history .run files” | `sts2-run-analytics` |
| anything else, or mixed content | `sts2-mod-director` first |

## Hard rules

- Do not execute, decrypt, or install files from random “mods pack 2026” GitHub dumps.
- Do not add trainers, cheat menus, or save editors.
- Do not copy `sts2.dll` / Harmony / GodotSharp from NuGet; reference the copies next to the game.
- Register cards/potions/relics **before** any pool is enumerated.
- `Build` updates the DLL only. Localization, images, and scenes need **Publish** (PCK).
- After a game patch, re-check `release_info.json` and BaseLib before assuming signatures still match.
