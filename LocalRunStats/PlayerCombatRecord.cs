namespace LocalRunStats;

// One row per player per finished fight — written by CombatStatsListener at
// AfterCombatEnd, alongside (not replacing) the older aggregate combats.jsonl.
public sealed class PlayerCombatRecord
{
    public string Timestamp { get; set; } = "";
    public int ActIndex { get; set; }
    public string EncounterId { get; set; } = "";
    public ulong PlayerNetId { get; set; }
    public string CharacterName { get; set; } = "";
    public int DamageDealt { get; set; }
    public int DamageTaken { get; set; }
    public int TurnsTaken { get; set; }
    public int CardsPlayed { get; set; }
}

// One row per AfterCardPlayed hook fire — raw play events, not pre-aggregated,
// so the graph overlay can group/count them however it needs (currently: play
// count per card name per player, for the whole run).
public sealed class CardPlayRecord
{
    public string Timestamp { get; set; } = "";
    public ulong PlayerNetId { get; set; }
    public string CharacterName { get; set; } = "";
    public string CardName { get; set; } = "";
}

// One row per (player, card) pair played during a single fight — written at
// AfterCombatEnd, folded from CombatStatsListener's per-fight counting dict.
// Distinct from CardPlayRecord (one row per raw play event, used for the
// whole-run aggregate): this is pre-counted per fight so the graph overlay
// doesn't need to re-derive fight boundaries from timestamps.
public sealed class CardPlayCountRecord
{
    public string Timestamp { get; set; } = "";
    public int ActIndex { get; set; }
    public string EncounterId { get; set; } = "";
    public ulong PlayerNetId { get; set; }
    public string CharacterName { get; set; } = "";
    public string CardName { get; set; } = "";
    public int Count { get; set; }
}

// One row per AfterGoldGained hook fire — cumulative total at that moment,
// not a delta, so a "gold over time" graph is a direct read (consecutive
// entries per player already show the running total).
public sealed class GoldRecord
{
    public string Timestamp { get; set; } = "";
    // Shared "how many fights have ended so far" counter (RunContext.CurrentStageIndex)
    // — the chart's actual x-axis grouping key, since gold events fire at
    // independent wall-clock moments per player and raw Timestamp grouping
    // never aligns them. See RunContext.AdvanceStage.
    public int StageIndex { get; set; }
    public int ActIndex { get; set; }
    public ulong PlayerNetId { get; set; }
    public string CharacterName { get; set; } = "";
    public int CurrentGold { get; set; }
}
