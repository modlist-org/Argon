using UnityEngine;

namespace Argon.Hud;

internal static class HudStyle
{
    internal static Color TextColor => Parse(GameStateSource.Preferences.TextColor, Color.white);

    internal static Color ProgressColor(float value)
    {
        var preferences = GameStateSource.Preferences;
        return preferences.UseColorGradients
            ? Gradient(value, preferences.ProgressLowColor, preferences.ProgressMidColor, preferences.ProgressHighColor)
            : TextColor;
    }

    internal static Color BpmColor(float value)
    {
        var preferences = GameStateSource.Preferences;
        return preferences.UseColorGradients
            ? Gradient(value, preferences.BpmLowColor, preferences.BpmMidColor, preferences.BpmHighColor)
            : TextColor;
    }

    internal static Color TimingColor(float milliseconds)
    {
        var preferences = GameStateSource.Preferences;
        if (!preferences.UseColorGradients) return TextColor;
        var ratio = Mathf.Clamp01(Mathf.Abs(milliseconds) / Mathf.Max(1f, preferences.TimingScaleMilliseconds));
        return Color.Lerp(Parse(preferences.TimingGoodColor, Color.green), Parse(preferences.TimingBadColor, Color.red), ratio);
    }

    internal static Color PurePerfectColor => Parse(GameStateSource.Preferences.PurePerfectColor, Color.green);

    internal static Color ComboColor(int combo, int tier)
    {
        var preferences = GameStateSource.Preferences;
        if (!preferences.UseColorGradients) return TextColor;
        if (tier >= 2) return Parse(preferences.ComboEarlyLateColor, Color.yellow);
        if (tier >= 1) return Parse(preferences.ComboPerfectColor, Color.green);
        return Color.Lerp(
            Parse(preferences.ComboLowColor, new Color(0.87f, 0.71f, 1f)),
            Parse(preferences.ComboHighColor, new Color(0.72f, 0.35f, 1f)),
            Mathf.Clamp01((float)Mathf.Max(0, combo) / Mathf.Max(1, preferences.ComboColorMax)));
    }

    internal static float FontSize => Mathf.Clamp(GameStateSource.Preferences.FontSize, 12f, 96f);

    internal static Color Parse(string? value, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        var normalized = value.StartsWith("#", System.StringComparison.Ordinal) ? value : "#" + value;
        return ColorUtility.TryParseHtmlString(normalized, out var color) ? color : fallback;
    }

    private static Color Gradient(float value, string low, string middle, string high)
    {
        value = Mathf.Clamp01(value);
        var lowColor = Parse(low, Color.red);
        var middleColor = Parse(middle, Color.yellow);
        var highColor = Parse(high, Color.green);
        return value <= 0.5f
            ? Color.Lerp(lowColor, middleColor, value * 2f)
            : Color.Lerp(middleColor, highColor, (value - 0.5f) * 2f);
    }
}
