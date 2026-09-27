using System;
using System.Collections.Generic;
using System.Linq;
using Argon.Hud;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Argon.Api;

/// <summary>Stable public surface for other UnityModManager mods to contribute HUD elements.</summary>
public static class ArgonApi
{
    public const int ApiVersion = 1;
    private static readonly Dictionary<string, HudElementDescriptor> Elements = new Dictionary<string, HudElementDescriptor>(StringComparer.Ordinal);

    internal static event Action<HudElementDescriptor, bool>? RegistrationChanged;
    public static bool IsAvailable { get; internal set; }
    internal static int RegistryRevision { get; private set; }

    /// <summary>All currently registered external elements.</summary>
    public static IReadOnlyCollection<HudElementDescriptor> RegisteredElements => Elements.Values.ToArray();

    /// <summary>Registers an element. Use a stable publisher.mod.element ID.</summary>
    public static HudRegistrationResult RegisterElement(HudElementDescriptor? descriptor)
    {
        if (!IsValid(descriptor, out var result)) return result;
        if (descriptor!.Id.StartsWith("argon.builtin.", StringComparison.Ordinal)) return HudRegistrationResult.DuplicateId;
        if (Elements.ContainsKey(descriptor.Id)) return HudRegistrationResult.DuplicateId;

        Elements.Add(descriptor.Id, descriptor);
        RegistryRevision++;
        try
        {
            RegistrationChanged?.Invoke(descriptor, true);
        }
        catch (Exception exception)
        {
            Elements.Remove(descriptor.Id);
            RegistryRevision++;
            Debug.LogException(exception);
            return HudRegistrationResult.InvalidDefinition;
        }

        return HudRegistrationResult.Registered;
    }

    /// <summary>Removes an element registration while retaining its saved layout data for a later re-registration.</summary>
    public static bool UnregisterElement(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || !Elements.TryGetValue(id, out var descriptor)) return false;
        Elements.Remove(id);
        RegistryRevision++;
        try
        {
            RegistrationChanged?.Invoke(descriptor, false);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
        return true;
    }

    /// <summary>Persists changes made to a context's JSON settings bag.</summary>
    public static void SaveSettings()
    {
        ArgonApiRuntime.Save();
    }

    /// <summary>Schedules an OnChange, EventDriven, or Manual element instance for its next update.</summary>
    public static bool RequestUpdate(string instanceId)
    {
        return ArgonApiRuntime.RequestUpdate(instanceId);
    }

    private static bool IsValid(HudElementDescriptor? descriptor, out HudRegistrationResult result)
    {
        if (descriptor == null || string.IsNullOrWhiteSpace(descriptor.Id) || string.IsNullOrWhiteSpace(descriptor.DisplayName) || descriptor.CreateView == null ||
            descriptor.UpdateView == null || descriptor.DefaultSize.x <= 0f || descriptor.DefaultSize.y <= 0f ||
            float.IsNaN(descriptor.DefaultSize.x) || float.IsNaN(descriptor.DefaultSize.y) ||
            float.IsInfinity(descriptor.DefaultSize.x) || float.IsInfinity(descriptor.DefaultSize.y) ||
            float.IsNaN(descriptor.DefaultPosition.x) || float.IsNaN(descriptor.DefaultPosition.y) ||
            float.IsInfinity(descriptor.DefaultPosition.x) || float.IsInfinity(descriptor.DefaultPosition.y) ||
            float.IsNaN(descriptor.RefreshInterval) || float.IsInfinity(descriptor.RefreshInterval))
        {
            result = HudRegistrationResult.InvalidDefinition;
            return false;
        }

        var idParts = descriptor.Id.Split('.');
        if (idParts.Length < 3 || idParts.Any(part => string.IsNullOrWhiteSpace(part) ||
                part.Any(character => !char.IsLetterOrDigit(character) && character != '-' && character != '_')) ||
            !Enum.IsDefined(typeof(HudElementAnchor), descriptor.DefaultAnchor) ||
            !Enum.IsDefined(typeof(HudRefreshPolicy), descriptor.RefreshPolicy) ||
            descriptor.RefreshPolicy == HudRefreshPolicy.Interval && descriptor.RefreshInterval <= 0f)
        {
            result = HudRegistrationResult.InvalidId;
            return false;
        }

        result = HudRegistrationResult.Registered;
        return true;
    }
}

internal static class ArgonApiRuntime
{
    private static Action? _save;
    private static Func<string, bool>? _requestUpdate;

    internal static void Bind(Action save, Func<string, bool> requestUpdate)
    {
        _save = save;
        _requestUpdate = requestUpdate;
        ArgonApi.IsAvailable = true;
    }

    internal static void Unbind()
    {
        _save = null;
        _requestUpdate = null;
        ArgonApi.IsAvailable = false;
    }

    internal static void Save()
    {
        _save?.Invoke();
    }

    internal static bool RequestUpdate(string instanceId)
    {
        return _requestUpdate?.Invoke(instanceId) ?? false;
    }
}

internal sealed class ExternalHudElementView : HudElementView
{
    private readonly HudElementDescriptor _descriptor;
    internal HudElementContext Context { get; }

    internal ExternalHudElementView(GameObject root, HudElementDescriptor descriptor, HudElementContext context)
        : base(root, root.GetComponent<RectTransform>() ?? throw new InvalidOperationException("External HUD views must include a RectTransform."))
    {
        _descriptor = descriptor;
        Context = context;
    }

    public override void Dispose()
    {
        try
        {
            _descriptor.DisposeView?.Invoke(Context, Root);
        }
        catch (Exception exception)
        {
            Debug.LogError($"[Argon] External HUD dispose callback '{_descriptor.Id}' failed: {exception}");
        }

        base.Dispose();
    }
}
