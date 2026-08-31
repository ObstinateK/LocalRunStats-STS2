using Godot;
using Log = MegaCrit.Sts2.Core.Logging.Log;

namespace LocalRunStats;

// Dev-only slider panel for HudTuning — see that file's doc comment. Placed
// on the left side (out of the way of the HUD panel it's tuning, which sits
// top-right) so both are visible and adjustable at once.
public sealed partial class HudTuningPanel : Control
{
    private static bool _attached;

    public static void EnsureAttached(Node root)
    {
        if (_attached) return;
        _attached = true;
        var panel = new HudTuningPanel { Name = "LocalRunStatsHudTuningPanel" };
        root.AddChild(panel);
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        TopLevel = true;
        Position = new Vector2(16f, 16f);
        Size = new Vector2(300f, 260f);

        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Stop };
        box.AnchorRight = 1f;
        box.AnchorBottom = 1f;
        AddChild(box);

        var bg = new ColorRect { Color = new Color(0, 0, 0, 0.65f), MouseFilter = MouseFilterEnum.Ignore };
        bg.AnchorRight = 1f;
        bg.AnchorBottom = 1f;
        AddChild(bg);
        MoveChild(bg, 0);

        // Graph panel geometry (GraphX/Y/Width/Height) was tuned once and is
        // now fixed in StatsGraphOverlay — no sliders for it here anymore.
        // Damage HUD position/size is still being adjusted, so those stay.
        AddSectionLabel(box, "Damage HUD (top-right panel)");
        AddSlider(box, "MarginRight", 0f, 800f, HudTuning.MarginRight, v => HudTuning.MarginRight = v);
        AddSlider(box, "MarginTop", 0f, 600f, HudTuning.MarginTop, v => HudTuning.MarginTop = v);
        AddSlider(box, "Width", 100f, 800f, HudTuning.Width, v => HudTuning.Width = v);
        AddSlider(box, "Height", 60f, 500f, HudTuning.Height, v => HudTuning.Height = v);

        var logButton = new Button { Text = "Log Values" };
        logButton.Pressed += () => Log.Info(
            $"[LocalRunStats] HUD TUNED VALUES: MarginRight={HudTuning.MarginRight} MarginTop={HudTuning.MarginTop} Width={HudTuning.Width} Height={HudTuning.Height}");
        box.AddChild(logButton);
    }

    private static void AddSectionLabel(VBoxContainer box, string text)
    {
        var lbl = new Label { Text = text };
        lbl.AddThemeColorOverride("font_color", new Color(0.7f, 0.9f, 1f));
        box.AddChild(lbl);
    }

    private static void AddSlider(VBoxContainer box, string label, float min, float max, float initial, System.Action<float> onChanged)
    {
        var row = new VBoxContainer();
        var lbl = new Label { Text = label };
        lbl.AddThemeColorOverride("font_color", Colors.White);
        row.AddChild(lbl);

        var slider = new HSlider { MinValue = min, MaxValue = max, Value = initial, Step = 1 };
        slider.CustomMinimumSize = new Vector2(260f, 0f);
        slider.ValueChanged += v =>
        {
            onChanged((float)v);
            HudTuning.RaiseChanged();
        };
        row.AddChild(slider);

        box.AddChild(row);
    }
}
