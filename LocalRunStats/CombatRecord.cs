namespace LocalRunStats;

public sealed class CombatRecord
{
    public string Timestamp { get; set; } = "";
    public string EncounterId { get; set; } = "";
    public int DamageDealt { get; set; }
    public int DamageTaken { get; set; }
    public int DamageBlocked { get; set; }
}
