# STS2 UI hosts

Confirm names in the current `sts2.dll` after a patch. These are the usual attach points in EA Godot 4.5.1.

## Run / overlay

| Type | Role |
| --- | --- |
| `NRun` | Run scene root. Attach run-lifetime controllers here. |
| `NOverlayStack` | Overlay host + shared backstop. `Push`/`Remove` `IOverlayScreen`. |
| `NModalContainer` | Single modal + dimmer. |
| `NSubmenuStack` | Submenu **lifecycle**, often not the visual parent. |
| `ActiveScreenContext` | Focus owner for the active overlay/submenu. |
| `NHoverTipSet` | Tooltip layer. Keep custom full-rect nodes **below** this. |

## Combat

| Type | Role |
| --- | --- |
| `NCombatUi` | Combat HUD. Best parent for meters, badges, extra labels. |
| `NCombatRoom` | Room/visual combat root. `PlayContainer` is for cards in play, not random HUD. |
| `NPlayerHand` | Hand layout. Do not parent HUD here. |
| `NEnergyCounter` | Energy orb. Character mods replace via BaseLib; QoL mods can sibling-attach. |
| `NStarCounter` | Star resource. Custom energy counters should expose `%StarAnchor`. |

`NCombatUi.OnCombatSetup` wires pile containers. Attach HUD children in `_Ready` (or `OnCombatSetup` postfix if the widget needs combat state immediately).

## Map / rewards / select

| Type | Role |
| --- | --- |
| Map / act map node (inspect current name) | Path UI, room icons |
| `NRewardsScreen` | Post-combat rewards |
| `NCardRewardSelectionScreen` | Card pick |
| `NCardLibrary` | Compendium cards — submenu, not overlay |
| `NRelicCollection` | Compendium relics |
| `NCharacterSelectScreen` | `InitCharacterButtons`, random roll |

## Settings

BaseLib injects a Mods tab. `SimpleModConfig` properties render as native-styled options. Independent alternative: [ModConfig-STS2](https://github.com/xhyrzldf/ModConfig-STS2) via reflection.

## Layering (safe order)

1. Native room / menu content
2. Custom dimming backstop
3. Custom screen / HUD content
4. Native tooltip + cursor

When geometry is wrong, log parent path, global position, size, anchors, viewport size, canvas layer, sibling index.
