using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Logging;

namespace LocalRunStats;

// Three data sources feed this engine, all read-only (nothing about this
// player is ever sent anywhere):
// - Card/relic pick-rate + Impact ("Pick"/"Impact" in the overlay) come from
//   sts2runs.com's public community API's aggregate endpoint (thousands of
//   players' runs) — switched from this player's own local history/*.run
//   files on request, for a statistically meaningful sample size. See
//   RefreshCommunityStatsAsync.
// - "Synergy" (deck-similarity-weighted win rate) needs full per-run deck
//   lists, which the aggregate endpoint doesn't expose, but sts2runs.com's
//   per-run detail endpoint (/api/runs/{id}) does — it's the same raw .run
//   JSON schema as our own local history files. There's no bulk "give me N
//   full decks" endpoint though, so this costs one HTTP request per run.
//   The goal is eventually *all* community runs (9,000+ and growing), which
//   is too many requests to do in one burst without hammering a small
//   community site's API — so RefreshCommunityRunDetailsAsync fetches a
//   bounded batch of new-to-us runs each launch, caches them to disk, and
//   picks up where it left off next time. A full backfill takes several
//   launches; after that, each launch just tops up with whatever's new
//   since last time. See RefreshCommunityRunDetailsAsync.
// - Local history/*.run files are also folded into the same Synergy dataset
//   (this player's own runs are a free addition on top of the community sample).
public static class HistoryStatsEngine
{
    private const string CommunityStatsUrl = "https://sts2runs.com/api/runs/community?ascMin=0&ascMax=10&mode=stats&include_elo=1";
    private const string CommunityRunListUrlFormat = "https://sts2runs.com/api/runs/community?ascMin=0&ascMax=10&page={0}&limit=100&sort=startTime&dir=desc&mode=runs";
    private const string CommunityRunDetailUrlFormat = "https://sts2runs.com/api/runs/{0}";

    // No cap on total cached runs — the goal is the entire community corpus.
    // These bound how much NEW fetching happens in a single launch, so a full
    // backfill is spread across several sessions instead of thousands of
    // requests in one burst.
    private const int CommunityRunFetchBudgetPerSession = 1500;
    private const int CommunityRunFetchConcurrency = 6;
    private const int CommunityRunListPageSafetyCeiling = 400; // 400 * limit(100) = 40,000 candidate ids; a hard stop against pagination bugs, not the normal exit path

    private static Dictionary<string, CardStatEntry> _cardStats = new();
    private static Dictionary<string, RelicStatEntry> _relicStats = new();
    private static List<ParsedRun> _localParsedRuns = new();
    private static Dictionary<int, ParsedRun> _communityParsedRuns = new();
    private static readonly object Lock = new();

    public static IReadOnlyDictionary<string, CardStatEntry> CardStats
    {
        get { lock (Lock) { return _cardStats; } }
    }

    public static IReadOnlyDictionary<string, RelicStatEntry> RelicStats
    {
        get { lock (Lock) { return _relicStats; } }
    }

    // Parses local history/*.run files, needed only to feed ComputeSynergy /
    // ComputeRelicSynergy (per-run deck lists). Does NOT populate CardStats /
    // RelicStats — those come from the community API, see RefreshCommunityStatsAsync.
    public static void Refresh()
    {
        try
        {
            var runs = FindAllRunFiles().Select(path => TryParseRun(path, unwrapRunKey: false)).Where(r => r != null).Select(r => r!).ToList();
            lock (Lock)
            {
                _localParsedRuns = runs;
            }
            Log.Info($"[LocalRunStats] Parsed {runs.Count} local history run(s) for deck-synergy computation.");
        }
        catch (Exception ex)
        {
            Log.Error("[LocalRunStats] Failed to parse local history for synergy: " + ex);
        }
    }

