using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;

namespace LocalRunStats;

// Generates a floor-by-floor HTML report for the most recently finished run
// (styled after sts2runs.com's own /run/{id} page, which the user pointed to
// as the target layout) and opens it in the system's default browser.
// Triggered by a button on StatsGraphOverlay.
//
// Local history/*.run files are only written once a run actually ends (win,
// death, or abandon — see HistoryStatsEngine's notes on the same files), so
// "most recently modified .run file" naturally means "the run that just
// finished" without needing a dedicated run-end hook.
//
// Card/relic/potion/monster/event ids in the .run JSON (e.g. "CARD.REAVE")
// are resolved to real display names via ModelDb.GetByIdOrNull<T> against
// the live game's model registry — this works even for cards/relics this
// player never picked, since ModelDb holds every canonical definition, not
// just ones "in play" this run (same technique as
// CombatStatsListener.GetEncounterDisplayName, just string-id-based instead
// of starting from a live CombatRoom).
public static class RunSummaryReport
{
    public static void OpenLatest()
    {
        try
        {
            var path = HistoryStatsEngine.FindAllRunFiles()
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (path == null)
            {
                Log.Warn("[LocalRunStats] No run history file found for the run summary report.");
                return;
            }

            var html = BuildHtml(path);
            var outDir = Path.Combine(Godot.OS.GetUserDataDir(), "mods", "local-run-stats");
            Directory.CreateDirectory(outDir);
            var outPath = Path.Combine(outDir, "run_summary.html");
            File.WriteAllText(outPath, html);
            OS.ShellOpen(outPath);
            Log.Info("[LocalRunStats] Opened run summary report: " + outPath);
        }
        catch (Exception ex)
        {
            Log.Error("[LocalRunStats] Failed to build run summary report: " + ex);
        }
    }

    // ---- Model-id -> display-name resolution ----

    private static ModelId ParseId(string fullId)
    {
        var dot = fullId.IndexOf('.');
        return dot < 0 ? new ModelId("", fullId) : new ModelId(fullId[..dot], fullId[(dot + 1)..]);
    }

