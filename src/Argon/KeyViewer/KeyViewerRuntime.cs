using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Argon.Compat;
using Argon.Storage;
using O5Kit.Core;
using SkyHook;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;

namespace Argon.KeyViewer;

internal sealed class KeyViewerRuntime : IDisposable
{
    private readonly ArgonStore _store;
    private readonly KeyViewerPreferences _settings;
    private readonly GameObject _root;
    private readonly RectTransform _handRow;
    private readonly RectTransform _footRow;
    private readonly TextMeshProUGUI _summary;
    private readonly TextMeshProUGUI _total;
    private readonly RectTransform _summaryBar;
    private readonly RectTransform _totalBar;
    private readonly List<KeySlotView> _slots = new List<KeySlotView>();
    private readonly List<RainTrail> _trails = new List<RainTrail>();
    private readonly Stack<RainTrail> _trailPool = new Stack<RainTrail>();
    private readonly RectTransform _rainLayer;
    private readonly RainGraphic _rain;
    private readonly Queue<float> _pressTimes = new Queue<float>();
    private int _lastKps = -1;
    private int _lastTotal = -1;
    private readonly bool[] _wasPressed = new bool[36];
    private readonly bool[] _wasGhostPressed = new bool[36];
    private readonly bool[] _hookPressed = new bool[36];
    private readonly bool[] _hookGhostPressed = new bool[36];
    // Hook events carry a Stopwatch timestamp taken on the hook thread, so presses and rain keep
    // their real timing even though they are applied on the next Unity frame (Quartz KvInputQueue idea).
    private readonly ConcurrentQueue<TimedHookEvent> _hookEvents = new ConcurrentQueue<TimedHookEvent>();
    private static readonly KeyCode[] AllKeyCodes = (KeyCode[])Enum.GetValues(typeof(KeyCode));
    private bool _disposed;
    private bool _captureNext;
    private int _captureSlot = -1;
    private bool _captureFoot;
    private bool _captureGhost;
    private string _captureMessage = string.Empty;
    private bool _countsDirty;
    private float _lastCountsSaveAt;
    private bool _keyLimitIntegrationPending;
    private bool _skyHookSubscribed;
    private bool _skyHookIntegrationPending;
    private bool _skyHookWarningLogged;
    private float _nextSkyHookRetryAt;
    private float _nextSkyHookCheckAt;

    internal KeyViewerRuntime(Transform parent, ArgonStore store)
    {
        _store = store;
        _settings = store.Document.Preferences.KeyViewer;
        EnsureArrays();
        EnsureSlotLayouts();
        if (_settings.GridLayoutVersion < 2)
        {
            if (Mathf.Approximately(_settings.KeySize, 56f) || Mathf.Approximately(_settings.KeySize, 60f)) _settings.KeySize = 50f;
            _settings.GridLayoutVersion = 2;
            _store.Save();
        }
        _settings.AutoSetupKeyLimit = false;
        if (_settings.VisualStyleVersion < 1)
        {
            if (string.Equals(_settings.BackgroundColor, "#8F3CFF32", StringComparison.OrdinalIgnoreCase))
                _settings.BackgroundColor = "#111113CC";
            if (string.Equals(_settings.OutlineColor, "#8D3EFF", StringComparison.OrdinalIgnoreCase))
                _settings.OutlineColor = "#55555B99";
            foreach (var layout in _settings.HandSlotLayouts.Values.Concat(_settings.FootSlotLayouts.Values).SelectMany(value => value))
            {
                if (layout.BorderWidth == 1.5f) layout.BorderWidth = 1f;
                if (layout.FontSize == 15f) layout.FontSize = 18f;
            }
            _settings.VisualStyleVersion = 1;
            _store.Save();
        }
        if (_settings.VisualStyleVersion < 2)
        {
            _settings.BackgroundColor = UpgradeColor(_settings.BackgroundColor, "#111113CC", "#0E0E11B8");
            _settings.OutlineColor = UpgradeColor(_settings.OutlineColor, "#55555B99", "#FFFFFF24");
            _settings.PressedBackgroundColor = UpgradeColor(_settings.PressedBackgroundColor, "#FFFFFFFF", "#FFFFFFE0");
            _settings.PressedOutlineColor = UpgradeColor(_settings.PressedOutlineColor, "#FFFFFFFF", "#FFFFFF24");
            _settings.TextColor = UpgradeColor(UpgradeColor(_settings.TextColor, "#FFFFFFFF", "#D4D4D8FF"), "#D4D4D8FF", "#EDEEF2C7");
            _settings.PressedTextColor = UpgradeColor(_settings.PressedTextColor, "#000000FF", "#141418E6");
            _settings.VisualStyleVersion = 2;
            _store.Save();
        }
        _root = new GameObject("ArgonKeyViewer");
        _root.transform.SetParent(parent, false);
        var rect = _root.AddComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0f, _settings.VerticalOffset);
        rect.sizeDelta = new Vector2(1200f, 150f);
        rect.localScale = Vector3.one * _settings.Scale;

        var group = _root.AddComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;

        // Rain lives on its own nested canvas behind the keys: moving notes rebuild only this mesh,
        // not the whole HUD canvas with all of its text.
        var rainObject = new GameObject("KeyRain");
        rainObject.transform.SetParent(_root.transform, false);
        _rainLayer = rainObject.AddComponent<RectTransform>();
        _rainLayer.anchorMin = Vector2.zero;
        _rainLayer.anchorMax = Vector2.one;
        _rainLayer.offsetMin = _rainLayer.offsetMax = Vector2.zero;
        rainObject.AddComponent<Canvas>();
        _rain = rainObject.AddComponent<RainGraphic>();
        _rain.raycastTarget = false;

        _summary = CreateSummaryBar("KPS", out _summaryBar);
        _total = CreateSummaryBar("Total", out _totalBar);

