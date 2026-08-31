using System;
using System.IO;
using System.Text.Json;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;

namespace LocalRunStats;

// Same ModelDb.Singleton<T> pattern as CombatStatsListener — see that file for why
// `new` is never used on model types. Registered via ModHelper.SubscribeForRunStateHooks.
//
// Scope note: ShouldAddToDeck fires for every card actually added to the deck
// (card rewards, events, shops, etc.) — it does NOT tell us what else was offered
// and skipped at a reward screen. This gives an ordered acquisition timeline, which
// is a real improvement over the end-of-run deck snapshot in runs.jsonl (order +
// mid-run visibility), but true offered-vs-skipped "pick rate" tracking is future
// work — see MOD_SPEC.md.
public sealed class RunStateListener : SingletonModel
{
    public static RunStateListener Instance => ModelDb.Singleton<RunStateListener>();

    public override bool ShouldReceiveCombatHooks => false;

    public override bool ShouldAddToDeck(CardModel card)
    {
        var result = base.ShouldAddToDeck(card);
        try
        {
            WriteRecord(card);
        }
        catch (Exception ex)
        {
            Log.Error("[LocalRunStats] Failed to record card pick: " + ex);
        }
        return result;
    }

    private static void WriteRecord(CardModel card)
    {
        var record = new CardPickRecord
        {
            Timestamp = DateTime.UtcNow.ToString("o"),
            CardId = card.Id.ToString(),
            Rarity = card.Rarity.ToString(),
        };

        var statsDir = Path.Combine(Godot.OS.GetUserDataDir(), "mods", "local-run-stats");
        Directory.CreateDirectory(statsDir);
        var path = Path.Combine(statsDir, "card_picks.jsonl");
        File.AppendAllText(path, JsonSerializer.Serialize(record) + Environment.NewLine);

        Log.Info($"[LocalRunStats] Recorded card added to deck: {record.CardId} ({record.Rarity})");
    }
}
