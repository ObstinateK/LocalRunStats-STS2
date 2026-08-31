namespace LocalRunStats;

public sealed class SynergyEntry
{
    public bool HasData;
    public double WinRateWithCard;
    public double WinRateWithoutCard;
    public double TotalSimilarityWeight;

    public double Synergy => WinRateWithCard - WinRateWithoutCard;
}
