using UnityEngine;
using UnityEngine.UI;

namespace Argon.KeyViewer;

// CSS-like 4px keycap geometry, independent of the O5Kit control sprite/PPU.
internal sealed class KeycapGraphic : Image
{
    public override Texture mainTexture => Texture2D.whiteTexture;
    internal Color BorderColor = new Color(1f, 1f, 1f, 0.14f);
    internal float BorderWidth = 1f;
    internal float VisualScale = 1f;

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        var rect = GetPixelAdjustedRect();
        var radius = Mathf.Min(4f * VisualScale, Mathf.Min(rect.width, rect.height) * 0.5f);
        var border = Mathf.Clamp(BorderWidth, 0f, Mathf.Min(rect.width, rect.height) * 0.5f);
        var inner = new Rect(rect.x + border, rect.y + border,
            Mathf.Max(0f, rect.width - border * 2f), Mathf.Max(0f, rect.height - border * 2f));
        const int points = 36;
        for (var layer = 4; layer >= 0; layer--)
        {
            var spread = layer * VisualScale;
            var shadowRect = new Rect(rect.x - spread, rect.y - spread - 4f * VisualScale,
                rect.width + spread * 2f, rect.height + spread * 2f);
            var start = mesh.currentVertCount;
            var shadow = new Color(0f, 0f, 0f, 0.06f);
            mesh.AddVert(shadowRect.center, shadow, Vector2.zero);
            for (var i = 0; i < points; i++)
                mesh.AddVert(CornerPoint(shadowRect, radius + spread, i), shadow, Vector2.zero);
            for (var i = 0; i < points; i++)
                mesh.AddTriangle(start, start + 1 + i, start + 1 + (i + 1) % points);
        }
        var faceStart = mesh.currentVertCount;
        mesh.AddVert(inner.center, color, Vector2.zero);
        for (var i = 0; i < points; i++)
        {
            var outerPoint = CornerPoint(rect, radius, i);
            var innerPoint = CornerPoint(inner, Mathf.Max(0f, radius - border), i);
            mesh.AddVert(innerPoint, color, Vector2.zero);
            var edgeColor = BorderColor;
            edgeColor.a *= Mathf.Lerp(0.286f, 1f, Mathf.InverseLerp(rect.yMin, rect.yMax, outerPoint.y));
            mesh.AddVert(outerPoint, edgeColor, Vector2.zero);
            mesh.AddVert(innerPoint, edgeColor, Vector2.zero);
        }
        for (var i = 0; i < points; i++)
        {
            var a = faceStart + 1 + i * 3;
            var b = faceStart + 1 + ((i + 1) % points) * 3;
            mesh.AddTriangle(faceStart, a, b);
            if (border <= 0f) continue;
            mesh.AddTriangle(a + 1, b + 1, b + 2);
            mesh.AddTriangle(a + 1, b + 2, a + 2);
        }
    }

    private static Vector2 CornerPoint(Rect rect, float radius, int index)
    {
        var corner = index / 9;
        var angle = (corner * 90f + index % 9 * 90f / 8f) * Mathf.Deg2Rad;
        var center = new Vector2(corner == 0 || corner == 3 ? rect.xMax - radius : rect.xMin + radius,
            corner < 2 ? rect.yMax - radius : rect.yMin + radius);
        return center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
    }
}
