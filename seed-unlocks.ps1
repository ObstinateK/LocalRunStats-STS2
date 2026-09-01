# Seeds a modded save profile's unlock progress (progress.save) from the
# matching vanilla (non-modded) profile, so playing with mods active doesn't
# mean re-grinding card/relic/potion unlocks from scratch.
#
# Slay the Spire 2's native mod loader routes ANY modded session to a
# separate save area (steam\<id>\modded\profileN\...) instead of the normal
# one (steam\<id>\profileN\...) - a deliberate safeguard so mods can never
# touch/corrupt your real save. This script only ever COPIES FROM the
# vanilla save INTO the modded one; it never writes to steam\<id>\profileN
# (your main save) at all.
#
#   powershell -File .\seed-unlocks.ps1
#
# IMPORTANT - turn off Steam Cloud sync for Slay the Spire 2 FIRST, or this
# won't stick: Steam Library -> right-click Slay the Spire 2 -> Properties ->
# General -> turn off "Keep saves in the Steam Cloud" (wording varies).
# Confirmed live: with Cloud sync on, launching the game after running this
# script silently pulled the old cloud-synced progress.save back down before
# the game even read it, reverting the seed with no error or warning shown -
# the file just quietly reverted to its old content on next launch. Once
# Cloud sync was turned off, re-running the script worked immediately.
#
# Close Slay the Spire 2 first - the game may overwrite progress.save with
# its own in-memory state on exit/autosave, undoing this copy.
# Safe to re-run any time you want to re-sync (e.g. after unlocking more on
# your main save) - each run backs up the modded profile's current
# progress.save first, with a timestamped name, before overwriting it.
#
# Handles multiple Steam accounts and multiple profile slots (profile1/2/3)
# on the same machine, and needs no configuration - the save path is a fixed
# OS-level location, not the game's (variable) install path.

$ErrorActionPreference = "Stop"

Write-Host "Reminder: this only works if Steam Cloud sync is OFF for Slay the Spire 2 (Steam Library -> right-click the game -> Properties -> General). If Cloud sync is on, your seeded unlocks will silently revert the next time you launch the game." -ForegroundColor Yellow

$gameLock = Get-Process -Name "SlayTheSpire2" -ErrorAction SilentlyContinue
if ($gameLock) {
    throw "Slay the Spire 2 is running (PID $($gameLock.Id)). Close it, then run this script again."
}

$steamRoot = Join-Path $env:APPDATA "SlayTheSpire2\steam"
if (-not (Test-Path $steamRoot)) {
    throw "Could not find $steamRoot - is Slay the Spire 2 installed, and has it been run at least once?"
}

$seededAny = $false

Get-ChildItem $steamRoot -Directory | ForEach-Object {
    $accountDir = $_.FullName
    $moddedRoot = Join-Path $accountDir "modded"
    if (-not (Test-Path $moddedRoot)) { return }

    Get-ChildItem $accountDir -Directory -Filter "profile*" | ForEach-Object {
        $profileName = $_.Name
        $vanillaProgress = Join-Path $accountDir "$profileName\saves\progress.save"
        $moddedProgress = Join-Path $moddedRoot "$profileName\saves\progress.save"

        if (-not (Test-Path $vanillaProgress)) { return }
        if (-not (Test-Path (Split-Path $moddedProgress))) { return }

        if (Test-Path $moddedProgress) {
            $backupPath = "$moddedProgress.pre-seed-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
            Copy-Item $moddedProgress $backupPath
            Write-Host "Backed up modded $profileName progress to $backupPath"
        }

        Copy-Item $vanillaProgress $moddedProgress -Force
        Write-Host "Seeded modded $profileName unlocks from your main save ($accountDir)"
        $seededAny = $true
    }
}

if (-not $seededAny) {
    Write-Host "Nothing to seed - no matching vanilla+modded profile pairs found under $steamRoot"
}
