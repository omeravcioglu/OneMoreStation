using System.Collections.Generic;
using UnityEngine;

namespace Metro
{
    /// <summary>2D geometry helpers used by generation, rendering and picking.</summary>
    public static class Geo
    {
        public static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
        public static Vector2 Perp(Vector2 v) => new Vector2(-v.y, v.x);

        public static float DistToSegment(Vector2 p, Vector2 a, Vector2 b, out float t)
        {
            Vector2 ab = b - a;
            float len2 = ab.sqrMagnitude;
            t = len2 > 1e-12f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2) : 0f;
            return Vector2.Distance(p, a + ab * t);
        }

        public static float DistToPolyline(Vector2 p, IList<Vector2> pts, out int segIndex, out float segT)
        {
            float best = float.MaxValue;
            segIndex = -1;
            segT = 0f;
            for (int i = 0; i < pts.Count - 1; i++)
            {
                float d = DistToSegment(p, pts[i], pts[i + 1], out float t);
                if (d < best)
                {
                    best = d;
                    segIndex = i;
                    segT = t;
                }
            }
            return best;
        }

        public static float PolylineLength(IList<Vector2> pts)
        {
            float len = 0f;
            for (int i = 0; i < pts.Count - 1; i++) len += Vector2.Distance(pts[i], pts[i + 1]);
            return len;
        }

        public static float SignedArea(IList<Vector2> poly)
        {
            float a = 0f;
            int n = poly.Count;
            for (int i = 0; i < n; i++)
            {
                Vector2 p = poly[i], q = poly[(i + 1) % n];
                a += p.x * q.y - q.x * p.y;
            }
            return a * 0.5f;
        }