    // Fallback for any id ModelDb can't resolve — "SHRINKER_BEETLE" -> "Shrinker Beetle".
    private static string Humanize(string fullId)
    {
        var dot = fullId.LastIndexOf('.');
        var entry = dot >= 0 ? fullId[(dot + 1)..] : fullId;
        var words = entry.Split('_', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", words.Select(w => w.Length == 0 ? w : char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant()));
    }

    private static string ResolveCardTitle(string fullId)
    {
        if (string.IsNullOrEmpty(fullId)) return "";
        try { return ModelDb.GetByIdOrNull<CardModel>(ParseId(fullId))?.Title ?? Humanize(fullId); }
        catch { return Humanize(fullId); }
    }

    private static string ResolveRelicTitle(string fullId)
    {
        if (string.IsNullOrEmpty(fullId)) return "";
        try { return ModelDb.GetByIdOrNull<RelicModel>(ParseId(fullId))?.Title?.GetRawText() ?? Humanize(fullId); }
        catch { return Humanize(fullId); }
    }

    private static string ResolvePotionTitle(string fullId)
    {
        if (string.IsNullOrEmpty(fullId)) return "";
        try { return ModelDb.GetByIdOrNull<PotionModel>(ParseId(fullId))?.Title?.GetRawText() ?? Humanize(fullId); }
        catch { return Humanize(fullId); }
    }

    private static string ResolveEncounterTitle(string fullId)
    {
        if (string.IsNullOrEmpty(fullId)) return "";
        try { return ModelDb.GetByIdOrNull<EncounterModel>(ParseId(fullId))?.Title?.GetRawText() ?? Humanize(fullId); }
        catch { return Humanize(fullId); }
    }

    private static string ResolveEventTitle(string fullId)
    {
        if (string.IsNullOrEmpty(fullId)) return "";
        try { return ModelDb.GetByIdOrNull<EventModel>(ParseId(fullId))?.Title?.GetRawText() ?? Humanize(fullId); }
        catch { return Humanize(fullId); }
    }

    private static string ResolveCharacterTitle(string fullId)
    {
        if (string.IsNullOrEmpty(fullId)) return "";
        try { return ModelDb.GetByIdOrNull<CharacterModel>(ParseId(fullId))?.Title?.GetRawText() ?? Humanize(fullId); }
        catch { return Humanize(fullId); }
    }

    // ---- JSON parsing (JsonNode, matching HistoryStatsEngine's style for
    // reading these same .run files) ----

    private static string Str(JsonNode n) { try { return n?.GetValue<string>(); } catch { return null; } }
    private static int Int(JsonNode n) { try { return n?.GetValue<int>() ?? 0; } catch { return 0; } }
    private static long Long(JsonNode n) { try { return n?.GetValue<long>() ?? 0; } catch { return 0; } }
    private static bool Bool(JsonNode n) { try { return n?.GetValue<bool>() ?? false; } catch { return false; } }

    private sealed class PlayerInfo
    {
        public long Id;
        public string CharacterName = "?";
        public int FinalDeckCount;
    }

    private sealed class FloorRow
    {
        public int FloorNumber;
        public string TypeLabel = "";
        public string TypeCss = "event";
        public string Icon = "❔";
        public string Name = "";
        public int TurnsTaken;
        public int CurrentHp;
        public int MaxHp;
        public int DamageTaken;
        public int HpHealed;
        public int GoldBefore;
        public int GoldAfter;
        public int DeckSize;
        public List<string> CardsGained = new();
        public List<string> CardsRemoved = new();
        public List<string> Curses = new();
        public List<string> RelicsGained = new();
        public List<string> PotionsGained = new();
        public List<string> PotionsUsed = new();
        public string ExtraNote = "";
    }

    private static string BuildHtml(string runFilePath)
    {
        var root = JsonNode.Parse(File.ReadAllText(runFilePath));
        if (root == null) throw new InvalidDataException("Empty/unparseable run file: " + runFilePath);

        var win = Bool(root["win"]);
        var wasAbandoned = Bool(root["was_abandoned"]);
        var ascension = Int(root["ascension"]);
        var runTimeSeconds = Int(root["run_time"]);
        var seed = Str(root["seed"]) ?? "";
        var killedByEncounter = Str(root["killed_by_encounter"]);

        var players = new List<PlayerInfo>();
        var playersNode = root["players"]?.AsArray();
        if (playersNode != null)
        {
            foreach (var p in playersNode)
            {
                if (p == null) continue;
                players.Add(new PlayerInfo
                {
                    Id = Long(p["id"]),
                    CharacterName = ResolveCharacterTitle(Str(p["character"]) ?? ""),
                    FinalDeckCount = p["deck"]?.AsArray().Count ?? 0,
                });
            }
        }

        // map_point_history is nested one sub-array PER ACT (confirmed
        // against real local .run files: a run that reached Act 3 has 3
        // outer entries), not a flat list of floors — flatten it here so
        // floor numbers run continuously across the whole run, matching
        // vanilla Slay the Spire's own floor-numbering convention.
        var mapPoints = (root["map_point_history"]?.AsArray() ?? new JsonArray())
            .SelectMany(actEntries => actEntries?.AsArray() ?? new JsonArray())
            .ToList();

        // Reconcile the running deck-size column against each player's known
        // FINAL count (see PlayerInfo.FinalDeckCount) rather than assuming
        // anything about how floor 1 / Neow's floor tags floor_added_to_deck
        // — starting size = final size - total gained + total removed, which
        // is exact regardless of that tagging.
        var totalGained = players.ToDictionary(p => p.Id, _ => 0);
        var totalRemoved = players.ToDictionary(p => p.Id, _ => 0);
        foreach (var mp in mapPoints)
        {
            foreach (var stat in mp?["player_stats"]?.AsArray() ?? new JsonArray())
            {
                var pid = Long(stat?["player_id"]);
                if (!totalGained.ContainsKey(pid)) continue;
                totalGained[pid] += stat["cards_gained"]?.AsArray().Count ?? 0;
                totalRemoved[pid] += stat["cards_removed"]?.AsArray().Count ?? 0;
            }
        }
        var runningDeckSize = players.ToDictionary(p => p.Id, p => p.FinalDeckCount - totalGained[p.Id] + totalRemoved[p.Id]);
        var lastGold = players.ToDictionary(p => p.Id, _ => -1);

        var rowsByPlayer = players.ToDictionary(p => p.Id, _ => new List<FloorRow>());

        var floorNumber = 0;
        foreach (var mp in mapPoints)
        {
            floorNumber++;
            var mapPointType = Str(mp?["map_point_type"]) ?? "unknown";
            var room = mp?["rooms"]?.AsArray()?.FirstOrDefault();
            var roomType = Str(room?["room_type"]) ?? mapPointType;
            var modelId = Str(room?["model_id"]);
            var turnsTaken = Int(room?["turns_taken"]);

            var (typeLabel, typeCss, icon, defaultName) = DescribeRoom(mapPointType, roomType);
            var name = modelId != null
                ? (mapPointType is "monster" or "elite" or "boss" ? ResolveEncounterTitle(modelId) : ResolveEventTitle(modelId))
                : defaultName;

            foreach (var stat in mp?["player_stats"]?.AsArray() ?? new JsonArray())
            {
                var pid = Long(stat?["player_id"]);
                if (!rowsByPlayer.TryGetValue(pid, out var rows)) continue;

                var cardsGainedNode = stat["cards_gained"]?.AsArray() ?? new JsonArray();
                var cardsRemovedNode = stat["cards_removed"]?.AsArray() ?? new JsonArray();
                runningDeckSize[pid] += cardsGainedNode.Count - cardsRemovedNode.Count;

                var row = new FloorRow
                {
                    FloorNumber = floorNumber,
                    TypeLabel = typeLabel,
                    TypeCss = typeCss,
                    Icon = icon,
                    Name = name,
                    TurnsTaken = turnsTaken,
                    CurrentHp = Int(stat["current_hp"]),
                    MaxHp = Int(stat["max_hp"]),
                    DamageTaken = Int(stat["damage_taken"]),
                    HpHealed = Int(stat["hp_healed"]),
                    GoldBefore = lastGold[pid],
                    GoldAfter = Int(stat["current_gold"]),
                    DeckSize = runningDeckSize[pid],
                };
                lastGold[pid] = row.GoldAfter;

                foreach (var c in cardsGainedNode)
                {
                    var id = Str(c?["id"]);
                    if (id == null) continue;
                    if (id.StartsWith("CURSE.", StringComparison.OrdinalIgnoreCase)) row.Curses.Add(ResolveCardTitle(id));
                    else row.CardsGained.Add(ResolveCardTitle(id));
                }
                foreach (var c in cardsRemovedNode)
                {
                    var id = Str(c?["id"]);
                    if (id != null) row.CardsRemoved.Add(ResolveCardTitle(id));
                }

                foreach (var r in stat["relic_choices"]?.AsArray() ?? new JsonArray())
                {
                    if (!Bool(r?["was_picked"])) continue;
                    var id = Str(r?["choice"]);
                    if (id != null) row.RelicsGained.Add(ResolveRelicTitle(id));
                }
                foreach (var r in stat["bought_relics"]?.AsArray() ?? new JsonArray())
                {
                    var id = Str(r);
                    if (id != null) row.RelicsGained.Add(ResolveRelicTitle(id));
                }
                foreach (var a in stat["ancient_choice"]?.AsArray() ?? new JsonArray())
                {
                    if (!Bool(a?["was_chosen"])) continue;
                    var textKey = Str(a?["TextKey"]);
                    if (textKey != null) row.RelicsGained.Add(ResolveRelicTitle("RELIC." + textKey));
                }

                foreach (var p in stat["potion_choices"]?.AsArray() ?? new JsonArray())
                {
                    if (!Bool(p?["was_picked"])) continue;
                    var id = Str(p?["choice"]);
                    if (id != null) row.PotionsGained.Add(ResolvePotionTitle(id));
                }
                foreach (var p in stat["potion_used"]?.AsArray() ?? new JsonArray())
                {
                    var id = Str(p);
                    if (id != null) row.PotionsUsed.Add(ResolvePotionTitle(id));
                }

                if (mapPointType == "rest_site")
                {
                    var choices = (stat["rest_site_choices"]?.AsArray() ?? new JsonArray())
                        .Select(Str).Where(s => s != null).Select(Humanize);
                    row.ExtraNote = string.Join(", ", choices);
                }

                rows.Add(row);
            }
        }

        return RenderHtml(win, wasAbandoned, ascension, runTimeSeconds, seed, killedByEncounter, players, rowsByPlayer);
    }

    private static (string label, string css, string icon, string defaultName) DescribeRoom(string mapPointType, string roomType) => mapPointType switch
    {
        "monster" => ("MONSTER", "monster", "⚔️", "Monster"),
        "elite" => ("ELITE", "elite", "💀", "Elite"),
        "boss" => ("BOSS", "boss", "👑", "Boss"),
        "shop" => ("SHOP", "shop", "🛍️", "Shop"),
        "treasure" => ("TREASURE", "treasure", "💰", "Treasure"),
        "rest_site" => ("REST", "rest", "🔥", "Rest Site"),
        "ancient" => ("ANCIENT", "ancient", "❓", "Ancient"),
        "unknown" => ("EVENT", "event", "❔", "Event"),
        _ => (mapPointType.ToUpperInvariant(), "event", "❔", Humanize(roomType)),
    };

    private static string RenderHtml(bool win, bool wasAbandoned, int ascension, int runTimeSeconds, string seed,
        string killedByEncounter, List<PlayerInfo> players, Dictionary<long, List<FloorRow>> rowsByPlayer)
    {
        var sb = new StringBuilder();
        sb.Append("<title>Run Summary</title><meta charset=\"utf-8\">");
        sb.Append(@"<style>
:root{color-scheme:dark;--bg:#0b0c10;--panel:#14161c;--border:#262a35;--text:#e8e9ee;--muted:#8b8fa3;}
body{background:var(--bg);color:var(--text);font-family:-apple-system,Segoe UI,Roboto,sans-serif;margin:0;padding:24px;}
h1{font-size:20px;margin:0 0 4px;}
.summary{color:var(--muted);font-size:13px;margin-bottom:16px;}
.tabs{display:flex;gap:8px;margin-bottom:16px;}
.tab{background:var(--panel);border:1px solid var(--border);color:var(--text);padding:6px 14px;border-radius:6px;cursor:pointer;font-size:13px;}
.tab.active{background:#2c3140;border-color:#4a5170;}
table{border-collapse:collapse;width:100%;font-size:13px;}
td,th{padding:8px 10px;vertical-align:top;text-align:left;}
tr{border-bottom:1px solid var(--border);}
th{color:var(--muted);font-weight:normal;font-size:11px;text-transform:uppercase;}
.badge{display:inline-block;padding:2px 8px;border-radius:4px;font-size:11px;font-weight:600;letter-spacing:.03em;}
.badge-monster{background:#132a3f;color:#7ec8ff;}
.badge-elite{background:#3a1a2f;color:#ff8fc7;}
.badge-boss{background:#3a0a0a;color:#ff7b7b;}
.badge-shop{background:#3a3218;color:#ffd977;}
.badge-treasure{background:#3a3218;color:#ffd977;}
.badge-rest{background:#2f1a0a;color:#ffab6b;}
.badge-ancient{background:#182f2a;color:#7effd9;}
.badge-event{background:#22242a;color:#c9ccd6;}
.hp-loss{color:#ff7b7b;}
.hp-gain{color:#7effa0;}
.pill{display:inline-block;padding:2px 8px;margin:2px 4px 2px 0;border-radius:4px;font-size:12px;border:1px solid transparent;}
.pill-card{background:#132a3f;color:#7ec8ff;border-color:#2c5a86;}
.pill-relic{background:#132a3f;color:#8fd6c8;border-color:#2c6a5a;}
.pill-potion{background:#3a1030;color:#ff8fd6;border-color:#6a2c56;}
.pill-curse{background:#3a1010;color:#ff8f8f;border-color:#6a2c2c;}
.pill-removed{background:#222;color:#888;text-decoration:line-through;border-color:#444;}
.cat{color:var(--muted);font-size:11px;text-transform:uppercase;margin-top:4px;}
.player-view{display:none;}
.player-view.active{display:block;}
</style>");

        sb.Append("<h1>Run Summary</h1>");
        var outcome = wasAbandoned ? "Abandoned" : win ? "Victory" : "Defeat";
        sb.Append("<div class=\"summary\">")
          .Append(Escape(outcome)).Append(" &middot; Ascension ").Append(ascension)
          .Append(" &middot; ").Append(runTimeSeconds / 60).Append("m ").Append(runTimeSeconds % 60).Append('s')
          .Append(" &middot; Seed ").Append(Escape(seed));
        if (!win && !wasAbandoned && !string.IsNullOrEmpty(killedByEncounter))
            sb.Append(" &middot; Killed by ").Append(Escape(ResolveEncounterTitle(killedByEncounter)));
        sb.Append("</div>");

        if (players.Count > 1)
        {
            sb.Append("<div class=\"tabs\">");
            for (var i = 0; i < players.Count; i++)
            {
                sb.Append("<div class=\"tab").Append(i == 0 ? " active" : "").Append("\" onclick=\"showPlayer(")
                  .Append(i).Append(")\" id=\"tab-").Append(i).Append("\">")
                  .Append(Escape(players[i].CharacterName)).Append("</div>");
            }
            sb.Append("</div>");
        }

        for (var i = 0; i < players.Count; i++)
        {
            var player = players[i];
            sb.Append("<div class=\"player-view").Append(i == 0 ? " active" : "").Append("\" id=\"player-").Append(i).Append("\">");
            sb.Append("<table><thead><tr><th></th><th>Floor</th><th>Type</th><th>Name</th><th>HP</th><th>Gold</th><th>Deck</th><th>Details</th></tr></thead><tbody>");
            foreach (var row in rowsByPlayer.GetValueOrDefault(player.Id, new List<FloorRow>()))
            {
                sb.Append("<tr>");
                sb.Append("<td>").Append(row.Icon).Append("</td>");
                sb.Append("<td>").Append(row.FloorNumber).Append("</td>");
                sb.Append("<td><span class=\"badge badge-").Append(row.TypeCss).Append("\">").Append(Escape(row.TypeLabel)).Append("</span></td>");
                sb.Append("<td>").Append(Escape(row.Name));
                if (row.TurnsTaken > 0) sb.Append(" <span style=\"color:var(--muted)\">(" + row.TurnsTaken + " turns)</span>");
                sb.Append("</td>");

                sb.Append("<td>").Append(row.CurrentHp).Append('/').Append(row.MaxHp);
                if (row.DamageTaken > 0) sb.Append(" <span class=\"hp-loss\">-").Append(row.DamageTaken).Append("</span>");
                else if (row.HpHealed > 0) sb.Append(" <span class=\"hp-gain\">+").Append(row.HpHealed).Append("</span>");
                sb.Append("</td>");

                sb.Append("<td>").Append(row.GoldBefore < 0 ? "&mdash;" : row.GoldBefore.ToString()).Append(" → ").Append(row.GoldAfter).Append("</td>");
                sb.Append("<td>").Append(row.DeckSize).Append("</td>");

                sb.Append("<td>");
                AppendPillGroup(sb, "Cards", row.CardsGained, "pill-card");
                AppendPillGroup(sb, "Curses", row.Curses, "pill-curse");
                AppendPillGroup(sb, "Removed", row.CardsRemoved, "pill-removed");
                AppendPillGroup(sb, "Relics", row.RelicsGained, "pill-relic");
                AppendPillGroup(sb, "Potions", row.PotionsGained, "pill-potion");
                AppendPillGroup(sb, "Used", row.PotionsUsed, "pill-potion");
                if (!string.IsNullOrEmpty(row.ExtraNote)) sb.Append("<div class=\"cat\">").Append(Escape(row.ExtraNote)).Append("</div>");
                sb.Append("</td>");

                sb.Append("</tr>");
            }
            sb.Append("</tbody></table></div>");
        }

        sb.Append(@"<script>
function showPlayer(i){
  document.querySelectorAll('.player-view').forEach(function(el,idx){el.classList.toggle('active', idx===i);});
  document.querySelectorAll('.tab').forEach(function(el,idx){el.classList.toggle('active', idx===i);});
}
</script>");

        return sb.ToString();
    }

    private static void AppendPillGroup(StringBuilder sb, string label, List<string> items, string cssClass)
    {
        if (items.Count == 0) return;
        sb.Append("<div class=\"cat\">").Append(label).Append("</div>");
        foreach (var item in items)
        {
            sb.Append("<span class=\"pill ").Append(cssClass).Append("\">").Append(Escape(item)).Append("</span>");
        }
    }

    private static string Escape(string s) => string.IsNullOrEmpty(s) ? "" : s
        .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
