#!/usr/bin/env bash
# Seeds a modded save profile's unlock progress (progress.save) from the
# matching vanilla (non-modded) profile, so playing with mods active doesn't
# mean re-grinding card/relic/potion unlocks from scratch.
#
# Slay the Spire 2's native mod loader routes ANY modded session to a
# separate save area (steam/<id>/modded/profileN/...) instead of the normal
# one (steam/<id>/profileN/...) - a deliberate safeguard so mods can never
# touch/corrupt your real save. This script only ever COPIES FROM the
# vanilla save INTO the modded one; it never writes to steam/<id>/profileN
# (your main save) at all.
#
#   ./seed-unlocks.sh
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
set -euo pipefail

echo "Reminder: this only works if Steam Cloud sync is OFF for Slay the Spire 2 (Steam Library -> right-click the game -> Properties -> General). If Cloud sync is on, your seeded unlocks will silently revert the next time you launch the game."

if pgrep -x "Slay the Spire 2" >/dev/null; then
  echo "Slay the Spire 2 is running (PID $(pgrep -x 'Slay the Spire 2')). Close it, then run this script again." >&2
  exit 1
fi

steam_root="${HOME}/Library/Application Support/SlayTheSpire2/steam"
if [[ ! -d "$steam_root" ]]; then
  echo "Could not find $steam_root - is Slay the Spire 2 installed, and has it been run at least once?" >&2
  exit 1
fi

seeded_any=0

for account_dir in "$steam_root"/*/; do
  [[ -d "$account_dir" ]] || continue
  account_dir="${account_dir%/}"
  modded_root="${account_dir}/modded"
  [[ -d "$modded_root" ]] || continue

  for profile_dir in "$account_dir"/profile*/; do
    [[ -d "$profile_dir" ]] || continue
    profile_name="$(basename "$profile_dir")"
    vanilla_progress="${account_dir}/${profile_name}/saves/progress.save"
    modded_saves_dir="${modded_root}/${profile_name}/saves"
    modded_progress="${modded_saves_dir}/progress.save"

    [[ -f "$vanilla_progress" ]] || continue
    [[ -d "$modded_saves_dir" ]] || continue

    if [[ -f "$modded_progress" ]]; then
      backup_path="${modded_progress}.pre-seed-$(date +%Y%m%d-%H%M%S)"
      cp "$modded_progress" "$backup_path"
      echo "Backed up modded $profile_name progress to $backup_path"
    fi

    cp "$vanilla_progress" "$modded_progress"
    echo "Seeded modded $profile_name unlocks from your main save ($account_dir)"
    seeded_any=1
  done
done

if [[ "$seeded_any" -eq 0 ]]; then
  echo "Nothing to seed - no matching vanilla+modded profile pairs found under $steam_root"
fi
