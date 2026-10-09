using System.Collections.Generic;
using UnityEngine;

namespace Metro
{
    public sealed class River
    {
        public string name;
        public readonly List<Vector2> points = new List<Vector2>();
        public readonly List<float> halfWidths = new List<float>();
        public Rect bounds;
        public float maxHalfWidth;
    }

    /// <summary>Sea (south coast), rivers and lakes with analytic point queries.</summary>
    public sealed class WaterSystem
    {
        public bool hasCoast;
        public float coastX0, coastStep;
        public float[] coastY = new float[0];
        public readonly List<River> rivers = new List<River>();
        public readonly List<List<Vector2>> lakes = new List<List<Vector2>>();
        readonly List<Rect> lakeBounds = new List<Rect>();

        public float CoastY(float x)
        {
            if (!hasCoast || coastY.Length < 2) return float.NegativeInfinity;
            float f = (x - coastX0) / coastStep;
            int i = Mathf.Clamp(Mathf.FloorToInt(f), 0, coastY.Length - 2);
            float t = Mathf.Clamp01(f - i);
            return Mathf.Lerp(coastY[i], coastY[i + 1], t);
        }

        public void AddLake(List<Vector2> poly)
        {
            lakes.Add(poly);
            lakeBounds.Add(Geo.Bounds(poly));
        }

        public void FinalizeShapes()
        {
            foreach (var r in rivers)
            {
                r.bounds = Geo.Bounds(r.points);
                r.maxHalfWidth = 0f;
                foreach (var w in r.halfWidths) r.maxHalfWidth = Mathf.Max(r.maxHalfWidth, w);
            }
            lakeBounds.Clear();
            foreach (var l in lakes) lakeBounds.Add(Geo.Bounds(l));
        }

        public bool IsWater(Vector2 p, float margin = 0f)
        {
            if (hasCoast && p.y < CoastY(p.x) + margin) return true;
            foreach (var r in rivers)
            {
                if (!Geo.Expand(r.bounds, r.maxHalfWidth + margin).Contains(p)) continue;
                if (RiverDistance(r, p, out float hw) < hw + margin) return true;
            }
            for (int i = 0; i < lakes.Count; i++)
            {
                if (!Geo.Expand(lakeBounds[i], margin).Contains(p)) continue;
                if (Geo.PointInPolygon(p, lakes[i])) return true;
                if (margin > 0f && LakeEdgeDistance(i, p) < margin) return true;
            }
            return false;
        }

        public float RiverDistance(River r, Vector2 p, out float halfWidth)
        {
            float best = float.MaxValue;
            halfWidth = 0f;
            var pts = r.points;
            for (int i = 0; i < pts.Count - 1; i++)
            {
                float d = Geo.DistToSegment(p, pts[i], pts[i + 1], out float t);
                if (d < best)
                {
                    best = d;
                    halfWidth = Mathf.Lerp(r.halfWidths[i], r.halfWidths[i + 1], t);
                }
            }
            return best;
        }

        float LakeEdgeDistance(int lake, Vector2 p)
        {
            var poly = lakes[lake];
            float best = float.MaxValue;
            for (int i = 0; i < poly.Count; i++)
            {
                float d = Geo.DistToSegment(p, poly[i], poly[(i + 1) % poly.Count], out _);
                if (d < best) best = d;
            }
            return best;
        }

        public float DistanceToCoast(Vector2 p) => hasCoast ? Mathf.Max(0f, p.y - CoastY(p.x)) : float.MaxValue;

        public float DistanceToRiver(Vector2 p)
        {
            float best = float.MaxValue;
            foreach (var r in rivers)
            {
                if (!Geo.Expand(r.bounds, 6f).Contains(p)) continue;
                float d = RiverDistance(r, p, out float hw) - hw;
                if (d < best) best = d;
            }
            return Mathf.Max(0f, best);
        }

        public float DistanceToWater(Vector2 p)
        {
            if (IsWater(p)) return 0f;
            float best = Mathf.Min(DistanceToCoast(p), DistanceToRiver(p));
            for (int i = 0; i < lakes.Count; i++)
            {
                if (!Geo.Expand(lakeBounds[i], 6f).Contains(p)) continue;
                best = Mathf.Min(best, LakeEdgeDistance(i, p));
            }
            return best;
        }

        /// <summary>Distance intervals (start, end) along a polyline that lie over water.</summary>
        public float WaterRanges(IList<Vector2> path, List<Vector2> ranges, float step = 0.04f)
        {
            ranges?.Clear();
            float total = 0f, waterLen = 0f, runStart = -1f;
            for (int i = 0; i < path.Count - 1; i++)
            {
                Vector2 a = path[i], b = path[i + 1];
                float len = Vector2.Distance(a, b);
                int steps = Mathf.Max(1, Mathf.CeilToInt(len / step));
                for (int s = 0; s < steps; s++)
                {
                    float t = (s + 0.5f) / steps;
                    bool w = IsWater(Vector2.Lerp(a, b, t));
                    float d0 = total + len * s / steps, d1 = total + len * (s + 1) / steps;
                    if (w)
                    {
                        waterLen += d1 - d0;
                        if (runStart < 0f) runStart = d0;
                    }
                    else if (runStart >= 0f)
                    {
                        ranges?.Add(new Vector2(runStart, d0));
                        runStart = -1f;
                    }
                }
                total += len;
            }
            if (runStart >= 0f) ranges?.Add(new Vector2(runStart, total));
            return waterLen;
        }
    }
}
