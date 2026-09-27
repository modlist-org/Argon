using TMPro;
using UnityEngine;

namespace Argon.Hud;

internal static class ComboHudElement
{
    internal static HudElementDefinition CreateDefinition()
    {
        return new HudElementDefinition(
            "argon.builtin.combo",
            "Combo",
            false,
            HudAnchor.TopCenter,
            new Vector2(0f, -112f),
            new Vector2(260f, 88f),
            HudUpdatePolicy.EveryFrame,
            0.01f,
            parent => new HudTextView(parent, "Hud.Combo", TextAlignmentOptions.Top),
            Update);
    }

    private static HudElementView? _lastView;
    private static int _lastCombo = -1;
    private static int _lastTier = -1;
    private static int _lastPure = -1;
    private static float _refreshAt;

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
            _lastView = null;
            return;
        }

        // Runs every frame for the pulse; rebuild the rich text only when the combo changes
        // (or periodically, to pick up colour edits) instead of formatting it 60+ times a second.
        var pure = preferences.ShowPurePerfectCombo ? snapshot.PurePerfectCombo : 0;
        if (ReferenceEquals(_lastView, view) && snapshot.Combo == _lastCombo && snapshot.ComboTier == _lastTier &&
            pure == _lastPure && Time.unscaledTime < _refreshAt)
        {
            textView.UpdateCounterPulse(snapshot.Combo, deltaTime, true);
            return;
        }

        _lastView = view;
        _lastCombo = snapshot.Combo;
        _lastTier = snapshot.ComboTier;
        _lastPure = pure;
        _refreshAt = Time.unscaledTime + 0.5f;

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
