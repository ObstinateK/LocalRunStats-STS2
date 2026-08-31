using System;

namespace LocalRunStats;

// Dev-time-only live tuning for CombatDamageHud's position/size, same pattern
// used earlier for the card/relic reward overlays: drag sliders in-game,
// values print to the log, tell me the final numbers and I bake them in as
// constants and delete this file + HudTuningPanel.
public static class HudTuning
{
    // MarginRight/MarginTop locked in live by the user (2026-08-29). Width/Height
    // bumped up from the tuned 100x159 to fit the new second table (Damage
    // Taken) + graph button that were added after that tuning pass — re-tune
    // with the still-present slider panel if the new layout doesn't fit well.
    public static float MarginRight = 234f;
    public static float MarginTop = 154f;
    public static float Width = 360f;
    public static float Height = 300f;

    // StatsGraphOverlay position/size — defaults to centered-on-screen at
    // 820x680 (see StatsGraphOverlay._Ready) until tuned.
    public static float GraphX = -1f; // -1 sentinel = "not yet tuned, center it"
    public static float GraphY = -1f;
    public static float GraphWidth = 820f;
    public static float GraphHeight = 680f;

    public static event Action Changed;

    public static void RaiseChanged() => Changed?.Invoke();
}
