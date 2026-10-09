using System.Collections.Generic;
using UnityEngine;

namespace Metro
{
    /// <summary>
    /// Builds a plausible network automatically. Used for the main-menu backdrop,
    /// automated screenshots and headless balance tests — never during normal play.
    /// </summary>
    public static class AutoPlanner
    {
        public static void BuildNetwork(GameState gs, int lineCount, bool free = true, float trainsPerKm = 0.5f, int wagons = 4)
        {
            var city = gs.city;
            var b = gs.build;
            bool oldFree = b.freeBuild;
            b.freeBuild = free;

            var revealed = new List<District>();
            foreach (var d in city.districts)
                if (d.revealed && d.id > 1) revealed.Add(d);
            revealed.Sort((x, y) => y.population.CompareTo(x.population));

            Vector2 c = city.districts[0].center;
            var used = new HashSet<District>();
            var created = new List<MetroLine>();

            foreach (var a in revealed)
            {
                if (created.Count >= lineCount) break;
                if (used.Contains(a)) continue;
                Vector2 da = (a.center - c).normalized;
                District best = null;
                float bestScore = float.MinValue;
                foreach (var o in revealed)
                {
                    if (o == a || used.Contains(o)) continue;
                    Vector2 dob = (o.center - c).normalized;
                    float opposite = (1f - Vector2.Dot(da, dob)) * 0.5f;
                    float score = opposite * 2f + Mathf.Log10(o.population + 10f) * 0.3f;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = o;
                    }
                }
                if (best == null) break;
                var via = (Vector2.Distance(a.center, city.districts[1].center) + Vector2.Distance(best.center, city.districts[1].center))
                          < (Vector2.Distance(a.center, c) + Vector2.Distance(best.center, c)) * 0.8f
                    ? city.districts[1].center
                    : c;
                var line = BuildAlong(gs, new List<Vector2> { a.center, via, best.center });
                if (line != null)
                {
                    created.Add(line);
                    used.Add(a);
                    used.Add(best);
                }
                else used.Add(a);
            }

            if (lineCount >= 4 && created.Count >= 2)
            {
                var ring = new List<Vector2>();
                float r = 3.6f;
                for (int i = 0; i < 9; i++)
                {
                    float ang = i / 9f * Mathf.PI * 2f + 0.3f;
                    ring.Add(c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r);
                }
                var loop = BuildAlong(gs, ring, true);
                if (loop != null) created.Add(loop);
            }

            if (created.Count > 0)
            {
                var first = created[0];
                b.BuildDepot(first.stops[first.stops.Count / 2]);
                for (int i = 1; i < created.Count; i += 2)
                {
                    var l = created[i];
                    int sid = l.stops[l.stops.Count / 2];
                    if (gs.net.GetStation(sid).depotId < 0) b.BuildDepot(sid);
                }
                // Every line needs a depot somewhere on it, or it can't have trains at all.
                foreach (var l in created)
                    if (!gs.net.LineHasDepot(l)) b.BuildDepot(l.stops[l.stops.Count / 2]);
                foreach (var l in created)
                {
                    b.SetWagons(l, Mathf.Min(wagons, b.MaxWagonsForLine(l)));
                    int n = Mathf.Max(2, Mathf.CeilToInt(l.path.length * trainsPerKm));
                    for (int i = 0; i < n; i++)
                    {
                        var res = b.AddTrain(l);
                        if (!res.ok)
                        {
                            bool expanded = false;
                            foreach (var d in gs.net.AliveDepots())
                            {
                                if (d.level < Balance.MaxDepotLevel && b.ExpandDepot(d.id).ok)
                                {
                                    expanded = true;
                                    break;
                                }
                            }
                            if (expanded && b.AddTrain(l).ok) continue;
                            break;
                        }
                    }
                }
            }
            b.freeBuild = oldFree;
            gs.EnsureFresh();
        }

        /// <summary>A first line for a small town: from beyond Old Town, through Central, out the other side.</summary>
        public static MetroLine BuildStarterLine(GameState gs, int maxStops = int.MaxValue)
        {
            var city = gs.city;
            var area = city.playArea;
            Vector2 c = city.districts[0].center, o = city.districts[1].center;
            Vector2 dir = (c - o).sqrMagnitude > 0.01f ? (c - o).normalized : Vector2.right;
            Vector2 start = o - dir * 1.2f;
            Vector2 end = c + dir * Mathf.Max(2.5f, Vector2.Distance(c, o));
            Vector2 Clamp(Vector2 p) => new Vector2(Mathf.Clamp(p.x, area.xMin + 1f, area.xMax - 1f), Mathf.Clamp(p.y, area.yMin + 1f, area.yMax - 1f));
            return BuildAlong(gs, new List<Vector2> { Clamp(start), c, Clamp(end) }, false, maxStops);
        }

        /// <summary>Extends a line from its nearest terminal toward a point, a station every ~1.4 km.</summary>
        public static bool ExtendToward(GameState gs, MetroLine line, Vector2 target) => ExtendToward(gs, line, target, out _);

