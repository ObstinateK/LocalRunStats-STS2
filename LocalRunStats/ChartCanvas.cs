using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace LocalRunStats;

// Minimal custom-drawn chart — Godot has no built-in charting Control, so
// this hand-rolls one. Two modes sharing the same axis/legend layout:
// grouped bars (one group per XLabel entry, one bar per player) for
// discrete per-stage data, or a connected line per player for continuous
// cumulative-over-time data. Hover over a bar or a line point to see its
// exact value in a small tooltip.
//
// _Draw() is used ONLY for the bars/lines/background rect — none of those
// need a font. All TEXT (title, legend, axis labels, tooltip) is real Label
// child nodes instead of DrawString calls. This is the fix for "font
// doesn't match the game" (reported live, shelved, revisited): a
// DrawString call has no theme to inherit from — two earlier attempts to
// manually guess the right Font resource for it (ThemeDB.GetProjectTheme()
// ?.DefaultFont, then GetThemeFont("font","Label")) both silently changed
// nothing. A real Label added to this same live scene tree resolves the
// game's actual font automatically via normal Godot theme cascade — proven
// already working elsewhere in this mod (StatsGraphOverlay's title Label,
// CombatDamageHud's labels never needed any special font code at all) — so
// this makes the chart's text go through the same mechanism instead of
// trying to replicate it manually.
public sealed partial class ChartCanvas : Control
{
    public string Title = "";

    private PlayerStatsLog.ChartData _data = new();
    private bool _isLine;
    private Vector2? _hoverPos;

    private Label _titleLabel;
    private Label _noDataLabel;
    private readonly List<ColorRect> _legendSwatches = new();
    private readonly List<Label> _legendLabels = new();
    private readonly List<Label> _xAxisLabels = new();
    private ColorRect _tooltipBg;
    private Label _tooltipLabel;

    // Populated by Recompute(), drawn as-is by _Draw() — no font involved.
    private readonly List<(Rect2 Rect, Color Color)> _barRects = new();
    private readonly List<(Vector2[] Points, Color Color)> _lineSeries = new();

    // Also populated by Recompute(), hit-tested against _hoverPos to find
    // what to show a tooltip for. Bars use rect containment; line points
    // use nearest-within-radius, since a point has no area of its own.
    private readonly List<(Rect2 Rect, string Tooltip)> _barHitboxes = new();
    private readonly List<(Vector2 Point, string Tooltip)> _lineHitboxes = new();

    private static readonly Color[] Palette =
    {
        new Color(0.35f, 0.65f, 1f),
        new Color(1f, 0.55f, 0.35f),
        new Color(0.55f, 1f, 0.55f),
        new Color(1f, 0.85f, 0.35f),
    };

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        // MouseExited is a signal, not an overridable virtual, on Control.
        MouseExited += () =>
        {
            _hoverPos = null;
            UpdateTooltip();
        };
        Resized += Recompute;

        _titleLabel = new Label { Text = Title, Position = new Vector2(4f, 0f), MouseFilter = MouseFilterEnum.Ignore };
        _titleLabel.AddThemeFontSizeOverride("font_size", 14);
        _titleLabel.AddThemeColorOverride("font_color", NativeTooltipStyle.TitleGold);
        AddChild(_titleLabel);

