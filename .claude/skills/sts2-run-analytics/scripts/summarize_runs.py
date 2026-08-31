#!/usr/bin/env python3
"""Summarize local Slay the Spire 2 history/*.run files. Stdlib only."""

from __future__ import annotations

import argparse
import json
import os
from collections import Counter, defaultdict
from pathlib import Path
from typing import Any, Iterable


def appdata_roots() -> list[Path]:
    roots: list[Path] = []
    appdata = os.environ.get("APPDATA")
    if appdata:
        roots.append(Path(appdata) / "SlayTheSpire2")
    home = Path.home()
    roots.append(home / "Library" / "Application Support" / "SlayTheSpire2")
    roots.append(home / ".local" / "share" / "SlayTheSpire2")
    xdg = os.environ.get("XDG_DATA_HOME")
    if xdg:
        roots.append(Path(xdg) / "SlayTheSpire2")
    return roots


def find_history_dirs(explicit: Path | None) -> list[Path]:
    if explicit:
        return [explicit] if explicit.is_dir() else []
    found: list[Path] = []
    for root in appdata_roots():
        if not root.is_dir():
            continue
        found.extend(p for p in root.rglob("history") if p.is_dir())
    return found


def iter_run_files(history_dirs: Iterable[Path]) -> Iterable[tuple[Path, Path]]:
    for history_dir in history_dirs:
        for path in sorted(history_dir.glob("*.run")):
            yield history_dir, path


def load_run(path: Path) -> dict[str, Any] | None:
    try:
        with path.open(encoding="utf-8") as handle:
            data = json.load(handle)
    except (OSError, json.JSONDecodeError):
        return None
    return data if isinstance(data, dict) else None


def profile_label(history_dir: Path) -> str:
    parts = [p.lower() for p in history_dir.parts]
    kind = "modded" if "modded" in parts else "vanilla"
    profile = "profile?"
    for part in history_dir.parts:
        if part.lower().startswith("profile"):
            profile = part
            break
    return f"{kind}/{profile}"


def character_name(run: dict[str, Any]) -> str:
    players = run.get("players") or []
    if players and isinstance(players[0], dict):
        char = players[0].get("character")
        if isinstance(char, dict):
            return str(char.get("id") or char.get("entry") or char)
        if char is not None:
            return str(char)
    return "?"


def result_of(run: dict[str, Any]) -> str:
    win = bool(run.get("win"))
    abandoned = bool(run.get("was_abandoned"))
    if win and not abandoned:
        return "win"
    if abandoned:
        return "abandon"
    return "kill"


def walk_map_points(run: dict[str, Any]) -> Iterable[dict[str, Any]]:
    history = run.get("map_point_history") or []
    if not isinstance(history, list):
        return
    for floor in history:
        if not isinstance(floor, list):
            continue
        for point in floor:
            if isinstance(point, dict):
                yield point


def summarize(history_dirs: list[Path]) -> None:
    if not history_dirs:
        print("No history folders found. Tried:")
        for root in appdata_roots():
            print(f"  {root}")
        print("Pass --history <dir> if saves live elsewhere.")
        return

    print("History folders:")
    for folder in history_dirs:
        print(f"  {folder}")

    results: Counter[str] = Counter()
    by_character: dict[str, Counter[str]] = defaultdict(Counter)
    card_offered: Counter[str] = Counter()
    card_picked: Counter[str] = Counter()
    events: Counter[str] = Counter()
    encounters: Counter[str] = Counter()
    files = 0
    failed = 0

    for history_dir, path in iter_run_files(history_dirs):
        run = load_run(path)
        if run is None:
            failed += 1
            continue
        files += 1
        label = profile_label(history_dir)
        char = character_name(run)
        outcome = result_of(run)
        results[outcome] += 1
        by_character[f"{label} | {char} | A{run.get('ascension', '?')}"][outcome] += 1

        for point in walk_map_points(run):
            for room in point.get("rooms") or []:
                if not isinstance(room, dict):
                    continue
                model_id = room.get("model_id")
                if model_id is None:
                    continue
                if "monster_ids" in room or room.get("room_type"):
                    encounters[str(model_id)] += 1
                else:
                    events[str(model_id)] += 1
            for stats in point.get("player_stats") or []:
                if not isinstance(stats, dict):
                    continue
                for choice in stats.get("card_choices") or []:
                    if not isinstance(choice, dict):
                        continue
                    card = choice.get("card") or {}
                    card_id = card.get("id") if isinstance(card, dict) else card
                    if card_id is None:
                        continue
                    key = str(card_id)
                    card_offered[key] += 1
                    if choice.get("was_picked"):
                        card_picked[key] += 1

    print(f"\nRuns parsed: {files}  failed: {failed}")
    print(f"Outcomes: {dict(results)}")
    if files:
        wins = results["win"]
        print(f"Win rate: {wins / files:.1%}")

    print("\nBy profile / character / ascension:")
    for key, counter in sorted(by_character.items()):
        total = sum(counter.values())
        win = counter["win"]
        print(f"  {key}: {dict(counter)}  win {win / total:.0%} (n={total})")

    print("\nTop card pick rates (min 5 offers):")
    ranked = []
    for card_id, offered in card_offered.items():
        if offered < 5:
            continue
        picked = card_picked[card_id]
        ranked.append((picked / offered, offered, picked, card_id))
    for rate, offered, picked, card_id in sorted(ranked, reverse=True)[:25]:
        print(f"  {rate:5.1%}  {picked}/{offered}  {card_id}")

    print("\nTop events:")
    for event_id, count in events.most_common(15):
        print(f"  {count:4d}  {event_id}")

    print("\nTop encounters:")
    for encounter_id, count in encounters.most_common(15):
        print(f"  {count:4d}  {encounter_id}")


def dump_sample(history_dirs: list[Path]) -> None:
    for _, path in iter_run_files(history_dirs):
        run = load_run(path)
        if run is None:
            continue
        print(f"Sample: {path}")
        print("Top-level keys:", sorted(run.keys()))
        points = list(walk_map_points(run))
        if not points:
            print("map_point_history empty or missing")
            return
        point = points[0]
        print("MapPoint keys:", sorted(point.keys()))
        rooms = point.get("rooms") or []
        if rooms and isinstance(rooms[0], dict):
            print("First room keys:", sorted(rooms[0].keys()))
            print("First room:", json.dumps(rooms[0], indent=2)[:2000])
        stats = point.get("player_stats") or []
        if stats and isinstance(stats[0], dict):
            print("First player_stats keys:", sorted(stats[0].keys()))
        return
    print("No readable .run files to dump.")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--history", type=Path, help="Single history directory")
    parser.add_argument(
        "--dump-sample",
        action="store_true",
        help="Print keys from the first .run instead of aggregating",
    )
    args = parser.parse_args()
    history_dirs = find_history_dirs(args.history)
    if args.dump_sample:
        dump_sample(history_dirs)
        return
    summarize(history_dirs)


if __name__ == "__main__":
    main()
