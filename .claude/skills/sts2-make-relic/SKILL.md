---
name: sts2-make-relic
description: Creates Slay the Spire 2 relics. Use when adding or editing an STS2 relic, starter relic, relic pool, relic icons, combat-start hooks, counters, or relic localization.
---

# Make an STS2 Relic

Relics are persistent `RelicModel` instances (prefer BaseLib `CustomRelicModel` or template `YourModRelic`). They receive semantic combat/run hooks while owned. Pool membership is mandatory — hover/inspect throw if `Pool` cannot be found.

## Implementation

```csharp
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

public sealed class SurveyorLensRelic : RelicModel
{
    public override RelicRarity Rarity => RelicRarity.Common;

    public override string PackedIconPath =>
        "res://FieldNotes/images/relics/surveyor_lens.png";

    protected override string PackedIconOutlinePath =>
        "res://FieldNotes/images/relics/surveyor_lens_outline.png";

    protected override string BigIconPath =>
        "res://FieldNotes/images/relics/surveyor_lens_large.png";

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new BlockVar(6m, ValueProp.Unpowered)
    ];

    protected override IEnumerable<IHoverTip> ExtraHoverTips =>
    [
        HoverTipFactory.Static(StaticHoverTip.Block)
    ];

    public override async Task BeforeCombatStart()
    {
        Flash();
        await CreatureCmd.GainBlock(
            Owner.Creature,
            DynamicVars.Block,
            cardPlay: null);
    }
}
```

`ValueProp.Unpowered` matches “not a card move”. Copy the `ValueProp` from a vanilla relic with the same timing.

Starter relics: same model, granted from the character’s starting-relic list (inspect current `CharacterModel`), not from `SharedRelicPool` if they must not appear as hallway rewards — or gate with `IsAllowed`.

## Registration

```csharp
ModHelper.AddModelToPool<SharedRelicPool, SurveyorLensRelic>();
```

Character-only relics: that character’s relic pool / BaseLib `CustomRelicPoolModel`. BaseLib `CustomRelicModel` auto-adds the **type**; still put it in a pool.

## Obtain

```csharp
var relic = ModelDb.Relic<SurveyorLensRelic>().ToMutable();
await RelicCmd.Obtain(relic, player);
```

Do not insert a canonical relic into a collection. Use `AfterObtained` / `AfterRemoved` for ownership state. Combat hooks may assume `Owner` is set.

## Localization

```text
res://<ModId>/localization/eng/relics.json
```

```json
{
  "SURVEYOR_LENS_RELIC.title": "Surveyor Lens",
  "SURVEYOR_LENS_RELIC.description": "At the start of combat, gain {Block:diff()} [gold]Block[/gold].",
  "SURVEYOR_LENS_RELIC.flavor": "Every path looks shorter after it has been measured.",
  "SURVEYOR_LENS_RELIC.selectionScreenPrompt": "Choose Surveyor Lens."
}
```

Event text can use `eventDescription`; otherwise the ordinary description is reused.

## Three icon paths

| Slot | Used by |
| --- | --- |
| packed small | top bar, lists |
| packed outline | some hover/presentation |
| large | inspect, rewards, events |

Override all three to namespaced PNGs. Testing only the top-bar icon is not enough.

## Stateful relics

Inspect vanilla relics for:

- `ShowCounter` / `DisplayAmount` / `InvokeDisplayAmountChanged`
- save-property fields that survive process restart
- `IsAllowed(IRunState)`

Mutate only mutable instances. BaseLib `GetUpgradeReplacement()` is for relics that transform.

## Hooks vs Harmony

Override `BeforeCombatStart`, turn hooks, `AfterCardPlayedLate`, etc. on the relic. Subscribe extra models with `ModHelper.SubscribeForRunStateHooks` / `SubscribeForCombatStateHooks` only when the behavior is not naturally on a relic/card/power.

## Validation

1. Pool contains the model (`get_Pool()` must not throw)
2. Reward can generate it (unless starter-only)
3. `RelicCmd.Obtain` owns a mutable instance
4. Combat hook fires once per combat
5. All three arts + hover/inspect
6. Save/load
7. Collection discovered vs locked presentation
