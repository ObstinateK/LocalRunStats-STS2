using Godot;

namespace LocalRunStats;

// Simple BBCode text panel for the Turns & Cards tab, sitting alongside the
// Turns/Cards charts — a per-card play-count breakdown (which cards, how many
// times) has no natural x-axis, so it reads better as a scrollable text table
// than as another bar chart.
public sealed partial class CardPlayCountsPanel : Control
{
    public string Title = "";

    private RichTextLabel _label;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;

        _label = new RichTextLabel
        {
            BbcodeEnabled = true,
            ScrollActive = true,
            MouseFilter = MouseFilterEnum.Stop,
        };
        _label.AddThemeColorOverride("default_color", Colors.White);
        _label.AddThemeFontSizeOverride("normal_font_size", 12);
        _label.AddThemeFontSizeOverride("bold_font_size", 13);
        AddChild(_label);

        LayoutLabel();
    }

    public override void _Notification(int what)
    {
        if (what == (long)NotificationResized) LayoutLabel();
    }

    private void LayoutLabel()
    {
        if (_label == null) return;
        _label.Position = new Vector2(4f, 20f);
        _label.Size = new Vector2(Size.X - 8f, Size.Y - 24f);
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(1f, 1f, 1f, 0.05f));
        var font = ThemeDB.FallbackFont;
        DrawString(font, new Vector2(4f, 14f), Title, HorizontalAlignment.Left, -1f, 14, Colors.White);
    }

    public void SetBbcode(string bbcode)
    {
        _label.Text = bbcode;
        QueueRedraw();
    }
}
