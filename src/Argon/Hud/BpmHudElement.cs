using System.Globalization;
using UnityEngine;
using TMPro;

namespace Argon.Hud;

internal static class BpmHudElement
{
    internal static HudElementDefinition CreateDefinition()
    {
        return new HudElementDefinition(
            "argon.builtin.bpm",
            "BPM",
            false,
            HudAnchor.TopRight,
            new Vector2(-24f, -24f),
            new Vector2(280f, 100f),
            HudUpdatePolicy.Interval,
            0.15f,
            parent => new HudTextView(parent, "Hud.BPM", TextAlignmentOptions.TopRight),
            Update);
    }

    private static void Update(HudElementView view, float _)
    {
        var textView = (HudTextView)view;
        if (!GameStateSource.TryGetBpm(out var bpm))
        {
            textView.SetText(string.Empty);
            return;
        }

        var digits = GameStateSource.Preferences.BpmDecimals;
        var tileBpm = bpm.TileBpm.ToString("F" + digits, CultureInfo.InvariantCulture);
        var currentBpm = bpm.CurrentBpm.ToString("F" + digits, CultureInfo.InvariantCulture);
        var kps = bpm.Kps.ToString("F" + digits, CultureInfo.InvariantCulture);
        var pseudo = GameStateSource.Current.IsPseudoBpm ? $" × {GameStateSource.Current.PseudoBeatCount}" : string.Empty;
        textView.SetText($"TBPM: {tileBpm}\nCBPM: {currentBpm}{pseudo}\nKPS: {kps}");
        textView.ApplyStyle(HudStyle.FontSize, HudStyle.BpmColor(bpm.CurrentBpm / 400f));
    }
}
