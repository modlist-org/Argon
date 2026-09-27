using System;
using System.Collections.Generic;
using System.Linq;
using Argon.Api;
using Argon.Storage;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Argon.Hud;

internal sealed class HudRuntime : IDisposable
{
    internal const string FpsInstanceId = "argon.builtin.fps.1";
    internal const string ProgressInstanceId = "argon.builtin.progress.1";
    internal const string AccuracyInstanceId = "argon.builtin.accuracy.1";
    internal const string BpmInstanceId = "argon.builtin.bpm.1";
    internal const string StatusInstanceId = "argon.builtin.status.1";
    internal const string JudgementInstanceId = "argon.builtin.judgement.1";
    internal const string ComboInstanceId = "argon.builtin.combo.1";
    internal const string ProgressBarInstanceId = "argon.builtin.progress-bar.1";

    private readonly GameObject _root;
    private readonly ArgonStore _store;
    private readonly HudElementRegistry _registry = new HudElementRegistry();
    private readonly Dictionary<string, RuntimeElement> _instances = new Dictionary<string, RuntimeElement>();
    private readonly List<RuntimeElement> _tickBuffer = new List<RuntimeElement>();
    private readonly RectTransform _verticalSnapGuide;
    private readonly RectTransform _horizontalSnapGuide;
    private bool _disposed;
    private float _time;
    internal Transform CanvasTransform => _root.transform;

    private HudRuntime(GameObject root, ArgonStore store)
    {
        _root = root;
        _store = store;
        _verticalSnapGuide = CreateSnapGuide(root.transform, "ArgonSnapGuide.Vertical");
        _horizontalSnapGuide = CreateSnapGuide(root.transform, "ArgonSnapGuide.Horizontal");
        ArgonApiRuntime.Bind(_store.Save, NotifyChanged);
        ArgonApi.RegistrationChanged += OnExternalRegistrationChanged;
    }

    internal static HudRuntime Create(Transform parent, ArgonStore store)
    {
        var root = new GameObject("ArgonHudCanvas");
        root.transform.SetParent(parent, false);
        HudRuntime? runtime = null;

        try
        {
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 100;
            root.AddComponent<GraphicRaycaster>();

            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            var rootGroup = root.AddComponent<CanvasGroup>();
            rootGroup.alpha = Mathf.Clamp01(store.Document.Preferences.HudOpacity);
            root.transform.localScale = Vector3.one * Mathf.Clamp(store.Document.Preferences.HudScale, 0.25f, 3f);

            runtime = new HudRuntime(root, store);
            runtime.RegisterBuiltIns();
            return runtime;
        }
        catch
        {
            runtime?.Dispose();
            Object.Destroy(root);
            throw;
        }
    }

    internal void Tick(float deltaTime)
    {
        if (_disposed)
        {
            return;
        }

        GameStateSource.Refresh(_store);
        var delta = Mathf.Max(deltaTime, 0f);
        _time += delta;
        _tickBuffer.Clear();
        _tickBuffer.AddRange(_instances.Values);

        foreach (var instance in _tickBuffer)
        {
            var autoHidden = GameStateSource.Preferences.HideHudDuringAuto && GameStateSource.Current.IsAuto &&
                             instance.Definition.Id != "argon.builtin.status";
            var shouldBeActive = instance.Enabled && !instance.Faulted && !autoHidden;
            if (instance.View.Root != null && instance.View.Root.activeSelf != shouldBeActive)
            {
                instance.View.Root.SetActive(shouldBeActive);
            }

            if (!shouldBeActive || instance.View.Root == null || !instance.View.Root.activeInHierarchy)
            {
                continue;
            }

            instance.Elapsed += delta;
            if (!ShouldUpdate(instance))
            {
                continue;
            }

            try
            {
                instance.Definition.UpdateView(instance.View, instance.Elapsed);
                instance.Elapsed = 0f;
            }
            catch (Exception exception)
            {
                // Hide for this session only; one bad frame must not persist Enabled=false into the layout.
                instance.Faulted = true;
                if (instance.View.Root != null)
                {
                    instance.View.Root.SetActive(false);
                }

                Debug.LogError($"[Argon] HUD element '{instance.Definition.Id}' failed and was hidden until re-enabled: {exception}");
            }
        }
    }

