---
name: sts2-setup
description: Sets up and repairs a Slay the Spire 2 (STS2) C# / Godot mod project. Use when creating a new STS2 mod, fixing BaseLib or manifest loading, publishing a .pck, pointing at Steam GameDir, or when the mod does not appear in Settings -> Mod Settings.
---

# STS2 Project Setup

Get a loadable STS2 Early Access mod on disk. Do this before adding content.

## Preferred bootstrap

Install templates, then create **one** project:

```bash
dotnet new install Alchyr.Sts2.Templates
dotnet new alchyrsts2contentmod --ModAuthor YourName -o MyMod
```

| Template | When |
| --- | --- |
| `alchyrsts2charmod` | New playable character |
| `alchyrsts2contentmod` | Cards, relics, events, enemies for existing characters |
| `alchyrsts2mod` | Empty / UI-only / Harmony-only |

Put the `.sln` in the **same directory** as the project (Godot requirement). No spaces in the project name.

Also required:

- .NET 9 SDK (match `sts2.runtimeconfig.json` TFM; currently `net9.0`)
- MegaDot, or Godot **4.5.1 .NET** matching the game PCK
- Steam STS2 install
- **BaseLib** via Steam workshop `3737335127` (or a GitHub release copied into `mods/BaseLib/`)

Edit `Directory.Build.props`:

- `<GodotPath>` → MegaDot / Godot exe (no quotes)
- `<Sts2Path>` → game root if not default Steam path

## Vanilla-only skeleton (no BaseLib)

Only when the user refuses BaseLib. Still reference **game-shipped** DLLs, never NuGet Harmony/GodotSharp:

```xml
<TargetFramework>net9.0</TargetFramework>
<Reference Include="sts2">
  <HintPath>$(Sts2DataDir)\sts2.dll</HintPath>
  <Private>false</Private>
</Reference>
<Reference Include="0Harmony">
  <HintPath>$(Sts2DataDir)\0Harmony.dll</HintPath>
  <Private>false</Private>
</Reference>
<Reference Include="GodotSharp">
  <HintPath>$(Sts2DataDir)\GodotSharp.dll</HintPath>
  <Private>false</Private>
</Reference>
```

Windows data dir is typically:

```text
<Steam>/steamapps/common/Slay the Spire 2/data_sts2_windows_x86_64/
```

## Manifest

One JSON beside the payloads. Filename should match `id`:

```json
{
  "id": "MyMod",
  "name": "My Mod",
  "author": "YourName",
  "description": "…",
  "version": "0.1.0",
  "has_pck": true,
  "has_dll": true,
  "min_game_version": "0.105.0",
  "dependencies": [{ "id": "BaseLib", "min_version": "3.1.2" }],
  "affects_gameplay": true
}
```

Loader contract (current EA):

- Recursively scans `<STS2>/mods/**/*.json`
- Loads `<id>.dll` / `<id>.pck` from the **manifest directory**
- `dependencies` load first
- `affects_gameplay: false` skips multiplayer lobby matching — set true if the mod changes combat, cards, relics, events, or enemies

Install layout:

```text
<STS2>/mods/MyMod/MyMod.json
<STS2>/mods/MyMod/MyMod.dll
<STS2>/mods/MyMod/MyMod.pck
```

## Initializer

```csharp
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;

[ModInitializer(nameof(Initialize))]
public static class ModEntry
{
    public static void Initialize()
    {
        new Harmony("author.mymod").PatchAll(typeof(ModEntry).Assembly);
        // BaseLib Custom*Model usually auto-registers in its constructor.
        // Vanilla-only mods must ModHelper.AddModelToPool here, before pools freeze.
    }
}
```

## Build vs Publish

| Change | Command |
| --- | --- |
| `.cs` only | Build (copies DLL into `mods/`) |
| loc, png, tscn, import | **Publish** local folder (rebuilds `.pck` via Godot) |

Templates wire Publish to the game `mods/` folder. If text/art is stale in-game, they Built instead of Published.

## Verify

1. Launch STS2 (not `-nomods`).
2. Settings → Mod Settings → mod listed, no load error.
3. Log: `MOD FINISHED LOADING` / BaseLib init / no missing payload.

## Common failures

- SDK `Godot.NET.Sdk/4.5.1` missing → `dotnet nuget add source https://api.nuget.org/v3/index.json`
- Wrong Godot path → Publish fails, DLL still loads, loc/art missing
- Manifest `id` ≠ DLL/PCK basename
- Extra `.json` under `mods/` parsed as a fake manifest
- NuGet Harmony instead of `0Harmony.dll` from the game data dir
- Game updated, BaseLib not yet → wait for BaseLib workshop update or pin beta/main branch

Read [references.md](references.md) for path cheatsheet and preflight.
