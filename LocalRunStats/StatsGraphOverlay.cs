using System.Collections.Generic;
using System.Linq;
using Godot;

namespace LocalRunStats;

// Full-panel graph view toggled by the button on CombatDamageHud. Not using
// NOverlayStack/IOverlayScreen (the "native" way to do a full-screen panel
// per the modding notes) — this mod hasn't touched that API yet, so it stays
// with the same TopLevel-Control-toggled-by-Visible approach already proven
// to work elsewhere in this mod, just at a higher ZIndex and with
// MouseFilter.Stop so it actually blocks clicks to whatever's behind it
// while open.
public sealed partial class StatsGraphOverlay : Control
{
    // Locked in 2026-08-29 (centered default, never actually re-tuned).
    private const float GraphWidth = 820f;
    private const float GraphHeight = 680f;

    private enum StatTab { DamageGold, TurnsCards }

    private static StatsGraphOverlay _instance;

    private StatTab _activeTab = StatTab.DamageGold;
    private bool _perStage = true;
    private int? _actFilter; // null = all acts

    private ColorRect _bg;
    private Button _closeButton;
    private Button _damageGoldTabButton;
    private Button _turnsCardsTabButton;
    private Button _perStageButton;
    private Button _cumulativeButton;
    private HBoxContainer _actFilterRow;

    private ChartCanvas _dealtChart;
    private ChartCanvas _takenChart;
    private ChartCanvas _goldChart;
    private ChartCanvas _turnsChart;
    private ChartCanvas _cardsChart;

    public static void EnsureAttached(Node root)
    {
        if (GodotObject.IsInstanceValid(_instance)) return;
        var overlay = new StatsGraphOverlay { Name = "LocalRunStatsGraphOverlay" };
        root.AddChild(overlay);
    }

    public static void Toggle()
    {
        if (!GodotObject.IsInstanceValid(_instance)) return;
        _instance.Visible = !_instance.Visible;
        if (_instance.Visible) _instance.Refresh();
    }

    public override void _Ready()
    {
        TopLevel = true;
        MouseFilter = MouseFilterEnum.Stop;
        Visible = false;
        ZIndex = 200;

        _bg = new ColorRect { Color = new Color(0.05f, 0.05f, 0.08f, 0.97f), MouseFilter = MouseFilterEnum.Stop };
        AddChild(_bg);

        var title = new Label { Text = "Run Stats", Position = new Vector2(16f, 8f) };
        title.AddThemeColorOverride("font_color", Colors.White);
        title.AddThemeFontSizeOverride("font_size", 20);
        AddChild(title);

        _closeButton = new Button { Text = "X", Size = new Vector2(32f, 32f) };
        _closeButton.Pressed += () => Visible = false;
        AddChild(_closeButton);

        _damageGoldTabButton = new Button { Text = "Damage & Gold", Position = new Vector2(16f, 44f), Size = new Vector2(140f, 28f) };
        _turnsCardsTabButton = new Button { Text = "Turns & Cards", Position = new Vector2(164f, 44f), Size = new Vector2(140f, 28f) };
        _damageGoldTabButton.Pressed += () => SetTab(StatTab.DamageGold);
        _turnsCardsTabButton.Pressed += () => SetTab(StatTab.TurnsCards);
        AddChild(_damageGoldTabButton);
        AddChild(_turnsCardsTabButton);

        _perStageButton = new Button { Text = "Per Stage", Position = new Vector2(16f, 80f), Size = new Vector2(120f, 28f) };
        _cumulativeButton = new Button { Text = "Cumulative", Position = new Vector2(144f, 80f), Size = new Vector2(120f, 28f) };
        _perStageButton.Pressed += () => SetMode(true);
        _cumulativeButton.Pressed += () => SetMode(false);
        AddChild(_perStageButton);
        AddChild(_cumulativeButton);

        // Rebuilt on every Refresh (act list grows as the run progresses).
        _actFilterRow = new HBoxContainer { Position = new Vector2(16f, 114f) };
        AddChild(_actFilterRow);

        _dealtChart = new ChartCanvas { Title = "Damage Dealt" };
        _takenChart = new ChartCanvas { Title = "Damage Taken" };
        _goldChart = new ChartCanvas { Title = "Gold" };
        _turnsChart = new ChartCanvas { Title = "Turns Taken" };
        _cardsChart = new ChartCanvas { Title = "Cards Played" };
        AddChild(_dealtChart);
        AddChild(_takenChart);
        AddChild(_goldChart);
        AddChild(_turnsChart);
        AddChild(_cardsChart);

        ApplyGeometry();

        _instance = this;
        UpdateModeButtonStyles();
        UpdateTabButtonStyles();
        Refresh();
    }