    internal bool SetEnabled(string instanceId, bool enabled)
    {
        if (!_instances.TryGetValue(instanceId, out var instance))
        {
            return false;
        }

        if (instance.View.Root == null)
        {
            Remove(instanceId);
            return false;
        }

        instance.Enabled = enabled;
        instance.Faulted = false;
        instance.View.Root.SetActive(enabled);
        instance.Dirty = true;
        _store.Save();
        return true;
    }

    internal bool SetBounds(string instanceId, HudAnchor anchor, Vector2 position, Vector2 size, bool save = true)
    {
        if (!_instances.TryGetValue(instanceId, out var instance))
        {
            return false;
        }

        size.x = Mathf.Max(16f, size.x);
        size.y = Mathf.Max(16f, size.y);
        instance.Layout.Anchor = anchor.ToString();
        instance.Layout.X = position.x;
        instance.Layout.Y = position.y;
        instance.Layout.Width = size.x;
        instance.Layout.Height = size.y;
        HudAnchorUtility.Apply(instance.View.Rect, anchor, position, size);
        if (save) _store.Save();
        return true;
    }

    internal void SaveLayout()
    {
        _store.Save();
    }

    internal void SetEditMode(bool enabled)
    {
        foreach (var instance in _instances.Values)
        {
            if (instance.View.Root == null) continue;
            var handle = GetOrAdd<HudElementEditHandle>(instance.View.Root);
            handle.Initialize(this, instance.InstanceId);
            handle.SetEditing(enabled);
        }

        if (!enabled) HideSnapGuides();
    }

    internal Vector2 SnapPosition(string instanceId, Vector2 position)
    {
        if (!_instances.TryGetValue(instanceId, out var instance)) return position;
        var parentRect = _root.GetComponent<RectTransform>();
        if (parentRect == null) return position;

        var draggedRect = instance.View.Rect;
        var parentBounds = parentRect.rect;
        var anchor = draggedRect.anchorMin;
        var anchorPoint = new Vector2(
            parentBounds.xMin + parentBounds.width * anchor.x,
            parentBounds.yMin + parentBounds.height * anchor.y);
        var pivotPosition = anchorPoint + position;
        var bounds = ScaleRect(draggedRect.rect, draggedRect.localScale);
        var threshold = 9f / Mathf.Max(0.25f, _store.Document.Preferences.HudScale);
        var deltaX = FindSnapDelta(
            pivotPosition.x + bounds.xMin,
            pivotPosition.x + (bounds.xMin + bounds.xMax) * 0.5f,
            pivotPosition.x + bounds.xMax,
            parentBounds.xMin,
            parentBounds.xMax,
            true,
            instanceId,
            threshold,
            out var guideX,
            out var snappedX);
        var deltaY = FindSnapDelta(
            pivotPosition.y + bounds.yMin,
            pivotPosition.y + (bounds.yMin + bounds.yMax) * 0.5f,
            pivotPosition.y + bounds.yMax,
            parentBounds.yMin,
            parentBounds.yMax,
            false,
            instanceId,
            threshold,
            out var guideY,
            out var snappedY);

        SetSnapGuides(snappedX, guideX, snappedY, guideY, parentBounds);
        return position + new Vector2(deltaX, deltaY);
    }

    internal void HideSnapGuides()
    {
        if (_verticalSnapGuide != null) _verticalSnapGuide.gameObject.SetActive(false);
        if (_horizontalSnapGuide != null) _horizontalSnapGuide.gameObject.SetActive(false);
    }

    internal bool SetStyle(string instanceId, float scale, float opacity, bool save = true)
    {
        if (!_instances.TryGetValue(instanceId, out var instance))
        {
            return false;
        }

        instance.Layout.Scale = Mathf.Clamp(scale, 0.1f, 4f);
        instance.Layout.Opacity = Mathf.Clamp01(opacity);
        instance.View.Rect.localScale = Vector3.one * instance.Layout.Scale;
        var group = GetOrAdd<CanvasGroup>(instance.View.Root);
        group.alpha = instance.Layout.Opacity;
        if (save) _store.Save();
        return true;
    }

