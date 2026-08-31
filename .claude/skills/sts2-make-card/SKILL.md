---
name: sts2-make-card
description: Creates Slay the Spire 2 cards (attacks, skills, powers). Use when adding or editing an STS2 card, card pool, upgrade, portrait, colorless vs character color, or card localization.
---

# Make an STS2 Card

A card is a `CardModel` (prefer BaseLib `CustomCardModel` or the template’s `YourModCard`).

Lifecycle: class → `ModelDb` id → **pool membership** → loc → portrait → `OnPlay` commands → upgrade.

## Identity

`FieldNotesCard` → `FIELD_NOTES_CARD`. That slug keys loc and default art. Do not rename after a public release.

## Implementation (vanilla-style; BaseLib subclass is the same `OnPlay`)

```csharp
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

public sealed class FieldNotesCard : CardModel
{
    public override bool GainsBlock => true;

    public override string PortraitPath =>
        "res://FieldNotes/images/cards/field_notes.png";

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new BlockVar(6m, ValueProp.Move),
        new CardsVar(1)
    ];

    public FieldNotesCard()
        : base(
            canonicalEnergyCost: 1,
            type: CardType.Skill,
            rarity: CardRarity.Uncommon,
            targetType: TargetType.Self)
    {
    }

    protected override async Task OnPlay(
        PlayerChoiceContext choiceContext,
        CardPlay cardPlay)
    {
        await CreatureCmd.GainBlock(
            Owner.Creature,
            DynamicVars.Block,
            cardPlay);

        await CardPileCmd.Draw(
            choiceContext,
            DynamicVars.Cards.BaseValue,
            Owner);
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Block.UpgradeValueBy(3m);
    }
}
```

With BaseLib, inherit `CustomCardModel`, pass the same constructor args, and optionally `CustomPortraitPath`. Auto-add registers the type; **still add it to a pool**.

## Registration

Vanilla:

```csharp
ModHelper.AddModelToPool<ColorlessCardPool, FieldNotesCard>();
```

Character cards: that character’s pool (template `YourModCard` / `CustomCardPoolModel`). Do not register the same model in two ordinary pools — `CardModel.Pool` picks the first match.

Call pool add in `[ModInitializer]` **before** anything enumerates pools.

## Commands vs patches

Implement the card in `OnPlay`. Do not Harmony the global play pipeline. After-play observers use `Hook.AfterCardPlayedLate`, not a wrapped `OnPlay`.

Useful commands: `CreatureCmd.GainBlock`, `DamageCmd.Attack`, `CardPileCmd.Draw` / `Add`, `PowerCmd.Apply<T>`, `PlayerCmd.*`.

Calculated damage/block: BaseLib `MakeCalculatedDamage` / `MakeCalculatedBlock`.

## Localization

`godot` or template folder → packed as:

```text
res://<ModId>/localization/eng/cards.json
```

```json
{
  "FIELD_NOTES_CARD.title": "Field Notes",
  "FIELD_NOTES_CARD.description": "Gain {Block:diff()} [gold]Block[/gold].\nDraw {Cards:diff()} card."
}
```

Formatters must match `CanonicalVars` names (`{Damage:diff()}`, `{Energy:energyIcons()}`, `{Cards:diff()}`). Copy a vanilla card that uses the same var type.

## Portrait

Override `PortraitPath` / `CustomPortraitPath` to a namespaced PNG. Frame, banner, and energy chrome come from the **pool**. Default atlas path is `res://images/atlases/card_atlas.sprites/<pool>/` — avoid colliding with vanilla.

## Mutable instances

`ModelDb.Card<T>()` is canonical. Decks need:

```csharp
var card = player.RunState.CreateCard<FieldNotesCard>(player);
await CardPileCmd.Add(card, PileType.Deck);
```

## Upgrades

Keep upgrades as `DynamicVar` mutations so `{Block:diff()}` and preview clones stay correct. Extra knobs: `MaxUpgradeLevel`, `GetResultPileType`, keywords/tags. Preview clones can double-apply upgrades if code mutates the wrong instance.

## Validation

1. `ModelDb.Card<T>()` resolves
2. Target pool’s `AllCards` contains it
3. Reward or grant creates an owner-bound card
4. Base and upgraded descriptions
5. Portrait in combat, deck, reward, library
6. Lands in the expected result pile
7. Save/load

If the card exists but never appears in rewards: pool / unlock / rarity filter — not `OnPlay`.
