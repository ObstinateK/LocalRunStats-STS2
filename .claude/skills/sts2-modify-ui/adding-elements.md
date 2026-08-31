# Ways to add a UI element

Prefer the smallest hook that still gets a named child into the tree.

## 1. Harmony `_Ready` + `AddChild` (always available)

Works with only game + Harmony references. Use for one or two widgets.

```csharp
[HarmonyPatch(typeof(NCombatUi), nameof(NCombatUi._Ready))]
internal static class Patch
{
    private static void Postfix(NCombatUi __instance)
    {
        if (__instance.GetNodeOrNull("MyWidget") is not null) return;
        __instance.AddChild(new MyWidget { Name = "MyWidget" });
    }
}
```

If decompiled UI uses `AddChildSafely`, match that.

## 2. RitsuLib node attachment (if the project already depends on it)

```csharp
ModNodeAttachmentRegistry.For(ModId)
    .RegisterReadyChild<NCombatUi, MyWidget>(
        "combat_badge",
        static _ => new MyWidget(),
        static (parent, node) => node.Bind(parent),
        new NodeAttachmentOptions
        {
            Name = "MyWidget",
            DuplicatePolicy = NodeAttachmentDuplicatePolicy.ReuseExistingByName,
            SetupTiming = NodeAttachmentSetupTiming.AfterAdd,
        });
```

From `.tscn`: `RegisterReadyChildFromScene<NCombatUi, Control>(...)`.

Retrieve later: `TryGetAttached<NCombatUi, MyWidget>(combatUi, "combat_badge", out var widget)`.

Do not add RitsuLib only for one label; Harmony is enough.

## 3. `CanvasLayer` for floating HUD

Parent a `CanvasLayer` to `NRun` so the widget survives combat↔map transitions and is not clipped by combat camera. Set `Layer` below tooltips. Example pattern: [STS2-DamageTracker](https://github.com/BAIGUANGMEI/STS2-DamageTracker).

## 4. Packed scene

After a code-created Control works:

1. Build `res://<ModId>/scenes/ui/my_widget.tscn`
2. `ResourceLoader.Load<PackedScene>(path).Instantiate<MyWidget>()`
3. Publish the PCK

Root must be the script type. Verify import and path listing inside the PCK.

## 5. Insert among siblings

If the widget must sit between two vanilla children, Harmony `_Ready` then:

```csharp
parent.MoveChild(widget, index);
```

Or RitsuLib `ChildIndex` / `InsertBeforeName` / `InsertAfterName` (only one of those).

Inspect sibling names in-game rather than hardcoding an index that a patch will shift.

## Updating data

Do not poll in `_Process` unless the value is inherently per-frame. Prefer:

- combat/run semantic hooks
- `ContentsChanged` on piles
- relic/card model hooks
- a 0.2s timer for expensive aggregates

Keep the widget dumb: a small service holds numbers, the Control only reads them on events.