    internal void SetGlobalStyle(float scale, float opacity, bool save = true)
    {
        _store.Document.Preferences.HudScale = Mathf.Clamp(scale, 0.25f, 3f);
        _store.Document.Preferences.HudOpacity = Mathf.Clamp01(opacity);
        var scaler = _root.GetComponent<CanvasScaler>();
        if (scaler != null)
        {
            scaler.referenceResolution = new Vector2(1920f, 1080f);
        }

        _root.transform.localScale = Vector3.one * _store.Document.Preferences.HudScale;
        var group = _root.GetComponent<CanvasGroup>();
        if (group != null) group.alpha = _store.Document.Preferences.HudOpacity;
        if (save) _store.Save();
    }

    internal bool SetOrder(string instanceId, int order)
    {
        if (!_instances.TryGetValue(instanceId, out var instance)) return false;
        // Remove, then insert at the target index, so dropping onto an occupied order lands exactly there.
        var ordered = _store.ActiveLayout.Elements.Where(element => element != instance.Layout).OrderBy(element => element.Order).ToList();
        ordered.Insert(Mathf.Clamp(order, 0, ordered.Count), instance.Layout);
        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].Order = i;
            if (_instances.TryGetValue(ordered[i].InstanceId, out var current))
            {
                current.View.Root.transform.SetSiblingIndex(i);
            }
        }

        _store.Save();
        return true;
    }

    internal bool CreateInstance(string elementId, out string instanceId)
    {
        instanceId = string.Empty;
        if (!_registry.TryGet(elementId, out var definition))
        {
            return false;
        }

        if (!definition.AllowMultiple && _instances.Values.Any(instance => instance.Definition.Id == elementId)) return false;

        instanceId = elementId + "." + Guid.NewGuid().ToString("N");
        var layoutData = CreateLayoutData(definition, instanceId, _store.ActiveLayout.Elements.Count);
        _store.ActiveLayout.Elements.Add(layoutData);
        if (!Add(definition, layoutData))
        {
            _store.ActiveLayout.Elements.Remove(layoutData);
            instanceId = string.Empty;
            return false;
        }

        _store.Save();
        return true;
    }

    internal bool NotifyChanged(string instanceId)
    {
        if (!_instances.TryGetValue(instanceId, out var instance))
        {
            return false;
        }

        instance.Dirty = true;
        return true;
    }

    internal bool Remove(string instanceId)
    {
        if (!_instances.TryGetValue(instanceId, out var instance))
        {
            return false;
        }

        _instances.Remove(instanceId);
        _store.ActiveLayout.Elements.Remove(instance.Layout);
        instance.View.Dispose();
        _store.Save();
        return true;
    }

    internal bool ActivateLayout(string layoutId)
    {
        if (_store.Document.Layouts.All(layout => layout.Id != layoutId))
        {
            return false;
        }

        _store.Document.ActiveLayoutId = layoutId;
        ReconcileActiveLayout();
        _store.Save();
        return true;
    }

    internal string CreateLayout(string name)
    {
        var id = "layout." + Guid.NewGuid().ToString("N");
        var clone = new HudLayoutData
        {
            Id = id,
            Name = string.IsNullOrWhiteSpace(name) ? "새 레이아웃" : name.Trim(),
            BuiltInDefaultsAdded = true,
            Elements = _store.ActiveLayout.Elements.Select(CloneLayoutData).ToList(),
        };
        _store.Document.Layouts.Add(clone);
        _store.Document.ActiveLayoutId = id;
        ReconcileActiveLayout();
        _store.Save();
        return id;
    }

    internal bool RenameLayout(string layoutId, string name)
    {
        var layout = _store.Document.Layouts.FirstOrDefault(candidate => candidate.Id == layoutId);
        if (layout == null || string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        layout.Name = name.Trim();
        _store.Save();
        return true;
    }

    internal bool DeleteLayout(string layoutId)
    {
        if (_store.Document.Layouts.Count <= 1)
        {
            return false;
        }

        var layout = _store.Document.Layouts.FirstOrDefault(candidate => candidate.Id == layoutId);
        if (layout == null)
        {
            return false;
        }

        _store.Document.Layouts.Remove(layout);
        if (_store.Document.ActiveLayoutId == layoutId)
        {
            _store.Document.ActiveLayoutId = _store.Document.Layouts[0].Id;
            ReconcileActiveLayout();
        }

        _store.Save();
        return true;
    }

    internal IReadOnlyList<HudLayoutData> Layouts => _store.Document.Layouts;
    internal IReadOnlyList<HudElementLayoutData> ActiveElements => _store.ActiveLayout.Elements;
    internal IReadOnlyList<HudLayoutData> LayoutProfiles => _store.Document.Layouts;
    internal string ActiveLayoutId => _store.ActiveLayout.Id;
    internal IReadOnlyCollection<HudElementDefinition> RegisteredDefinitions => _registry.Definitions;
    internal IReadOnlyList<HudElementDefinition> AvailableDefinitions => _registry.Definitions
        .Where(definition => definition.AllowMultiple || _store.ActiveLayout.Elements.All(element => element.ElementId != definition.Id))
        .ToArray();

    internal string GetElementName(string elementId)
    {
        return _registry.TryGet(elementId, out var definition) ? definition.DisplayName : elementId;
    }

    internal bool TryGetElement(string instanceId, out HudElementLayoutData layout)
    {
        if (_instances.TryGetValue(instanceId, out var instance))
        {
            layout = instance.Layout;
            return true;
        }

        layout = null!;
        return false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var instance in _instances.Values)
        {
            instance.View.Dispose();
        }

        _instances.Clear();
        _tickBuffer.Clear();
        ArgonApi.RegistrationChanged -= OnExternalRegistrationChanged;
        ArgonApiRuntime.Unbind();
        if (_root != null)
        {
            Object.Destroy(_root);
        }
    }

    private void RegisterBuiltIns()
    {
        var definitions = new[]
        {
            (FpsHudElement.CreateDefinition(), FpsInstanceId),
            (ProgressHudElement.CreateDefinition(), ProgressInstanceId),
            (AccuracyHudElement.CreateDefinition(), AccuracyInstanceId),
            (BpmHudElement.CreateDefinition(), BpmInstanceId),
            (StatusHudElement.CreateDefinition(), StatusInstanceId),
            (JudgementHudElement.CreateDefinition(), JudgementInstanceId),
            (ComboHudElement.CreateDefinition(), ComboInstanceId),
            (ProgressBarHudElement.CreateDefinition(), ProgressBarInstanceId),
        };

        foreach (var entry in definitions)
        {
            _registry.Register(entry.Item1);
        }

        foreach (var descriptor in ArgonApi.RegisteredElements)
        {
            RegisterExternalDefinition(descriptor);
        }

        if (_store.Document.SchemaVersion < 2)
        {
            foreach (var layout in _store.Document.Layouts)
            {
                foreach (var entry in definitions)
                {
                    if (layout.Elements.All(element => element.InstanceId != entry.Item2))
                    {
                        layout.Elements.Add(CreateLayoutData(entry.Item1, entry.Item2, layout.Elements.Count));
                    }
                }

                layout.BuiltInDefaultsAdded = true;
            }

            _store.Document.SchemaVersion = 2;
            _store.Save();
        }

        ReconcileActiveLayout();
    }

    private float FindSnapDelta(
        float objectMin,
        float objectCenter,
        float objectMax,
        float canvasMin,
        float canvasMax,
        bool horizontal,
        string instanceId,
        float threshold,
        out float guidePosition,
        out bool snapped)
    {
        var bestDistance = threshold;
        var bestDelta = 0f;
        guidePosition = 0f;
        snapped = false;
        var canvasCenter = (canvasMin + canvasMax) * 0.5f;

        ConsiderSnap(objectMin, canvasMin, ref bestDistance, ref bestDelta, ref guidePosition, ref snapped);
        ConsiderSnap(objectMin, canvasCenter, ref bestDistance, ref bestDelta, ref guidePosition, ref snapped);
        ConsiderSnap(objectMin, canvasMax, ref bestDistance, ref bestDelta, ref guidePosition, ref snapped);
        ConsiderSnap(objectCenter, canvasMin, ref bestDistance, ref bestDelta, ref guidePosition, ref snapped);
        ConsiderSnap(objectCenter, canvasCenter, ref bestDistance, ref bestDelta, ref guidePosition, ref snapped);
        ConsiderSnap(objectCenter, canvasMax, ref bestDistance, ref bestDelta, ref guidePosition, ref snapped);
        ConsiderSnap(objectMax, canvasMin, ref bestDistance, ref bestDelta, ref guidePosition, ref snapped);
        ConsiderSnap(objectMax, canvasCenter, ref bestDistance, ref bestDelta, ref guidePosition, ref snapped);
        ConsiderSnap(objectMax, canvasMax, ref bestDistance, ref bestDelta, ref guidePosition, ref snapped);

        foreach (var other in _instances.Values)
        {
            if (other.InstanceId == instanceId || !other.Enabled || other.View.Root == null || !other.View.Root.activeInHierarchy) continue;
            var rect = other.View.Rect;
            var otherBounds = ScaleRect(rect.rect, rect.localScale);
            var pivot = rect.localPosition;
            var otherMin = horizontal ? pivot.x + otherBounds.xMin : pivot.y + otherBounds.yMin;
            var otherCenter = horizontal
                ? pivot.x + (otherBounds.xMin + otherBounds.xMax) * 0.5f
                : pivot.y + (otherBounds.yMin + otherBounds.yMax) * 0.5f;
            var otherMax = horizontal ? pivot.x + otherBounds.xMax : pivot.y + otherBounds.yMax;
            ConsiderSnap(objectMin, otherMin, ref bestDistance, ref bestDelta, ref guidePosition, ref snapped);
            ConsiderSnap(objectMin, otherCenter, ref bestDistance, ref bestDelta, ref guidePosition, ref snapped);
            ConsiderSnap(objectMin, otherMax, ref bestDistance, ref bestDelta, ref guidePosition, ref snapped);
            ConsiderSnap(objectCenter, otherMin, ref bestDistance, ref bestDelta, ref guidePosition, ref snapped);
            ConsiderSnap(objectCenter, otherCenter, ref bestDistance, ref bestDelta, ref guidePosition, ref snapped);
            ConsiderSnap(objectCenter, otherMax, ref bestDistance, ref bestDelta, ref guidePosition, ref snapped);
            ConsiderSnap(objectMax, otherMin, ref bestDistance, ref bestDelta, ref guidePosition, ref snapped);
            ConsiderSnap(objectMax, otherCenter, ref bestDistance, ref bestDelta, ref guidePosition, ref snapped);
            ConsiderSnap(objectMax, otherMax, ref bestDistance, ref bestDelta, ref guidePosition, ref snapped);
        }

        return bestDelta;
    }

    private static Rect ScaleRect(Rect rect, Vector3 scale)
    {
        var min = Vector2.Scale(rect.min, new Vector2(scale.x, scale.y));
        var max = Vector2.Scale(rect.max, new Vector2(scale.x, scale.y));
        return Rect.MinMaxRect(Mathf.Min(min.x, max.x), Mathf.Min(min.y, max.y), Mathf.Max(min.x, max.x), Mathf.Max(min.y, max.y));
    }

    private static void ConsiderSnap(
        float objectPosition,
        float targetPosition,
        ref float bestDistance,
        ref float bestDelta,
        ref float guidePosition,
        ref bool snapped)
    {
        var delta = targetPosition - objectPosition;
        var distance = Mathf.Abs(delta);
        if (distance > bestDistance) return;
        bestDistance = distance;
        bestDelta = delta;
        guidePosition = targetPosition;
        snapped = true;
    }

    private void SetSnapGuides(bool showVertical, float x, bool showHorizontal, float y, Rect canvasBounds)
    {
        if (!showVertical && !showHorizontal)
        {
            HideSnapGuides();
            return;
        }

        _verticalSnapGuide.sizeDelta = new Vector2(2f, canvasBounds.height);
        _verticalSnapGuide.anchoredPosition = new Vector2(x, canvasBounds.center.y);
        _verticalSnapGuide.gameObject.SetActive(showVertical);
        _horizontalSnapGuide.sizeDelta = new Vector2(canvasBounds.width, 2f);
        _horizontalSnapGuide.anchoredPosition = new Vector2(canvasBounds.center.x, y);
        _horizontalSnapGuide.gameObject.SetActive(showHorizontal);
        _verticalSnapGuide.SetAsLastSibling();
        _horizontalSnapGuide.SetAsLastSibling();
    }

    private static RectTransform CreateSnapGuide(Transform parent, string name)
    {
        var guide = new GameObject(name, typeof(RectTransform), typeof(Image));
        guide.transform.SetParent(parent, false);
        var rect = guide.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        var image = guide.GetComponent<Image>();
        image.color = new Color(0.42f, 0.78f, 1f, 0.82f);
        image.raycastTarget = false;
        guide.SetActive(false);
        return rect;
    }

    private void ReconcileActiveLayout()
    {
        foreach (var instance in _instances.Values)
        {
            instance.View.Dispose();
        }

        _instances.Clear();
        _tickBuffer.Clear();

        foreach (var element in _store.ActiveLayout.Elements.OrderBy(element => element.Order))
        {
            if (_registry.TryGet(element.ElementId, out var definition))
            {
                Add(definition, element);
            }
        }
    }

    private bool Add(HudElementDefinition definition, HudElementLayoutData layout)
    {
        if (string.IsNullOrWhiteSpace(layout.InstanceId) || _instances.ContainsKey(layout.InstanceId))
        {
            Debug.LogWarning($"[Argon] HUD instance ID '{layout.InstanceId}' is empty or already in use.");
            return false;
        }

        if (!definition.AllowMultiple)
        {
            foreach (var existing in _instances.Values)
            {
                if (existing.Definition.Id == definition.Id)
                {
                    return false;
                }
            }
        }

        HudElementView? view = null;
        try
        {
            _creatingLayout = layout;
            view = definition.CreateView(_root.transform);
            var anchor = Enum.TryParse(layout.Anchor, true, out HudAnchor parsedAnchor) ? parsedAnchor : definition.DefaultAnchor;
            var position = new Vector2(layout.X, layout.Y);
            var size = new Vector2(Mathf.Max(16f, layout.Width), Mathf.Max(16f, layout.Height));
            HudAnchorUtility.Apply(view.Rect, anchor, position, size);
            layout.Anchor = anchor.ToString();
            layout.Width = size.x;
            layout.Height = size.y;
            layout.Scale = Mathf.Clamp(layout.Scale, 0.1f, 4f);
            layout.Opacity = Mathf.Clamp01(layout.Opacity);
            view.Rect.localScale = Vector3.one * layout.Scale;
            var canvasGroup = GetOrAdd<CanvasGroup>(view.Root);
            canvasGroup.alpha = layout.Opacity;
            view.Root.SetActive(layout.Enabled);
            view.Root.transform.SetSiblingIndex(Mathf.Clamp(layout.Order, 0, Mathf.Max(0, _root.transform.childCount - 1)));
            _instances.Add(layout.InstanceId, new RuntimeElement(layout, definition, view, _time));
            return true;
        }
        catch (Exception exception)
        {
            view?.Dispose();
            Debug.LogError($"[Argon] Could not create HUD element '{definition.Id}': {exception}");
            return false;
        }
        finally
        {
            _creatingLayout = null;
        }
    }

    private static HudElementLayoutData CreateLayoutData(HudElementDefinition definition, string instanceId, int order)
    {
        return new HudElementLayoutData
        {
            InstanceId = instanceId,
            ElementId = definition.Id,
            Enabled = true,
            Anchor = definition.DefaultAnchor.ToString(),
            X = definition.DefaultPosition.x,
            Y = definition.DefaultPosition.y,
            Width = definition.DefaultSize.x,
            Height = definition.DefaultSize.y,
            Order = order,
            Scale = 1f,
            Opacity = 1f,
        };
    }

    private static HudElementLayoutData CloneLayoutData(HudElementLayoutData source)
    {
        var clone = new HudElementLayoutData
        {
            InstanceId = source.InstanceId,
            ElementId = source.ElementId,
            Enabled = source.Enabled,
            Anchor = source.Anchor,
            X = source.X,
            Y = source.Y,
            Width = source.Width,
            Height = source.Height,
            Order = source.Order,
            Scale = source.Scale,
            Opacity = source.Opacity,
            Configuration = (Newtonsoft.Json.Linq.JObject)source.Configuration.DeepClone(),
        };

        return clone;
    }

    private void OnExternalRegistrationChanged(HudElementDescriptor descriptor, bool registered)
    {
        if (_disposed) return;
        if (registered)
        {
            RegisterExternalDefinition(descriptor);
            ReconcileActiveLayout();
            return;
        }

        _registry.Unregister(descriptor.Id);
        var removals = _instances.Values.Where(instance => instance.Definition.Id == descriptor.Id).ToArray();
        foreach (var instance in removals)
        {
            _instances.Remove(instance.InstanceId);
            instance.View.Dispose();
        }
    }

    private void RegisterExternalDefinition(HudElementDescriptor descriptor)
    {
        if (_registry.TryGet(descriptor.Id, out _))
        {
            Debug.LogWarning($"[Argon] External HUD element '{descriptor.Id}' collides with an existing definition.");
            return;
        }

        var updatePolicy = descriptor.RefreshPolicy switch
        {
            HudRefreshPolicy.EveryFrame => HudUpdatePolicy.EveryFrame,
            HudRefreshPolicy.Interval => HudUpdatePolicy.Interval,
            HudRefreshPolicy.OnChange => HudUpdatePolicy.OnChange,
            HudRefreshPolicy.EventDriven => HudUpdatePolicy.EventDriven,
            HudRefreshPolicy.Manual => HudUpdatePolicy.Manual,
            _ => HudUpdatePolicy.Interval,
        };

        var definition = new HudElementDefinition(
            descriptor.Id,
            descriptor.DisplayName,
            descriptor.AllowMultiple,
            (HudAnchor)(int)descriptor.DefaultAnchor,
            descriptor.DefaultPosition,
            descriptor.DefaultSize,
            updatePolicy,
            descriptor.RefreshInterval,
            parent =>
            {
                var layout = _creatingLayout;
                var instanceId = layout?.InstanceId ?? descriptor.Id + ".pending";
                var context = new HudElementContext(instanceId, layout?.Configuration ?? new Newtonsoft.Json.Linq.JObject());
                var root = descriptor.CreateView(parent, context);
                if (root == null) throw new InvalidOperationException($"External HUD element '{descriptor.Id}' returned a null view.");
                if (root.GetComponent<RectTransform>() == null)
                {
                    Object.Destroy(root);
                    throw new InvalidOperationException($"External HUD element '{descriptor.Id}' root must have a RectTransform.");
                }
                if (root.transform.parent != parent) root.transform.SetParent(parent, false);
                return new ExternalHudElementView(root, descriptor, context);
            },
            (view, deltaTime) =>
            {
                var externalView = (ExternalHudElementView)view;
                descriptor.UpdateView(externalView.Context, externalView.Root, deltaTime);
            });

        _registry.Register(definition);
    }

    private HudElementLayoutData? _creatingLayout;

    private bool ShouldUpdate(RuntimeElement instance)
    {
        switch (instance.Definition.UpdatePolicy)
        {
            case HudUpdatePolicy.EveryFrame:
                return true;
            case HudUpdatePolicy.Interval:
                if (_time < instance.NextUpdateAt)
                {
                    return false;
                }

                instance.NextUpdateAt = _time + instance.Definition.UpdateInterval;
                return true;
            case HudUpdatePolicy.OnChange:
            case HudUpdatePolicy.EventDriven:
            case HudUpdatePolicy.Manual:
                if (!instance.Dirty)
                {
                    return false;
                }

                instance.Dirty = false;
                return true;
            default:
                return false;
        }
    }

    // Unity overloads == for destroyed objects; `??` would bypass that check.
    private static T GetOrAdd<T>(GameObject gameObject) where T : Component
    {
        var component = gameObject.GetComponent<T>();
        return component != null ? component : gameObject.AddComponent<T>();
    }

    private sealed class RuntimeElement
    {
        internal string InstanceId => Layout.InstanceId;
        internal HudElementLayoutData Layout { get; }
        internal HudElementDefinition Definition { get; }
        internal HudElementView View { get; }
        internal bool Enabled { get => Layout.Enabled; set => Layout.Enabled = value; }
        internal bool Faulted { get; set; }
        internal bool Dirty { get; set; } = true;
        internal float NextUpdateAt { get; set; }
        internal float Elapsed { get; set; }

        internal RuntimeElement(HudElementLayoutData layout, HudElementDefinition definition, HudElementView view, float createdAt)
        {
            Layout = layout;
            Definition = definition;
            View = view;
            NextUpdateAt = createdAt;
        }
    }
}
