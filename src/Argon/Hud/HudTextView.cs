using O5Kit.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Argon.Hud;

internal sealed class HudTextView : HudElementView
{
    private string _currentText = string.Empty;

    internal TextMeshProUGUI Label { get; }
    private float _lastFontSize = -1f;
    private Color _lastColor;
    private bool _hasStyle;
    private bool _hasCounterValue;
    private int _lastCounterValue;
    private float _counterPulseRemaining;

    internal HudTextView(Transform parent, string name, TextAlignmentOptions alignment)
        : this(CreateRoot(parent, name, alignment))
    {
    }

    private HudTextView((GameObject root, RectTransform rect, TextMeshProUGUI label) parts)
        : base(parts.root, parts.rect)
    {
        Label = parts.label;
    }

    internal void SetText(string text)
    {
        if (_currentText == text)
        {
            return;
        }

        _currentText = text;
        Label.text = text;
    }

    internal void ApplyStyle(float fontSize, Color color)
    {
        if (!_hasStyle || !Mathf.Approximately(_lastFontSize, fontSize))
        {
            Label.fontSize = fontSize;
            _lastFontSize = fontSize;
        }

        if (!_hasStyle || _lastColor != color)
        {
            Label.color = color;
            _lastColor = color;
        }

        _hasStyle = true;
    }

    internal void UpdateCounterPulse(int value, float deltaTime, bool enabled)
    {
        if (!enabled || !_hasStyle)
        {
            _hasCounterValue = false;
            _counterPulseRemaining = 0f;
            if (_hasStyle) Label.fontSize = _lastFontSize;
            return;
        }

        if (!_hasCounterValue)
        {
            _lastCounterValue = value;
            _hasCounterValue = true;
        }
        else if (value > _lastCounterValue)
        {
            _counterPulseRemaining = 0.5f;
            _lastCounterValue = value;
        }
        else if (value < _lastCounterValue)
        {
            _counterPulseRemaining = 0f;
            _lastCounterValue = value;
        }

        var progress = 1f - _counterPulseRemaining / 0.5f;
        var pulse = _counterPulseRemaining > 0f
            ? 1f + (30f / 78f) * Mathf.Pow(2f, -10f * progress)
            : 1f;
        Label.fontSize = _lastFontSize * pulse;
        _counterPulseRemaining = Mathf.Max(0f, _counterPulseRemaining - Mathf.Max(0f, deltaTime));
    }

    private static (GameObject root, RectTransform rect, TextMeshProUGUI label) CreateRoot(
        Transform parent,
        string name,
        TextAlignmentOptions alignment)
    {
        var root = new GameObject(name);
        root.transform.SetParent(parent, false);

        var rect = root.AddComponent<RectTransform>();
        var label = root.AddComponent<TextMeshProUGUI>();
        label.font = O5Boot.Fonts.Medium;
        label.fontSize = 28f;
        label.color = Color.white;
        label.alignment = alignment;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Overflow;
        label.raycastTarget = false;

        var layout = root.AddComponent<LayoutElement>();
        layout.ignoreLayout = true;

        return (root, rect, label);
    }
}
