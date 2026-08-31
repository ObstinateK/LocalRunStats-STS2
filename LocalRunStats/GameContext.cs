using MegaCrit.Sts2.Core.Entities.Players;

namespace LocalRunStats;

// Cheap way to get at "the local player" outside of a combat hook's own
// parameters. Cached from the first combat damage event each run (Creature
// carries a .Player reference); a run's Player object stays valid for its
// whole duration, so this is safe to read later from non-combat screens
// like the card reward screen.
public static class GameContext
{
    public static Player LocalPlayer { get; set; }
}
