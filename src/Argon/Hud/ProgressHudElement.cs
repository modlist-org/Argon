using System.Globalization;
using TMPro;
using UnityEngine;

namespace Argon.Hud;

internal static class ProgressHudElement
{
    internal static HudElementDefinition CreateDefinition()
    {
        return new HudElementDefinition(
            "argon.builtin.progress",
            "Progress",
            false,
            HudAnchor.TopLeft,
            new Vector2(24f, -64f),
            new Vector2(360f, 42f),
            HudUpdatePolicy.Interval,
            0.15f,
            parent => new HudTextView(parent, "Hud.Progress", TextAlignmentOptions.TopLeft),
            Update);
    }

    private static void Update(HudElementView view, float _)
    {
        var textView = (HudTextView)view;
        if (!GameStateSource.TryGetProgress(out var progress))
        {
            textView.SetText(string.Empty);
            return;
        }

        var percentage = GameStateSource.FormatPercent(progress, GameStateSource.Preferences.ProgressDecimals);
        var snapshot = GameStateSource.Current;
        textView.SetText($"Progress: {percentage}  ·  Tile {snapshot.Sequence + 1}/{snapshot.TotalTiles}");
        textView.ApplyStyle(HudStyle.FontSize, HudStyle.ProgressColor(progress));
    }
}
