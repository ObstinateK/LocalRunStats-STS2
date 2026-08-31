using System.Collections.Generic;
using System.Linq;

namespace LocalRunStats;

// Per-player (multiplayer-aware, keyed by Player.NetId) damage tracking for
// the live combat HUD: this fight (resets each combat), per-act running
// totals (folded in at combat end), and a grand total across the run. Dealt
// and Taken are tracked in parallel with the same shape.
public sealed class PlayerDamageTracker
{
    public ulong NetId;
    public string CharacterName = "?";

    public int CurrentFightDealt;
    public readonly Dictionary<int, int> DealtByActIndex = new();
    public int TotalDealt => DealtByActIndex.Values.Sum();

    public int CurrentFightTaken;
    public readonly Dictionary<int, int> TakenByActIndex = new();
    public int TotalTaken => TakenByActIndex.Values.Sum();

    // Turns/cards-played aren't shown on the live HUD table, only in the
    // graph overlay's per-fight log — so unlike Dealt/Taken, no ByAct/Total
    // tracking needed here, just a per-fight counter reset after each fight.
    public int CurrentFightTurns;
    public int CurrentFightCardsPlayed;
}
