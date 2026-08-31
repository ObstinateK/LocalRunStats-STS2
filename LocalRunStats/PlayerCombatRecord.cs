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

// One row per AfterGoldGained hook fire — cumulative total at that moment,
// not a delta, so a "gold over time" graph is a direct read (consecutive
// entries per player already show the running total).
public sealed class GoldRecord
{
    public string Timestamp { get; set; } = "";
    public int ActIndex { get; set; }
    public ulong PlayerNetId { get; set; }
    public string CharacterName { get; set; } = "";
    public int CurrentGold { get; set; }
}
