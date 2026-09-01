using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace LocalRunStats;

// Generates a floor-by-floor HTML report for the CURRENT run (styled after
// sts2runs.com's own /run/{id} page, which the user pointed to as the target
// layout) and opens it in the system's default browser. Triggered by a
// button on StatsGraphOverlay.
//
// Reads RunManager.Instance.History directly — a LIVE, always-up-to-date
// object the game itself maintains throughout the run (confirmed via
// reflection: RunManager is a static singleton with a `RunHistory History`
// property; RunHistory.MapPointHistory is a flat, growing List<> updated as
// the run progresses, NOT the nested-per-act structure the on-disk .run file
// uses — that nesting is apparently introduced only at save/serialize time).
// This replaced an earlier version that parsed the most-recently-modified
// history/*.run file instead — reported live as showing "info from a
// previous run" because .run files are only written once a run actually
// ends, so it could never reflect an in-progress run. Reading the live
// object fixes that and is simpler besides: strongly-typed C# objects
// instead of JsonNode walking, and richer data (e.g. AncientChoiceHistoryEntry
// and EventOptionHistoryEntry carry an already-resolved LocString Title, so
// event/ancient flavor text no longer needs a separate localization lookup
// the way the raw file's {"key":...,"table":...} shape would have).
public static class RunSummaryReport
{
    public static void OpenCurrent()
    {
        try
        {
            var runManager = RunManager.Instance;
            var history = runManager?.History;
            if (history == null)
            {
                Log.Warn("[LocalRunStats] No active run history found for the run summary report.");
                return;
            }

            var html = BuildHtml(history);
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
    // Same ModelDb.GetByIdOrNull<T> technique as CombatStatsListener.GetEncounterDisplayName,
    // just against a ModelId we already have in hand instead of a string to parse.

    private static string Humanize(ModelId id) => Humanize(id.Entry);

    // Fallback for any id ModelDb can't resolve — "SHRINKER_BEETLE" -> "Shrinker Beetle".
    private static string Humanize(string entry)
    {
        var words = entry.Split('_', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", words.Select(w => w.Length == 0 ? w : char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant()));
    }

    private static string ResolveCardTitle(ModelId id)
    {
        try { return ModelDb.GetByIdOrNull<CardModel>(id)?.Title ?? Humanize(id); }
        catch { return Humanize(id); }
    }

    private static string ResolveRelicTitle(ModelId id)
    {
        try { return ModelDb.GetByIdOrNull<RelicModel>(id)?.Title?.GetRawText() ?? Humanize(id); }
        catch { return Humanize(id); }
    }

    private static string ResolvePotionTitle(ModelId id)
    {
        try { return ModelDb.GetByIdOrNull<PotionModel>(id)?.Title?.GetRawText() ?? Humanize(id); }
        catch { return Humanize(id); }
    }

    private static string ResolveEncounterTitle(ModelId id)
    {
        try { return ModelDb.GetByIdOrNull<EncounterModel>(id)?.Title?.GetRawText() ?? Humanize(id); }
        catch { return Humanize(id); }
    }

    private static string ResolveEventTitle(ModelId id)
    {
        try { return ModelDb.GetByIdOrNull<EventModel>(id)?.Title?.GetRawText() ?? Humanize(id); }
        catch { return Humanize(id); }
    }

    private static string ResolveCharacterTitle(ModelId id)
    {
        try { return ModelDb.GetByIdOrNull<CharacterModel>(id)?.Title?.GetRawText() ?? Humanize(id); }
        catch { return Humanize(id); }
    }

    private sealed class PlayerInfo
    {
        public ulong Id;
        public string CharacterName = "?";
        public int CurrentDeckCount;
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
        public List<string> RelicsRemoved = new();
        public List<string> PotionsGained = new();
        public List<string> PotionsUsed = new();
        public List<string> UpgradedCards = new();
        public string ExtraNote = "";
    }

    private static string BuildHtml(RunHistory history)
    {
        var players = history.Players.Select(p => new PlayerInfo
        {
            Id = p.Id,
            CharacterName = ResolveCharacterTitle(p.Character),
            CurrentDeckCount = p.Deck.Count(),
        }).ToList();

        // Reconcile the running deck-size column against each player's
        // CURRENT deck count (as of right now — this run may still be in
        // progress) rather than assuming anything about how floor 1/Neow's
        // floor tags FloorAddedToDeck: startingSize = currentSize -
        // totalGained + totalRemoved, exact regardless of that tagging, and
        // self-corrects every time the report is regenerated mid-run.
        // MapPointHistory is nested one sub-list PER ACT (confirmed by the
        // compiler, not just assumed: List<List<MapPointHistoryEntry>>) —
        // same nesting as the on-disk .run file, apparently regardless of
        // whether it's read live or after serialization. Flatten so floor
        // numbers run continuously across the whole run, matching vanilla
        // Slay the Spire's own floor-numbering convention.
        var mapPoints = history.MapPointHistory.SelectMany(actEntries => actEntries).ToList();

        var totalGained = players.ToDictionary(p => p.Id, _ => 0);
        var totalRemoved = players.ToDictionary(p => p.Id, _ => 0);
        foreach (var mp in mapPoints)
        {
            foreach (var stat in mp.PlayerStats)
            {
                if (!totalGained.ContainsKey(stat.PlayerId)) continue;
                totalGained[stat.PlayerId] += stat.CardsGained.Count;
                totalRemoved[stat.PlayerId] += stat.CardsRemoved.Count;
            }
        }
        var runningDeckSize = players.ToDictionary(p => p.Id, p => p.CurrentDeckCount - totalGained[p.Id] + totalRemoved[p.Id]);
        var lastGold = players.ToDictionary(p => p.Id, _ => -1);

        var rowsByPlayer = players.ToDictionary(p => p.Id, _ => new List<FloorRow>());

        var floorNumber = 0;
        foreach (var mp in mapPoints)
        {
            floorNumber++;
            var room = mp.Rooms.FirstOrDefault();
            var (typeLabel, typeCss, icon, defaultName) = DescribeRoom(mp.MapPointType);
            var name = room?.ModelId != null
                ? (mp.MapPointType is MapPointType.Monster or MapPointType.Elite or MapPointType.Boss
                    ? ResolveEncounterTitle(room.ModelId)
                    : ResolveEventTitle(room.ModelId))
                : defaultName;
            var turnsTaken = room?.TurnsTaken ?? 0;

            foreach (var stat in mp.PlayerStats)
            {
                if (!rowsByPlayer.TryGetValue(stat.PlayerId, out var rows)) continue;

                runningDeckSize[stat.PlayerId] += stat.CardsGained.Count - stat.CardsRemoved.Count;

                var row = new FloorRow
                {
                    FloorNumber = floorNumber,
                    TypeLabel = typeLabel,
                    TypeCss = typeCss,
                    Icon = icon,
                    Name = name,
                    TurnsTaken = turnsTaken,
                    CurrentHp = stat.CurrentHp,
                    MaxHp = stat.MaxHp,
                    DamageTaken = stat.DamageTaken,
                    HpHealed = stat.HpHealed,
                    GoldBefore = lastGold[stat.PlayerId],
                    GoldAfter = stat.CurrentGold,
                    DeckSize = runningDeckSize[stat.PlayerId],
                };
                lastGold[stat.PlayerId] = row.GoldAfter;

                foreach (var c in stat.CardsGained)
                {
                    if (c.Id.Category.Equals("CURSE", StringComparison.OrdinalIgnoreCase)) row.Curses.Add(ResolveCardTitle(c.Id));
                    else row.CardsGained.Add(ResolveCardTitle(c.Id));
                }
                foreach (var c in stat.CardsRemoved) row.CardsRemoved.Add(ResolveCardTitle(c.Id));
                foreach (var id in stat.UpgradedCards) row.UpgradedCards.Add(ResolveCardTitle(id));

                foreach (var choice in stat.RelicChoices) if (choice.wasPicked) row.RelicsGained.Add(ResolveRelicTitle(choice.choice));
                foreach (var id in stat.BoughtRelics) row.RelicsGained.Add(ResolveRelicTitle(id));
                foreach (var id in stat.RelicsRemoved) row.RelicsRemoved.Add(ResolveRelicTitle(id));
                // AncientChoiceHistoryEntry.Title is already a resolved
                // LocString (unlike the raw .run file's ancient_choice.TextKey,
                // which needed guessing it was a relic id) — use it directly.
                foreach (var a in stat.AncientChoices) if (a.WasChosen) row.RelicsGained.Add(a.Title?.GetRawText() ?? Humanize(a.TextKey ?? ""));

                foreach (var choice in stat.PotionChoices) if (choice.wasPicked) row.PotionsGained.Add(ResolvePotionTitle(choice.choice));
                foreach (var id in stat.BoughtPotions) row.PotionsGained.Add(ResolvePotionTitle(id));
                foreach (var id in stat.PotionUsed) row.PotionsUsed.Add(ResolvePotionTitle(id));

                foreach (var id in stat.BoughtColorless) row.CardsGained.Add(ResolveCardTitle(id));

                var notes = new List<string>();
                if (mp.MapPointType == MapPointType.RestSite && stat.RestSiteChoices.Count > 0)
                    notes.Add(string.Join(", ", stat.RestSiteChoices.Select(Humanize)));
                // EventOptionHistoryEntry.Title is already resolved text —
                // this is the one place the live object gives us something
                // the raw .run file's {"key":...,"table":...} shape couldn't
                // without a separate localization system.
                if (stat.EventChoices.Count > 0)
                    notes.Add(string.Join(", ", stat.EventChoices.Select(e => e.Title?.GetRawText()).Where(t => !string.IsNullOrEmpty(t))));
                row.ExtraNote = string.Join(" — ", notes);

                rows.Add(row);
            }
        }

        return RenderHtml(history, players, rowsByPlayer);
    }

    private static (string label, string css, string icon, string defaultName) DescribeRoom(MapPointType mapPointType) => mapPointType switch
    {
        MapPointType.Monster => ("MONSTER", "monster", "⚔️", "Monster"),
        MapPointType.Elite => ("ELITE", "elite", "💀", "Elite"),
        MapPointType.Boss => ("BOSS", "boss", "👑", "Boss"),
        MapPointType.Shop => ("SHOP", "shop", "🛍️", "Shop"),
        MapPointType.Treasure => ("TREASURE", "treasure", "💰", "Treasure"),
        MapPointType.RestSite => ("REST", "rest", "🔥", "Rest Site"),
        MapPointType.Ancient => ("ANCIENT", "ancient", "❓", "Ancient"),
        MapPointType.Unknown => ("EVENT", "event", "❔", "Event"),
        _ => (mapPointType.ToString().ToUpperInvariant(), "event", "❔", mapPointType.ToString()),
    };

    private static string RenderHtml(RunHistory history, List<PlayerInfo> players, Dictionary<ulong, List<FloorRow>> rowsByPlayer)
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
        var runTimeSeconds = (int)history.RunTime;
        var statusText = history.WasAbandoned ? "In Progress / Abandoned" : history.Win ? "Victory" : "Defeat";
        sb.Append("<div class=\"summary\">")
          .Append(Escape(statusText)).Append(" &middot; Ascension ").Append(history.Ascension)
          .Append(" &middot; ").Append(runTimeSeconds / 60).Append("m ").Append(runTimeSeconds % 60).Append('s')
          .Append(" &middot; Seed ").Append(Escape(history.Seed ?? ""));
        if (!history.Win && history.KilledByEncounter != null)
            sb.Append(" &middot; Killed by ").Append(Escape(ResolveEncounterTitle(history.KilledByEncounter)));
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
                AppendPillGroup(sb, "Upgraded", row.UpgradedCards, "pill-card");
                AppendPillGroup(sb, "Curses", row.Curses, "pill-curse");
                AppendPillGroup(sb, "Removed", row.CardsRemoved, "pill-removed");
                AppendPillGroup(sb, "Relics", row.RelicsGained, "pill-relic");
                AppendPillGroup(sb, "Relics Removed", row.RelicsRemoved, "pill-removed");
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
