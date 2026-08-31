namespace LocalRunStats;

public sealed class RelicStatEntry
{
    public int TimesOffered { get; set; }
    public int TimesPicked { get; set; }
    public int RunsWithRelic { get; set; }
    public int WinsWithRelic { get; set; }
    public int RunsWithoutRelic { get; set; }
    public int WinsWithoutRelic { get; set; }

    public double PickRate => TimesOffered == 0 ? 0 : (double)TimesPicked / TimesOffered;
    public double WinRateWithRelic => RunsWithRelic == 0 ? 0 : (double)WinsWithRelic / RunsWithRelic;
    public double WinRateWithoutRelic => RunsWithoutRelic == 0 ? 0 : (double)WinsWithoutRelic / RunsWithoutRelic;
    public double Impact => WinRateWithRelic - WinRateWithoutRelic;
}
