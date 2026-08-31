using System.Collections.Generic;
using System.Linq;
using Godot;

namespace LocalRunStats;

// Minimal custom-drawn chart — Godot has no built-in charting Control, so
// this hand-rolls one via _Draw(). Two modes sharing the same axis/legend
// layout: grouped bars (one group per XLabel entry, one bar per player) for
// discrete per-stage data, or a connected line per player for continuous
// cumulative-over-time data. Hover over a bar or a line point to see its
// exact value in a small tooltip.
public sealed partial class ChartCanvas : Control
{
    public string Title = "";

    private PlayerStatsLog.ChartData _data = new();
    private bool _isLine;
    private Vector2? _hoverPos;

    // Rebuilt every _Draw() call, hit-tested against _hoverPos to find what
    // to show a tooltip for. Bars use rect containment; line points use
    // nearest-within-radius, since a point has no area of its own.
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
            QueueRedraw();
        };
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion motion)
        {
            _hoverPos = motion.Position;
            QueueRedraw();
        }
    }

    public void SetData(PlayerStatsLog.ChartData data, bool isLine)
    {
        _data = data ?? new PlayerStatsLog.ChartData();
        _isLine = isLine;
        QueueRedraw();
    }

    public override void _Draw()
    {
        _barHitboxes.Clear();
        _lineHitboxes.Clear();

        var font = ThemeDB.FallbackFont;
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(1f, 1f, 1f, 0.05f));
        DrawString(font, new Vector2(4f, 14f), Title, HorizontalAlignment.Left, -1f, 14, Colors.White);

        var playerNames = _data.SeriesByPlayer.Keys.ToList();
        if (_data.XLabels.Count == 0 || playerNames.Count == 0)
        {
            DrawString(font, new Vector2(4f, Size.Y / 2f), "(no data yet)", HorizontalAlignment.Left, -1f, 12, new Color(1f, 1f, 1f, 0.5f));
            return;
        }

        // Legend
        var lx = 4f;
        for (var i = 0; i < playerNames.Count; i++)
        {
            DrawRect(new Rect2(lx, 20f, 8f, 8f), Palette[i % Palette.Length]);
            DrawString(font, new Vector2(lx + 11f, 28f), playerNames[i], HorizontalAlignment.Left, -1f, 10, Colors.White);
            lx += 11f + playerNames[i].Length * 6f + 14f;
        }

        var chartTop = 36f;
        var chartBottom = Size.Y - 16f;
        var chartHeight = System.MathF.Max(1f, chartBottom - chartTop);
        var chartLeft = 4f;
        var chartRight = Size.X - 4f;
        var chartWidth = System.MathF.Max(1f, chartRight - chartLeft);

        var maxValue = _data.SeriesByPlayer.Values.SelectMany(v => v).DefaultIfEmpty(0f).Max();
        if (maxValue <= 0f) maxValue = 1f;

        var groupCount = _data.XLabels.Count;
        var groupWidth = chartWidth / groupCount;

        // Down-sample x labels if there are too many to read (cumulative mode
        // can have one entry per fight across a whole run).
        // Centered, not left-aligned: both bar groups and line points are
        // positioned at the group's horizontal CENTER (g*groupWidth +
        // groupWidth*0.5 — see DrawBars/DrawLines below), so a left-aligned
        // label at g*groupWidth used to sit under the START of the group
        // instead of under the actual bar/point, making the axis look
        // shifted relative to the data it was labeling.
        var labelEvery = System.Math.Max(1, groupCount / 20);
        for (var g = 0; g < groupCount; g++)
        {
            if (g % labelEvery != 0) continue;
            DrawString(font, new Vector2(chartLeft + g * groupWidth, chartBottom + 12f), _data.XLabels[g],
                HorizontalAlignment.Center, groupWidth, 9, new Color(1f, 1f, 1f, 0.7f));
        }

        if (_isLine)
        {
            DrawLines(playerNames, chartLeft, chartBottom, chartHeight, groupWidth, groupCount, maxValue);
        }
        else
        {
            DrawBars(playerNames, chartLeft, chartBottom, chartHeight, groupWidth, groupCount, maxValue);
        }

        DrawHoverTooltip(font);
    }

    private void DrawBars(List<string> playerNames, float chartLeft, float chartBottom, float chartHeight, float groupWidth, int groupCount, float maxValue)
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
                DrawRect(rect, Palette[pIdx % Palette.Length]);
                _barHitboxes.Add((rect, $"{playerNames[pIdx]}\n{_data.XLabels[g]}: {value:0}"));
            }
        }
    }

    private void DrawLines(List<string> playerNames, float chartLeft, float chartBottom, float chartHeight, float groupWidth, int groupCount, float maxValue)
    {
        for (var pIdx = 0; pIdx < playerNames.Count; pIdx++)
        {
            var series = _data.SeriesByPlayer[playerNames[pIdx]];
            if (series.Count < 2) continue;

            var count = System.Math.Min(series.Count, groupCount);
            var points = new Vector2[count];
            for (var g = 0; g < count; g++)
            {
                var x = chartLeft + g * groupWidth + groupWidth * 0.5f;
                var y = chartBottom - chartHeight * (series[g] / maxValue);
                points[g] = new Vector2(x, y);
                _lineHitboxes.Add((points[g], $"{playerNames[pIdx]}\n{_data.XLabels[g]}: {series[g]:0}"));
            }

            DrawPolyline(points, Palette[pIdx % Palette.Length], 2f, antialiased: true);
        }
    }

    private void DrawHoverTooltip(Font font)
    {
        if (!_hoverPos.HasValue) return;
        var hover = _hoverPos.Value;

        string tooltip = null;
        var anchor = hover;

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

        if (tooltip == null) return;

        var lines = tooltip.Split('\n');
        var maxLineWidth = 0f;
        foreach (var line in lines) maxLineWidth = System.MathF.Max(maxLineWidth, font.GetStringSize(line, HorizontalAlignment.Left, -1, 11).X);
        var boxSize = new Vector2(maxLineWidth + 10f, lines.Length * 14f + 6f);

        var boxPos = anchor + new Vector2(8f, -boxSize.Y - 6f);
        boxPos.X = Mathf.Clamp(boxPos.X, 0f, Size.X - boxSize.X);
        boxPos.Y = Mathf.Clamp(boxPos.Y, 0f, Size.Y - boxSize.Y);

        DrawRect(new Rect2(boxPos, boxSize), new Color(0f, 0f, 0f, 0.85f));
        DrawRect(new Rect2(boxPos, boxSize), new Color(1f, 1f, 1f, 0.3f), filled: false);
        for (var i = 0; i < lines.Length; i++)
        {
            DrawString(font, boxPos + new Vector2(5f, 14f + i * 14f), lines[i], HorizontalAlignment.Left, -1f, 11, Colors.White);
        }
    }
}
