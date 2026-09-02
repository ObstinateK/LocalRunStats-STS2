using Godot;
using MegaCrit.Sts2.Core.Logging;

namespace LocalRunStats;

// Reuses the game's OWN native tooltip background — the dark-navy,
// teal-bordered box shown for keyword tooltips like "Block", and already
// used for this mod's own Card Stats tooltip via NHoverTipSet — for other
// custom panels in this mod, instead of hand-picking colors to approximate
// it. Requested: "make the graph overlay look like [the native tooltip
// box] in the image."
//
// res://scenes/ui/hover_tip.tscn is the scene NHoverTipSet instantiates per
// tooltip entry (confirmed via decompile: NHoverTipSet.AssetPaths lists it
// alongside hover_tip_set.tscn). %Bg is its background node — also
// confirmed via decompile: NHoverTipSet itself reaches into a freshly
// instantiated hover_tip.tscn via GetNode<CanvasItem>("%Bg") to swap in a
// debuff material for negative-effect tooltips, so that unique name is
// real, not guessed.
public static class NativeTooltipStyle
{
    private const string ScenePath = "res://scenes/ui/hover_tip.tscn";

    // Amber/gold used for tooltip titles like "Block"/"Card Stats" —
    // matches the screenshot; there's no simple way to pull a Label's
    // theme color override back out at this level, so this one value is
    // hand-matched rather than reused from a live node.
    public static readonly Color TitleGold = new(0.94f, 0.71f, 0.31f);

    // Returns a detached Control ready to be added as a background — the
    // caller positions/sizes it same as any other Control (this mod's
    // panels already do that explicitly in ApplyGeometry-style layout
    // methods, so no anchors are set here). Falls back to a plain dark
    // panel if the scene can't be loaded, so a missing/renamed asset in a
    // future game update degrades instead of breaking the overlay.
    public static Control CreateBackground()
    {
        try
        {
            var scene = GD.Load<PackedScene>(ScenePath);
            var root = scene.Instantiate<Control>();
            var bg = root.GetNode<Control>("%Bg");
            root.RemoveChild(bg);
            root.QueueFree();
            bg.MouseFilter = Control.MouseFilterEnum.Ignore;
            return bg;
        }
        catch (System.Exception ex)
        {
            Log.Warn("[LocalRunStats] Failed to load native tooltip background (" + ex.Message + "), falling back to a plain panel.");
            return new ColorRect { Color = new Color(0.05f, 0.05f, 0.08f, 0.97f), MouseFilter = Control.MouseFilterEnum.Ignore };
        }
    }
}
