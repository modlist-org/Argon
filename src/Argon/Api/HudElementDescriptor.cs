using System;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Argon.Api;

/// <summary>Update scheduling mode for a registered HUD element.</summary>
public enum HudRefreshPolicy
{
    EveryFrame,
    Interval,
    OnChange,
    EventDriven,
    Manual,
}

/// <summary>Screen-relative anchor used by an element's default layout.</summary>
public enum HudElementAnchor
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

/// <summary>Result of a public element registration request.</summary>
public enum HudRegistrationResult
{
    Registered,
    InvalidDefinition,
    InvalidId,
    DuplicateId,
}

/// <summary>Per-instance data shared by create, update, and dispose callbacks.</summary>
public sealed class HudElementContext
{
    /// <summary>Stable layout instance identifier.</summary>
    public string InstanceId { get; }

    /// <summary>JSON settings bag persisted in this instance's layout.</summary>
    public JObject Settings { get; }

    internal HudElementContext(string instanceId, JObject settings)
    {
        InstanceId = instanceId;
        Settings = settings;
    }
}

/// <summary>Public definition for one Argon HUD element.</summary>
public sealed class HudElementDescriptor
{
    public string Id { get; }
    public string DisplayName { get; }
    public string Description { get; }
    public bool AllowMultiple { get; }
    public HudElementAnchor DefaultAnchor { get; }
    public Vector2 DefaultPosition { get; }
    public Vector2 DefaultSize { get; }
    public HudRefreshPolicy RefreshPolicy { get; }
    public float RefreshInterval { get; }
    public Func<Transform, HudElementContext, GameObject> CreateView { get; }
    public Action<HudElementContext, GameObject, float> UpdateView { get; }
    public Action<HudElementContext, GameObject>? DisposeView { get; }

    public HudElementDescriptor(
        string id,
        string displayName,
        string description,
        Func<Transform, HudElementContext, GameObject> createView,
        Action<HudElementContext, GameObject, float> updateView,
        Vector2 defaultSize,
        HudElementAnchor defaultAnchor = HudElementAnchor.TopLeft,
        Vector2 defaultPosition = default,
        HudRefreshPolicy refreshPolicy = HudRefreshPolicy.Interval,
        float refreshInterval = 0.25f,
        bool allowMultiple = false,
        Action<HudElementContext, GameObject>? disposeView = null)
    {
        Id = id;
        DisplayName = displayName;
        Description = description;
        CreateView = createView;
        UpdateView = updateView;
        DefaultSize = defaultSize;
        DefaultAnchor = defaultAnchor;
        DefaultPosition = defaultPosition;
        RefreshPolicy = refreshPolicy;
        RefreshInterval = refreshInterval;
        AllowMultiple = allowMultiple;
        DisposeView = disposeView;
    }
}