        _handRow = CreateRow(_root.transform, "HandKeys", 72f, -32f);
        _footRow = CreateRow(_root.transform, "FootKeys", 72f, -112f);
        ApplyRowPositions();
        RebuildSlots();
        _root.SetActive(_store.Document.Preferences.KeyViewerEnabled);
        _keyLimitIntegrationPending = _store.Document.Preferences.KeyViewerEnabled && _settings.AutoSetupKeyLimit;
        _skyHookIntegrationPending = _store.Document.Preferences.KeyViewerEnabled;
    }

    internal bool Enabled => _store.Document.Preferences.KeyViewerEnabled;
    private static string UpgradeColor(string value, string oldValue, string newValue) =>
        string.Equals(value.TrimStart('#'), oldValue.TrimStart('#'), StringComparison.OrdinalIgnoreCase) ? newValue : value;
    internal int CurrentKps => _pressTimes.Count;
    internal int TotalCount => _settings.TotalCount;

    internal int GetSlotCount(int index, bool foot)
    {
        for (var i = 0; i < _slots.Count; i++)
        {
            var slot = _slots[i];
            if (slot.Index == index && slot.IsFoot == foot) return _settings.KeyCounts[slot.CountIndex];
        }

        return 0;
    }

    internal void SetEnabled(bool enabled)
    {
        if (_disposed)
        {
            return;
        }

        _store.Document.Preferences.KeyViewerEnabled = enabled;
        _root.SetActive(enabled);
        if (!enabled) ClearTrails();
        Array.Clear(_wasPressed, 0, _wasPressed.Length);
        Array.Clear(_wasGhostPressed, 0, _wasGhostPressed.Length);
        ClearHookStates();
        _captureNext = false;
        _skyHookIntegrationPending = enabled;
        if (!enabled) ReleaseSkyHook();
        ApplyKeyLimit();
        _store.Save();
    }

    internal void SetHandKeyCount(int count)
    {
        _settings.HandKeyCount = NormalizeHandCount(count);
        RebuildSlots();
        ApplyKeyLimit();
        _store.Save();
    }

    internal void SetFootKeyCount(int count)
    {
        _settings.FootKeyCount = NormalizeFootCount(count);
        RebuildSlots();
        ApplyKeyLimit();
        _store.Save();
    }

    internal void SetRain(bool enabled)
    {
        _settings.ShowRain = enabled;
        if (!enabled) ClearTrails();
        _store.Save();
    }

    internal void SetGhostRain(bool enabled)
    {
        _settings.ShowGhostRain = enabled;
        if (!enabled) ClearTrails();
        _store.Save();
    }

    internal void SetAutoKeyLimit(bool enabled)
    {
        _settings.AutoSetupKeyLimit = enabled;
        ApplyKeyLimit();
        _store.Save();
    }

    internal void SetScale(float value, bool save = true)
    {
        _settings.Scale = Mathf.Clamp(value, 0.25f, 3f);
        _root.transform.localScale = Vector3.one * _settings.Scale;
        if (save) _store.Save();
    }

    internal void SetVerticalOffset(float value, bool save = true)
    {
        _settings.VerticalOffset = Mathf.Clamp(value, 0f, 600f);
        ((RectTransform)_root.transform).anchoredPosition = new Vector2(0f, _settings.VerticalOffset);
        if (save) _store.Save();
    }

    internal void SetHandOffsetX(float value, bool save = true) => SetRowOffset(true, true, value, save);
    internal void SetHandOffsetY(float value, bool save = true) => SetRowOffset(true, false, value, save);
    internal void SetFootOffsetX(float value, bool save = true) => SetRowOffset(false, true, value, save);
    internal void SetFootOffsetY(float value, bool save = true) => SetRowOffset(false, false, value, save);

    internal void SetKeySize(float value, bool save = true)
    {
        _settings.KeySize = Mathf.Clamp(value, 32f, 96f);
        foreach (var slot in _slots)
        {
            ApplySlotLayout(slot);
        }

        UpdateRootSize();
        if (save) _store.Save();
    }

    internal Vector2 GetSlotOffset(int slot, bool foot)
    {
        var layout = GetSlotLayout(slot, foot);
        return layout == null ? Vector2.zero : new Vector2(layout.OffsetX, layout.OffsetY);
    }

    internal Vector2 GetSlotSize(int slot, bool foot)
    {
        var layout = GetSlotLayout(slot, foot);
        var defaultHeight = _settings.KeySize * (foot ? 0.6f : 1f);
        var defaultWidth = defaultHeight;
        if (!foot)
        {
            JrpKeyLayout.Key(_settings.HandKeyCount, slot, out _, out _, out var width);
            defaultWidth *= width / 50f;
        }
        return layout == null
            ? new Vector2(defaultWidth, defaultHeight)
            : new Vector2(layout.Width > 0f ? layout.Width : defaultWidth,
                layout.Height > 0f ? layout.Height : defaultHeight);
    }

    internal Vector2 GetSlotSizeOverride(int slot, bool foot)
    {
        var layout = GetSlotLayout(slot, foot);
        return layout == null ? Vector2.zero : new Vector2(layout.Width, layout.Height);
    }

    internal void RestoreSlotGeometry(int slot, bool foot, Vector2 offset, Vector2 sizeOverride)
    {
        var layout = GetSlotLayout(slot, foot);
        if (layout == null) return;
        layout.OffsetX = offset.x;
        layout.OffsetY = offset.y;
        layout.Width = sizeOverride.x;
        layout.Height = sizeOverride.y;
        RefreshSlotLayout(slot, foot);
        UpdateRootSize();
    }

    internal Vector2 GetSlotPosition(int slot, bool foot)
    {
        var count = foot ? _settings.FootKeyCount : _settings.HandKeyCount;
        var offset = GetSlotOffset(slot, foot);
        if (!foot)
        {
            JrpKeyLayout.Key(count, slot, out var keyX, out var keyY, out _);
            return new Vector2(keyX, keyY) * (_settings.KeySize / 50f) + offset;
        }
        JrpKeyLayout.Foot(_settings.HandKeyCount, count, slot, out var footX, out var footY);
        return new Vector2(footX, footY) * (_settings.KeySize / 50f) + offset;
    }

    internal void SetSlotOffset(int slot, bool foot, Vector2 offset, bool save = true)
    {
        var layout = GetSlotLayout(slot, foot);
        if (layout == null) return;
        layout.OffsetX = Mathf.Clamp(offset.x, -1200f, 1200f);
        layout.OffsetY = Mathf.Clamp(offset.y, -600f, 600f);
        RefreshSlotLayout(slot, foot);
        UpdateRootSize();
        if (save) _store.Save();
    }

    internal void SetSlotSize(int slot, bool foot, Vector2 size, bool save = true)
    {
        var layout = GetSlotLayout(slot, foot);
        if (layout == null) return;
        layout.Width = Mathf.Clamp(size.x, 24f, 240f);
        layout.Height = Mathf.Clamp(size.y, 24f, 240f);
        RefreshSlotLayout(slot, foot);
        UpdateRootSize();
        if (save) _store.Save();
    }

    internal void ResetSlotLayout(int slot, bool foot)
    {
        var layout = GetSlotLayout(slot, foot);
        if (layout == null) return;
        layout.OffsetX = 0f;
        layout.OffsetY = 0f;
        layout.Width = 0f;
        layout.Height = 0f;
        RefreshSlotLayout(slot, foot);
        UpdateRootSize();
        _store.Save();
    }

    internal void RestoreJrpLayout()
    {
        _settings.KeySize = 50f;
        _settings.HandOffsetX = _settings.HandOffsetY = 0f;
        _settings.FootOffsetX = _settings.FootOffsetY = 0f;
        foreach (var slot in _slots)
        {
            var layout = GetSlotLayout(slot.Index, slot.IsFoot);
            if (layout == null) continue;
            layout.OffsetX = layout.OffsetY = 0f;
            layout.Width = layout.Height = 0f;
        }
        RebuildSlots();
        _store.Save();
    }

    internal float GetSlotBorderWidth(int slot, bool foot) => GetSlotLayout(slot, foot)?.BorderWidth ?? 1.5f;
    internal float GetSlotFontSize(int slot, bool foot) => GetSlotLayout(slot, foot)?.FontSize ?? 15f;
    internal bool GetSlotNoteEffect(int slot, bool foot) => GetSlotLayout(slot, foot)?.NoteEffectEnabled ?? true;
    internal bool GetSlotCounterVisible(int slot, bool foot) => GetSlotLayout(slot, foot)?.CounterVisible ?? true;

    internal string GetSlotColor(int slot, bool foot, string channel)
    {
        var layout = GetSlotLayout(slot, foot);
        var value = channel switch
        {
            "background" => layout?.BackgroundColor,
            "pressed-background" => layout?.PressedBackgroundColor,
            "outline" => layout?.OutlineColor,
            "pressed-outline" => layout?.PressedOutlineColor,
            "text" => layout?.TextColor,
            "pressed-text" => layout?.PressedTextColor,
            _ => null,
        };
        if (!string.IsNullOrWhiteSpace(value)) return value!;
        return channel switch
        {
            "background" => _settings.BackgroundColor,
            "pressed-background" => _settings.PressedBackgroundColor,
            "outline" => _settings.OutlineColor,
            "pressed-outline" => _settings.PressedOutlineColor,
            "text" => _settings.TextColor,
            "pressed-text" => _settings.PressedTextColor,
            _ => "#FFFFFFFF",
        };
    }

    internal void SetSlotColor(int slot, bool foot, string channel, string value)
    {
        var layout = GetSlotLayout(slot, foot);
        if (layout == null) return;
        switch (channel)
        {
            case "background": layout.BackgroundColor = value; break;
            case "pressed-background": layout.PressedBackgroundColor = value; break;
            case "outline": layout.OutlineColor = value; break;
            case "pressed-outline": layout.PressedOutlineColor = value; break;
            case "text": layout.TextColor = value; break;
            case "pressed-text": layout.PressedTextColor = value; break;
        }

        RefreshColors();
        _store.Save();
    }

    internal void SetSlotBorderWidth(int slot, bool foot, float value, bool save = true)
    {
        var layout = GetSlotLayout(slot, foot);
        if (layout == null) return;
        layout.BorderWidth = Mathf.Clamp(value, 0f, 12f);
        RefreshSlotLayout(slot, foot);
        if (save) _store.Save();
    }

    internal void SetSlotFontSize(int slot, bool foot, float value, bool save = true)
    {
        var layout = GetSlotLayout(slot, foot);
        if (layout == null) return;
        layout.FontSize = Mathf.Clamp(value, 8f, 48f);
        RefreshSlotLayout(slot, foot);
        if (save) _store.Save();
    }

    internal void SetSlotNoteEffect(int slot, bool foot, bool enabled)
    {
        var layout = GetSlotLayout(slot, foot);
        if (layout == null) return;
        layout.NoteEffectEnabled = enabled;
        _store.Save();
    }

    internal void SetSlotCounterVisible(int slot, bool foot, bool visible)
    {
        var layout = GetSlotLayout(slot, foot);
        if (layout == null) return;
        layout.CounterVisible = visible;
        RefreshSlotText();
        _store.Save();
    }

    internal void SetRainSpeed(float value, bool save = true)
    {
        _settings.RainSpeed = Mathf.Clamp(value, 10f, 600f);
        if (save) _store.Save();
    }

    internal void SetRainHeight(float value, bool save = true)
    {
        _settings.RainHeight = Mathf.Clamp(value, 20f, 1200f);
        if (save) _store.Save();
    }

    internal void RefreshColors()
    {
        foreach (var slot in _slots)
        {
            var pressed = _wasPressed[slot.CountIndex];
            ApplySlotColors(slot, pressed);
        }
    }

    internal void ResetCounts()
    {
        Array.Clear(_settings.KeyCounts, 0, _settings.KeyCounts.Length);
        _settings.TotalCount = 0;
        _pressTimes.Clear();
        _store.Save();
        RefreshSlotText();
    }

    internal string GetSlotLabel(int slot, bool foot, bool ghost)
    {
        var binding = GetBinding(slot, foot, ghost);
        if (binding == null)
        {
            return "없음";
        }

        if (!string.IsNullOrWhiteSpace(binding.Label))
        {
            return binding.Label!;
        }

        if (binding.KeyCode >= ArgonStore.NativeKeyBase)
        {
            var keyLabel = HookKeyMapper.FromNative((ushort)(binding.KeyCode - ArgonStore.NativeKeyBase));
            return keyLabel != KeyLabel.Unknown
                ? keyLabel.ToString()
                : "Key " + (binding.KeyCode - ArgonStore.NativeKeyBase).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        var code = (KeyCode)binding.KeyCode;
        if (code >= KeyCode.Alpha0 && code <= KeyCode.Alpha9) return ((int)code - (int)KeyCode.Alpha0).ToString();
        return code switch
        {
            KeyCode.LeftShift => "LShift", KeyCode.RightShift => "RShift",
            KeyCode.LeftControl => "LCtrl", KeyCode.RightControl => "RCtrl",
            KeyCode.LeftAlt => "LAlt", KeyCode.RightAlt => "RAlt",
            KeyCode.Semicolon => ";", KeyCode.Quote => "'", KeyCode.Comma => ",",
            KeyCode.Period => ".", KeyCode.Slash => "/", KeyCode.Backslash => "\\",
            KeyCode.Equals => "=", KeyCode.Minus => "-", KeyCode.Return => "Enter",
            _ => code.ToString(),
        };
    }

    internal string GetCustomSlotLabel(int slot, bool foot, bool ghost)
    {
        return GetBinding(slot, foot, ghost)?.Label ?? string.Empty;
    }

    internal bool IsSlotPressed(int slot, bool foot)
    {
        var countIndex = foot ? 20 + slot : slot;
        return countIndex >= 0 && countIndex < _wasPressed.Length && _wasPressed[countIndex];
    }

    internal bool SetSlotLabel(int slot, bool foot, bool ghost, string label)
    {
        var binding = GetBinding(slot, foot, ghost);
        if (binding == null) return false;
        var normalized = string.IsNullOrWhiteSpace(label) ? null : label.Trim();
        binding.Label = normalized != null && normalized.Length > 32 ? normalized.Substring(0, 32) : normalized;
        RefreshSlotText();
        _store.Save();
        return true;
    }

    internal void BeginCapture(int slot, bool foot, bool ghost)
    {
        _captureSlot = slot;
        _captureFoot = foot;
        _captureGhost = ghost;
        _captureMessage = "다음 키 입력을 기다리는 중… (Esc: 취소)";
        _captureNext = true;
    }

    internal string CaptureMessage => _captureMessage;
    internal bool IsCapturing => _captureNext;

    internal void UpdateRuntime()
    {
        if (_disposed)
        {
            return;
        }

        UpdateTrails();
        var now = Time.unscaledTime;
        if (_store.Document.Preferences.KeyViewerEnabled &&
            ((_skyHookIntegrationPending && now >= _nextSkyHookRetryAt) || (!_skyHookIntegrationPending && now >= _nextSkyHookCheckAt)))
        {
            EnsureSkyHook();
            _nextSkyHookCheckAt = now + 2f;
        }
        if (Application.isFocused) ProcessHookEvents();
        else
        {
            ClearHookStates();
            while (_hookEvents.TryDequeue(out _)) { }
        }
        if (_keyLimitIntegrationPending)
        {
            ApplyKeyLimit();
        }

        if (_captureNext)
        {
            PollCapture();
        }

        Prune(_pressTimes, now);
        if (!_root.activeInHierarchy)
        {
            return;
        }

        var useGlobalInput = _skyHookSubscribed && SkyHookManager.Instance != null && SkyHookManager.Instance.isHookActive;
        for (var i = 0; i < _slots.Count; i++)
        {
            var slot = _slots[i];
            var binding = GetBinding(slot.Index, slot.IsFoot, false);
            var keyCode = binding != null ? (KeyCode)binding.KeyCode : KeyCode.None;
            var pressed = useGlobalInput
                ? _hookPressed[slot.CountIndex]
                : Application.isFocused && keyCode != KeyCode.None && (int)keyCode < ArgonStore.NativeKeyBase && Input.GetKey(keyCode);
            if (pressed != _wasPressed[slot.CountIndex])
            {
                _wasPressed[slot.CountIndex] = pressed;
                ApplySlotColors(slot, pressed);
                if (pressed)
                {
                    _settings.KeyCounts[slot.CountIndex] = Math.Max(0, _settings.KeyCounts[slot.CountIndex]) + 1;
                    _settings.TotalCount = Math.Max(0, _settings.TotalCount) + 1;
                    _pressTimes.Enqueue(now);
                    SpawnTrail(slot.Rect, false, GetSlotNoteEffect(slot.Index, slot.IsFoot), now);
                    _countsDirty = true;
                }
                else ReleaseTrail(slot.Rect, false, now);
            }

            var ghostBinding = GetBinding(slot.Index, slot.IsFoot, true);
            var ghostCode = ghostBinding != null ? (KeyCode)ghostBinding.KeyCode : KeyCode.None;
            var ghostPressed = _settings.ShowGhostRain && (useGlobalInput
                ? _hookGhostPressed[slot.CountIndex]
                : Application.isFocused && ghostCode != KeyCode.None && (int)ghostCode < ArgonStore.NativeKeyBase && Input.GetKey(ghostCode));
            if (ghostPressed && !_wasGhostPressed[slot.CountIndex])
            {
                SpawnTrail(slot.Rect, true, GetSlotNoteEffect(slot.Index, slot.IsFoot), now);
            }
            if (!ghostPressed && _wasGhostPressed[slot.CountIndex]) ReleaseTrail(slot.Rect, true, now);

            _wasGhostPressed[slot.CountIndex] = ghostPressed;
            slot.SetCount(_settings.KeyCounts[slot.CountIndex]);
        }

        var kps = _pressTimes.Count;
        if (kps != _lastKps)
        {
            _lastKps = kps;
            NumberText.Set(_summary, kps);
        }

        if (_settings.TotalCount != _lastTotal)
        {
            _lastTotal = _settings.TotalCount;
            NumberText.Set(_total, _lastTotal);
        }

        _summaryBar.gameObject.SetActive(_settings.ShowTotalKps);
        // Counts change every press; the store debounces and defers writes during play anyway.
        if (_countsDirty && now - _lastCountsSaveAt >= 10f)
        {
            _store.Save();
            _countsDirty = false;
            _lastCountsSaveAt = now;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_countsDirty) _store.Save();
        ReleaseSkyHook();
        RestoreKeyLimit();
        _trails.Clear();
        _trailPool.Clear();
        if (_root != null) UnityEngine.Object.Destroy(_root);
    }

    private TextMeshProUGUI CreateSummaryBar(string title, out RectTransform rect)
    {
        var root = new GameObject(title + "Bar");
        root.transform.SetParent(_root.transform, false);
        rect = root.AddComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        var image = root.AddComponent<KeycapGraphic>();
        image.sprite = O5Boot.Sprites.RoundedControl;
        image.type = Image.Type.Sliced;
        image.color = new Color32(14, 14, 17, 184);
        image.raycastTarget = false;
        var outline = root.AddComponent<Outline>();
        outline.enabled = false;
        outline.effectDistance = new Vector2(1f, -1f);
        outline.effectColor = new Color32(85, 85, 91, 153);
        var label = CreateText(root.transform, "Title", 13f, TextAlignmentOptions.Left);
        label.text = title;
        label.rectTransform.anchorMin = Vector2.zero;
        label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = new Vector2(10f, 0f);
        label.rectTransform.offsetMax = new Vector2(-60f, 0f);
        var value = CreateText(root.transform, "Value", 12f, TextAlignmentOptions.Right);
        value.rectTransform.anchorMin = Vector2.zero;
        value.rectTransform.anchorMax = Vector2.one;
        value.rectTransform.offsetMin = new Vector2(50f, 0f);
        value.rectTransform.offsetMax = new Vector2(-10f, 0f);
        value.text = "0";
        return value;
    }

    private void UpdateSummaryBounds()
    {
        if (_slots.Count == 0) return;
        var scale = _settings.KeySize / 50f;
        for (var i = 0; i < 2; i++)
        {
            JrpKeyLayout.Stat(_settings.HandKeyCount, i == 1, out var x, out var y, out var width, out var height);
            var rect = i == 0 ? _summaryBar : _totalBar;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = _handRow.anchoredPosition - new Vector2(0f, _handRow.rect.height * 0.5f) + new Vector2(x, y) * scale;
            rect.sizeDelta = new Vector2(width, height) * scale;
            LayoutStatText(rect, height > 30f);
        }
    }

    internal static void LayoutStatText(RectTransform rect, bool stacked)
    {
        var texts = rect.GetComponentsInChildren<TextMeshProUGUI>();
        for (var i = 0; i < texts.Length; i++)
        {
            var text = texts[i];
            var title = i == 0;
            text.alignment = stacked ? TextAlignmentOptions.Center : title ? TextAlignmentOptions.Left : TextAlignmentOptions.Right;
            text.rectTransform.anchorMin = new Vector2(0f, stacked && title ? 0.35f : 0f);
            text.rectTransform.anchorMax = new Vector2(1f, stacked && !title ? 0.4f : 1f);
            text.rectTransform.offsetMin = new Vector2(stacked ? 3f : title ? 10f : 50f, 0f);
            text.rectTransform.offsetMax = new Vector2(stacked ? -3f : title ? -60f : -10f, 0f);
        }
    }

    private void RebuildSlots()
    {
        ClearTrails();
        Array.Clear(_wasPressed, 0, _wasPressed.Length);
        Array.Clear(_wasGhostPressed, 0, _wasGhostPressed.Length);
        ClearHookStates();
        foreach (var slot in _slots)
        {
            if (slot.Root != null) UnityEngine.Object.Destroy(slot.Root);
        }

        _slots.Clear();
        BuildRow(_handRow, _settings.HandKeyCount, false);
        if (_settings.FootKeyCount > 0)
        {
            BuildRow(_footRow, _settings.FootKeyCount, true);
            _footRow.gameObject.SetActive(true);
        }
        else
        {
            _footRow.gameObject.SetActive(false);
        }

        UpdateRootSize();
        ApplyRowPositions();
        RefreshSlotText();
        RefreshColors();
        _lastKps = _lastTotal = -1;
    }

    private void BuildRow(RectTransform parent, int count, bool foot)
    {
        for (var i = 0; i < count; i++)
        {
            var countIndex = foot ? 20 + i : i;
            var root = new GameObject(foot ? "FootKey" + i : "HandKey" + i);
            root.transform.SetParent(parent, false);
            var rect = root.AddComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            var background = root.AddComponent<KeycapGraphic>();
            background.sprite = O5Boot.Sprites.RoundedControl;
            background.type = Image.Type.Sliced;
            background.color = ParseColor(_settings.BackgroundColor, new Color(0.56f, 0.24f, 1f, 0.2f));
            background.raycastTarget = false;
            var outline = root.AddComponent<Outline>();
            outline.enabled = false;
            outline.effectColor = ParseColor(_settings.OutlineColor, new Color(0.55f, 0.24f, 1f, 1f));
            outline.effectDistance = new Vector2(1.5f, -1.5f);

            var label = CreateText(root.transform, "KeyLabel", 15f, TextAlignmentOptions.Center);
            label.fontStyle = FontStyles.Bold;
            label.rectTransform.anchorMin = new Vector2(0f, 0.3f);
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = new Vector2(3f, 0f);
            label.rectTransform.offsetMax = new Vector2(-3f, 0f);
            label.raycastTarget = false;
            var countLabel = CreateText(root.transform, "Count", 11f, TextAlignmentOptions.Center);
            countLabel.characterSpacing = 6f;
            countLabel.rectTransform.anchorMin = new Vector2(0f, 0.05f);
            countLabel.rectTransform.anchorMax = new Vector2(1f, 0.35f);
            countLabel.rectTransform.offsetMin = Vector2.zero;
            countLabel.rectTransform.offsetMax = Vector2.zero;
            countLabel.color = new Color(1f, 1f, 1f, 0.72f);
            countLabel.raycastTarget = false;

            var slot = new KeySlotView(root, rect, background, outline, label, countLabel, i, countIndex, foot);
            _slots.Add(slot);
            ApplySlotLayout(slot);
        }
    }

    private void ApplySlotLayout(KeySlotView slot)
    {
        var size = GetSlotSize(slot.Index, slot.IsFoot);
        slot.Rect.sizeDelta = size;
        slot.Rect.anchoredPosition = GetSlotPosition(slot.Index, slot.IsFoot);
        var border = GetSlotBorderWidth(slot.Index, slot.IsFoot);
        slot.Outline.effectDistance = new Vector2(border, -border);
        slot.Background.BorderWidth = border;
        slot.Background.SetVerticesDirty();
        slot.SetFontSize(GetSlotFontSize(slot.Index, slot.IsFoot));
    }

    private void RefreshSlotLayout(int slot, bool foot)
    {
        foreach (var view in _slots)
        {
            if (view.Index == slot && view.IsFoot == foot)
            {
                ApplySlotLayout(view);
                UpdateSummaryBounds();
                return;
            }
        }
    }

    private static RectTransform CreateRow(Transform parent, string name, float height, float y)
    {
        var root = new GameObject(name);
        root.transform.SetParent(parent, false);
        var rect = root.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, y);
        rect.sizeDelta = new Vector2(-20f, height);
        return rect;
    }

    private void SetRowOffset(bool hand, bool horizontal, float value, bool save)
    {
        var clamped = horizontal ? Mathf.Clamp(value, -1000f, 1000f) : Mathf.Clamp(value, -300f, 300f);
        if (hand)
        {
            if (horizontal) _settings.HandOffsetX = clamped;
            else _settings.HandOffsetY = clamped;
        }
        else
        {
            if (horizontal) _settings.FootOffsetX = clamped;
            else _settings.FootOffsetY = clamped;
        }

        ApplyRowPositions();
        UpdateSummaryBounds();
        if (save) _store.Save();
    }

    private void ApplyRowPositions()
    {
        _handRow.anchoredPosition = new Vector2(_settings.HandOffsetX, -32f + _settings.HandOffsetY);
        _footRow.anchoredPosition = new Vector2(_settings.FootOffsetX,
            -32f + _settings.FootOffsetY);
    }

    private void UpdateRootSize()
    {
        var rootRect = (RectTransform)_root.transform;
        var halfWidth = Mathf.Max(
            GetLayoutHalfWidth(_settings.HandKeyCount, false),
            _settings.FootKeyCount > 0 ? GetLayoutHalfWidth(_settings.FootKeyCount, true) : 0f);
        rootRect.sizeDelta = new Vector2(Mathf.Max(620f, halfWidth * 2f + 40f), _settings.FootKeyCount == 0 ? 110f : 150f);
        UpdateSummaryBounds();
    }

    private float GetLayoutHalfWidth(int count, bool foot)
    {
        var max = 0f;
        for (var i = 0; i < count; i++)
        {
            var center = Mathf.Abs(GetSlotPosition(i, foot).x);
            max = Mathf.Max(max, center + GetSlotSize(i, foot).x * 0.5f);
        }

        return max;
    }

    private void RefreshSlotText()
    {
        foreach (var slot in _slots)
        {
            slot.SetText(GetSlotLabel(slot.Index, slot.IsFoot, false), GetSlotCounterVisible(slot.Index, slot.IsFoot));
            slot.SetCount(_settings.KeyCounts[slot.CountIndex]);
        }
    }

    private void ApplySlotColors(KeySlotView slot, bool pressed)
    {
        var background = pressed ? "pressed-background" : "background";
        var outline = pressed ? "pressed-outline" : "outline";
        var text = pressed ? "pressed-text" : "text";
        slot.SetPressed(pressed,
            ParseColor(GetSlotColor(slot.Index, slot.IsFoot, background), pressed ? Color.white : new Color(0.56f, 0.24f, 1f, 0.2f)),
            ParseColor(GetSlotColor(slot.Index, slot.IsFoot, outline), Color.white),
            ParseColor(GetSlotColor(slot.Index, slot.IsFoot, text), pressed ? Color.black : Color.white));
    }

    private void PollCapture()
    {
        foreach (var keyCode in AllKeyCodes)
        {
            // Mouse buttons (and joystick codes above them) would bind on the next click anywhere.
            if (keyCode == KeyCode.None || keyCode >= KeyCode.Mouse0 || !Input.GetKeyDown(keyCode))
            {
                continue;
            }

            if (keyCode == KeyCode.Escape)
            {
                CancelCapture();
                return;
            }

            var binding = GetBinding(_captureSlot, _captureFoot, _captureGhost);
            if (binding == null)
            {
                _captureMessage = "선택한 키 항목이 없습니다.";
                _captureNext = false;
                return;
            }

            binding.KeyCode = (int)keyCode;
            binding.Label = keyCode.ToString();
            _captureMessage = "키 변경: " + keyCode;
            _captureNext = false;
            ApplyKeyLimit();
            _store.Save();
            RefreshSlotText();
            return;
        }
    }

    internal void CancelCapture()
    {
        if (!_captureNext) return;
        _captureNext = false;
        _captureMessage = "키 변경 취소됨";
    }

    private void EnsureSkyHook()
    {
        if (!_store.Document.Preferences.KeyViewerEnabled)
        {
            _skyHookIntegrationPending = false;
            return;
        }

        if (!Application.isPlaying)
        {
            _nextSkyHookRetryAt = Time.unscaledTime + 1f;
            return;
        }

        try
        {
            var manager = SkyHookManager.Instance;
            if (manager == null) throw new InvalidOperationException("SkyHook manager is not initialized yet.");
            if (!_skyHookSubscribed)
            {
                SkyHookManager.KeyUpdated.AddListener(OnSkyHookEvent);
                _skyHookSubscribed = true;
            }

            // The game owns this shared hook. Observe it; never start or stop it.
            _skyHookIntegrationPending = !SkyHookManager.Instance.isHookActive;
            _skyHookWarningLogged = false;
            if (_skyHookIntegrationPending) _nextSkyHookRetryAt = Time.unscaledTime + 1f;
        }
        catch (Exception exception)
        {
            _skyHookIntegrationPending = true;
            _nextSkyHookRetryAt = Time.unscaledTime + 1f;
            if (!_skyHookWarningLogged)
            {
                Debug.LogWarning($"[Argon] Global key events are unavailable; Unity input polling remains active: {exception.Message}");
                _skyHookWarningLogged = true;
            }
        }
    }

    private void ReleaseSkyHook()
    {
        if (_skyHookSubscribed)
        {
            try
            {
                SkyHookManager.KeyUpdated.RemoveListener(OnSkyHookEvent);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[Argon] Could not detach the global key listener: {exception.Message}");
            }

            _skyHookSubscribed = false;
        }

        while (_hookEvents.TryDequeue(out _)) { }
        ClearHookStates();
        _skyHookIntegrationPending = false;
    }

    private readonly struct TimedHookEvent
    {
        internal readonly SkyHookEvent Event;
        internal readonly long Timestamp;

        internal TimedHookEvent(SkyHookEvent input, long timestamp)
        {
            Event = input;
            Timestamp = timestamp;
        }
    }

    // Runs on SkyHook's thread: timestamp immediately, touch nothing else.
    private void OnSkyHookEvent(SkyHookEvent input)
    {
        _hookEvents.Enqueue(new TimedHookEvent(input, Stopwatch.GetTimestamp()));
    }

    private void ProcessHookEvents()
    {
        var now = Time.unscaledTime;
        var nowTicks = Stopwatch.GetTimestamp();
        while (_hookEvents.TryDequeue(out var timed))
        {
            var input = timed.Event;
            var age = (float)((nowTicks - timed.Timestamp) / (double)Stopwatch.Frequency);
            var at = now - Mathf.Clamp(age, 0f, 0.25f);
            var pressed = input.Type == SkyHook.EventType.KeyPressed;
            if (pressed && _captureNext)
            {
                CaptureHookKey(input);
                continue; // the key being bound is not a counted press
            }

            foreach (var slot in _slots)
            {
                var primary = GetBinding(slot.Index, slot.IsFoot, false);
                if (MatchesHookKey(primary, input))
                {
                    var wasPressed = _hookPressed[slot.CountIndex];
                    _hookPressed[slot.CountIndex] = pressed;
                    if (pressed && !wasPressed && !_wasPressed[slot.CountIndex]) RegisterHookPress(slot, at);
                    else if (!pressed && wasPressed)
                    {
                        _wasPressed[slot.CountIndex] = false;
                        ApplySlotColors(slot, false);
                        ReleaseTrail(slot.Rect, false, at);
                    }
                }

                var ghost = GetBinding(slot.Index, slot.IsFoot, true);
                if (MatchesHookKey(ghost, input))
                {
                    var wasPressed = _hookGhostPressed[slot.CountIndex];
                    _hookGhostPressed[slot.CountIndex] = pressed;
                    if (pressed && !wasPressed && _settings.ShowGhostRain && GetSlotNoteEffect(slot.Index, slot.IsFoot))
                    {
                        SpawnTrail(slot.Rect, true, true, at);
                    }
                    _wasGhostPressed[slot.CountIndex] = pressed && _settings.ShowGhostRain;
                    if (!pressed) ReleaseTrail(slot.Rect, true, at);
                }
            }
        }
    }

    private void RegisterHookPress(KeySlotView slot, float at)
    {
        _wasPressed[slot.CountIndex] = true;
        ApplySlotColors(slot, true);
        _settings.KeyCounts[slot.CountIndex] = Math.Max(0, _settings.KeyCounts[slot.CountIndex]) + 1;
        _settings.TotalCount = Math.Max(0, _settings.TotalCount) + 1;
        _pressTimes.Enqueue(at);
        SpawnTrail(slot.Rect, false, GetSlotNoteEffect(slot.Index, slot.IsFoot), at);
        _countsDirty = true;
    }

    private void CaptureHookKey(SkyHookEvent input)
    {
        var binding = GetBinding(_captureSlot, _captureFoot, _captureGhost);
        if (binding == null)
        {
            _captureMessage = "선택한 키 항목이 없습니다.";
            _captureNext = false;
            return;
        }

        var mapped = HookKeyMapper.ToUnity(input.Label);
        if (mapped == KeyCode.Escape)
        {
            CancelCapture();
            return;
        }

        binding.KeyCode = mapped != KeyCode.None ? (int)mapped : ArgonStore.NativeKeyBase + input.Key;
        binding.Label = mapped != KeyCode.None
            ? mapped.ToString()
            : input.Label != KeyLabel.Unknown ? input.Label.ToString() : "Key " + input.Key;
        _captureMessage = "키 변경: " + binding.Label;
        _captureNext = false;
        ApplyKeyLimit();
        _store.Save();
        RefreshSlotText();
    }

    private static bool MatchesHookKey(KeyBindingData? binding, SkyHookEvent input)
    {
        if (binding == null || binding.KeyCode == (int)KeyCode.None) return false;
        if (binding.KeyCode >= ArgonStore.NativeKeyBase) return input.Key == binding.KeyCode - ArgonStore.NativeKeyBase;
        var label = HookKeyMapper.FromUnity((KeyCode)binding.KeyCode);
        return label != KeyLabel.Unknown && input.Label == label;
    }

    private void ClearHookStates()
    {
        Array.Clear(_hookPressed, 0, _hookPressed.Length);
        Array.Clear(_hookGhostPressed, 0, _hookGhostPressed.Length);
    }

    private KeyBindingData? GetBinding(int slot, bool foot, bool ghost)
    {
        var map = ghost
            ? foot ? _settings.GhostFootBindingSets! : _settings.GhostHandBindingSets!
            : foot ? _settings.FootBindingSets! : _settings.HandBindingSets!;
        var keyCount = foot ? _settings.FootKeyCount : _settings.HandKeyCount;
        if (!map.TryGetValue(keyCount, out var bindings)) return null;
        var bindingIndex = slot;
        return bindingIndex >= 0 && bindingIndex < bindings.Length ? bindings[bindingIndex] : null;
    }

    private void SpawnTrail(RectTransform source, bool ghost, bool slotEffectEnabled, float startedAt)
    {
        if (!slotEffectEnabled || (ghost ? !_settings.ShowGhostRain : !_settings.ShowRain))
        {
            return;
        }

        if (_trails.Count >= 512)
        {
            _trailPool.Push(_trails[0]);
            _trails.RemoveAt(0);
        }

        var trail = _trailPool.Count > 0 ? _trailPool.Pop() : new RainTrail();
        trail.Source = source;
        trail.Ghost = ghost;
        trail.StartedAt = startedAt;
        trail.ReleasedAt = null;
        trail.BaseColor = ParseColor(ghost ? _settings.GhostRainColor : _settings.RainColor,
            ghost ? Color.white : new Color(0.51f, 0.12f, 0.86f, 0.8f));
        _trails.Add(trail);
    }

    private void ReleaseTrail(RectTransform source, bool ghost, float releasedAt)
    {
        for (var i = 0; i < _trails.Count; i++)
        {
            var trail = _trails[i];
            if (trail.Source == source && trail.Ghost == ghost && !trail.ReleasedAt.HasValue)
                trail.ReleasedAt = Math.Max(releasedAt, trail.StartedAt);
        }
    }

    private void ClearTrails()
    {
        foreach (var trail in _trails) _trailPool.Push(trail);
        _trails.Clear();
        if (_rain.Quads.Count > 0)
        {
            _rain.Quads.Clear();
            _rain.SetVerticesDirty();
        }
    }

    private void UpdateTrails()
    {
        var hadQuads = _rain.Quads.Count > 0;
        _rain.Quads.Clear();
        if (_trails.Count == 0)
        {
            if (hadQuads) _rain.SetVerticesDirty();
            return;
        }

        var now = Time.unscaledTime;
        var write = 0;
        for (var i = 0; i < _trails.Count; i++)
        {
            var trail = _trails[i];
            if (trail.Source == null || !RainGeometry.Sample(now, trail.StartedAt, trail.ReleasedAt, _settings.RainSpeed,
                    _settings.RainHeight, out var bottom, out var height, out var opacity))
            {
                _trailPool.Push(trail);
                continue;
            }

            _trails[write++] = trail;
            var sourceRect = trail.Source.rect;
            var top = _rainLayer.InverseTransformPoint(trail.Source.TransformPoint(new Vector3(sourceRect.center.x, sourceRect.yMax)));
            var color = trail.BaseColor;
            color.a *= opacity;
            _rain.Quads.Add(new RainGraphic.Quad
            {
                Rect = new Rect(top.x - sourceRect.width * 0.5f, top.y + bottom, sourceRect.width, height),
                Color = color,
            });
        }

        _trails.RemoveRange(write, _trails.Count - write);
        _rain.SetVerticesDirty();
    }

    private void ApplyKeyLimit()
    {
        // Key-viewer mappings must never replace the game's playable keys.
        _keyLimitIntegrationPending = false;
        _settings.AutoSetupKeyLimit = false;
    }

    private void RestoreKeyLimit()
    {
        // No game input state is owned by this viewer.
    }

    private void EnsureArrays()
    {
        if (_settings.KeyCounts == null || _settings.KeyCounts.Length != 36)
        {
            var old = _settings.KeyCounts ?? Array.Empty<int>();
            _settings.KeyCounts = new int[36];
            Array.Copy(old, _settings.KeyCounts, Math.Min(old.Length, _settings.KeyCounts.Length));
        }

        _settings.HandBindingSets ??= KeyBindingData.CreateDefaultHandBindingSets();
        _settings.FootBindingSets ??= KeyBindingData.CreateDefaultFootBindingSets();
        _settings.GhostHandBindingSets ??= KeyBindingData.CreateDefaultGhostHandBindingSets();
        _settings.GhostFootBindingSets ??= KeyBindingData.CreateDefaultGhostFootBindingSets();
        RepairBindings(_settings.HandBindingSets, KeyBindingData.CreateDefaultHandBindingSets());
        RepairBindings(_settings.FootBindingSets, KeyBindingData.CreateDefaultFootBindingSets());
        RepairBindings(_settings.GhostHandBindingSets, KeyBindingData.CreateDefaultGhostHandBindingSets());
        RepairBindings(_settings.GhostFootBindingSets, KeyBindingData.CreateDefaultGhostFootBindingSets());
    }

    private void EnsureSlotLayouts()
    {
        _settings.HandSlotLayouts ??= new Dictionary<int, KeyViewerSlotLayout[]>();
        _settings.FootSlotLayouts ??= new Dictionary<int, KeyViewerSlotLayout[]>();
        EnsureSlotLayoutSets(_settings.HandSlotLayouts, new[] { 10, 12, 16, 20 });
        EnsureSlotLayoutSets(_settings.FootSlotLayouts, new[] { 2, 4, 6, 8, 16 });
    }

    private static void EnsureSlotLayoutSets(Dictionary<int, KeyViewerSlotLayout[]> layouts, int[] counts)
    {
        foreach (var count in counts)
        {
            if (!layouts.TryGetValue(count, out var values) || values == null || values.Length != count)
            {
                var old = values ?? Array.Empty<KeyViewerSlotLayout>();
                values = new KeyViewerSlotLayout[count];
                for (var i = 0; i < count; i++)
                {
                    values[i] = i < old.Length && old[i] != null ? old[i] : new KeyViewerSlotLayout();
                }

                layouts[count] = values;
            }
            else
            {
                for (var i = 0; i < values.Length; i++) values[i] ??= new KeyViewerSlotLayout();
            }

            foreach (var layout in values)
            {
                layout.OffsetX = ClampFinite(layout.OffsetX, 0f, -1200f, 1200f);
                layout.OffsetY = ClampFinite(layout.OffsetY, 0f, -600f, 600f);
                layout.Width = layout.Width <= 0f ? 0f : ClampFinite(layout.Width, 56f, 24f, 240f);
                layout.Height = layout.Height <= 0f ? 0f : ClampFinite(layout.Height, 56f, 24f, 240f);
                layout.BorderWidth = ClampFinite(layout.BorderWidth, 1f, 0f, 12f);
                layout.FontSize = ClampFinite(layout.FontSize, 18f, 8f, 48f);
            }
        }
    }

    private static float ClampFinite(float value, float fallback, float min, float max)
    {
        return float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, min, max);
    }

    private KeyViewerSlotLayout? GetSlotLayout(int slot, bool foot)
    {
        var count = foot ? _settings.FootKeyCount : _settings.HandKeyCount;
        var layouts = foot ? _settings.FootSlotLayouts : _settings.HandSlotLayouts;
        if (layouts == null || !layouts.TryGetValue(count, out var values) || slot < 0 || slot >= values.Length)
        {
            return null;
        }

        return values[slot];
    }

    private static void RepairBindings(Dictionary<int, KeyBindingData[]> map, Dictionary<int, KeyBindingData[]> defaults)
    {
        foreach (var pair in defaults)
        {
            if (!map.TryGetValue(pair.Key, out var values) || values == null || values.Length != pair.Value.Length)
            {
                map[pair.Key] = pair.Value;
                continue;
            }

            for (var i = 0; i < values.Length; i++) values[i] ??= new KeyBindingData { KeyCode = pair.Value[i].KeyCode };
        }
    }

    private static TextMeshProUGUI CreateText(Transform parent, string name, float size, TextAlignmentOptions alignment)
    {
        var root = new GameObject(name);
        root.transform.SetParent(parent, false);
        var text = root.AddComponent<TextMeshProUGUI>();
        text.font = O5Kit.Core.O5Boot.Fonts.Regular;
        text.fontSize = size;
        text.color = Color.white;
        text.alignment = alignment;
        text.raycastTarget = false;
        text.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
        return text;
    }

    private static void Prune(Queue<float> queue, float now)
    {
        while (queue.Count > 0 && now - queue.Peek() > 1f) queue.Dequeue();
    }

    private static Color ParseColor(string value, Color fallback)
    {
        var normalized = value.StartsWith("#", StringComparison.Ordinal) ? value : "#" + value;
        return ColorUtility.TryParseHtmlString(normalized, out var color) ? color : fallback;
    }

    private static int NormalizeHandCount(int value) => value == 10 || value == 12 || value == 20 ? value : 16;
    private static int NormalizeFootCount(int value) => value == 0 || value == 2 || value == 4 || value == 6 || value == 8 || value == 16 ? value : 4;

    private sealed class KeySlotView
    {
        internal GameObject Root { get; }
        internal RectTransform Rect { get; }
        internal KeycapGraphic Background { get; }
        internal Outline Outline { get; }
        private readonly TextMeshProUGUI _label;
        private readonly TextMeshProUGUI _count;
        internal int Index { get; }
        internal int CountIndex { get; }
        internal bool IsFoot { get; }

        internal KeySlotView(GameObject root, RectTransform rect, KeycapGraphic background, Outline outline,
            TextMeshProUGUI label, TextMeshProUGUI count, int index, int countIndex, bool foot)
        {
            Root = root;
            Rect = rect;
            Background = background;
            Outline = outline;
            _label = label;
            _count = count;
            Index = index;
            CountIndex = countIndex;
            IsFoot = foot;
        }

        internal void SetPressed(bool pressed, Color background, Color outline, Color text)
        {
            Background.color = background;
            Background.BorderColor = outline;
            Background.SetVerticesDirty();
            Outline.effectColor = outline;
            _label.color = text;
            _count.color = text;
        }

        internal void SetFontSize(float size)
        {
            _label.fontSize = size;
            _label.enableAutoSizing = true;
            _label.fontSizeMin = Mathf.Min(8f, size);
            _label.fontSizeMax = size;
            _label.overflowMode = TextOverflowModes.Ellipsis;
        }

        private string? _lastKey;
        private bool? _lastShowCounter;
        private int _lastCount = -1;

        internal void SetText(string key, bool showCounter)
        {
            if (_lastKey != key)
            {
                _lastKey = key;
                _label.text = key;
            }

            if (_lastShowCounter == showCounter) return;
            _lastShowCounter = showCounter;
            _label.rectTransform.anchorMin = new Vector2(0f, showCounter ? 0.3f : 0f);
            _count.gameObject.SetActive(showCounter);
            _lastCount = -1;
        }

        internal void SetCount(int count)
        {
            if (_lastShowCounter != true || count == _lastCount) return;
            _lastCount = count;
            NumberText.Set(_count, count);
        }
    }

    private sealed class RainTrail
    {
        internal RectTransform? Source;
        internal bool Ghost;
        internal float StartedAt;
        internal float? ReleasedAt;
        internal Color BaseColor;
    }
}
