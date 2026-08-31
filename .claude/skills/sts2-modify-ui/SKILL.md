---
name: sts2-modify-ui
description: Adds and modifies Slay the Spire 2 UI. Use when injecting HUD widgets, attaching Godot Controls to NCombatUi or NRun, building overlays, settings tabs, card/relic libraries, tooltips, animation-speed QoL, or any request to put a new element on an existing STS2 screen.
---

# Modify STS2 UI

The usual request is not "a new menu from scratch". It is **put a Control on a screen that already exists** (combat HUD, map, relic bar, character select, settings).

Initializer runs **before** run UI exists. Never `AddChild` from `[ModInitializer]`. Attach when the host node is `_Ready`.

## Pick a host (do this first)

| User wants | Host | Pattern |
| --- | --- | --- |
| Badge / meter / extra label **on combat** | `NCombatUi` | Attach child on `_Ready` |
| Always-visible run widget (map + combat) | `NRun` | Child or `CanvasLayer` |
| Full-screen panel (F-key, pause-like) | `NOverlayStack` | `IOverlayScreen` |
| Settings toggle / slider | BaseLib `SimpleModConfig` | Property → Mods tab |
| Card/relic browser | `NCardLibrary` / `NRelicCollection` | Native `Create()` + submenu stack |
| Modal confirm | `NModalContainer` | Single modal + backstop |
| Independent floating panel | `CanvasLayer` under `NRun` | Damage-tracker style |

Inspect the live parent chain of a vanilla control next to the insertion point. Wrong parent = clipped, unclickable, or covering tooltips.

## Add a child to an existing screen

This is the default for "add a UI element".

```csharp
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Combat;

[HarmonyPatch(typeof(NCombatUi), nameof(NCombatUi._Ready))]
internal static class CombatHudBadgePatch
{
    private const string NodeName = "MyModCombatBadge";

    private static void Postfix(NCombatUi __instance)
    {
        if (__instance.GetNodeOrNull(NodeName) is not null)
            return;

        var badge = new MyCombatBadge { Name = NodeName };
        __instance.AddChild(badge);
        // Prefer AddChildSafely if the decompiled host uses it for UI children.
        badge.Bind(__instance);
    }
}

public sealed partial class MyCombatBadge : Control
{
    private Label _label = null!;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore; // let clicks pass unless this is a button
        _label = new Label { Text = "0", Position = new Vector2(24f, 24f) };
        AddChild(_label);
    }

    public void Bind(NCombatUi combatUi)
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);
        Position = Vector2.Zero;
        // Size = combatUi.Size only if this is a full-rect overlay host
    }

    public void SetText(string text) => _label.Text = text;
}
```

Rules:

- Duplicate-guard by **node name on this instance**, not a static bool (statics survive scene swaps).
- `MouseFilter.Ignore` for decorative HUD; `Stop` only for clickable chrome.
- Place tooltip-safe: native room → custom dimmer → custom content → **native tooltip/cursor still on top**.
- After attach, grab it with `GetNodeOrNull<MyCombatBadge>(NodeName)` from combat hooks to update text.

If the project uses **RitsuLib**, prefer `ModNodeAttachmentRegistry.For(modId).RegisterReadyChild<NCombatUi, MyCombatBadge>(...)` instead of a one-off Harmony patch. Same idea: parent ready → named child → bind.

## Overlay (new screen, not a HUD chip)

Use when the element should dim the run and take focus. See handbook ch.14:

1. Patch `NRun._Ready`, add an input controller child
2. Implement `Control, IOverlayScreen`
3. `NOverlayStack.Instance.Push` / `Remove`
4. `QueueFree` in `AfterOverlayClosed`
5. `UseSharedBackstop = true` for room input blocking
6. `NetScreenType.None` unless this is a real synced multiplayer screen

Do not `AddChild` an overlay onto `NSubmenuStack` and expect geometry to work.

## Settings (add controls to Mods tab)

BaseLib `SimpleModConfig`: public static properties become toggles/sliders automatically. For custom chrome, override `SetupConfigUI` and `optionContainer.AddChild(...)`. Collapsible sections: `section.ContentContainer.AddChild`, never `section.AddChild`.

At least one visible property is required for older BaseLib versions or the page stays hidden.

Set `affects_gameplay: false` on the manifest **only** if the UI cannot change combat outcomes (pure HUD/settings). A damage meter that is display-only can be false; a "skip animations / auto-win" control cannot.

## Native widgets vs plain Godot

`Button` / `Label` prove lifecycle. Native look requires instantiating the vanilla `.tscn` (or BaseLib node factories) from the game PCK, not cloning private fields. Reuse `NCardLibrary.Create()` / `NRelicCollection.Create()` only after inspecting the vanilla caller's submenu stack **and** visual parent.

## QoL catalog (common "UI improvement" asks)

| Improvement | Approach |
| --- | --- |
| Combat damage / intent meter | Child of `NCombatUi` or `CanvasLayer`; subscribe combat hooks |
| Extra relic/potion readout | Child near relic bar; read `Player` collections |
| Map annotations | Child of map host; do not fight camera zoom blindly |
| Faster animations | Settings slider + Harmony on animation timescale **if no hook**; keep it optional |
| Bigger tooltips | Theme / hover layer — do not bury `NHoverTipSet` |
| Card library filters | Wrap or postfix library populate; prefer BaseLib if it already filters |
| Character-select extra info | Child of `NCharacterSelectScreen` after `InitCharacterButtons` |
| Always-on debug | `IOverlayScreen` hotkey, not a combat-only child |

## Failure modes

| Symptom | Likely cause |
| --- | --- |
| Element never appears | Attached in initializer, or wrong type `_Ready`, or `NCombatUi` not created yet (need a combat) |
| Appears twice | Missing name guard, or patch hits base + derived |
| Clicks fall through | `MouseFilter.Ignore` on a button |
| Clicks steal the game | Full-rect `Stop` covering the screen |
| Tooltips invisible | Custom Control above hover layer |
| Shifted / clipped | Content-sized parent; log global rect vs viewport |
| Invisible blocker after close | Overlay not `Remove`d from `NOverlayStack` |
| Looks unstyled | Plain Godot theme; instantiate native button scene |

## Validation

1. Host `_Ready` runs once per scene instance
2. Named child exists; a second combat/run does not duplicate
3. HUD updates from a combat hook without per-frame spam
4. Map, rewards, pause, hover still layer correctly
5. Keyboard/controller: focus neighbors if the element is interactive
6. Settings persist after relaunch

Read [hosts.md](hosts.md) for node names and [adding-elements.md](adding-elements.md) for attach options.
