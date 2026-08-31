---
name: sts2-make-character
description: Creates a playable Slay the Spire 2 character (custom class). Use when the user wants a new STS2 character, starter deck, character select art, energy counter, rest-site visuals, or a character-specific card pool.
---

# Make an STS2 Character

Build a playable class with BaseLib `CustomCharacterModel` (or the character template’s generated subclass).

## Start from the character template

If this repo is not already a character mod:

```bash
dotnet new alchyrsts2charmod --ModAuthor Name -o TheAlchemist
```

The template already subclasses BaseLib, wires a card pool, and expects loc generation (Rider Alt+Enter “Generate localization”, or the BaseLib analyzer).

Do not invent a second character registration path. `CustomCharacterModel` constructors call `CustomContentDictionary.AddCharacter(this)`.

## Minimum playable loop

A character that only exists on the select screen is unfinished. Ship:

1. `CustomCharacterModel` with HP, gold, starting relics, starting deck
2. Character-colored `CustomCardPoolModel` + 8–12 cards (strikes/defends + 2 signature cards)
3. One starter relic
4. Loc: `characters.json`, `cards.json`, `relics.json`, `ancients.json` if the template requires it
5. Visual stubs: combat creature scene, char-select icon, in-run icon
6. Publish (PCK), then a new run from character select

## Model

Inherit `BaseLib.Abstracts.CustomCharacterModel`. Override only what the fantasy needs. Important knobs:

| Concern | Typical override / path |
| --- | --- |
| Loc in-class | `Localization` → `CharacterLoc` (or generated JSON) |
| Hide from vanilla select | `HideFromVanillaCharacterSelect` |
| Random-select eligible | `AllowInVanillaRandomCharacterSelect` |
| Combat body | `CustomVisualPath` or `res://scenes/creature_visuals/<class>.tscn` |
| Select screen | `CustomCharacterSelectBg`, `CustomCharacterSelectIconPath`, locked icon, transition |
| In-run / history icon | `CustomIconPath` or `CustomIcon` |
| Energy orb | `CustomEnergyCounterPath` (preferred) |
| Rest / merchant | `CustomRestSiteAnimPath`, `CustomMerchantAnimPath` |
| SFX | `CustomAttackSfx`, `CustomCastSfx`, `CustomDeathSfx` |
| Starting gold | `StartingGold` (vanilla default 99) |

Spine/Godot anims: `SetupAnimationState` if the skeleton is missing Attack/Hit/Dead clips. `CreateCustomVisuals` only when a generated `NCreatureVisuals` is not enough.

Inspect a **current** vanilla `CharacterModel` (Ironclad equivalent) in `sts2.dll` for starting HP, deck construction, and relic grant. Copy that construction style; do not guess field names from STS1.

## Card pool

Register the character’s cards on **that character’s pool**, not `ColorlessCardPool`, unless they are truly colorless.

Template pattern: inherit `YourModCard` so pool + color + energy tint stay consistent. Colorless/shared cards are a later expansion.

## Character-select and UI

BaseLib already patches `NCharacterSelectScreen` so `CustomCharacterModel` instances appear (unless hidden). After adding art:

- Confirm the button uses `CustomCharacterSelectIconPath`
- Confirm locked state art if the character is gated
- Confirm `UnlocksAfterRunAs` only if designing an unlock, not for the first custom class

Do not Harmony-transpiler the select screen unless BaseLib’s hide/show flags are insufficient.

## Animation and chrome (often forgotten)

Missing these makes the class feel “modded-broken”:

- Rest site anim
- Merchant anim
- Map marker (`CustomMapMarkerPath`)
- Energy counter + `%StarAnchor` if using a custom orb (BaseLib reparents the star counter)

Ship placeholders (recolored Ironclad-adjacent scenes) rather than null paths.

## Validation

1. Character appears on select (and not duplicated).
2. New run loads combat visuals, energy counter, HP.
3. Starter relic obtained via `RelicCmd` / vanilla obtain path — not stuffed into a list.
4. Starter cards are owner-bound mutable instances from `RunState.CreateCard`.
5. Compendium filter works unless `HideInCompendium`.
6. Save/load mid-run.
7. Multiplayer: set `affects_gameplay: true`; lobby partners need the mod.

## Prompt pattern

User: “Alchemist, potion master, 68 HP, relic that +1 potion slot, cards about brewing.”

Agent: implement character + starter relic + 2 potions + 4 brew cards, leave a TODO list for remaining deck/elites. Do not stall for a 60-card design doc.
