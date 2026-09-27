using UnityEngine;

namespace Argon.Hud;

internal enum HudAnchor
{
    TopLeft,
    TopCenter,
    TopRight,
    MiddleLeft,
    Center,
    MiddleRight,
    BottomLeft,
    BottomCenter,
    BottomRight,
}

internal static class HudAnchorUtility
{
    internal static void Apply(RectTransform rect, HudAnchor anchor, Vector2 position, Vector2 size)
    {
        var normalizedAnchor = ToNormalizedPosition(anchor);
        rect.anchorMin = normalizedAnchor;
        rect.anchorMax = normalizedAnchor;
        rect.pivot = normalizedAnchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static Vector2 ToNormalizedPosition(HudAnchor anchor)
    {
        switch (anchor)
        {
            case HudAnchor.TopLeft:
                return new Vector2(0f, 1f);
            case HudAnchor.TopCenter:
                return new Vector2(0.5f, 1f);
            case HudAnchor.TopRight:
                return new Vector2(1f, 1f);
            case HudAnchor.MiddleLeft:
                return new Vector2(0f, 0.5f);
            case HudAnchor.Center:
                return new Vector2(0.5f, 0.5f);
            case HudAnchor.MiddleRight:
                return new Vector2(1f, 0.5f);
            case HudAnchor.BottomLeft:
                return new Vector2(0f, 0f);
            case HudAnchor.BottomCenter:
                return new Vector2(0.5f, 0f);
            case HudAnchor.BottomRight:
                return new Vector2(1f, 0f);
            default:
                return new Vector2(0.5f, 0.5f);
        }
    }
}