        _noDataLabel = new Label { Text = "(no data yet)", Visible = false, MouseFilter = MouseFilterEnum.Ignore };
        _noDataLabel.AddThemeFontSizeOverride("font_size", 12);
        _noDataLabel.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f, 0.5f));
        AddChild(_noDataLabel);

        _tooltipBg = new ColorRect { Visible = false, MouseFilter = MouseFilterEnum.Ignore, Color = new Color(0f, 0f, 0f, 0.85f) };
        AddChild(_tooltipBg);
        _tooltipLabel = new Label { Visible = false, MouseFilter = MouseFilterEnum.Ignore };
        _tooltipLabel.AddThemeFontSizeOverride("font_size", 11);
        _tooltipLabel.AddThemeColorOverride("font_color", Colors.White);
        AddChild(_tooltipLabel);

        Recompute();
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion motion)
        {
            _hoverPos = motion.Position;
            UpdateTooltip();
        }
    }

    public void SetData(PlayerStatsLog.ChartData data, bool isLine)
    {
        _data = data ?? new PlayerStatsLog.ChartData();
        _isLine = isLine;
        Recompute();
    }

    // Rebuilds every text child (legend, axis labels) and the draw-only bar/
    // line geometry. Called on new data (SetData) and on resize (this
    // Control's Size changes whenever StatsGraphOverlay lays its charts
    // out), since label positions and bar/line geometry both depend on it.
    private void Recompute()
    {
        if (_titleLabel == null) return; // not _Ready yet

        foreach (var swatch in _legendSwatches) swatch.QueueFree();
        foreach (var label in _legendLabels) label.QueueFree();
        foreach (var label in _xAxisLabels) label.QueueFree();
        _legendSwatches.Clear();
        _legendLabels.Clear();
        _xAxisLabels.Clear();
        _barRects.Clear();
        _lineSeries.Clear();
        _barHitboxes.Clear();
        _lineHitboxes.Clear();

        var playerNames = _data.SeriesByPlayer.Keys.ToList();
        if (_data.XLabels.Count == 0 || playerNames.Count == 0)
        {
            _noDataLabel.Visible = true;
            _noDataLabel.Position = new Vector2(4f, Size.Y / 2f - 8f);
            QueueRedraw();
            UpdateTooltip();
            return;
        }
        _noDataLabel.Visible = false;

        // Legend
        var lx = 4f;
        for (var i = 0; i < playerNames.Count; i++)
        {
            var swatch = new ColorRect { Position = new Vector2(lx, 20f), Size = new Vector2(8f, 8f), Color = Palette[i % Palette.Length], MouseFilter = MouseFilterEnum.Ignore };
            AddChild(swatch);
            _legendSwatches.Add(swatch);

            var label = new Label { Text = playerNames[i], Position = new Vector2(lx + 11f, 20f), MouseFilter = MouseFilterEnum.Ignore };
            label.AddThemeFontSizeOverride("font_size", 10);
            label.AddThemeColorOverride("font_color", Colors.White);
            AddChild(label);
            _legendLabels.Add(label);

            lx += 11f + playerNames[i].Length * 6f + 14f;
        }

        var chartTop = 36f;
        var chartBottom = Size.Y - 16f;
        var chartHeight = MathF.Max(1f, chartBottom - chartTop);
        var chartLeft = 4f;
        var chartRight = Size.X - 4f;
        var chartWidth = MathF.Max(1f, chartRight - chartLeft);

        var maxValue = _data.SeriesByPlayer.Values.SelectMany(v => v).DefaultIfEmpty(0f).Max();
        if (maxValue <= 0f) maxValue = 1f;

        var groupCount = _data.XLabels.Count;
        var groupWidth = chartWidth / groupCount;

        // Down-sample x labels if there are too many to read (cumulative mode
        // can have one entry per fight across a whole run). Centered, not
        // left-aligned: both bar groups and line points are positioned at
        // the group's horizontal CENTER (g*groupWidth + groupWidth*0.5 —
        // see RecomputeBars/RecomputeLines below), so a left-aligned label
        // at g*groupWidth would sit under the START of the group instead of
        // under the actual bar/point.
        var labelEvery = Math.Max(1, groupCount / 20);
        for (var g = 0; g < groupCount; g++)
        {
            if (g % labelEvery != 0) continue;
            var label = new Label
            {
                Text = _data.XLabels[g],
                Position = new Vector2(chartLeft + g * groupWidth, chartBottom + 10f),
                Size = new Vector2(groupWidth, 14f),
                HorizontalAlignment = HorizontalAlignment.Center,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            label.AddThemeFontSizeOverride("font_size", 9);
            label.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f, 0.7f));
            AddChild(label);
            _xAxisLabels.Add(label);
        }

        if (_isLine) RecomputeLines(playerNames, chartLeft, chartBottom, chartHeight, groupWidth, groupCount, maxValue);
        else RecomputeBars(playerNames, chartLeft, chartBottom, chartHeight, groupWidth, groupCount, maxValue);

        QueueRedraw();
        UpdateTooltip();
    }

    private void RecomputeBars(List<string> playerNames, float chartLeft, float chartBottom, float chartHeight, float groupWidth, int groupCount, float maxValue)
    {
        var barWidth = groupWidth / (playerNames.Count + 1);
        for (var g = 0; g < groupCount; g++)
        {
            for (var pIdx = 0; pIdx < playerNames.Count; pIdx++)
            {
                var series = _data.SeriesByPlayer[playerNames[pIdx]];
                if (g >= series.Count) continue;
                var value = series[g];
                var barHeight = chartHeight * (value / maxValue);
                var x = chartLeft + g * groupWidth + pIdx * barWidth + barWidth * 0.1f;
                var rect = new Rect2(x, chartBottom - barHeight, barWidth * 0.8f, barHeight);
                _barRects.Add((rect, Palette[pIdx % Palette.Length]));
                _barHitboxes.Add((rect, $"{playerNames[pIdx]}\n{_data.XLabels[g]}: {value:0}"));
            }
        }
    }

    private void RecomputeLines(List<string> playerNames, float chartLeft, float chartBottom, float chartHeight, float groupWidth, int groupCount, float maxValue)
    {
        for (var pIdx = 0; pIdx < playerNames.Count; pIdx++)
        {
            var series = _data.SeriesByPlayer[playerNames[pIdx]];
            if (series.Count < 2) continue;

            var count = Math.Min(series.Count, groupCount);
            var points = new Vector2[count];
            for (var g = 0; g < count; g++)
            {
                var x = chartLeft + g * groupWidth + groupWidth * 0.5f;
                var y = chartBottom - chartHeight * (series[g] / maxValue);
                points[g] = new Vector2(x, y);
                _lineHitboxes.Add((points[g], $"{playerNames[pIdx]}\n{_data.XLabels[g]}: {series[g]:0}"));
            }

            _lineSeries.Add((points, Palette[pIdx % Palette.Length]));
        }
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(1f, 1f, 1f, 0.05f));
        foreach (var (rect, color) in _barRects) DrawRect(rect, color);
        foreach (var (points, color) in _lineSeries) DrawPolyline(points, color, 2f, antialiased: true);
    }

    private void UpdateTooltip()
    {
        if (_tooltipBg == null) return; // not _Ready yet

        string tooltip = null;
        var anchor = Vector2.Zero;

        if (_hoverPos.HasValue)
        {
            var hover = _hoverPos.Value;
            anchor = hover;
            if (_isLine)
            {
                var bestDist = 10f; // pixel hit radius
                foreach (var (point, text) in _lineHitboxes)
                {
                    var d = point.DistanceTo(hover);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        tooltip = text;
                        anchor = point;
                    }
                }
            }
            else
            {
                foreach (var (rect, text) in _barHitboxes)
                {
                    if (rect.HasPoint(hover))
                    {
                        tooltip = text;
                        anchor = new Vector2(rect.Position.X + rect.Size.X / 2f, rect.Position.Y);
                        break;
                    }
                }
            }
        }

        if (tooltip == null)
        {
            _tooltipBg.Visible = false;
            _tooltipLabel.Visible = false;
            return;
        }

        _tooltipLabel.Text = tooltip;
        var font = _tooltipLabel.GetThemeFont("font");
        var fontSize = _tooltipLabel.GetThemeFontSize("font_size");
        var lines = tooltip.Split('\n');
        var maxLineWidth = 0f;
        foreach (var line in lines) maxLineWidth = MathF.Max(maxLineWidth, font.GetStringSize(line, HorizontalAlignment.Left, -1, fontSize).X);
        var boxSize = new Vector2(maxLineWidth + 10f, lines.Length * 14f + 6f);

        var boxPos = anchor + new Vector2(8f, -boxSize.Y - 6f);
        boxPos.X = Mathf.Clamp(boxPos.X, 0f, Size.X - boxSize.X);
        boxPos.Y = Mathf.Clamp(boxPos.Y, 0f, Size.Y - boxSize.Y);

        _tooltipBg.Position = boxPos;
        _tooltipBg.Size = boxSize;
        _tooltipBg.Visible = true;

        _tooltipLabel.Position = boxPos + new Vector2(5f, 3f);
        _tooltipLabel.Size = boxSize - new Vector2(10f, 6f);
        _tooltipLabel.Visible = true;
    }
}
