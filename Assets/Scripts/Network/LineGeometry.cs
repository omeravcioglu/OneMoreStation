using System.Collections.Generic;
using UnityEngine;

namespace Metro
{
    /// <summary>Octilinear (0/45/90 degree) track geometry and train kinematics.</summary>
    public static class LineGeometry
    {
        public const float FilletRadius = 0.55f;

        /// <summary>
        /// Path from a to b made of one straight and one 45° leg, like a real metro map.
        /// diagonalFirst chooses which leg leaves a. The corner is rounded with a fillet.
        /// </summary>
        public static List<Vector2> OctilinearPath(Vector2 a, Vector2 b, bool diagonalFirst)
        {
            var pts = new List<Vector2> { a };
            Vector2 corner = Corner(a, b, diagonalFirst, out bool hasCorner);
            if (hasCorner) AddFillet(pts, a, corner, b, FilletRadius);
            pts.Add(b);
            return pts;
        }

        public static Vector2 Corner(Vector2 a, Vector2 b, bool diagonalFirst, out bool hasCorner)
        {
            Vector2 d = b - a;
            float adx = Mathf.Abs(d.x), ady = Mathf.Abs(d.y);
            float minor = Mathf.Min(adx, ady), major = Mathf.Max(adx, ady);
            if (minor < 0.02f || major - minor < 0.02f)
            {
                hasCorner = false;
                return a;
            }
            hasCorner = true;
            float sx = Mathf.Sign(d.x), sy = Mathf.Sign(d.y);
            Vector2 diag = new Vector2(sx * minor, sy * minor);
            return diagonalFirst ? a + diag : b - diag;
        }

        /// <summary>Unit direction of the path as it leaves a.</summary>
        public static Vector2 StartDirection(Vector2 a, Vector2 b, bool diagonalFirst)
        {
            Vector2 c = Corner(a, b, diagonalFirst, out bool has);
            Vector2 d = has ? c - a : b - a;
            return d.sqrMagnitude > 1e-9f ? d.normalized : Vector2.right;
        }

        /// <summary>Unit direction of the path as it arrives at b.</summary>
        public static Vector2 EndDirection(Vector2 a, Vector2 b, bool diagonalFirst)
        {
            Vector2 c = Corner(a, b, diagonalFirst, out bool has);
            Vector2 d = has ? b - c : b - a;
            return d.sqrMagnitude > 1e-9f ? d.normalized : Vector2.right;
        }

        /// <summary>Chooses the bend so the new segment continues smoothly from an incoming direction.</summary>
        public static bool BestBend(Vector2 a, Vector2 b, Vector2 incoming)
        {
            if (incoming.sqrMagnitude < 1e-6f) return false;
            float d1 = Vector2.Dot(StartDirection(a, b, true), incoming);
            float d2 = Vector2.Dot(StartDirection(a, b, false), incoming);
            return d1 > d2 + 1e-3f;
        }

        static void AddFillet(List<Vector2> pts, Vector2 a, Vector2 c, Vector2 b, float radius)
        {
            Vector2 d1 = c - a, d2 = b - c;
            float l1 = d1.magnitude, l2 = d2.magnitude;
            if (l1 < 1e-4f || l2 < 1e-4f)
            {
                pts.Add(c);
                return;
            }
            d1 /= l1;
            d2 /= l2;
            float theta = Mathf.Acos(Mathf.Clamp(Vector2.Dot(d1, d2), -1f, 1f));
            if (theta < 0.01f)
            {
                pts.Add(c);
                return;
            }
            float tanHalf = Mathf.Tan(theta * 0.5f);
            float t = radius * tanHalf;
            float maxT = 0.45f * Mathf.Min(l1, l2);
            if (t > maxT)
            {
                t = maxT;
                radius = t / tanHalf;
            }
            Vector2 p1 = c - d1 * t, p2 = c + d2 * t;
            float turn = Mathf.Sign(Geo.Cross(d1, d2));
            Vector2 center = p1 + Geo.Perp(d1) * turn * radius;
            float a0 = Mathf.Atan2(p1.y - center.y, p1.x - center.x);
            float a1 = Mathf.Atan2(p2.y - center.y, p2.x - center.x);
            float delta = Mathf.DeltaAngle(a0 * Mathf.Rad2Deg, a1 * Mathf.Rad2Deg) * Mathf.Deg2Rad;
            int segs = Mathf.Clamp(Mathf.CeilToInt(theta / (Mathf.PI / 28f)), 2, 14);
            for (int i = 0; i <= segs; i++)
            {
                float ang = a0 + delta * i / segs;
                pts.Add(center + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * radius);
            }
        }

