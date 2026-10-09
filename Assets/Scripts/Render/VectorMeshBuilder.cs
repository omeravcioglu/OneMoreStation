using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Metro
{
    /// <summary>
    /// Half-width of a stroke: a world size clamped to a pixel range (evaluated in the shader,
    /// so lines keep a sensible thickness at every zoom level without rebuilding meshes).
    /// Pixel sizes are 1080p reference pixels: they scale with the screen height (<see cref="PxScale"/>),
    /// so the map looks the same on a laptop and a 4K monitor.
    /// </summary>
    public struct StrokeWidth
    {
        public float baseHalf, minPx, maxPx;

        /// <summary>Screen pixels per reference pixel (screen height / 1080); kept up to date by the camera.</summary>
        public static float PxScale = 1f;

        public StrokeWidth(float baseHalf, float minPx, float maxPx)
        {
            this.baseHalf = baseHalf;
            this.minPx = minPx;
            this.maxPx = maxPx;
        }

        public static StrokeWidth World(float half, float minPx = 0.5f) => new StrokeWidth(half, minPx, 100000f);
        public static StrokeWidth Pixels(float halfPx) => new StrokeWidth(0f, halfPx, halfPx);
        public StrokeWidth Scaled(float k) => new StrokeWidth(baseHalf * k, minPx * k, maxPx * k);

        /// <summary>Evaluates the world-space half width for a given world-units-per-pixel.</summary>
        public float Evaluate(float pixelWorldSize)
        {
            float px = Mathf.Clamp(baseHalf / Mathf.Max(pixelWorldSize, 1e-6f), minPx * PxScale, maxPx * PxScale);
            return px * pixelWorldSize;
        }
    }

    /// <summary>Accumulates anti-aliased vector primitives into a mesh for the Metro/Vector shader.</summary>
    public sealed class VectorMeshBuilder
    {
        readonly List<Vector3> pos = new List<Vector3>(4096);
        readonly List<Color32> col = new List<Color32>(4096);
        readonly List<Vector2> sdf = new List<Vector2>(4096);
        readonly List<Vector4> off = new List<Vector4>(4096);
        readonly List<Vector4> wpar = new List<Vector4>(4096);
        readonly List<int> tri = new List<int>(8192);

        readonly List<int> inL = new List<int>(), inR = new List<int>(), outL = new List<int>(), outR = new List<int>();

        static readonly Vector4 FillParams = new Vector4(1f, 1f, 1f, 1f);

        public int VertexCount => pos.Count;

        public void Clear()
        {
            pos.Clear();
            col.Clear();
            sdf.Clear();
            off.Clear();
            wpar.Clear();
            tri.Clear();
        }

        int V(Vector2 p, Color32 c, Vector2 s, Vector2 fixedOff, Vector2 growOff, StrokeWidth w, float radiusUnits)
        {
            pos.Add(new Vector3(p.x, p.y, 0f));
            col.Add(c);
            sdf.Add(s);
            off.Add(new Vector4(fixedOff.x, fixedOff.y, growOff.x, growOff.y));
            wpar.Add(new Vector4(w.baseHalf, w.minPx, w.maxPx, radiusUnits));
            return pos.Count - 1;
        }

        int VFill(Vector2 p, Color32 c)
        {
            pos.Add(new Vector3(p.x, p.y, 0f));
            col.Add(c);
            sdf.Add(Vector2.zero);
            off.Add(Vector4.zero);
            wpar.Add(FillParams);
            return pos.Count - 1;
        }

        void Quad(int a, int b, int c, int d)
        {
            tri.Add(a); tri.Add(b); tri.Add(c);
            tri.Add(a); tri.Add(c); tri.Add(d);
        }

        /// <summary>Filled circle of radius (radiusUnits × half-width) centred at center + offsetUnits × half-width.</summary>
        public void AddDisc(Vector2 center, StrokeWidth w, float radiusUnits, Color32 c, Vector2 offsetUnits = default)
        {
            int a = V(center, c, new Vector2(-1f, -1f), offsetUnits, new Vector2(-1f, -1f) * radiusUnits, w, radiusUnits);
            int b = V(center, c, new Vector2(1f, -1f), offsetUnits, new Vector2(1f, -1f) * radiusUnits, w, radiusUnits);
            int d = V(center, c, new Vector2(1f, 1f), offsetUnits, new Vector2(1f, 1f) * radiusUnits, w, radiusUnits);
            int e = V(center, c, new Vector2(-1f, 1f), offsetUnits, new Vector2(-1f, 1f) * radiusUnits, w, radiusUnits);
            Quad(a, b, d, e);
        }

        /// <summary>
        /// Stroke along a polyline with miter joins (bevel + round join on sharp turns) and optional round caps.
        /// laneUnits offsets the whole stroke sideways (parallel lines on a shared corridor).
        /// </summary>
        public void AddPolyline(IList<Vector2> pts, StrokeWidth w, Color32 c, float laneUnits = 0f, bool closed = false,
            bool roundCaps = true, float halfUnits = 1f, IList<float> scales = null)
        {
            int n = pts.Count;
            if (n < 2) return;
            inL.Clear();
            inR.Clear();
            outL.Clear();
            outR.Clear();
            for (int i = 0; i < n; i++)
            {
                Vector2 p = pts[i];
                bool hasPrev = closed || i > 0;
                bool hasNext = closed || i < n - 1;
                Vector2 dPrev = hasPrev ? p - pts[(i - 1 + n) % n] : Vector2.zero;
                Vector2 dNext = hasNext ? pts[(i + 1) % n] - p : Vector2.zero;
                if (dPrev.sqrMagnitude < 1e-14f) hasPrev = false;
                if (dNext.sqrMagnitude < 1e-14f) hasNext = false;
                float s = scales != null ? scales[i] : 1f;
                float hu = halfUnits * s;
                if (hasPrev && hasNext)
                {
                    Vector2 n1 = Geo.Perp(dPrev.normalized), n2 = Geo.Perp(dNext.normalized);
                    Vector2 sum = n1 + n2;
                    bool bevel = sum.sqrMagnitude < 1e-6f;
                    Vector2 m = Vector2.zero;
                    if (!bevel)
                    {
                        m = sum.normalized;
                        float len = 1f / Mathf.Max(Vector2.Dot(m, n1), 1e-4f);
                        if (len > 2.2f) bevel = true;
                        else m *= len;
                    }
                    if (!bevel)
                    {
                        int L = V(p, c, new Vector2(0f, 1f), m * laneUnits, m * hu, w, hu);
                        int R = V(p, c, new Vector2(0f, -1f), m * laneUnits, -m * hu, w, hu);
                        inL.Add(L); inR.Add(R); outL.Add(L); outR.Add(R);
                    }
                    else
                    {
                        inL.Add(V(p, c, new Vector2(0f, 1f), n1 * laneUnits, n1 * hu, w, hu));
                        inR.Add(V(p, c, new Vector2(0f, -1f), n1 * laneUnits, -n1 * hu, w, hu));
                        outL.Add(V(p, c, new Vector2(0f, 1f), n2 * laneUnits, n2 * hu, w, hu));
                        outR.Add(V(p, c, new Vector2(0f, -1f), n2 * laneUnits, -n2 * hu, w, hu));
                        Vector2 avg = (n1 + n2);
                        avg = avg.sqrMagnitude > 1e-6f ? avg.normalized : n1;
                        AddDisc(p, w, hu, c, avg * laneUnits);
                    }
                }
                else
                {
                    Vector2 d = hasNext ? dNext : dPrev;
                    Vector2 nn = d.sqrMagnitude > 1e-14f ? Geo.Perp(d.normalized) : Vector2.up;
                    int L = V(p, c, new Vector2(0f, 1f), nn * laneUnits, nn * hu, w, hu);
                    int R = V(p, c, new Vector2(0f, -1f), nn * laneUnits, -nn * hu, w, hu);
                    inL.Add(L); inR.Add(R); outL.Add(L); outR.Add(R);
                }
            }
            int segs = closed ? n : n - 1;
            for (int k = 0; k < segs; k++)
            {
                int i = k, j = (k + 1) % n;
                if ((pts[j] - pts[i]).sqrMagnitude < 1e-14f) continue;
                Quad(outL[i], inL[j], inR[j], outR[i]);
            }
            if (!closed && roundCaps)
            {
                Vector2 d0 = pts[1] - pts[0];
                Vector2 d1 = pts[n - 1] - pts[n - 2];
                Vector2 n0 = d0.sqrMagnitude > 1e-14f ? Geo.Perp(d0.normalized) : Vector2.up;
                Vector2 n1 = d1.sqrMagnitude > 1e-14f ? Geo.Perp(d1.normalized) : Vector2.up;
                float s0 = scales != null ? scales[0] : 1f, s1 = scales != null ? scales[n - 1] : 1f;
                AddDisc(pts[0], w, halfUnits * s0, c, n0 * laneUnits);
                AddDisc(pts[n - 1], w, halfUnits * s1, c, n1 * laneUnits);
            }
        }

        /// <summary>A straight stroke between two points with round caps.</summary>
        public void AddSegment(Vector2 a, Vector2 b, StrokeWidth w, Color32 c, float halfUnits = 1f, float laneUnits = 0f, bool roundCaps = true)
        {
            if ((b - a).sqrMagnitude < 1e-12f)
            {
                AddDisc(a, w, halfUnits, c);
                return;
            }
            Vector2 n = Geo.Perp((b - a).normalized);
            int a0 = V(a, c, new Vector2(0f, 1f), n * laneUnits, n * halfUnits, w, halfUnits);
            int a1 = V(a, c, new Vector2(0f, -1f), n * laneUnits, -n * halfUnits, w, halfUnits);
            int b0 = V(b, c, new Vector2(0f, 1f), n * laneUnits, n * halfUnits, w, halfUnits);
            int b1 = V(b, c, new Vector2(0f, -1f), n * laneUnits, -n * halfUnits, w, halfUnits);
            Quad(a0, b0, b1, a1);
            if (roundCaps)
            {
                AddDisc(a, w, halfUnits, c, n * laneUnits);
                AddDisc(b, w, halfUnits, c, n * laneUnits);
            }
        }

        /// <summary>
        /// A capsule centred at center, oriented along dir, whose length and thickness are both in half-width units,
        /// so it scales with zoom like the lines (used for trains).
        /// </summary>
        public void AddCapsule(Vector2 center, Vector2 dir, float halfLenUnits, float halfWidthUnits, StrokeWidth w, Color32 c, Vector2 offsetUnits)
        {
            dir = dir.sqrMagnitude > 1e-12f ? dir.normalized : Vector2.right;
            Vector2 n = Geo.Perp(dir);
            float body = Mathf.Max(0f, halfLenUnits - halfWidthUnits);
            Vector2 fa = offsetUnits - dir * body, fb = offsetUnits + dir * body;
            int a0 = V(center, c, new Vector2(0f, 1f), fa, n * halfWidthUnits, w, halfWidthUnits);
            int a1 = V(center, c, new Vector2(0f, -1f), fa, -n * halfWidthUnits, w, halfWidthUnits);
            int b0 = V(center, c, new Vector2(0f, 1f), fb, n * halfWidthUnits, w, halfWidthUnits);
            int b1 = V(center, c, new Vector2(0f, -1f), fb, -n * halfWidthUnits, w, halfWidthUnits);
            Quad(a0, b0, b1, a1);
            AddDisc(center, w, halfWidthUnits, c, fa);
            AddDisc(center, w, halfWidthUnits, c, fb);
        }

        /// <summary>Filled polygon in world space, triangulated by the caller (indices into pts).</summary>
        public void AddFill(IList<Vector2> pts, IList<int> tris, Color32 c)
        {
            int baseIndex = pos.Count;
            for (int i = 0; i < pts.Count; i++) VFill(pts[i], c);
            for (int i = 0; i < tris.Count; i++) tri.Add(baseIndex + tris[i]);
        }

        public void AddRect(Rect r, Color32 c)
        {
            int a = VFill(new Vector2(r.xMin, r.yMin), c);
            int b = VFill(new Vector2(r.xMax, r.yMin), c);
            int d = VFill(new Vector2(r.xMax, r.yMax), c);
            int e = VFill(new Vector2(r.xMin, r.yMax), c);
            Quad(a, b, d, e);
        }

        /// <summary>Filled region between a function curve and a horizontal baseline (used for the sea).</summary>
        public void AddStripBelow(IList<Vector2> top, float baselineY, Color32 c)
        {
            if (top.Count < 2) return;
            int prevTop = VFill(top[0], c);
            int prevBot = VFill(new Vector2(top[0].x, baselineY), c);
            for (int i = 1; i < top.Count; i++)
            {
                int t = VFill(top[i], c);
                int b = VFill(new Vector2(top[i].x, baselineY), c);
                Quad(prevBot, b, t, prevTop);
                prevTop = t;
                prevBot = b;
            }
        }

        /// <summary>Dashed stroke along a polyline (dash/gap lengths in world units), optionally shifted sideways by laneUnits.</summary>
        public void AddDashed(IList<Vector2> pts, StrokeWidth w, Color32 c, float dash, float gap, float halfUnits = 1f, float laneUnits = 0f, float offset = 0f)
        {
            // offset shifts the pattern along the path (marching ants).
            float period = dash + gap;
            float phase = period > 1e-6f ? ((offset % period) + period) % period : 0f;
            bool on = true;
            if (phase >= dash)
            {
                phase -= dash;
                on = false;
            }
            for (int i = 0; i < pts.Count - 1; i++)
            {
                Vector2 a = pts[i], b = pts[i + 1];
                float len = Vector2.Distance(a, b);
                float t = 0f;
                while (t < len)
                {
                    float remain = (on ? dash : gap) - phase;
                    float step = Mathf.Min(remain, len - t);
                    if (on)
                    {
                        Vector2 p0 = Vector2.Lerp(a, b, t / len), p1 = Vector2.Lerp(a, b, (t + step) / len);
                        AddSegment(p0, p1, w, c, halfUnits, laneUnits, false);
                    }
                    t += step;
                    phase += step;
                    if (phase >= (on ? dash : gap) - 1e-5f)
                    {
                        phase = 0f;
                        on = !on;
                    }
                }
            }
        }

        public void Apply(Mesh mesh, Bounds bounds)
        {
            mesh.Clear();
            mesh.indexFormat = pos.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(pos);
            mesh.SetColors(col);
            mesh.SetUVs(0, sdf);
            mesh.SetUVs(1, off);
            mesh.SetUVs(2, wpar);
            mesh.SetTriangles(tri, 0, false);
            mesh.bounds = bounds;
        }
    }
}
