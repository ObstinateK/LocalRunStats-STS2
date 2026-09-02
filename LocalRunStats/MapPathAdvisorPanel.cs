using System;
using System.Collections.Generic;
using Godot;

namespace LocalRunStats;

// Small goal-toggle row added to the native map screen by
// MapPathHighlightPatch — lets the player switch which goal the
// highlighted "best remaining path" is optimized for.
public sealed partial class MapPathAdvisorPanel : Control
{
    // Locked in 2026-09-01 (second round of tuning — was (29, 186) before
    // that, (16, 16) originally). Tuning sliders removed once again once a
    // working spot was found — same pattern as the Damage HUD's own tuning
    // panel earlier in this mod.
    private static readonly Vector2 Offset = new(15f, 435f);

    private static readonly (MapPathAdvisor.Goal Goal, string Label)[] Goals =
    {
        (MapPathAdvisor.Goal.Elites, "Elites"),
        (MapPathAdvisor.Goal.Events, "Events"),
        (MapPathAdvisor.Goal.RestSites, "Upgrades"),
        (MapPathAdvisor.Goal.Shops, "Shops"),
        (MapPathAdvisor.Goal.Treasures, "Treasure"),
    };

    private readonly List<Button> _buttons = new();
    private MapPathAdvisor.Goal _selectedGoal;
    private Action<MapPathAdvisor.Goal> _onGoalSelected;

    public void Initialize(Control parent, MapPathAdvisor.Goal initialGoal, Action<MapPathAdvisor.Goal> onGoalSelected)
    {
        _selectedGoal = initialGoal;
        _onGoalSelected = onGoalSelected;
    }

    public override void _Ready()
    {
        TopLevel = true;
        MouseFilter = MouseFilterEnum.Ignore;
        ZIndex = 150;
        Position = Offset;

        var label = new Label { Text = "Best Path For:", Position = Vector2.Zero };
        label.AddThemeColorOverride("font_color", Colors.White);
        label.AddThemeFontSizeOverride("font_size", 12);
        AddChild(label);

        var y = 22f;
        foreach (var (goal, text) in Goals)
        {
            var button = new Button { Text = text, Position = new Vector2(0f, y), Size = new Vector2(100f, 26f) };
            button.Pressed += () => Select(goal);
            AddChild(button);
            _buttons.Add(button);
            y += 30f;
        }

        UpdateButtonStyles();
    }

    private void Select(MapPathAdvisor.Goal goal)
    {
        _selectedGoal = goal;
        UpdateButtonStyles();
        _onGoalSelected?.Invoke(goal);
    }

    private void UpdateButtonStyles()
    {
        for (var i = 0; i < Goals.Length; i++)
        {
            _buttons[i].Modulate = Goals[i].Goal == _selectedGoal ? Colors.White : new Color(1f, 1f, 1f, 0.55f);
        }
    }
}
