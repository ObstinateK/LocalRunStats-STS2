using System;

namespace LocalRunStats;

// Marks when the current run started, so per-run-scoped views (the graph
// overlay) can filter out older runs' rows from the lifetime JSONL logs.
// Set from CombatDamageHudPatch, the same place CombatStatsListener.ResetForNewRun()
// is called — see that method's doc comment for why "NRun._Ready on a fresh
// NRun instance" is being used as the closest thing to a real "run started" event.
public static class RunContext
{
    public static DateTime CurrentRunStartUtc = DateTime.MinValue;
}
