---
name: sts2-make-enemy
description: Creates Slay the Spire 2 monsters and encounters (hallway, elite, boss). Use when adding an STS2 enemy, custom fight, encounter scene with Marker2D slots, or injecting an encounter into an act.
---

# Make an STS2 Enemy

An enemy is two models:

1. **Monster** — HP, moves, intents, visuals (`CustomMonsterModel` / vanilla monster model)
2. **Encounter** — who spawns, room type, act eligibility, combat scene (`CustomEncounterModel`)

A monster that is never referenced by an encounter will not appear on the map.

## Encounter (BaseLib)

```csharp
using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;

public sealed class TwinSlimeEncounter : CustomEncounterModel
{
    public TwinSlimeEncounter() : base(RoomType.Monster) { }

    public override bool IsValidForAct(ActModel act) =>
        act is Overgrowth; // inspect current act type names in sts2.dll

    public override string? CustomScenePath =>
        "res://MyMod/scenes/encounters/twin_slime.tscn";

    // Required by EncounterModel (confirm exact signatures in current BaseLib/sts2.dll):
    // AllPossibleMonsters — every monster that can appear
    // GenerateMonsters — mutable instances for this fight; use this.Rng for rolls
}
```

`RoomType` must be `Monster`, `Elite`, or `Boss`. Weak hallway fights: override `IsWeak` (first 3 fights in act 1, first 2 later). Tags stop back-to-back similar encounters.

Constructor `autoAdd: true` registers via `CustomContentDictionary.AddEncounter`. For a **custom act**, keep the act’s vanilla encounter list empty and return true from `IsValidForAct` for that act only.

## Combat scene

1920×1080 `Control`, full-rect anchors, `MouseFilter` Ignore, `Marker2D` children as slots. Initial monsters are placed in **marker order**. Extra spawns: `CreatureCmd.Add` with those marker names. Override `Slots` only if not using the default scene reader.

Backgrounds: `CustomEncounterBackground` or vanilla path `res://scenes/backgrounds/<id>/...`. Boss map icons: `CustomRunHistoryIconPath`.

## Monster

Inspect `CustomMonsterModel` in the loaded BaseLib version and a vanilla trash mob in `sts2.dll`. Typical work:

- HP / strength scaling with ascension
- Move pool + intent strings (loc table `monsters.json` or current table name)
- Powers applied at combat start via `PowerCmd`
- Visual path (Spine/Godot), same conversion rules as characters
- Death / attack SFX

Generate **mutable** monster instances in `GenerateMonsters`, not canonical `ModelDb` rows.

## Act placement without BaseLib

There is no card-style pool for encounters. Postfix the act’s encounter list getter (same spirit as events). Keep duplicate-id guards. Existing saves do not retroactively rewrite generated maps.

## Loc and assets

```text
res://<ModId>/localization/eng/monsters.json
```

Plus encounter title if the UI shows one. Publish the PCK after scene/art changes.

## Validation

1. `ModelDb` resolves the encounter and each monster
2. A **new** Overgrowth (or target act) run can roll the fight
3. Weak/elite/boss appear in the intended map-node rarity
4. Markers line up; no overlapping hitboxes
5. Intents, HP, and loc show in combat
6. Kill credits / run history icon (boss)
7. Save/load mid-act still has remaining map nodes

## Prompt pattern

User: “Act 1 weak fight, two slimes, one splits.” Implement one encounter + two monster classes + a 2-slot scene. Leave split-on-death as a follow-up that inspects a vanilla splitter.
