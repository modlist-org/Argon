using System;

namespace Argon.KeyViewer;

internal static class KeyLayoutGeometry
{
    internal const float Grid = 10f;
    internal const float DefaultSize = 60f;
    internal const float HandY = 40f;
    internal const float FootY = HandY;

    internal static float CenterX(int index, int count, float size)
    {
        var pitch = (float)Math.Ceiling(size / Grid) * Grid + Grid;
        var width = (count - 1) * pitch + size;
        var left = (float)Math.Floor(-width * 0.5f / Grid) * Grid;
        return left + size * 0.5f + index * pitch;
    }
}
