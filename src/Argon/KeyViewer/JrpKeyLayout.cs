namespace Argon.KeyViewer;

// JRP KeyViewer.Initialize0/1/2/3KeyViewer; coordinates are key centers.
internal static class JrpKeyLayout
{
    private static readonly int[] Back = { 12, 13, 9, 8, 10, 11, 14, 15 };

    internal static void Key(int count, int slot, out float x, out float y, out float width)
    {
        width = 50f;
        y = 0f;
        if (slot < 8) x = 54f * slot;
        else if (count == 10)
        {
            x = slot == 8 ? 81f : 216f;
            y = -54f;
            width = 131f;
        }
        else if (count == 12 || (count == 20 && slot >= 16))
        {
            var index = count == 20 ? slot - 8 : slot;
            x = index == 8 ? 135f : index == 9 ? 81f : index == 10 ? 216f : 297f;
            width = index == 8 || index == 10 ? 77f : 50f;
            y = count == 20 ? -108f : -54f;
        }
        else
        {
            x = 0f;
            for (var i = 0; i < Back.Length; i++) if (Back[i] == slot) x = 54f * i;
            y = -54f;
        }
        x += width * 0.5f - 214f;
    }

    internal static void Stat(int count, bool total, out float x, out float y, out float width, out float height)
    {
        var slim = count == 16;
        width = slim ? 212f : 77f;
        height = slim ? 30f : 50f;
        x = (total ? (slim ? 216f : 351f) : 0f) + width * 0.5f - 214f;
        y = slim ? -100f : count == 20 ? -108f : -54f;
    }

    internal static void Foot(int handCount, int count, int slot, out float x, out float y)
    {
        var columns = count > 10 ? count / 2 : count;
        var index = slot % columns;
        var column = index % 2 == 0 ? index / 2 : (columns + 1) / 2 + index / 2;
        x = 432f + column * 34f + 15f - 214f;
        var top = handCount == 16 ? 115f : handCount == 20 ? 133f : 79f;
        y = 15f + slot / columns * 30f - top;
    }
}
