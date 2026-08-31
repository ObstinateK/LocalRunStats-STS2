using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
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

    // Every chart/table below used to group and key its per-player series
    // directly by CharacterName (a string). That silently broke in co-op
    // whenever two players picked the SAME character: e.g. `fight.FirstOrDefault(r
    // => r.CharacterName == "Silent")` would only ever find ONE of the two
    // Silents' rows per fight, so the second player's data got dropped, not
    // just mislabeled. Every record type already carries PlayerNetId (the
    // true unique identity), so the fix is to key everything by NetId
    // internally and only use CharacterName for display — resolved through
    // this helper into "Silent 1"/"Silent 2" when (and only when) more than
    // one distinct NetId in the given data shares the same raw character
    // name; a lone player keeps their plain name. Numbering follows
    // first-occurrence order in `players`, so it's stable for one Build call
    // but is recomputed fresh (never persisted) each time from whatever data
    // is currently available — e.g. before a second same-character player has
    // logged anything, the first one is shown unsuffixed, then both gain
    // numbers once there's data to detect the collision.
    public static Dictionary<ulong, string> DisambiguateCharacterNames(IEnumerable<(ulong NetId, string CharacterName)> players)
    {
        var distinct = new List<(ulong NetId, string CharacterName)>();
        var seenNetIds = new HashSet<ulong>();
        foreach (var p in players)
        {
            if (seenNetIds.Add(p.NetId)) distinct.Add(p);
        }

        var countByName = distinct.GroupBy(p => p.CharacterName).ToDictionary(g => g.Key, g => g.Count());
        var ordinalByName = new Dictionary<string, int>();
        var result = new Dictionary<ulong, string>();
        foreach (var p in distinct)
        {
            if (countByName[p.CharacterName] <= 1)
            {
                result[p.NetId] = p.CharacterName;
            }
            else
            {
                ordinalByName.TryGetValue(p.CharacterName, out var n);
                n++;
                ordinalByName[p.CharacterName] = n;
                result[p.NetId] = $"{p.CharacterName} {n}";
            }
        }
        return result;
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
    public static ChartData BuildDamageChartData(bool perStage, bool dealt, int? actFilter) =>
        BuildFightMetric(perStage, actFilter, r => dealt ? r.DamageDealt : r.DamageTaken);

    public static ChartData BuildTurnsChartData(bool perStage, int? actFilter) =>
        BuildFightMetric(perStage, actFilter, r => r.TurnsTaken);

    public static ChartData BuildCardsPlayedChartData(bool perStage, int? actFilter) =>
        BuildFightMetric(perStage, actFilter, r => r.CardsPlayed);

    private static ChartData BuildFightMetric(bool perStage, int? actFilter, Func<PlayerCombatRecord, float> selector)
    {
        var records = FilterToCurrentRun(ReadAllLines<PlayerCombatRecord>("player_combat_stats.jsonl"), r => r.Timestamp);
        if (actFilter.HasValue) records = records.Where(r => r.ActIndex == actFilter.Value).ToList();
        return perStage ? BuildPerStageFightMetric(records, selector) : BuildCumulativeFightMetric(records, selector);
    }

    // One bar-group per fight (grouped by the shared Timestamp all players'
    // records for that fight were written with — see
    // CombatStatsListener.WritePerPlayerRecords), in chronological order.
    // Raw per-fight value, not a running total.
    private static ChartData BuildPerStageFightMetric(List<PlayerCombatRecord> records, Func<PlayerCombatRecord, float> selector)
    {
        var data = new ChartData();
        if (records.Count == 0) return data;

        var displayNames = DisambiguateCharacterNames(records.Select(r => (r.PlayerNetId, r.CharacterName)));
        var netIds = displayNames.Keys.ToList();
        foreach (var netId in netIds) data.SeriesByPlayer[displayNames[netId]] = new List<float>();

        var fights = records.GroupBy(r => r.Timestamp).OrderBy(g => g.Key).ToList();
        var stageIndex = 0;
        foreach (var fight in fights)
        {
            stageIndex++;
            data.XLabels.Add(ShortenEncounterId(fight.First().EncounterId, stageIndex));
            foreach (var netId in netIds)
            {
                var record = fight.FirstOrDefault(r => r.PlayerNetId == netId);
                data.SeriesByPlayer[displayNames[netId]].Add(record != null ? selector(record) : 0f);
            }
        }
        return data;
    }

    // Group by fight (Timestamp), same as BuildPerStageFightMetric — a
    // previous version advanced the x-axis once per RECORD instead of once
    // per FIGHT, so in co-op a single fight (which writes one record per
    // player, all sharing the same Timestamp) consumed two x-axis slots for
    // what was actually one moment. Whichever player's record sorted second
    // then looked "one fight behind" the other — confirmed live: "fight 1
    // shows Ironclad did 0 damage, but it shows up in fight 2."
    private static ChartData BuildCumulativeFightMetric(List<PlayerCombatRecord> records, Func<PlayerCombatRecord, float> selector)
    {
        var data = new ChartData();
        if (records.Count == 0) return data;

        var displayNames = DisambiguateCharacterNames(records.Select(r => (r.PlayerNetId, r.CharacterName)));
        var netIds = displayNames.Keys.ToList();
        var runningTotals = netIds.ToDictionary(id => id, _ => 0f);
        foreach (var netId in netIds) data.SeriesByPlayer[displayNames[netId]] = new List<float>();

        var fights = records.GroupBy(r => r.Timestamp).OrderBy(g => g.Key).ToList();
        var fightIndex = 0;
        foreach (var fight in fights)
        {
            fightIndex++;
            data.XLabels.Add(fightIndex.ToString());
            foreach (var netId in netIds)
            {
                var record = fight.FirstOrDefault(r => r.PlayerNetId == netId);
                if (record != null)
                {
                    runningTotals[netId] += selector(record);
                }
                // Player had no record for this fight (e.g. joined mid-run) ->
                // carry their running total forward unchanged.
                data.SeriesByPlayer[displayNames[netId]].Add(runningTotals[netId]);
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
        var displayNames = DisambiguateCharacterNames(ordered.Select(r => (r.PlayerNetId, r.CharacterName)));

        if (perStage)
        {
            // One bar per gold-gain event = the amount gained at that specific
            // event (delta from that player's previous known total), not the
            // running total — mirrors damage's per-fight raw value. Includes
            // the baseline row RunContext.EnsureBaselineGoldCaptured writes at
            // run start, so a player's very first bar is their starting gold,
            // not their first real pickup on top of an assumed-zero balance.
            var lastKnown = new Dictionary<ulong, float>();
            var index = 0;
            foreach (var r in ordered)
            {
                index++;
                data.XLabels.Add(index.ToString());
                lastKnown.TryGetValue(r.PlayerNetId, out var previous);
                var delta = System.Math.Max(0f, r.CurrentGold - previous);
                lastKnown[r.PlayerNetId] = r.CurrentGold;

                foreach (var netId in lastKnown.Keys.ToList())
                {
                    var name = displayNames[netId];
                    if (!data.SeriesByPlayer.TryGetValue(name, out var series))
                    {
                        series = new List<float>(new float[data.XLabels.Count - 1]);
                        data.SeriesByPlayer[name] = series;
                    }
                    series.Add(netId == r.PlayerNetId ? delta : 0f);
                }
            }
        }
        else
        {
            // Same fix as BuildCumulativeFightMetric: align by actual
            // chronological moment (grouped by Timestamp), not by each
            // player's own event index — two players' gold events are
            // independent, so "player A's 3rd pickup" and "player B's 3rd
            // pickup" are not the same moment and shouldn't share an x-axis
            // slot. The baseline row (see above) means every player's very
            // first moment is their starting gold, not 0.
            var netIds = displayNames.Keys.ToList();
            var lastKnown = netIds.ToDictionary(id => id, _ => 0f);
            foreach (var netId in netIds) data.SeriesByPlayer[displayNames[netId]] = new List<float>();

            var moments = ordered.GroupBy(r => r.Timestamp).OrderBy(g => g.Key).ToList();
            var index = 0;
            foreach (var moment in moments)
            {
                index++;
                data.XLabels.Add(index.ToString());
                foreach (var netId in netIds)
                {
                    var record = moment.FirstOrDefault(r => r.PlayerNetId == netId);
                    if (record != null) lastKnown[netId] = record.CurrentGold;
                    data.SeriesByPlayer[displayNames[netId]].Add(lastKnown[netId]);
                }
            }
        }
        return data;
    }

    // Per-player breakdown of which cards were played and how many times,
    // for the whole run so far (no act filter — "how many times has this
    // card been played" is a run-wide question, not a per-act one). Rendered
    // as BBCode text (by CardPlayCountsPanel) rather than a chart: card names
    // have no natural x-axis ordering the way fights/turns do.
    public static string BuildOverallCardPlayCounts()
    {
        var records = FilterToCurrentRun(ReadAllLines<CardPlayRecord>("card_plays.jsonl"), r => r.Timestamp);
        if (records.Count == 0) return "(no cards played yet)";

        var displayNames = DisambiguateCharacterNames(records.Select(r => (r.PlayerNetId, r.CharacterName)));
        var sb = new StringBuilder();
        foreach (var playerGroup in records.GroupBy(r => r.PlayerNetId))
        {
            sb.Append($"[b]{Escape(displayNames[playerGroup.Key])}[/b]\n[table=2]");
            var counts = playerGroup.GroupBy(r => r.CardName)
                .Select(g => (Name: g.Key, Count: g.Count()))
                .OrderByDescending(c => c.Count)
                .ThenBy(c => c.Name, StringComparer.Ordinal);
            foreach (var (name, count) in counts)
            {
                sb.Append($"[cell]{Escape(name)}[/cell][cell]x{count}[/cell]");
            }
            sb.Append("[/table]\n");
        }
        return sb.ToString();
    }

    // One self-contained BBCode block per fight (grouped by the shared
    // Timestamp all players' records for that fight were written with, same
    // convention as BuildPerStageFightMetric), each with its own per-player
    // card table. Returned as a list, one entry per fight, rather than one
    // combined string — CardPlayCountsPanel lays these out in an
    // HFlowContainer (fights side-by-side, wrapping to a new row once a row
    // is full) instead of one long vertical list.
    public static List<string> BuildCardPlayCountsByFightBlocks()
    {
        var records = FilterToCurrentRun(ReadAllLines<CardPlayCountRecord>("card_play_fights.jsonl"), r => r.Timestamp);
        if (records.Count == 0) return new List<string>();

        var displayNames = DisambiguateCharacterNames(records.Select(r => (r.PlayerNetId, r.CharacterName)));
        var blocks = new List<string>();
        var fights = records.GroupBy(r => r.Timestamp).OrderBy(g => g.Key).ToList();
        var stageIndex = 0;
        foreach (var fight in fights)
        {
            stageIndex++;
            var sb = new StringBuilder();
            sb.Append($"[b]Fight {stageIndex}: {Escape(ShortenEncounterId(fight.First().EncounterId, stageIndex))}[/b]\n");
            foreach (var playerGroup in fight.GroupBy(r => r.PlayerNetId))
            {
                sb.Append($"{Escape(displayNames[playerGroup.Key])}\n[table=2]");
                foreach (var row in playerGroup.OrderByDescending(r => r.Count).ThenBy(r => r.CardName, StringComparer.Ordinal))
                {
                    sb.Append($"[cell]{Escape(row.CardName)}[/cell][cell]x{row.Count}[/cell]");
                }
                sb.Append("[/table]\n");
            }
            blocks.Add(sb.ToString());
        }
        return blocks;
    }

    private static string Escape(string s) => s.Replace("[", "[lb]");

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
