using System;
using System.Linq;
using System.Text;
using Godot;
using Log = MegaCrit.Sts2.Core.Logging.Log;

namespace LocalRunStats;

// Always-visible run-wide damage panel (map, combat, and reward screens
// alike — matches the reference screenshot, which shows it overlaid even on
// the card-choice screen). Parented to NRun via CombatDamageHudPatch, so it
// lives for the whole run, not just during combat.
//
// Multiplayer-aware: one row per Player.NetId that's dealt/taken damage this
// run (see CombatStatsListener.DamageByPlayer) — rows appear as each player
// deals/takes their first hit rather than being pre-populated, since there's
// no simple "enumerate every player in this run" access from here.
public sealed partial class CombatDamageHud : Control
{
    // Tuned live via the (now-removed) HudTuningPanel slider tool; locked in
    // 2026-08-31.
    private const float MarginRight = 189f;
    private const float MarginTop = 93f;
    private const float PanelWidth = 300f;
    private const float PanelHeight = 308f;

    private static CombatDamageHud _instance;

    private RichTextLabel _label;
    private Button _graphButton;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        // TopLevel makes Position an absolute global coordinate, not
        // parent/anchor-relative (confirmed live: anchors were silently
        // ignored and Position was used verbatim, landing off-screen at
        // x=-360). Compute the top-right corner from the actual viewport
        // size instead of relying on anchors.
        TopLevel = true;
        ZIndex = 100;
        ApplyGeometry();

        _label = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _label.AnchorRight = 1f;
        _label.AnchorBottom = 1f;
        _label.AddThemeColorOverride("default_color", new Color(0.85f, 0.9f, 1f));
        _label.AddThemeFontSizeOverride("normal_font_size", 12);
        _label.AddThemeFontSizeOverride("bold_font_size", 13);
        AddChild(_label);

        // Sits to the left of the "COMBAT DAMAGE TAKEN" header (top of the
        // panel — see BuildBbcode) — opens the graph overlay on click.
        _graphButton = new Button
        {
            Text = "\U0001F4C8", // chart-increasing emoji as a compact icon
            Position = new Vector2(-28f, 0f),
            Size = new Vector2(24f, 24f),
        };
        _graphButton.Pressed += StatsGraphOverlay.Toggle;
        AddChild(_graphButton);

        _instance = this;
        Refresh();

        var parent = GetParent();
        Log.Info($"[LocalRunStats] CombatDamageHud attached. Parent={parent?.Name} parentType={parent?.GetType().Name} " +
            $"parentSize={(parent as Control)?.Size} globalPos={GlobalPosition} size={Size} visible={IsVisibleInTree()}");
    }

    public override void _ExitTree()
    {
        if (_instance == this) _instance = null;
    }

    public static void RefreshAll()
    {
        if (GodotObject.IsInstanceValid(_instance)) _instance.Refresh();
    }

    private void ApplyGeometry()
    {
        var viewportSize = GetViewport().GetVisibleRect().Size;
        Position = new Vector2(viewportSize.X - PanelWidth - MarginRight, MarginTop);
        Size = new Vector2(PanelWidth, PanelHeight);
        CustomMinimumSize = Size;
    }

    private void Refresh()
    {
        _label.Text = BuildBbcode();
    }

    private static string BuildBbcode()
    {
        var players = CombatStatsListener.Instance.DamageByPlayer.Values.ToList();
        if (players.Count == 0) return "[b]COMBAT DAMAGE TAKEN[/b]\n(none yet)\n\n[b]COMBAT DAMAGE DEALT[/b]\n(none yet)";

        var maxActIndex = players.SelectMany(p => p.DealtByActIndex.Keys.Concat(p.TakenByActIndex.Keys)).DefaultIfEmpty(-1).Max();
        var acts = maxActIndex + 1; // count of acts reached so far, ActModel.Index is 0-based -> displayed as A1, A2, ...

        var sb = new StringBuilder();
        AppendTable(sb, "COMBAT DAMAGE TAKEN", players, acts, dealt: false);
        sb.Append('\n');
        AppendTable(sb, "COMBAT DAMAGE DEALT", players, acts, dealt: true);
        return sb.ToString();
    }

    private static void AppendTable(StringBuilder sb, string title, System.Collections.Generic.List<PlayerDamageTracker> players, int acts, bool dealt)
    {
        var columns = acts + 3; // name, FIGHT, A1..An, sigma
        sb.Append($"[b]{title}[/b]\n");
        sb.Append($"[table={columns}]");

        sb.Append("[cell][/cell][cell]FIGHT[/cell]");
        for (var a = 0; a < acts; a++) sb.Append($"[cell]A{a + 1}[/cell]");
        sb.Append("[cell]Σ[/cell]");

        foreach (var p in players)
        {
            var currentFight = dealt ? p.CurrentFightDealt : p.CurrentFightTaken;
            var byAct = dealt ? p.DealtByActIndex : p.TakenByActIndex;
            var total = dealt ? p.TotalDealt : p.TotalTaken;

            sb.Append($"[cell]{Escape(p.CharacterName)}[/cell]");
            sb.Append($"[cell]{currentFight}[/cell]");
            for (var a = 0; a < acts; a++)
            {
                byAct.TryGetValue(a, out var dmg);
                sb.Append($"[cell]{dmg}[/cell]");
            }
            sb.Append($"[cell]{total}[/cell]");
        }

        sb.Append("[/table]");
    }

    private static string Escape(string s) => s.Replace("[", "[lb]");
}
