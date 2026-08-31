namespace LocalRunStats;

public sealed class CardStatEntry
{
    public int TimesOffered { get; set; }
    public int TimesPicked { get; set; }
    public int RunsWithCard { get; set; }
    public int WinsWithCard { get; set; }
    public int RunsWithoutCard { get; set; }
    public int WinsWithoutCard { get; set; }

    public double PickRate => TimesOffered == 0 ? 0 : (double)TimesPicked / TimesOffered;
    public double WinRateWithCard => RunsWithCard == 0 ? 0 : (double)WinsWithCard / RunsWithCard;
    public double WinRateWithoutCard => RunsWithoutCard == 0 ? 0 : (double)WinsWithoutCard / RunsWithoutCard;
    public double Impact => WinRateWithCard - WinRateWithoutCard;
}
