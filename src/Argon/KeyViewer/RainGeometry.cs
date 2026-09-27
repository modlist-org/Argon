using System;

namespace Argon.KeyViewer;

internal static class RainGeometry
{
    // A held note grows from the key edge. On release its tail follows the head.
    internal static bool Sample(float now, float started, float? released, float speed, float distance,
        out float bottom, out float height, out float opacity)
    {
        speed = Math.Max(10f, speed);
        distance = Math.Max(20f, distance);
        bottom = released.HasValue ? Math.Max(0f, now - released.Value) * speed : 0f;
        var head = Math.Max(4f, Math.Max(0f, (released ?? now) - started) * speed) + bottom;
        height = Math.Max(0f, Math.Min(distance, head) - bottom);
        opacity = Math.Min(1f, Math.Max(0f, (distance - bottom) / Math.Min(40f, distance)));
        return height > 0f;
    }
}
