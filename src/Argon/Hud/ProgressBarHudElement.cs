using UnityEngine;
using UnityEngine.UI;

namespace Argon.Hud;

internal static class ProgressBarHudElement
{
    internal static HudElementDefinition CreateDefinition()
    {
        return new HudElementDefinition(
            "argon.builtin.progress-bar",
            "진행 바",
            false,
            HudAnchor.TopCenter,
            new Vector2(0f, -18f),
            new Vector2(640f, 14f),
            HudUpdatePolicy.Interval,
            0.05f,
            parent => new ProgressBarHudView(parent),
            Update);
    }

    private static void Update(HudElementView view, float _)
    {
        var snapshot = GameStateSource.Current;
        var bar = (ProgressBarHudView)view;
        bar.SetProgress(snapshot.InGame ? snapshot.Progress : 0f, snapshot.InGame);
    }
}

internal sealed class ProgressBarHudView : HudElementView
{
    private readonly Image _background;
    private readonly Image _fill;
    private readonly Outline _border;
    private float _progress = -1f;

    internal ProgressBarHudView(Transform parent) : this(Create(parent))
    {
    }

    private ProgressBarHudView((GameObject root, RectTransform rect, Image background, Image fill, Outline border) parts)
        : base(parts.root, parts.rect)
    {
        _background = parts.background;
        _fill = parts.fill;
        _border = parts.border;
    }

    internal void SetProgress(float progress, bool visible)
    {
        progress = Mathf.Clamp01(progress);
        var preferences = GameStateSource.Preferences;
        _background.color = HudStyle.Parse(preferences.ProgressBarBackgroundColor, new Color(0.05f, 0.05f, 0.07f, 0.75f));
        _fill.color = HudStyle.Parse(preferences.ProgressBarFillColor, new Color(0.75f, 0.48f, 1f, 1f));
        _border.effectColor = HudStyle.Parse(preferences.ProgressBarBorderColor, Color.white);
        _background.enabled = visible;
        _fill.enabled = visible;
        if (Mathf.Approximately(progress, _progress))
        {
            return;
        }

        _progress = progress;
        var max = Mathf.Max(1f, Rect.rect.width);
        _fill.rectTransform.sizeDelta = new Vector2(max * progress, 0f);
    }

    private static (GameObject root, RectTransform rect, Image background, Image fill, Outline border) Create(Transform parent)
    {
        var root = new GameObject("Hud.ProgressBar");
        root.transform.SetParent(parent, false);
        var rect = root.AddComponent<RectTransform>();
        var background = root.AddComponent<Image>();
        background.color = new Color(0.05f, 0.05f, 0.07f, 0.75f);
        background.raycastTarget = false;
        var border = root.AddComponent<Outline>();
        border.effectColor = Color.white;
        border.effectDistance = new Vector2(1f, -1f);
        border.useGraphicAlpha = false;

        var fillObject = new GameObject("Fill");
        fillObject.transform.SetParent(root.transform, false);
        var fillRect = fillObject.AddComponent<RectTransform>();
        fillRect.anchorMin = new Vector2(0f, 0f);
        fillRect.anchorMax = new Vector2(0f, 1f);
        fillRect.pivot = new Vector2(0f, 0.5f);
        fillRect.anchoredPosition = Vector2.zero;
        var fill = fillObject.AddComponent<Image>();
        fill.color = new Color(0.75f, 0.48f, 1f, 1f);
        fill.raycastTarget = false;

        return (root, rect, background, fill, border);
    }
}
