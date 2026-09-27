using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Argon.KeyViewer;

/// <summary>
/// Draws every rain note as one quad in a single mesh, so notes cost no GameObjects and no
/// per-note canvas work. Technique adapted from Quartz (GPL-3.0) modules/KeyViewer/RainRenderer.cs.
/// </summary>
internal sealed class RainGraphic : MaskableGraphic
{
    internal struct Quad
    {
        internal Rect Rect;
        internal Color32 Color;
    }

    internal readonly List<Quad> Quads = new List<Quad>();

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        for (var i = 0; i < Quads.Count; i++)
        {
            var quad = Quads[i];
            var r = quad.Rect;
            var start = vh.currentVertCount;
            vh.AddVert(new Vector3(r.xMin, r.yMin), quad.Color, Vector2.zero);
            vh.AddVert(new Vector3(r.xMin, r.yMax), quad.Color, Vector2.up);
            vh.AddVert(new Vector3(r.xMax, r.yMax), quad.Color, Vector2.one);
            vh.AddVert(new Vector3(r.xMax, r.yMin), quad.Color, Vector2.right);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start + 2, start + 3, start);
        }
    }
}