    // Fire-and-forget from RunStatsRecorder.Initialize. Builds/extends the
    // on-disk cache of individual community run decks (see class doc), then
    // folds them into the Synergy dataset alongside local runs.
    public static async Task RefreshCommunityRunDetailsAsync()
    {
        try
        {
            var cache = LoadCommunityRunCache();
            var alreadyKnown = new HashSet<int>(cache.Keys);
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

            // Runs are listed newest-first. A page that yields zero new ids means
            // we've caught up to what a previous session already cached, so stop —
            // this makes an already-synced launch cost just one list request
            // instead of scanning deep into history every time.
            var idsToFetch = new List<int>();
            for (var page = 0; page < CommunityRunListPageSafetyCeiling && idsToFetch.Count < CommunityRunFetchBudgetPerSession; page++)
            {
                var listJson = await http.GetStringAsync(string.Format(CommunityRunListUrlFormat, page));
                var runsArr = JsonNode.Parse(listJson)?["runs"]?.AsArray();
                if (runsArr == null || runsArr.Count == 0) break;

                var newOnThisPage = 0;
                foreach (var item in runsArr)
                {
                    var id = item?["id"]?.GetValue<int>();
                    if (id == null || alreadyKnown.Contains(id.Value) || idsToFetch.Contains(id.Value)) continue;
                    idsToFetch.Add(id.Value);
                    newOnThisPage++;
                    if (idsToFetch.Count >= CommunityRunFetchBudgetPerSession) break;
                }

                if (newOnThisPage == 0) break;
            }

            if (idsToFetch.Count == 0)
            {
                Log.Info($"[LocalRunStats] Community synergy dataset: {cache.Count} run(s) already cached, nothing new to fetch.");
                MergeSynergyDatasets(cache);
                return;
            }

            Log.Info($"[LocalRunStats] Community synergy dataset: {cache.Count} cached, fetching {idsToFetch.Count} new run detail(s) from sts2runs.com...");

            var fetched = new List<(int Id, ParsedRun Run)>();
            var semaphore = new SemaphoreSlim(CommunityRunFetchConcurrency);
            var tasks = idsToFetch.Select(async id =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var detailJson = await http.GetStringAsync(string.Format(CommunityRunDetailUrlFormat, id));
                    var run = TryParseRun(detailJson, unwrapRunKey: true, isRawJson: true);
                    if (run != null)
                    {
                        lock (fetched) { fetched.Add((id, run)); }
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn($"[LocalRunStats] Failed to fetch community run {id}: {ex.Message}");
                }
                finally
                {
                    semaphore.Release();
                }
            });
            await Task.WhenAll(tasks);

            foreach (var (id, run) in fetched) cache[id] = run;
            AppendCommunityRunCache(fetched);
            MergeSynergyDatasets(cache);

            Log.Info($"[LocalRunStats] Community synergy dataset ready: {cache.Count} run(s) with full deck data ({fetched.Count} newly fetched).");
        }
        catch (Exception ex)
        {
            Log.Error("[LocalRunStats] Failed to build community synergy dataset: " + ex);
        }
    }

    private static void MergeSynergyDatasets(Dictionary<int, ParsedRun> community)
    {
        lock (Lock)
        {
            _communityParsedRuns = community;
        }
    }

    // Fire-and-forget from RunStatsRecorder.Initialize — must not block mod
    // init on network I/O. Populates CardStats / RelicStats once the request
    // completes; until then (or if it fails, e.g. no internet) those stay at
    // whatever they were, and the overlay shows "--" for pick/impact.
    public static async Task RefreshCommunityStatsAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            var json = await http.GetStringAsync(CommunityStatsUrl);
            var root = JsonNode.Parse(json);
            var stats = root?["stats"];
            if (stats == null)
            {
                Log.Warn("[LocalRunStats] Community stats response had no 'stats' field.");
                return;
            }

            var totalRuns = stats["totalRuns"]?.GetValue<int>() ?? 0;
            var totalWins = stats["wins"]?.GetValue<int>() ?? 0;
            var cardRatings = stats["eloResults"]?["ratings"]?.AsArray();
            var relicRatings = stats["relicEloResults"]?["ratings"]?.AsArray();

            var cardStats = ParseCardEloRatings(cardRatings, totalRuns, totalWins);
            var relicStats = ParseRelicEloRatings(relicRatings, totalRuns, totalWins);

            lock (Lock)
            {
                _cardStats = cardStats;
                _relicStats = relicStats;
            }
            WriteCache(cardStats, relicStats);
            Log.Info($"[LocalRunStats] Loaded community stats from sts2runs.com: {totalRuns} community runs, {cardStats.Count} cards, {relicStats.Count} relics.");
        }
        catch (Exception ex)
        {
            Log.Error("[LocalRunStats] Failed to fetch community stats from sts2runs.com: " + ex);
        }
    }

    // Community card ids already match our CARD.XXX ModelId format exactly.
    // "games"/"wins" = runs where this card ended up in the deck; there's no
    // per-card "without" count from this endpoint, so it's approximated from
    // the community-wide totals (totalRuns - games, totalWins - wins).
    private static Dictionary<string, CardStatEntry> ParseCardEloRatings(JsonArray ratings, int totalRuns, int totalWins)
    {
        var stats = new Dictionary<string, CardStatEntry>();
        if (ratings == null) return stats;

        foreach (var node in ratings)
        {
            var id = node?["id"]?.GetValue<string>();
            if (id == null) continue;
            var games = node["games"]?.GetValue<int>() ?? 0;
            var wins = node["wins"]?.GetValue<int>() ?? 0;
            stats[id] = new CardStatEntry
            {
                TimesOffered = node["offers"]?.GetValue<int>() ?? 0,
                TimesPicked = node["picks"]?.GetValue<int>() ?? 0,
                RunsWithCard = games,
                WinsWithCard = wins,
                RunsWithoutCard = Math.Max(0, totalRuns - games),
                WinsWithoutCard = Math.Max(0, totalWins - wins),
            };
        }

        return stats;
    }

    // Community relic ids are bare (e.g. "SILKEN_TRESS") — our RelicModel.Id
    // renders as "RELIC.SILKEN_TRESS", so prefix to match.
    private static Dictionary<string, RelicStatEntry> ParseRelicEloRatings(JsonArray ratings, int totalRuns, int totalWins)
    {
        var stats = new Dictionary<string, RelicStatEntry>();
        if (ratings == null) return stats;

        foreach (var node in ratings)
        {
            var rawId = node?["id"]?.GetValue<string>();
            if (rawId == null) continue;
            var id = "RELIC." + rawId;
            var games = node["games"]?.GetValue<int>() ?? 0;
            var wins = node["wins"]?.GetValue<int>() ?? 0;
            stats[id] = new RelicStatEntry
            {
                TimesOffered = node["offers"]?.GetValue<int>() ?? 0,
                TimesPicked = node["picks"]?.GetValue<int>() ?? 0,
                RunsWithRelic = games,
                WinsWithRelic = wins,
                RunsWithoutRelic = Math.Max(0, totalRuns - games),
                WinsWithoutRelic = Math.Max(0, totalWins - wins),
            };
        }

        return stats;
    }

    // Deck-similarity-weighted win rate with vs. without the offered card, where
    // similarity = fraction of currentDeckIds present in a given historical run's
    // final deck. Runs with zero overlap with the current deck don't contribute —
    // this is meant to answer "in decks that looked like mine, did this card help".
    public static SynergyEntry ComputeSynergy(string offeredCardId, IReadOnlyCollection<string> currentDeckIds)
    {
        return ComputeSynergyInternal(offeredCardId, currentDeckIds, CombinedSynergyRuns(), r => r.DeckCardIds);
    }

    // Same idea as ComputeSynergy, but keyed off the current relic collection
    // instead of the current deck.
    public static SynergyEntry ComputeRelicSynergy(string offeredRelicId, IReadOnlyCollection<string> currentRelicIds)
    {
        return ComputeSynergyInternal(offeredRelicId, currentRelicIds, CombinedSynergyRuns(), r => r.RelicIds);
    }

    // This player's local runs plus whatever's been cached from sts2runs.com.
    private static List<ParsedRun> CombinedSynergyRuns()
    {
        lock (Lock)
        {
            var combined = new List<ParsedRun>(_localParsedRuns.Count + _communityParsedRuns.Count);
            combined.AddRange(_localParsedRuns);
            combined.AddRange(_communityParsedRuns.Values);
            return combined;
        }
    }

    private static SynergyEntry ComputeSynergyInternal(string offeredId, IReadOnlyCollection<string> currentIds, List<ParsedRun> runs, Func<ParsedRun, HashSet<string>> selector)
    {
        var entry = new SynergyEntry();
        if (currentIds.Count == 0) return entry;

        double weightedWinWith = 0, weightWith = 0, weightedWinWithout = 0, weightWithout = 0;
        foreach (var run in runs)
        {
            var runIds = selector(run);
            var overlapCount = 0;
            foreach (var id in currentIds)
            {
                if (runIds.Contains(id)) overlapCount++;
            }
            if (overlapCount == 0) continue;
            var similarity = (double)overlapCount / currentIds.Count;
            var winValue = run.Win ? 1.0 : 0.0;

            if (runIds.Contains(offeredId))
            {
                weightedWinWith += similarity * winValue;
                weightWith += similarity;
            }
            else
            {
                weightedWinWithout += similarity * winValue;
                weightWithout += similarity;
            }
        }

        if (weightWith <= 0 || weightWithout <= 0) return entry;

        entry.HasData = true;
        entry.WinRateWithCard = weightedWinWith / weightWith;
        entry.WinRateWithoutCard = weightedWinWithout / weightWithout;
        entry.TotalSimilarityWeight = weightWith + weightWithout;
        return entry;
    }

    // Only DeckCardIds/RelicIds/Win are needed now — CardStats/RelicStats
    // (which used to come from card_choices/relic_choices was_picked here)
    // are sourced from the community API instead. See ComputeSynergy /
    // ComputeRelicSynergy for the only remaining consumer of this data.
    private sealed class ParsedRun
    {
        public bool Win;
        public HashSet<string> DeckCardIds = new();
        public HashSet<string> RelicIds = new();
    }

    // Local files hold one run's JSON directly; sts2runs.com's per-run detail
    // endpoint wraps the same schema as { "run": {...}, "userId": ... }
    // (unwrapRunKey = true unwraps that). isRawJson = true means `input` is
    // already the JSON text (community fetch); otherwise it's a file path.
    private static ParsedRun TryParseRun(string input, bool unwrapRunKey, bool isRawJson = false)
    {
        try
        {
            var json = isRawJson ? input : File.ReadAllText(input);
            var parsed = JsonNode.Parse(json);
            var root = unwrapRunKey ? parsed?["run"] : parsed;
            if (root == null) return null;

            var wasAbandoned = root["was_abandoned"]?.GetValue<bool>() ?? false;
            if (wasAbandoned) return null;

            var run = new ParsedRun
            {
                Win = root["win"]?.GetValue<bool>() ?? false,
            };

            var players = root["players"]?.AsArray();
            if (players != null && players.Count > 0)
            {
                var deck = players[0]?["deck"]?.AsArray();
                if (deck != null)
                {
                    foreach (var card in deck)
                    {
                        var id = card?["id"]?.GetValue<string>();
                        if (id != null) run.DeckCardIds.Add(id);
                    }
                }

                var relics = players[0]?["relics"]?.AsArray();
                if (relics != null)
                {
                    foreach (var relic in relics)
                    {
                        var id = relic?["id"]?.GetValue<string>();
                        if (id != null) run.RelicIds.Add(id);
                    }
                }
            }

            return run;
        }
        catch (Exception ex)
        {
            Log.Warn($"[LocalRunStats] Skipping unparseable run ({(isRawJson ? "community fetch" : input)}): {ex.Message}");
            return null;
        }
    }

    // Compact persisted form of a community ParsedRun — avoids re-fetching
    // the same run's full JSON (and re-hitting sts2runs.com) every launch.
    private sealed class CommunityRunCacheEntry
    {
        public int Id { get; set; }
        public bool Win { get; set; }
        public List<string> DeckCardIds { get; set; } = new();
        public List<string> RelicIds { get; set; } = new();
    }

    private static string CommunityRunCachePath => Path.Combine(Godot.OS.GetUserDataDir(), "mods", "local-run-stats", "community_run_cache.jsonl");

    private static Dictionary<int, ParsedRun> LoadCommunityRunCache()
    {
        var result = new Dictionary<int, ParsedRun>();
        var path = CommunityRunCachePath;
        if (!File.Exists(path)) return result;

        foreach (var line in File.ReadAllLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var entry = JsonSerializer.Deserialize<CommunityRunCacheEntry>(line);
                if (entry == null) continue;
                result[entry.Id] = new ParsedRun
                {
                    Win = entry.Win,
                    DeckCardIds = new HashSet<string>(entry.DeckCardIds),
                    RelicIds = new HashSet<string>(entry.RelicIds),
                };
            }
            catch (Exception ex)
            {
                Log.Warn($"[LocalRunStats] Skipping malformed community run cache line: {ex.Message}");
            }
        }

        return result;
    }

    private static void AppendCommunityRunCache(List<(int Id, ParsedRun Run)> newEntries)
    {
        if (newEntries.Count == 0) return;
        var statsDir = Path.Combine(Godot.OS.GetUserDataDir(), "mods", "local-run-stats");
        Directory.CreateDirectory(statsDir);

        var lines = newEntries.Select(e => JsonSerializer.Serialize(new CommunityRunCacheEntry
        {
            Id = e.Id,
            Win = e.Run.Win,
            DeckCardIds = e.Run.DeckCardIds.ToList(),
            RelicIds = e.Run.RelicIds.ToList(),
        }));
        File.AppendAllLines(CommunityRunCachePath, lines);
    }

    private static IEnumerable<string> FindAllRunFiles()
    {
        var userDataDir = Godot.OS.GetUserDataDir();
        var steamRoot = Path.Combine(userDataDir, "steam");
        if (!Directory.Exists(steamRoot)) yield break;

        foreach (var accountDir in Directory.GetDirectories(steamRoot))
        {
            foreach (var f in FindHistoryFilesUnder(accountDir)) yield return f;

            var moddedDir = Path.Combine(accountDir, "modded");
            if (Directory.Exists(moddedDir))
            {
                foreach (var f in FindHistoryFilesUnder(moddedDir)) yield return f;
            }
        }
    }

    private static IEnumerable<string> FindHistoryFilesUnder(string root)
    {
        foreach (var profileDir in Directory.GetDirectories(root, "profile*"))
        {
            var historyDir = Path.Combine(profileDir, "saves", "history");
            if (Directory.Exists(historyDir))
            {
                foreach (var f in Directory.GetFiles(historyDir, "*.run")) yield return f;
            }
        }
    }

    private static void WriteCache(Dictionary<string, CardStatEntry> cardStats, Dictionary<string, RelicStatEntry> relicStats)
    {
        var statsDir = Path.Combine(Godot.OS.GetUserDataDir(), "mods", "local-run-stats");
        Directory.CreateDirectory(statsDir);
        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(Path.Combine(statsDir, "card_stats.json"), JsonSerializer.Serialize(cardStats, options));
        File.WriteAllText(Path.Combine(statsDir, "relic_stats.json"), JsonSerializer.Serialize(relicStats, options));
    }
}
