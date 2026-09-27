using UnityEngine;
using TMPro;

namespace Argon.Hud;

internal static class FpsHudElement
{
    internal static HudElementDefinition CreateDefinition()
    {
        return new HudElementDefinition(
            "argon.builtin.fps",
            "FPS",
            false,
            HudAnchor.TopLeft,
            new Vector2(24f, -24f),
            new Vector2(180f, 42f),
            HudUpdatePolicy.Interval,
            0.25f,
            parent => new HudTextView(parent, "Hud.FPS", TextAlignmentOptions.TopLeft),
            Update);
    }

    private static void Update(HudElementView view, float _)
    {
        var fpsView = (HudTextView)view;
        var delta = Mathf.Max(Time.smoothDeltaTime, 0.001f);
        fpsView.SetText($"FPS: {Mathf.RoundToInt(1f / delta)}");
        fpsView.ApplyStyle(HudStyle.FontSize, HudStyle.TextColor);
    }
}
