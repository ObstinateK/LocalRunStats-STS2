using System.Collections.Generic;
using Godot;

namespace LocalRunStats;

// Turns & Cards tab panel: overall per-player card-play totals as plain
// BBCode text, followed by a per-fight breakdown. The per-fight breakdown
// used to be one long vertical list of "[b]Fight N[/b]" blocks — changed on
// request to lay fights out side-by-side instead, wrapping to a new row once
// a row runs out of width, like a table. Godot's HFlowContainer does exactly
// this natively (Godot's "flex-wrap" container), so each fight becomes its
// own small RichTextLabel child rather than more text appended to one big
// label.
public sealed partial class CardPlayCountsPanel : Control
{
    public string Title = "";

    private const float FightBlockMinWidth = 150f;

    private ScrollContainer _scroll;
    private RichTextLabel _overallLabel;
    private Label _byFightHeader;
    private HFlowContainer _fightFlow;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;

        // HorizontalScrollMode.Disabled is what actually makes this wrap: it
        // clamps the child's width to the ScrollContainer's own viewport
        // width instead of letting it grow as wide as it wants (with a
        // horizontal scrollbar to match). Without this, the HFlowContainer
        // below has no fixed width to wrap within, so its minimum size
        // collapses to fit just ONE child — which made it wrap after every
        // single fight block, i.e. look exactly like the one-long-list layout
        // this was meant to replace.
        _scroll = new ScrollContainer
        {
            MouseFilter = MouseFilterEnum.Stop,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        AddChild(_scroll);

        var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _scroll.AddChild(column);

        _overallLabel = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            MouseFilter = MouseFilterEnum.Stop,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _overallLabel.AddThemeColorOverride("default_color", Colors.White);
        _overallLabel.AddThemeFontSizeOverride("normal_font_size", 12);
        _overallLabel.AddThemeFontSizeOverride("bold_font_size", 13);
        column.AddChild(_overallLabel);

        _byFightHeader = new Label { Text = "--- By Fight ---" };
        _byFightHeader.AddThemeColorOverride("font_color", Colors.White);
        column.AddChild(_byFightHeader);

        _fightFlow = new HFlowContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddChild(_fightFlow);

        LayoutScroll();
    }

    public override void _Notification(int what)
    {
        if (what == (long)NotificationResized) LayoutScroll();
    }

    private void LayoutScroll()
    {
        if (_scroll == null) return;
        _scroll.Position = new Vector2(4f, 20f);
        _scroll.Size = new Vector2(Size.X - 8f, Size.Y - 24f);
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(1f, 1f, 1f, 0.05f));
        var font = ThemeDB.FallbackFont;
        DrawString(font, new Vector2(4f, 14f), Title, HorizontalAlignment.Left, -1f, 14, Colors.White);
    }

    public void SetData(string overallBbcode, IReadOnlyList<string> fightBlocks)
    {
        _overallLabel.Text = overallBbcode;
        _byFightHeader.Visible = fightBlocks.Count > 0;

        foreach (var child in _fightFlow.GetChildren())
        {
            _fightFlow.RemoveChild(child);
            child.QueueFree();
        }

        foreach (var block in fightBlocks)
        {
            var label = new RichTextLabel
            {
                BbcodeEnabled = true,
                FitContent = true,
                ScrollActive = false,
                CustomMinimumSize = new Vector2(FightBlockMinWidth, 0f),
                MouseFilter = MouseFilterEnum.Stop,
                Text = block,
            };
            label.AddThemeColorOverride("default_color", Colors.White);
            label.AddThemeFontSizeOverride("normal_font_size", 11);
            label.AddThemeFontSizeOverride("bold_font_size", 12);
            _fightFlow.AddChild(label);
        }

        QueueRedraw();
    }
}
