using TMPro;
using UnityEngine;

namespace Argon.Hud;

internal static class ComboHudElement
{
    internal static HudElementDefinition CreateDefinition()
    {
        return new HudElementDefinition(
            "argon.builtin.combo",
            "콤보",
            false,
            HudAnchor.TopCenter,
            new Vector2(0f, -112f),
            new Vector2(260f, 88f),
            HudUpdatePolicy.EveryFrame,
            0.01f,
            parent => new HudTextView(parent, "Hud.Combo", TextAlignmentOptions.Top),
            Update);
    }

    private static void Update(HudElementView view, float deltaTime)
    {
        var textView = (HudTextView)view;
        var snapshot = GameStateSource.Current;
        var preferences = GameStateSource.Preferences;
        if (!snapshot.InGame || !preferences.ShowCombo)
        {
            textView.SetText(string.Empty);
            textView.ApplyStyle(HudStyle.FontSize, HudStyle.TextColor);
            textView.UpdateCounterPulse(0, deltaTime, false);
            return;
        }

        var tierColor = ColorUtility.ToHtmlStringRGBA(HudStyle.ComboColor(snapshot.Combo, snapshot.ComboTier));
        var countColor = ColorUtility.ToHtmlStringRGBA(HudStyle.ComboColor(snapshot.Combo, 0));
        var tierName = snapshot.ComboTier >= 2 ? "Early/Late Perfect" : snapshot.ComboTier == 1 ? "Perfect" : "XPerfect";
        var combo = $"<color=#{tierColor}>{tierName}</color>\n<color=#{countColor}>{snapshot.Combo.ToString(System.Globalization.CultureInfo.InvariantCulture)} Combo</color>";
        if (preferences.ShowPurePerfectCombo && snapshot.PurePerfectCombo > 0)
        {
            var purePerfectColor = ColorUtility.ToHtmlStringRGBA(HudStyle.PurePerfectColor);
            combo += $"\nPure Perfect: <color=#{purePerfectColor}>{snapshot.PurePerfectCombo.ToString(System.Globalization.CultureInfo.InvariantCulture)}</color>";
        }

        textView.SetText(combo);
        textView.ApplyStyle(HudStyle.FontSize, HudStyle.TextColor);
        textView.UpdateCounterPulse(snapshot.Combo, deltaTime, true);
    }
}