    public override void _ExitTree()
    {
        if (_instance == this) _instance = null;
    }

    private IReadOnlyList<ChartCanvas> ActiveCharts => _activeTab == StatTab.DamageGold
        ? new[] { _dealtChart, _takenChart, _goldChart }
        : new[] { _turnsChart, _cardsChart };

    private void ApplyGeometry()
    {
        var viewportSize = GetViewport().GetVisibleRect().Size;
        var w = GraphWidth;
        var h = GraphHeight;
        var x = (viewportSize.X - w) / 2f;
        var y = (viewportSize.Y - h) / 2f;
        Position = new Vector2(x, y);
        Size = new Vector2(w, h);

        _bg.Size = Size;
        _closeButton.Position = new Vector2(w - 44f, 8f);

        var chartTop = 158f; // room for tab row (44) + mode buttons (80) + act filter row (114)
        var chartWidth = w - 32f;

        var active = ActiveCharts;
        var chartHeight = System.MathF.Max(60f, (h - chartTop - 16f) / active.Count - 8f);

        var allCharts = new[] { _dealtChart, _takenChart, _goldChart, _turnsChart, _cardsChart };
        foreach (var chart in allCharts) chart.Visible = active.Contains(chart);

        for (var i = 0; i < active.Count; i++)
        {
            active[i].Position = new Vector2(16f, chartTop + i * (chartHeight + 12f));
            active[i].Size = new Vector2(chartWidth, chartHeight);
            active[i].QueueRedraw();
        }
    }

    private void SetTab(StatTab tab)
    {
        _activeTab = tab;
        UpdateTabButtonStyles();
        ApplyGeometry();
        Refresh();
    }

    private void UpdateTabButtonStyles()
    {
        _damageGoldTabButton.Modulate = _activeTab == StatTab.DamageGold ? Colors.White : new Color(1f, 1f, 1f, 0.55f);
        _turnsCardsTabButton.Modulate = _activeTab == StatTab.TurnsCards ? Colors.White : new Color(1f, 1f, 1f, 0.55f);
    }

    private void SetMode(bool perStage)
    {
        _perStage = perStage;
        UpdateModeButtonStyles();
        Refresh();
    }

    private void UpdateModeButtonStyles()
    {
        _perStageButton.Modulate = _perStage ? Colors.White : new Color(1f, 1f, 1f, 0.55f);
        _cumulativeButton.Modulate = _perStage ? new Color(1f, 1f, 1f, 0.55f) : Colors.White;
    }

    private void RebuildActFilterButtons()
    {
        foreach (var child in _actFilterRow.GetChildren()) child.QueueFree();

        var availableActs = PlayerStatsLog.GetAvailableActs();
        AddActFilterButton("All", null);
        foreach (var actIndex in availableActs)
        {
            AddActFilterButton($"A{actIndex + 1}", actIndex);
        }

        // If the currently-selected act filter no longer has any data (e.g.
        // fresh run), fall back to All instead of showing an empty chart set.
        if (_actFilter.HasValue && !availableActs.Contains(_actFilter.Value))
        {
            _actFilter = null;
        }
    }

    private void AddActFilterButton(string label, int? actIndex)
    {
        var button = new Button { Text = label, CustomMinimumSize = new Vector2(48f, 24f) };
        button.Modulate = _actFilter == actIndex ? Colors.White : new Color(1f, 1f, 1f, 0.55f);
        button.Pressed += () =>
        {
            _actFilter = actIndex;
            Refresh();
        };
        _actFilterRow.AddChild(button);
    }

    private void Refresh()
    {
        RebuildActFilterButtons();

        // Per Stage is one bar per individual fight/event -> bars. Cumulative
        // is a running total over time -> a line reads better than one bar
        // per fight for a whole run's worth of points.
        var isLine = !_perStage;

        if (_activeTab == StatTab.DamageGold)
        {
            _dealtChart.SetData(PlayerStatsLog.BuildDamageChartData(_perStage, dealt: true, _actFilter), isLine);
            _takenChart.SetData(PlayerStatsLog.BuildDamageChartData(_perStage, dealt: false, _actFilter), isLine);
            _goldChart.SetData(PlayerStatsLog.BuildGoldChartData(_perStage, _actFilter), isLine);
        }
        else
        {
            _turnsChart.SetData(PlayerStatsLog.BuildTurnsChartData(_perStage, _actFilter), isLine);
            _cardsChart.SetData(PlayerStatsLog.BuildCardsPlayedChartData(_perStage, _actFilter), isLine);
        }
    }
}
