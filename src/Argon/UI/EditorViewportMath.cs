using System;

namespace Argon.UI;

internal static class EditorViewportMath
{
    internal static float ZoomPan(float pointer, float pan, float oldZoom, float newZoom) =>
        pointer - (pointer - pan) * newZoom / oldZoom;

    internal static float SnapOffset(float offset, float origin, float spacing) =>
        (float)Math.Round((origin + offset) / spacing) * spacing - origin;

    internal static float ResizeAxis(float center, float size, float direction, float delta, bool snap,
        out float centerShift)
    {
        if (direction == 0f)
        {
            centerShift = 0f;
            return size;
        }
        var fixedEdge = center - direction * size * 0.5f;
        var movingEdge = center + direction * size * 0.5f + delta;
        if (snap) movingEdge = SnapOffset(movingEdge, 0f, 10f);
        var result = Math.Max(24f, Math.Min(240f, direction * (movingEdge - fixedEdge)));
        centerShift = direction * (result - size) * 0.5f;
        return result;
    }
}
