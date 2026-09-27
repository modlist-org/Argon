using System;
using System.IO;
using Argon.KeyViewer;
using Argon.UI;

static void Near(float actual, float expected)
{
    if (Math.Abs(actual - expected) > 0.001f)
        throw new Exception($"Expected {expected}, got {actual}");
}

// Held notes grow upwards without leaving the key, then detach on release.
RainGeometry.Sample(0.5f, 0f, null, 100f, 200f, out var bottom, out var height, out var alpha);
Near(bottom, 0f); Near(height, 50f); Near(alpha, 1f);
RainGeometry.Sample(0.75f, 0f, 0.5f, 100f, 200f, out bottom, out height, out alpha);
Near(bottom, 25f); Near(height, 50f);
// A down/up pair received in one frame must still leave a visible short note.
RainGeometry.Sample(1f, 1f, 1f, 100f, 200f, out bottom, out height, out alpha);
Near(height, 4f);
// A long hold stays bounded but does not disappear while held.
if (!RainGeometry.Sample(100f, 0f, null, 100f, 200f, out bottom, out height, out alpha))
    throw new Exception("Held note disappeared");
Near(bottom, 0f); Near(height, 200f);
if (RainGeometry.Sample(3f, 0f, 0.5f, 100f, 200f, out bottom, out height, out alpha))
    throw new Exception("Released note did not expire");
// Geometry/fading must not depend on the number of rendered frames.
foreach (var fps in new[] { 30, 60, 144 })
{
    for (var frame = 0; frame <= fps; frame++)
        RainGeometry.Sample(frame / (float)fps, 0f, 0.5f, 100f, 200f, out bottom, out height, out alpha);
    Near(bottom, 50f); Near(height, 50f); Near(alpha, 1f);
}
// The viewer must never mutate the game's key filter or own the shared hook.
var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var runtime = File.ReadAllText(Path.Combine(root, "src/Argon/KeyViewer/KeyViewerRuntime.cs"));
foreach (var forbidden in new[] { "Persistence.keyLimiterKeys", "SkyHookManager.StartHook(", "SkyHookManager.StopHook(" })
    if (runtime.Contains(forbidden)) throw new Exception("Input ownership regression: " + forbidden);
// Zoom preserves the content point under the pointer, including negative pans.
foreach (var zoom in new[] { 0.25f, 1f, 2f })
{
    var pan = EditorViewportMath.ZoomPan(127f, -42f, 0.75f, zoom);
    Near((127f - pan) / zoom, (127f + 42f) / 0.75f);
    Near(EditorViewportMath.ZoomPan(127f, pan, zoom, 0.75f), -42f);
}
// Snapping must include the slot/lane origin rather than quantizing offsets alone.
Near(EditorViewportMath.SnapOffset(2f, 30.5f, 10f) + 30.5f, 30f);
Near(EditorViewportMath.SnapOffset(-9f, -30.5f, 10f) - 30.5f, -40f);
// Off-grid resize starts must snap the moving edge, keeping the opposite edge fixed.
// Every default layout starts on the very same grid used for dragging/resetting.
foreach (var count in new[] { 2, 4, 6, 8, 10, 12, 16, 20 })
{
    for (var index = 0; index < count; index++)
    {
        var center = KeyLayoutGeometry.CenterX(index, count, KeyLayoutGeometry.DefaultSize);
        var left = center - 30f;
        Near(left / 10f, (float)Math.Round(left / 10f));
        Near((center + 30f) / 10f, (float)Math.Round((center + 30f) / 10f));
        Near(EditorViewportMath.SnapOffset(0f, left, 10f), 0f);
        if (index > 0) Near(center - KeyLayoutGeometry.CenterX(index - 1, count, 60f), 70f);
    }
}
foreach (var y in new[] { KeyLayoutGeometry.HandY, KeyLayoutGeometry.FootY })
{
    Near((y + 30f) / 10f, (float)Math.Round((y + 30f) / 10f));
    Near((y - 30f) / 10f, (float)Math.Round((y - 30f) / 10f));
}
foreach (var direction in new[] { -1f, 1f })
{
    var center = 31.5f;
    var size = EditorViewportMath.ResizeAxis(center, 56f, direction, direction * 3f, true, out var shift);
    Near(center + shift - direction * size * 0.5f, center - direction * 28f);
    var movingEdge = center + shift + direction * size * 0.5f;
    Near(movingEdge / 10f, (float)Math.Round(movingEdge / 10f));
}
Near(EditorViewportMath.ResizeAxis(31.5f, 56f, 0f, 19f, true, out var inactiveShift), 56f);
Near(inactiveShift, 0f);
Near(EditorViewportMath.ResizeAxis(31.5f, 56f, 1f, 3f, false, out var freeShift), 59f);
Near(freeShift, 1.5f);
Near(EditorViewportMath.ResizeAxis(31.5f, 56f, 1f, -1000f, true, out var limitedShift), 24f);
Near(31.5f + limitedShift - 12f, 31.5f - 28f);
// Selection must not destroy the target during pointer-down or rebuild unrelated pages.
// Literal fixtures from JRP Initialize0/1/3KeyViewer (left edge, vertical delta, width).
foreach (var count in new[] { 10, 12, 16, 20 })
    for (var i = 0; i < 8; i++)
    {
        JrpKeyLayout.Key(count, i, out var x, out var y, out var width);
        Near(x - width / 2f + 214f, 54f * i); Near(y, 0f); Near(width, 50f);
    }
