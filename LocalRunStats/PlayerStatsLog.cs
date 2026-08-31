using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using MegaCrit.Sts2.Core.Logging;

namespace LocalRunStats;

// Shared JSONL read/write for the background per-fight/per-gold-event logs,
// plus the aggregation the graph overlay reads from. Everything here is
// this player's own local data — no network involved (unlike HistoryStatsEngine's
// community calls).
public static class PlayerStatsLog
{
    private static string StatsDir => Path.Combine(Godot.OS.GetUserDataDir(), "mods", "local-run-stats");

    public static void AppendJsonLine<T>(string fileName, T record)
    {
        Directory.CreateDirectory(StatsDir);
        var path = Path.Combine(StatsDir, fileName);
        File.AppendAllText(path, JsonSerializer.Serialize(record) + Environment.NewLine);
    }

    private static List<T> ReadAllLines<T>(string fileName)
    {
        var result = new List<T>();
        var path = Path.Combine(StatsDir, fileName);
        if (!File.Exists(path)) return result;

        foreach (var line in File.ReadAllLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var record = JsonSerializer.Deserialize<T>(line);
                if (record != null) result.Add(record);
            }
            catch (Exception ex)
            {
                Log.Warn($"[LocalRunStats] Skipping unreadable line in {fileName}: {ex.Message}");
            }
        }
        return result;
    }

    // player_combat_stats.jsonl / gold_log.jsonl are lifetime logs (every run
    // ever played, append-only) — the graph overlay is meant to show only the
    // *current* run, so every read used for charting filters down to rows
    // timestamped after RunContext.CurrentRunStartUtc.
    private static List<T> FilterToCurrentRun<T>(List<T> records, Func<T, string> timestampSelector)
    {
        return records.Where(r =>
        {
            if (!DateTime.TryParse(timestampSelector(r), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var ts)) return false;
            return ts >= RunContext.CurrentRunStartUtc;
        }).ToList();
    }

    // xLabels + one value list per player, same length/order as xLabels.
    public sealed class ChartData
    {
        public List<string> XLabels { get; set; } = new();
        public Dictionary<string, List<float>> SeriesByPlayer { get; set; } = new();
    }

    // Distinct act indices (0-based) present in this run's data so far, for
    // populating the act-filter buttons. Union of damage + gold logs.
    public static List<int> GetAvailableActs()
    {
        var damage = FilterToCurrentRun(ReadAllLines<PlayerCombatRecord>("player_combat_stats.jsonl"), r => r.Timestamp);
        var gold = FilterToCurrentRun(ReadAllLines<GoldRecord>("gold_log.jsonl"), r => r.Timestamp);
        return damage.Select(r => r.ActIndex).Concat(gold.Select(r => r.ActIndex)).Distinct().OrderBy(a => a).ToList();
    }

    // actFilter null = all acts. "Per Stage" here means one bar/point per
    // individual fight ("stage" of the run), not per act — act is a
    // separate, orthogonal filter on top of that, not the bucketing itself.
    public static ChartData BuildDamageChartData(bool perStage, bool dealt, int? actFilter)
    {
        var records = FilterToCurrentRun(ReadAllLines<PlayerCombatRecord>("player_combat_stats.jsonl"), r => r.Timestamp);
        if (actFilter.HasValue) records = records.Where(r => r.ActIndex == actFilter.Value).ToList();
        return perStage
            ? BuildPerStageDamage(records, dealt)
            : BuildCumulativeDamage(records, dealt);
    }

    // One bar-group per fight (grouped by the shared Timestamp all players'
    // records for that fight were written with — see
    // CombatStatsListener.WritePerPlayerRecords), in chronological order.
    // Raw per-fight value, not a running total.
    private static ChartData BuildPerStageDamage(List<PlayerCombatRecord> records, bool dealt)
    {
        var data = new ChartData();
        if (records.Count == 0) return data;

        var playerNames = records.Select(r => r.CharacterName).Distinct().ToList();
        foreach (var name in playerNames) data.SeriesByPlayer[name] = new List<float>();

        var fights = records.GroupBy(r => r.Timestamp).OrderBy(g => g.Key).ToList();
        var stageIndex = 0;
        foreach (var fight in fights)
        {
            stageIndex++;
            data.XLabels.Add(ShortenEncounterId(fight.First().EncounterId, stageIndex));
            foreach (var name in playerNames)
            {
                var record = fight.FirstOrDefault(r => r.CharacterName == name);
                data.SeriesByPlayer[name].Add(record != null ? (dealt ? record.DamageDealt : record.DamageTaken) : 0f);
            }
        }
        return data;
    }

    private static ChartData BuildCumulativeDamage(List<PlayerCombatRecord> records, bool dealt)
    {
        var data = new ChartData();
        if (records.Count == 0) return data;

        var ordered = records.OrderBy(r => r.Timestamp).ToList();
        var runningTotals = new Dictionary<string, float>();
        var fightIndex = 0;

        foreach (var r in ordered)
        {
            fightIndex++;
            runningTotals.TryGetValue(r.CharacterName, out var total);
            total += dealt ? r.DamageDealt : r.DamageTaken;
            runningTotals[r.CharacterName] = total;

            data.XLabels.Add(fightIndex.ToString());
            foreach (var name in runningTotals.Keys.ToList())
            {
                if (!data.SeriesByPlayer.TryGetValue(name, out var series))
                {
                    series = new List<float>(new float[data.XLabels.Count - 1]); // pad so all series stay aligned to XLabels
                    data.SeriesByPlayer[name] = series;
                }
                series.Add(runningTotals[name]);
            }
            // Any series that didn't get a point this iteration (a player who
            // exists but wasn't the one updated) still needs padding to stay
            // aligned with XLabels — repeat their last known value.
            foreach (var kvp in data.SeriesByPlayer)
            {
                while (kvp.Value.Count < data.XLabels.Count)
                {
                    kvp.Value.Add(kvp.Value.Count > 0 ? kvp.Value[^1] : 0f);
                }
            }
        }
        return data;
    }

    public static ChartData BuildGoldChartData(bool perStage, int? actFilter)
    {
        var records = FilterToCurrentRun(ReadAllLines<GoldRecord>("gold_log.jsonl"), r => r.Timestamp);
        if (actFilter.HasValue) records = records.Where(r => r.ActIndex == actFilter.Value).ToList();
        var data = new ChartData();
        if (records.Count == 0) return data;

        var ordered = records.OrderBy(r => r.Timestamp).ToList();

        if (perStage)
        {
            // One bar per gold-gain event = the amount gained at that specific
            // event (delta from that player's previous known total), not the
            // running total — mirrors damage's per-fight raw value.
            var lastKnown = new Dictionary<string, float>();
            var index = 0;
            foreach (var r in ordered)
            {
                index++;
                data.XLabels.Add(index.ToString());
                lastKnown.TryGetValue(r.CharacterName, out var previous);
                var delta = System.Math.Max(0f, r.CurrentGold - previous);
                lastKnown[r.CharacterName] = r.CurrentGold;

                foreach (var name in lastKnown.Keys.ToList())
                {
                    if (!data.SeriesByPlayer.TryGetValue(name, out var series))
                    {
                        series = new List<float>(new float[data.XLabels.Count - 1]);
                        data.SeriesByPlayer[name] = series;
                    }
                    series.Add(name == r.CharacterName ? delta : 0f);
                }
            }
        }
        else
        {
            var byPlayer = ordered.GroupBy(r => r.CharacterName).ToDictionary(g => g.Key, g => g.ToList());
            var maxCount = byPlayer.Values.Max(v => v.Count);
            for (var i = 0; i < maxCount; i++) data.XLabels.Add(i.ToString());

            foreach (var (name, list) in byPlayer)
            {
                var series = new List<float>();
                for (var i = 0; i < maxCount; i++)
                {
                    series.Add(i < list.Count ? list[i].CurrentGold : (series.Count > 0 ? series[^1] : 0f));
                }
                data.SeriesByPlayer[name] = series;
            }
        }
        return data;
    }

    private static string ShortenEncounterId(string encounterId, int fallbackIndex)
    {
        if (string.IsNullOrEmpty(encounterId)) return fallbackIndex.ToString();
        // Encounter ids look like "ENCOUNTER.SHRINKER_BEETLE_WEAK" — strip the
        // category prefix and truncate so labels don't overrun the chart.
        var dot = encounterId.LastIndexOf('.');
        var name = dot >= 0 ? encounterId[(dot + 1)..] : encounterId;
        return name.Length > 10 ? name[..10] : name;
    }
}