        // ------------------------------------------------------------------ kinematics

        /// <summary>Minutes needed to travel a distance from stop to stop with a trapezoid speed profile.</summary>
        public static float TravelTime(float dist)
        {
            float a = Balance.TrainAccel, v = Balance.TrainMaxSpeed;
            if (dist <= 0f) return 0f;
            float tAcc = v / a;
            float dAcc = 0.5f * a * tAcc * tAcc;
            if (2f * dAcc >= dist) return 2f * Mathf.Sqrt(dist / a);
            return 2f * tAcc + (dist - 2f * dAcc) / v;
        }

        /// <summary>Distance covered after t minutes on a run of length dist.</summary>
        public static float DistanceAt(float dist, float t)
        {
            float a = Balance.TrainAccel, v = Balance.TrainMaxSpeed;
            if (dist <= 0f) return 0f;
            float total = TravelTime(dist);
            if (t <= 0f) return 0f;
            if (t >= total) return dist;
            float tAcc = v / a;
            float dAcc = 0.5f * a * tAcc * tAcc;
            if (2f * dAcc >= dist)
            {
                float half = total * 0.5f;
                return t < half ? 0.5f * a * t * t : dist - 0.5f * a * (total - t) * (total - t);
            }
            if (t < tAcc) return 0.5f * a * t * t;
            if (t < total - tAcc) return dAcc + v * (t - tAcc);
            float r = total - t;
            return dist - 0.5f * a * r * r;
        }
    }

    /// <summary>A line's full geometry as one polyline, with the distance of every stop along it.</summary>
    public sealed class LinePath
    {
        public readonly List<Vector2> points = new List<Vector2>();
        public readonly List<float> cum = new List<float>();
        public readonly List<float> stopS = new List<float>();
        public readonly List<float> segLane = new List<float>();
        public readonly List<float> segTime = new List<float>();
        public float length;
        public bool loop;

        public int SegmentCount => segLane.Count;
        public float SegStart(int k) => stopS[k];
        public float SegEnd(int k) => k + 1 < stopS.Count ? stopS[k + 1] : length;
        public float SegLength(int k) => SegEnd(k) - SegStart(k);

        public Vector2 Evaluate(float s, out Vector2 tangent) => Geo.SampleAt(points, cum, s, out tangent);

        public int SegmentAt(float s)
        {
            int segs = SegmentCount;
            for (int k = segs - 1; k >= 0; k--)
                if (s >= stopS[k] - 1e-4f) return k;
            return 0;
        }

        public static LinePath Build(MetroNetwork net, MetroLine line)
        {
            var lp = new LinePath { loop = line.loop };
            int n = line.stops.Count;
            int segs = line.SegmentCount;
            float acc = 0f;
            for (int k = 0; k < segs; k++)
            {
                int u = line.stops[k], v = line.stops[(k + 1) % n];
                var e = net.GetEdge(u, v);
                lp.stopS.Add(acc);
                if (e == null)
                {
                    // Should not happen after Rebuild; fall back to a straight segment.
                    Vector2 pu = net.stations[u].pos, pv = net.stations[v].pos;
                    if (lp.points.Count == 0)
                    {
                        lp.points.Add(pu);
                        lp.cum.Add(0f);
                    }
                    acc += Vector2.Distance(pu, pv);
                    lp.points.Add(pv);
                    lp.cum.Add(acc);
                    lp.segLane.Add(0f);
                    lp.segTime.Add(LineGeometry.TravelTime(Vector2.Distance(pu, pv)));
                    continue;
                }
                bool reversed = u != e.a;
                int m = e.path.Count;
                for (int q = 0; q < m; q++)
                {
                    Vector2 p = reversed ? e.path[m - 1 - q] : e.path[q];
                    if (lp.points.Count == 0)
                    {
                        lp.points.Add(p);
                        lp.cum.Add(0f);
                        continue;
                    }
                    if (q == 0) continue;
                    acc += Vector2.Distance(lp.points[lp.points.Count - 1], p);
                    lp.points.Add(p);
                    lp.cum.Add(acc);
                }
                float lane = e.LaneUnits(line.id);
                lp.segLane.Add(reversed ? -lane : lane);
                lp.segTime.Add(LineGeometry.TravelTime(e.length));
            }
            if (!line.loop) lp.stopS.Add(acc);
            lp.length = acc;
            return lp;
        }
    }
}
