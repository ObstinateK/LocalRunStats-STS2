using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Saves;

namespace LocalRunStats;

public static class RunStatsRecorder
{
    private static string StatsDir => Path.Combine(Godot.OS.GetUserDataDir(), "mods", "local-run-stats");
    private static string RunsPath => Path.Combine(StatsDir, "runs.jsonl");

    public static void Initialize()
    {
        Directory.CreateDirectory(StatsDir);
        ModManager.OnMetricsUpload += OnRunFinished;
        ModHelper.SubscribeForCombatStateHooks("local-run-stats", _ => new[] { CombatStatsListener.Instance });
        ModHelper.SubscribeForRunStateHooks("local-run-stats", _ => new MegaCrit.Sts2.Core.Models.AbstractModel[] { RunStateListener.Instance, GoldTracker.Instance });
        Log.Info("[LocalRunStats] Subscribed to ModManager.OnMetricsUpload, combat hooks, and run-state hooks. Stats dir: " + StatsDir);
        HistoryStatsEngine.Refresh();
        _ = HistoryStatsEngine.RefreshCommunityStatsAsync(); // fire-and-forget — must not block init on network I/O
        _ = HistoryStatsEngine.RefreshCommunityRunDetailsAsync(); // fire-and-forget — builds the community Synergy dataset
    }

    private static void OnRunFinished(SerializableRun run, bool isVictory, ulong localPlayerId)
    {
        try
        {
            var player = run.Players?.FirstOrDefault(p => p.NetId == localPlayerId) ?? run.Players?.FirstOrDefault();
            if (player == null)
            {
                Log.Warn("[LocalRunStats] No player found in SerializableRun, skipping record.");
                return;
            }

            var record = new RunRecord
            {
                Timestamp = DateTime.UtcNow.ToString("o"),
                CharacterId = player.CharacterId.ToString(),
                Ascension = run.Ascension,
                GameMode = run.GameMode.ToString(),
                IsVictory = isVictory,
                FloorReached = run.FloorReached,
                RunTimeSeconds = run.RunTime,
                Gold = player.Gold,
                DamageDealt = player.ExtraFields?.DamageDealt ?? 0,
                Deck = player.Deck?.Select(c => new DeckCardRecord { Id = c.Id.ToString(), UpgradeLevel = c.CurrentUpgradeLevel }).ToList() ?? new List<DeckCardRecord>(),
                Relics = player.Relics?.Select(r => r.Id.ToString()).ToList() ?? new List<string>(),
            };

            AppendRun(record);
            HistoryStatsEngine.Refresh();
            Log.Info($"[LocalRunStats] Recorded run: {record.CharacterId} asc{record.Ascension} {(isVictory ? "WIN" : "LOSS")} floor={record.FloorReached} dmg={record.DamageDealt}");
        }
        catch (Exception ex)
        {
            Log.Error("[LocalRunStats] Failed to record run: " + ex);
        }
    }

    private static void AppendRun(RunRecord record)
    {
        var line = JsonSerializer.Serialize(record);
        File.AppendAllText(RunsPath, line + Environment.NewLine);
    }

}
