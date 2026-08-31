using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace LocalRunStats;

[ModInitializer("ModLoaded")]
public static class ModEntry
{
    public static void ModLoaded()
    {
        Log.Info("[LocalRunStats] mod loaded.");
        RunStatsRecorder.Initialize();

        // ModInitializer skips the loader's automatic Harmony.PatchAll (see TryLoadMod
        // in MOD_SPEC.md), so patches only apply if we run PatchAll ourselves.
        new Harmony("samvith.local-run-stats").PatchAll(typeof(ModEntry).Assembly);
        Log.Info("[LocalRunStats] Harmony patches applied.");
    }
}
