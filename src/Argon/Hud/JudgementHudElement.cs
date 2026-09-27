using System.Globalization;
using TMPro;
using UnityEngine;

namespace Argon.Hud;

internal static class JudgementHudElement
{
    internal static HudElementDefinition CreateDefinition()
    {
        return new HudElementDefinition(
            "argon.builtin.judgement",
            "판정 · 타이밍",
            false,
            HudAnchor.BottomCenter,
            new Vector2(0f, 44f),
            new Vector2(420f, 76f),
            HudUpdatePolicy.Interval,
            0.1f,
            parent => new HudTextView(parent, "Hud.Judgement", TextAlignmentOptions.Bottom),
            Update);
    }

    private static void Update(HudElementView view, float _)
    {
        var textView = (HudTextView)view;
        var snapshot = GameStateSource.Current;
        var preferences = GameStateSource.Preferences;
        if (!snapshot.InGame)
        {
            textView.SetText(string.Empty);
            textView.ApplyStyle(HudStyle.FontSize, HudStyle.TextColor);
            return;
        }

        var lines = new System.Collections.Generic.List<string>(3);
        if (preferences.ShowJudgement && snapshot.HasJudgement)
        {
            lines.Add(snapshot.Judgement);
        }

        if (preferences.ShowTiming)
        {
            var digits = Mathf.Clamp(preferences.TimingDecimals, 0, 5);
            lines.Add($"Timing: {snapshot.TimingMilliseconds.ToString("F" + digits, CultureInfo.InvariantCulture)} ms  ·  Avg: {snapshot.AverageTimingMilliseconds.ToString("F" + digits, CultureInfo.InvariantCulture)} ms");
        }

        if (preferences.ShowTimingScale)
        {
            lines.Add("Timing scale: " + (snapshot.TimingScale * 100f).ToString("F1", CultureInfo.InvariantCulture) + "%");
        }

        textView.SetText(string.Join("\n", lines));
        textView.ApplyStyle(HudStyle.FontSize, preferences.ShowTiming ? HudStyle.TimingColor(snapshot.TimingMilliseconds) : HudStyle.TextColor);
    }
}
