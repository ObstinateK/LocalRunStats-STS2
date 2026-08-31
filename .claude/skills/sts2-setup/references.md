# STS2 setup reference

## Preflight (run conceptually, then confirm files exist)

1. Read `<STS2>/release_info.json` for game version + commit.
2. Read `<STS2>/data_sts2_*/sts2.runtimeconfig.json` for TFM / runtime.
3. Confirm `sts2.dll`, `0Harmony.dll`, `GodotSharp.dll` share that data directory.
4. Confirm `SlayTheSpire2.pck` Godot line is **4.5.1** unless `release_info` says otherwise.
5. Confirm BaseLib is in workshop `Steam/steamapps/workshop/content/2868840/3737335127/BaseLib` or `mods/BaseLib/`.

## Paths

| Platform | Game root | Local mods |
| --- | --- | --- |
| Windows | `Steam/steamapps/common/Slay the Spire 2` | `<game>/mods` |
| Linux | `~/.steam/steam/steamapps/common/Slay the Spire 2` | `<game>/mods` |
| macOS | `…/Slay the Spire 2/SlayTheSpire2.app` | `…/Contents/MacOS/mods` |

Workshop content: `Steam/steamapps/workshop/content/2868840/<workshopId>/`.

## Loc lookup

`ModManager.GetModdedLocTables` loads:

```text
res://<manifest id>/localization/<language>/<file>
```

English table names used by vanilla: `cards.json`, `relics.json`, `potions.json`, `events.json`, `characters.json`, `powers.json`, `monsters.json` (confirm against current PCK). Language code `eng`.

A file under `res://localization/...` without the manifest id **will not merge**.

## Recommended repo shape

```text
MyMod/
  MyMod.csproj
  Directory.Build.props
  MyMod.json
  ModEntry.cs
  Character/   Cards/   Relics/   Events/   Encounters/   Monsters/   Gui/   Patches/
  MyMod/
    localization/eng/
    images/
    scenes/
```

## Docs

- Template setup: https://github.com/Alchyr/ModTemplate-StS2/wiki/Setup
- Modding basics: https://github.com/Alchyr/ModTemplate-StS2/wiki/Modding-Basics
- Vanilla handbook: https://fresh-milkshake.github.io/Modding-Tutorial/
