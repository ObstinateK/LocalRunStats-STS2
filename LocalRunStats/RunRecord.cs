using System.Collections.Generic;

namespace LocalRunStats;

public sealed class DeckCardRecord
{
    public string Id { get; set; } = "";
    public int UpgradeLevel { get; set; }
}

public sealed class RunRecord
{
    public string Timestamp { get; set; } = "";
    public string CharacterId { get; set; } = "";
    public int Ascension { get; set; }
    public string GameMode { get; set; } = "";
    public bool IsVictory { get; set; }
    public int FloorReached { get; set; }
    public long RunTimeSeconds { get; set; }
    public int Gold { get; set; }
    public int DamageDealt { get; set; }
    public List<DeckCardRecord> Deck { get; set; } = new();
    public List<string> Relics { get; set; } = new();
}