var fixtures = new[] {
    (10, 8, 81f, 131f), (10, 9, 216f, 131f),
    (12, 8, 135f, 77f), (12, 9, 81f, 50f), (12, 10, 216f, 77f), (12, 11, 297f, 50f),
    (16, 12, 0f, 50f), (16, 13, 54f, 50f), (16, 9, 108f, 50f), (16, 8, 162f, 50f),
    (16, 10, 216f, 50f), (16, 11, 270f, 50f), (16, 14, 324f, 50f), (16, 15, 378f, 50f)
};
foreach (var (count, slot, left, expectedWidth) in fixtures)
{
    JrpKeyLayout.Key(count, slot, out var x, out var y, out var width);
    Near(x - width / 2f + 214f, left); Near(y, -54f); Near(width, expectedWidth);
}
// JRP 20-key: the same ordered middle row as 16-key, plus four bottom keys.
for (var slot = 8; slot < 16; slot++)
{
    JrpKeyLayout.Key(16, slot, out var expectedX, out var expectedY, out var expectedWidth);
    JrpKeyLayout.Key(20, slot, out var x, out var y, out var width);
    Near(x, expectedX); Near(y, expectedY); Near(width, expectedWidth);
}
foreach (var (slot, left, expectedWidth) in new[] { (16, 135f, 77f), (17, 81f, 50f), (18, 216f, 77f), (19, 297f, 50f) })
{
    JrpKeyLayout.Key(20, slot, out var x, out var y, out var width);
    Near(x - width / 2f + 214f, left); Near(y, -108f); Near(width, expectedWidth);
}
foreach (var count in new[] { 10, 12, 16, 20 })
    foreach (var total in new[] { false, true })
    {
        JrpKeyLayout.Stat(count, total, out var x, out var y, out var width, out var statHeight);
        Near(x - width / 2f + 214f, total ? count == 16 ? 216f : 351f : 0f);
        Near(y, count == 16 ? -100f : count == 20 ? -108f : -54f);
        Near(width, count == 16 ? 212f : 77f);
        Near(statHeight, count == 16 ? 30f : 50f);
    }
foreach (var count in new[] { 2, 4, 6, 8, 16 })
{
    var columns = count == 16 ? 8 : count;
    for (var slot = 0; slot < count; slot++)
    {
        JrpKeyLayout.Foot(20, count, slot, out var x, out var y);
        var index = slot % columns;
        Near(x - 15f + 214f, 432f + (index / 2 + (index % 2 == 0 ? 0 : columns / 2)) * 34f);
        Near(y, -118f + slot / columns * 30f);
    }
}
var host = File.ReadAllText(Path.Combine(root, "src/Argon/UI/ArgonHost.cs"));
var previewStart = host.IndexOf("private void BuildEditorPreviewKeys(", StringComparison.Ordinal);
var previewEnd = host.IndexOf("private static List<GameObject> CreateEditorSelectionHandles", previewStart, StringComparison.Ordinal);
var preview = host.Substring(previewStart, previewEnd - previewStart);
if (preview.Contains("() => SelectKeySlot") || !preview.Contains("GetComponent<OventHandler>().enabled = false"))
    throw new Exception("Preview mouse-down selection regression");
var selectStart = host.IndexOf("internal void SelectKeySlot(", StringComparison.Ordinal);
var selectEnd = host.IndexOf("private void BuildLayoutPage(", selectStart, StringComparison.Ordinal);
if (host.Substring(selectStart, selectEnd - selectStart).Contains("BuildWindowContent()"))
    throw new Exception("Selection rebuilds the entire window");
// Allocation-free counters must print exactly what ToString() would.
var digits = new char[16];
foreach (var value in new[] { 0, 7, 10, 999, 123456, -42, int.MaxValue, int.MinValue })
{
    var length = Argon.NumberText.Write(value, digits);
    var written = new string(digits, digits.Length - length, length);
    if (written != value.ToString(System.Globalization.CultureInfo.InvariantCulture))
        throw new Exception($"NumberText wrote {written} for {value}");
}
// Progress/attempt records must not write the whole config synchronously every tile.
var store = File.ReadAllText(Path.Combine(root, "src/Argon/Storage/ArgonStore.cs"));
var saveStart = store.IndexOf("internal void Save()", StringComparison.Ordinal);
var saveBody = store.Substring(saveStart, store.IndexOf("internal void Tick(", saveStart, StringComparison.Ordinal) - saveStart);
if (saveBody.Contains("WriteNow(") || saveBody.Contains("File."))
    throw new Exception("ArgonStore.Save must only mark the document dirty");
Console.WriteLine("PASS: counters, debounced saves;");
Console.WriteLine("PASS: JRP 10/12/16/20 keys, stats, foot layouts; rain, input, editor regression guards.");