        public static bool ExtendToward(GameState gs, MetroLine line, Vector2 target, out string error)
        {
            error = null;
            if (line == null || line.loop)
            {
                error = "no line";
                return false;
            }
            var net = gs.net;
            var first = net.stations[line.stops[0]];
            var last = net.stations[line.stops[line.stops.Count - 1]];
            bool atEnd = Vector2.Distance(last.pos, target) <= Vector2.Distance(first.pos, target);
            var anchor = atEnd ? last : first;
            var stops = new List<PlannedStop>();
            Vector2 lastDir = Vector2.zero;
            Vector2 prevPos = anchor.pos;
            float len = Vector2.Distance(anchor.pos, target);
            int n = Mathf.Max(1, Mathf.RoundToInt(len / 1.4f));
            var tmp = new List<PlannedStop> { new PlannedStop { stationId = anchor.id, pos = anchor.pos } };
            for (int i = 1; i <= n; i++) TryAddStop(gs, tmp, Vector2.Lerp(anchor.pos, target, i / (float)n), ref lastDir, ref prevPos);
            for (int i = 1; i < tmp.Count; i++)
            {
                if (!tmp[i].IsNew && line.stops.Contains(tmp[i].stationId)) break;
                stops.Add(tmp[i]);
            }
            if (stops.Count == 0)
            {
                error = "no valid stop sites";
                return false;
            }
            var res = gs.build.ExtendLine(line, atEnd, stops, false, false);
            error = res.error;
            return res.ok;
        }

        /// <summary>Places stations roughly every 1.4 km along waypoints and builds a line through them.</summary>
        public static MetroLine BuildAlong(GameState gs, List<Vector2> waypoints, bool loop = false, int maxStops = int.MaxValue)
        {
            var net = gs.net;
            var stops = new List<PlannedStop>();
            var pts = new List<Vector2>(waypoints);
            if (loop) pts.Add(waypoints[0]);
            float spacing = 1.4f;
            float carry = 0f;
            Vector2 lastDir = Vector2.zero;
            Vector2 prevPos = pts[0];
            TryAddStop(gs, stops, pts[0], ref lastDir, ref prevPos);
            for (int i = 0; i < pts.Count - 1; i++)
            {
                Vector2 a = pts[i], bpt = pts[i + 1];
                float len = Vector2.Distance(a, bpt);
                float s = spacing - carry;
                while (s <= len)
                {
                    TryAddStop(gs, stops, Vector2.Lerp(a, bpt, s / len), ref lastDir, ref prevPos);
                    s += spacing;
                }
                carry = len - (s - spacing);
            }
            if (!loop) TryAddStop(gs, stops, pts[pts.Count - 1], ref lastDir, ref prevPos);
            if (!loop && stops.Count > maxStops) stops.RemoveRange(maxStops, stops.Count - maxStops);
            if (stops.Count < 2) return null;
            bool closing = false;
            if (loop && stops.Count >= 3)
            {
                Vector2 last = stops[stops.Count - 1].pos;
                closing = LineGeometry.BestBend(last, stops[0].pos, lastDir);
            }
            var res = gs.build.BuildLine(stops, loop && stops.Count >= 3, closing, out var line);
            return res.ok ? line : null;
        }

        static void TryAddStop(GameState gs, List<PlannedStop> stops, Vector2 p, ref Vector2 lastDir, ref Vector2 prevPos)
        {
            var net = gs.net;
            var city = gs.city;
            // Snap to an existing station nearby (creates interchanges).
            var near = net.NearestStation(p, 0.7f);
            PlannedStop stop;
            if (near != null)
            {
                foreach (var s in stops)
                    if (s.stationId == near.id) return;
                stop = new PlannedStop { stationId = near.id, pos = near.pos };
            }
            else
            {
                Vector2 q = p;
                bool ok = false;
                for (int k = 0; k < 12 && !ok; k++)
                {
                    ok = gs.build.ValidStationSite(q, out _);
                    foreach (var s in stops)
                        if (s.IsNew && Vector2.Distance(s.pos, q) < Balance.MinStationSpacing) ok = false;
                    if (!ok) q = p + new Vector2(Mathf.Cos(k * 2.1f), Mathf.Sin(k * 2.1f)) * (0.25f + 0.08f * k);
                }
                if (!ok) return;
                stop = new PlannedStop { stationId = -1, pos = q };
            }
            if (stops.Count > 0)
            {
                Vector2 from = stops[stops.Count - 1].pos;
                if (Vector2.Distance(from, stop.pos) < Balance.MinStationSpacing) return;
                stop.diagonalFirst = LineGeometry.BestBend(from, stop.pos, lastDir);
                lastDir = LineGeometry.EndDirection(from, stop.pos, stop.diagonalFirst);
            }
            stops.Add(stop);
            prevPos = stop.pos;
        }
    }
}
