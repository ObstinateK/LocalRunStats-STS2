---
name: sts2-make-event
description: Creates Slay the Spire 2 map / act events. Use when adding an STS2 event, event options, event combat, event localization, or injecting an event into Overgrowth or another act.
---

# Make an STS2 Act Event

Events are `EventModel` (prefer BaseLib `CustomEventModel`). They are **discovered** by `ModelDb` but **selected** from an act’s `AllEvents` plus `ModelDb.AllSharedEvents`. There is no `ModHelper.AddModelToPool` for events.

## Prefer BaseLib injection

`CustomEventModel.Acts` — if empty, the event is treated as shared (any act). If set (e.g. `Overgrowth`), BaseLib adds it to those acts. Override `IsAllowed` to prevent spawning.

Vanilla-only: Harmony postfix on the concrete act getter (see below).

## Model (vanilla-style)

```csharp
public sealed class AbandonedObservatoryEvent : EventModel
{
    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new GoldVar(60),
        new StringVar("Card", ModelDb.Card<FieldNotesCard>().Title)
    ];

    protected override IReadOnlyList<EventOption> GenerateInitialOptions()
    {
        return
        [
            new EventOption(
                this,
                TakeNotes,
                InitialOptionKey("TAKE_NOTES"),
                HoverTipFactory.FromCardWithCardHoverTips<FieldNotesCard>()),
            new EventOption(
                this,
                SellNotes,
                InitialOptionKey("SELL_NOTES"))
        ];
    }

    private async Task TakeNotes()
    {
        var owner = Owner ?? throw new InvalidOperationException("Event has no owner.");
        var card = owner.RunState.CreateCard<FieldNotesCard>(owner);
        var addedCard = await CardPileCmd.Add(card, PileType.Deck);
        CardCmd.PreviewCardPileAdd(addedCard, 2f);
        SetEventFinished(L10NLookup(
            "ABANDONED_OBSERVATORY_EVENT.pages.TAKE_NOTES.description"));
    }

    private async Task SellNotes()
    {
        var owner = Owner ?? throw new InvalidOperationException("Event has no owner.");
        await PlayerCmd.GainGold(DynamicVars.Gold.IntValue, owner);
        SetEventFinished(L10NLookup(
            "ABANDONED_OBSERVATORY_EVENT.pages.SELL_NOTES.description"));
    }
}
```

BaseLib helpers: `Option(TakeNotes)`, `LockedOption(...)`, `PageDescription("INITIAL")`, `CustomInitialPortraitPath`.

Passing `null` instead of a delegate creates a locked option.

## Localization

```text
res://<ModId>/localization/eng/events.json
```

Keys follow `EVENT_ID.pages.INITIAL.description` and `…options.TAKE_NOTES.title|description`. `InitialOptionKey("TAKE_NOTES")` expands to that path. Keep it so event history UI resolves vanilla-style keys.

## Portrait / layout

Default layout looks up:

```text
res://images/events/abandoned_observatory_event.png
```

(`CreateInitialPortrait` is not virtual in older handbooks — BaseLib `CustomInitialPortraitPath` is the namespaced escape hatch.)

Custom composition: `EventLayoutType.Custom` + scene implementing `ICustomEventNode`. Do this only after a default-layout event works.

Combat events: `LayoutType` combat, `CanonicalEncounter`, `IsShared` (required for combat events), `EnterCombatWithoutExitingEvent` / `Resume`. Inspect a vanilla fight event before copying.

## Vanilla act patch (if not using BaseLib.Acts)

```csharp
[HarmonyPatch(typeof(Overgrowth), nameof(Overgrowth.AllEvents), MethodType.Getter)]
internal static class OvergrowthEventsPatch
{
    private static void Postfix(ref IEnumerable<EventModel> __result)
    {
        var customEvent = ModelDb.Event<AbandonedObservatoryEvent>();
        if (__result.All(e => e.Id != customEvent.Id))
            __result = __result.Append(customEvent);
    }
}
```

Shared-everywhere: postfix `ModelDb.AllSharedEvents` with duplicate protection. Acts generate rooms when a run **starts** — a new patch does not inject into an already generated save.

## Eligibility

Override `IsAllowed(IRunState)` for relics present, already-visited, act index, multiplayer, etc. Keep it deterministic. Randomness belongs in seeded `Rng` / `CalculateVars`.

Multi-page: `SetEventState` instead of `SetEventFinished`. Death warnings: `ThatDoesDamage` / `ThatDecreasesMaxHp`. Relic options: use native relic presentation helpers from a vanilla relic event.

## Validation

1. `ModelDb.Event<T>()` resolves
2. Target act list contains it after patches/BaseLib
3. New run can place it in an unknown room
4. Options render text + hover
5. Each option mutates once
6. Completes back to the map
7. History records the choice
8. Save before entry and mid-event reloads