        public static bool PointInPolygon(Vector2 p, IList<Vector2> poly)
        {
            bool inside = false;
            int n = poly.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                Vector2 a = poly[i], b = poly[j];
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x)
                    inside = !inside;
            }
            return inside;
        }

        public static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Cross(b - a, p - a);
            float d2 = Cross(c - b, p - b);
            float d3 = Cross(a - c, p - c);
            bool hasNeg = d1 < 0 || d2 < 0 || d3 < 0;
            bool hasPos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(hasNeg && hasPos);
        }

        /// <summary>Ear-clipping triangulation of a simple polygon. Returns indices into the input.</summary>
        public static List<int> Triangulate(IList<Vector2> poly)
        {
            var result = new List<int>();
            int n = poly.Count;
            if (n < 3) return result;
            var idx = new List<int>(n);
            bool ccw = SignedArea(poly) > 0f;
            for (int i = 0; i < n; i++) idx.Add(ccw ? i : n - 1 - i);

            int guard = 0;
            while (idx.Count > 3 && guard++ < n * n + 100)
            {
                bool clipped = false;
                int count = idx.Count;
                for (int i = 0; i < count; i++)
                {
                    int i0 = idx[(i + count - 1) % count], i1 = idx[i], i2 = idx[(i + 1) % count];
                    Vector2 a = poly[i0], b = poly[i1], c = poly[i2];
                    if (Cross(b - a, c - b) <= 1e-9f) continue;
                    bool contains = false;
                    for (int j = 0; j < count; j++)
                    {
                        int k = idx[j];
                        if (k == i0 || k == i1 || k == i2) continue;
                        if (PointInTriangle(poly[k], a, b, c)) { contains = true; break; }
                    }
                    if (contains) continue;
                    result.Add(i0); result.Add(i1); result.Add(i2);
                    idx.RemoveAt(i);
                    clipped = true;
                    break;
                }
                if (!clipped)
                {
                    // Degenerate input: clip anyway so we always terminate.
                    result.Add(idx[0]); result.Add(idx[1]); result.Add(idx[2]);
                    idx.RemoveAt(1);
                }
            }
            if (idx.Count == 3) { result.Add(idx[0]); result.Add(idx[1]); result.Add(idx[2]); }
            return result;
        }

        public static Vector2 Centroid(IList<Vector2> poly)
        {
            float area = 0f, cx = 0f, cy = 0f;
            int n = poly.Count;
            if (n == 0) return Vector2.zero;
            for (int i = 0; i < n; i++)
            {
                Vector2 a = poly[i], b = poly[(i + 1) % n];
                float cr = a.x * b.y - b.x * a.y;
                area += cr;
                cx += (a.x + b.x) * cr;
                cy += (a.y + b.y) * cr;
            }
            area *= 0.5f;
            if (Mathf.Abs(area) < 1e-6f)
            {
                Vector2 avg = Vector2.zero;
                foreach (var p in poly) avg += p;
                return avg / n;
            }
            return new Vector2(cx / (6f * area), cy / (6f * area));
        }

        /// <summary>Sutherland–Hodgman clip keeping the side where dot(p - origin, normal) &lt;= 0.</summary>
        public static List<Vector2> ClipHalfPlane(List<Vector2> poly, Vector2 origin, Vector2 normal)
        {
            var result = new List<Vector2>(poly.Count + 2);
            int n = poly.Count;
            for (int i = 0; i < n; i++)
            {
                Vector2 a = poly[i], b = poly[(i + 1) % n];
                float da = Vector2.Dot(a - origin, normal), db = Vector2.Dot(b - origin, normal);
                bool ina = da <= 0f, inb = db <= 0f;
                if (ina) result.Add(a);
                if (ina != inb)
                {
                    float t = da / (da - db);
                    result.Add(a + (b - a) * t);
                }
            }
            return result;
        }

        public static List<Vector2> CatmullRom(IList<Vector2> c, int samplesPerSegment)
        {
            var outPts = new List<Vector2>();
            int n = c.Count;
            if (n < 2)
            {
                outPts.AddRange(c);
                return outPts;
            }
            for (int i = 0; i < n - 1; i++)
            {
                Vector2 p0 = c[Mathf.Max(i - 1, 0)], p1 = c[i], p2 = c[i + 1], p3 = c[Mathf.Min(i + 2, n - 1)];
                for (int s = 0; s < samplesPerSegment; s++)
                {
                    float t = s / (float)samplesPerSegment;
                    float t2 = t * t, t3 = t2 * t;
                    outPts.Add(0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3));
                }
            }
            outPts.Add(c[n - 1]);
            return outPts;
        }

        public static Rect Bounds(IList<Vector2> pts)
        {
            if (pts.Count == 0) return new Rect();
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var p in pts)
            {
                if (p.x < minX) minX = p.x;
                if (p.y < minY) minY = p.y;
                if (p.x > maxX) maxX = p.x;
                if (p.y > maxY) maxY = p.y;
            }
            return Rect.MinMaxRect(minX, minY, maxX, maxY);
        }

        public static Rect Expand(Rect r, float m) => Rect.MinMaxRect(r.xMin - m, r.yMin - m, r.xMax + m, r.yMax + m);

        public static float SmoothStep(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        /// <summary>Samples a polyline at distance s; returns the point and unit tangent.</summary>
        public static Vector2 SampleAt(IList<Vector2> pts, IList<float> cum, float s, out Vector2 tangent)
        {
            int n = pts.Count;
            if (n == 0) { tangent = Vector2.right; return Vector2.zero; }
            if (n == 1) { tangent = Vector2.right; return pts[0]; }
            if (s <= 0f)
            {
                tangent = (pts[1] - pts[0]).normalized;
                return pts[0];
            }
            float total = cum[n - 1];
            if (s >= total)
            {
                tangent = (pts[n - 1] - pts[n - 2]).normalized;
                return pts[n - 1];
            }
            int lo = 0, hi = n - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) >> 1;
                if (cum[mid] <= s) lo = mid; else hi = mid;
            }
            float segLen = cum[hi] - cum[lo];
            float t = segLen > 1e-6f ? (s - cum[lo]) / segLen : 0f;
            Vector2 d = pts[hi] - pts[lo];
            tangent = d.sqrMagnitude > 1e-12f ? d.normalized : Vector2.right;
            return pts[lo] + d * t;
        }
    }
}
