using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Argon.Hud;

internal sealed class HudElementEditHandle : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    private HudRuntime? _runtime;
    private RectTransform? _rect;
    private Image? _raycastImage;
    private Outline? _outline;
    private HudElementResizeHandle? _resizeHandle;
    private string _instanceId = string.Empty;
    private Vector2 _size = new Vector2(200f, 40f);
    private HudAnchor _anchor;
    private bool _dragging;

    internal void Initialize(HudRuntime runtime, string instanceId)
    {
        _runtime = runtime;
        _instanceId = instanceId;
        _rect = GetComponent<RectTransform>();
        if (runtime.TryGetElement(instanceId, out var layout))
        {
            _anchor = System.Enum.TryParse(layout.Anchor, true, out HudAnchor parsed) ? parsed : HudAnchor.Center;
            _size = new Vector2(layout.Width, layout.Height);
        }
    }

    internal void SetEditing(bool editing)
    {
        if (_rect == null)
        {
            _rect = GetComponent<RectTransform>();
        }

        if (editing)
        {
            EnsureControls();
            if (_raycastImage != null) _raycastImage.raycastTarget = true;
            if (_outline != null) _outline.enabled = true;
            if (_resizeHandle != null) _resizeHandle.gameObject.SetActive(true);
            return;
        }

        if (_raycastImage != null) _raycastImage.raycastTarget = false;
        if (_outline != null) _outline.enabled = false;
        if (_resizeHandle != null) _resizeHandle.gameObject.SetActive(false);
        _dragging = false;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (_runtime == null || _rect == null || _resizeHandle != null && _resizeHandle.IsDragging)
        {
            return;
        }

        _dragging = true;
        if (_outline != null) _outline.effectColor = new Color(0.68f, 0.35f, 1f, 1f);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!_dragging || _runtime == null || _rect == null)
        {
            return;
        }

        var scale = Mathf.Max(0.01f, GetComponentInParent<Canvas>()?.scaleFactor ?? 1f);
        var position = _rect.anchoredPosition + eventData.delta / scale;
        _rect.anchoredPosition = _runtime.SnapPosition(_instanceId, position);
        _runtime.SetBounds(_instanceId, _anchor, _rect.anchoredPosition, _rect.sizeDelta, false);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        if (_outline != null) _outline.effectColor = new Color(0.48f, 0.78f, 1f, 1f);
        _runtime?.HideSnapGuides();
        _runtime?.SaveLayout();
    }

    internal void SetSize(Vector2 size, bool commit)
    {
        if (_rect == null || _runtime == null)
        {
            return;
        }

        _size = new Vector2(Mathf.Max(32f, size.x), Mathf.Max(24f, size.y));
        _runtime.SetBounds(_instanceId, _anchor, _rect.anchoredPosition, _size, commit);
    }

    private void EnsureControls()
    {
        if (_rect == null)
        {
            return;
        }

        _raycastImage = GetComponent<Image>() ?? gameObject.AddComponent<Image>();
        var raycastColor = _raycastImage.color;
        raycastColor.a = 0f;
        _raycastImage.color = raycastColor;
        _raycastImage.raycastTarget = true;

        _outline = GetComponent<Outline>() ?? gameObject.AddComponent<Outline>();
        _outline.effectColor = new Color(0.48f, 0.78f, 1f, 1f);
        _outline.effectDistance = new Vector2(2f, -2f);
        _outline.useGraphicAlpha = false;

        if (_resizeHandle != null)
        {
            return;
        }

        var grip = new GameObject("ArgonResizeHandle");
        grip.transform.SetParent(transform, false);
        var rect = grip.AddComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(1f, 0f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(18f, 18f);
        var image = grip.AddComponent<Image>();
        image.color = new Color(0.68f, 0.35f, 1f, 0.9f);
        image.raycastTarget = true;
        _resizeHandle = grip.AddComponent<HudElementResizeHandle>();
        _resizeHandle.Initialize(this);
        grip.SetActive(false);
    }
}

internal sealed class HudElementResizeHandle : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    private HudElementEditHandle? _owner;
    private bool _dragging;

    internal bool IsDragging => _dragging;

    internal void Initialize(HudElementEditHandle owner)
    {
        _owner = owner;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        _dragging = true;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!_dragging || _owner == null)
        {
            return;
        }

        var scale = Mathf.Max(0.01f, GetComponentInParent<Canvas>()?.scaleFactor ?? 1f);
        var rect = transform.parent as RectTransform;
        if (rect != null)
        {
            _owner.SetSize(rect.sizeDelta + eventData.delta / scale, false);
        }
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (_dragging)
        {
            _dragging = false;
            _owner?.SetSize((transform.parent as RectTransform)?.sizeDelta ?? Vector2.zero, true);
        }
    }
}
