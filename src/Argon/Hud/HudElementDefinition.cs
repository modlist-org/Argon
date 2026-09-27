using System;
using System.Collections.Generic;
using Argon.Localization;
using UnityEngine;

namespace Argon.Hud;

internal enum HudUpdatePolicy
{
    EveryFrame,
    Interval,
    OnChange,
    EventDriven,
    Manual,
}

internal sealed class HudElementDefinition
{
    internal string Id { get; }
    private readonly string _displayName;

    // Built-in names are translated at read time so a language switch applies without re-registering.
    internal string DisplayName => Id.StartsWith(BuiltinPrefix, StringComparison.Ordinal)
        ? L.T("hud.element." + Id.Substring(BuiltinPrefix.Length), _displayName)
        : _displayName;

    private const string BuiltinPrefix = "argon.builtin.";
    internal bool AllowMultiple { get; }
    internal HudAnchor DefaultAnchor { get; }
    internal Vector2 DefaultPosition { get; }
    internal Vector2 DefaultSize { get; }
    internal HudUpdatePolicy UpdatePolicy { get; }
    internal float UpdateInterval { get; }
    internal Func<Transform, HudElementView> CreateView { get; }
    internal Action<HudElementView, float> UpdateView { get; }

    internal HudElementDefinition(
        string id,
        string displayName,
        bool allowMultiple,
        HudAnchor defaultAnchor,
        Vector2 defaultPosition,
        Vector2 defaultSize,
        HudUpdatePolicy updatePolicy,
        float updateInterval,
        Func<Transform, HudElementView> createView,
        Action<HudElementView, float> updateView)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("HUD element ID cannot be empty.", nameof(id));
        }

        if (defaultSize.x <= 0f || defaultSize.y <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(defaultSize), "HUD element size must be positive.");
        }

        Id = id;
        _displayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
        AllowMultiple = allowMultiple;
        DefaultAnchor = defaultAnchor;
        DefaultPosition = defaultPosition;
        DefaultSize = defaultSize;
        UpdatePolicy = updatePolicy;
        UpdateInterval = Mathf.Max(updateInterval, 0.01f);
        CreateView = createView ?? throw new ArgumentNullException(nameof(createView));
        UpdateView = updateView ?? throw new ArgumentNullException(nameof(updateView));
    }
}

internal abstract class HudElementView : IDisposable
{
    private bool _disposed;

    internal GameObject Root { get; }
    internal RectTransform Rect { get; }

    protected HudElementView(GameObject root, RectTransform rect)
    {
        Root = root ?? throw new ArgumentNullException(nameof(root));
        Rect = rect ?? throw new ArgumentNullException(nameof(rect));
    }

    public virtual void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (Root != null)
        {
            UnityEngine.Object.Destroy(Root);
        }
    }
}

internal sealed class HudElementRegistry
{
    private readonly Dictionary<string, HudElementDefinition> _definitions = new Dictionary<string, HudElementDefinition>();

    internal bool Register(HudElementDefinition definition)
    {
        if (definition == null)
        {
            throw new ArgumentNullException(nameof(definition));
        }

        if (_definitions.ContainsKey(definition.Id))
        {
            Debug.LogWarning($"[Argon] HUD element ID '{definition.Id}' is already registered.");
            return false;
        }

        _definitions.Add(definition.Id, definition);
        return true;
    }

    internal bool Unregister(string id)
    {
        return _definitions.Remove(id);
    }

    internal IReadOnlyCollection<HudElementDefinition> Definitions => _definitions.Values;

    internal bool TryGet(string id, out HudElementDefinition definition)
    {
        return _definitions.TryGetValue(id, out definition!);
    }
}
