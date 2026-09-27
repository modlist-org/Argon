using System.Globalization;
using TMPro;
using UnityEngine;

namespace Argon.Hud;

internal static class AccuracyHudElement
{
    internal static HudElementDefinition CreateDefinition()
    {
        return new HudElementDefinition(
            "argon.builtin.accuracy",
            "Accuracy",
            false,
            HudAnchor.TopLeft,
            new Vector2(24f, -112f),
            new Vector2(380f, 220f),
            HudUpdatePolicy.Interval,
            0.15f,
            parent => new HudTextView(parent, "Hud.Accuracy", TextAlignmentOptions.TopLeft),
            Update);
    }

    private static void Update(HudElementView view, float _)
    {
        var textView = (HudTextView)view;
        if (!GameStateSource.TryGetAccuracy(out var accuracy))
        {
            textView.SetText(string.Empty);
            return;
        }

        var preferences = GameStateSource.Preferences;
        var decimals = preferences.AccuracyDecimals;
        var lines = new System.Collections.Generic.List<string>(5)
        {
            "Accuracy: " + GameStateSource.FormatPercent(accuracy.Accuracy, decimals),
        };
        if (preferences.ShowPotentialValues)
        {
            lines.Add("Potential: " + GameStateSource.FormatPercent(accuracy.PotentialAccuracy, decimals));
        }

        if (preferences.ShowAbsoluteAccuracy)
        {
            lines.Add("Absolute: " + GameStateSource.FormatPercent(GameStateSource.Current.AbsoluteAccuracy, decimals));
        }

        lines.Add("X-Accuracy: " + GameStateSource.FormatPercent(accuracy.XAccuracy, decimals));
        if (preferences.ShowPotentialValues)
        {
            lines.Add("Potential X: " + GameStateSource.FormatPercent(accuracy.PotentialXAccuracy, decimals));
        }

        if (preferences.ShowXScore)
        {
            lines.Add($"X-Score: {accuracy.XScore} / {accuracy.MaxXScore}" +
                      (accuracy.PotentialXScore != accuracy.XScore ? $" (potential {accuracy.PotentialXScore})" : string.Empty));
        }

        textView.SetText(string.Join("\n", lines));
        textView.ApplyStyle(HudStyle.FontSize, HudStyle.ProgressColor(accuracy.Accuracy));
    }
}
