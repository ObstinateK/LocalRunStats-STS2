# STS2 RunHistory field map

Source: community parsers (`slay_the_saves` crate, sts2-save-rebuild). Re-dump a local `.run` after each major patch.

## Result

- `win` + `was_abandoned` → won / abandoned / killed
- `killed_by_encounter`, `killed_by_event`

## Players (end of run)

```text
players[].character
players[].deck[].id
players[].deck[].floor_added_to_deck
players[].deck[].enchantment?
players[].relics[].id
players[].relics[].floor_added_to_deck
players[].potions[].id
players[].potions[].slot_index
players[].max_potion_slot_count
players[].id                  # net/player id
```

## Map points (the choice log)

```text
map_point_history[act_or_floor][index]
  map_point_type
  rooms[]
    Encounter: model_id, monster_ids, turns_taken, room_type
    Event:     model_id, turns_taken   (+ extra keys — dump them)
    Other:     turns_taken
  player_stats[]
    player_id, current_hp, max_hp, current_gold
    damage_taken, hp_healed, max_hp_gained, max_hp_lost
    gold_gained, gold_lost, gold_spent, gold_stolen
    card_choices[].card.id, card_choices[].was_picked
    potion_choices[].choice, potion_choices[].was_picked
```

Shop purchases, rest-site actions, and event **option indexes** may appear as additional keys as `schema_version` climbs. Walk unknown keys with the dump helper rather than guessing STS1 field names (`card_choices` at the **run root** is STS1, not STS2).

## Paths reminder

| Profile | Folder |
| --- | --- |
| Unmodded | `...\steam\<id>\profileN\saves\history\` |
| Modded | `...\steam\<id>\modded\profileN\saves\history\` |

Steam Cloud mirror: `<Steam>/userdata/<account>/2868840/remote/...` — same JSON, do not treat as a second dataset if it duplicates AppData.
