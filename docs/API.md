# Argon HUD Element API (v1)

Other UnityModManager mods can contribute HUD elements through `Argon.Api.ArgonApi`. Reference the matching `Argon.dll` when compiling your mod, then use only the public types in the `Argon.Api` namespace. The API intentionally does not require JALib or O5Kit.

## Lifecycle and threading

- `ArgonApi.ApiVersion` identifies the public API contract. Check it before depending on a newer API.
- `ArgonApi.IsAvailable` is true while Argon's HUD runtime is active. Registration may happen before or after Argon initializes; early registrations are retained and reconciled when the runtime starts.
- Call registration, unregistration, `RequestUpdate`, and Unity UI callbacks on Unity's main thread.
- Element IDs must have at least three dot-separated, non-empty segments containing letters, digits, `_`, or `-` (for example, `publisher.mod.element`). IDs under `argon.builtin.*` are reserved.
- A created view must be a `GameObject` whose root has a `RectTransform`. Argon parents and lays it out. `DisposeView` is for releasing the mod's subscriptions/resources; Argon destroys the root after the callback.
- Callback exceptions are isolated to that element and logged. An update failure disables that instance until it is re-enabled/recreated.

## Minimal element

```csharp
using Argon.Api;
using TMPro;
using UnityEngine;

internal static class ExampleHud
{
    private static HudElementContext? _context;

    internal static HudRegistrationResult Register()
    {
        if (ArgonApi.ApiVersion != 1)
            return HudRegistrationResult.InvalidDefinition;

        return ArgonApi.RegisterElement(new HudElementDescriptor(
            id: "example.author.status",
            displayName: "Example status",
            description: "A small example HUD item.",
            createView: (parent, context) =>
            {
                _context = context;
                var root = new GameObject("ExampleStatus", typeof(RectTransform));
                root.transform.SetParent(parent, false);
                var label = root.AddComponent<TextMeshProUGUI>();
                label.text = context.Settings["text"]?.Value<string>() ?? "Ready";
                return root;
            },
            updateView: (context, root, deltaTime) =>
            {
                var label = root.GetComponent<TextMeshProUGUI>();
                if (label == null) return;

                var text = context.Settings["text"]?.Value<string>() ?? "Ready";
                if (label.text != text) label.text = text;
            },
            defaultSize: new Vector2(220f, 36f),
            defaultAnchor: HudElementAnchor.TopRight,
            defaultPosition: new Vector2(-24f, -24f),
            refreshPolicy: HudRefreshPolicy.Interval,
            refreshInterval: 0.25f,
            allowMultiple: false,
            disposeView: (context, root) =>
            {
                if (ReferenceEquals(_context, context)) _context = null;
                // Unsubscribe this instance's events here. Do not destroy root;
                // Argon owns the view GameObject.
            }));
    }

    internal static void SetText(string text)
    {
        if (_context == null) return;
        _context.Settings["text"] = text;
        ArgonApi.SaveSettings();
        ArgonApi.RequestUpdate(_context.InstanceId);
    }

    internal static void Unregister()
    {
        ArgonApi.UnregisterElement("example.author.status");
    }
}
```

## Contracts

- `RegisterElement` returns `Registered`, `DuplicateId`, `InvalidId`, or `InvalidDefinition`. A duplicate or malformed registration is not installed.
- `HudElementContext.InstanceId` identifies the layout instance. `HudElementContext.Settings` is a persisted `JObject` specific to that instance, so do not keep a second authoritative copy of those settings.
- `HudRefreshPolicy.EveryFrame` and `Interval` are scheduled by Argon. For `OnChange`, `EventDriven`, and `Manual`, call `ArgonApi.RequestUpdate(instanceId)` when your data changes; updates still execute on Argon's next Unity tick.
- `ArgonApi.SaveSettings()` writes the current Argon document. Call it after modifying the settings bag.
- `UnregisterElement(id)` disposes current instances but preserves their layout records and settings. Re-registering the same ID allows those records to be restored.
- `DisposeView`, `CreateView`, and `UpdateView` callbacks are owned by the registering mod and must not outlive its registration.

This API is an extension point for trusted installed mods. It does not sandbox arbitrary Unity code. A companion sample mod and live in-game API compatibility verification remain release checklist items.
