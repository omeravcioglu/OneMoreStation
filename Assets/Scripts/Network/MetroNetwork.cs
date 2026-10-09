using System.Collections.Generic;
using UnityEngine;

namespace Metro
{
    /// <summary>
    /// Stations, lines, track edges and depots. Ids are indices into the lists and are never reused;
    /// removed objects stay in place with alive == false so ids stay stable for the simulation arrays.
    /// </summary>
    public sealed class MetroNetwork
    {
        public readonly CityModel city;
        public readonly List<Station> stations = new List<Station>();
        public readonly List<MetroLine> lines = new List<MetroLine>();
        public readonly List<Depot> depots = new List<Depot>();
        public readonly Dictionary<long, TrackEdge> edges = new Dictionary<long, TrackEdge>();
        /// <summary>Bend orientation per station pair, stored as "diagonal first" from the lower id.</summary>
        public readonly Dictionary<long, bool> bendPrefs = new Dictionary<long, bool>();
        public int version;

        public MetroNetwork(CityModel city)
        {
            this.city = city;
        }

        public static long Key(int a, int b)
        {
            if (a > b) (a, b) = (b, a);
            return ((long)a << 32) | (uint)b;
        }

        // ------------------------------------------------------------------ lookups

        public Station GetStation(int id) =>
            id >= 0 && id < stations.Count && stations[id] != null && stations[id].alive ? stations[id] : null;

        public MetroLine GetLine(int id) =>
            id >= 0 && id < lines.Count && lines[id] != null && lines[id].alive ? lines[id] : null;

        public Depot GetDepot(int id) =>
            id >= 0 && id < depots.Count && depots[id] != null && depots[id].alive ? depots[id] : null;

        public TrackEdge GetEdge(int a, int b) => edges.TryGetValue(Key(a, b), out var e) ? e : null;

        public IEnumerable<Station> AliveStations()
        {
            foreach (var s in stations)
                if (s != null && s.alive) yield return s;
        }

        public IEnumerable<MetroLine> AliveLines()
        {
            foreach (var l in lines)
                if (l != null && l.alive) yield return l;
        }

        public IEnumerable<Depot> AliveDepots()
        {
            foreach (var d in depots)
                if (d != null && d.alive) yield return d;
        }

        public int StationCount
        {
            get
            {
                int c = 0;
                foreach (var s in stations)
                    if (s != null && s.alive) c++;
                return c;
            }
        }

        public int LineCount
        {
            get
            {
                int c = 0;
                foreach (var l in lines)
                    if (l != null && l.alive) c++;
                return c;
            }
        }

        public int DepotCapacity
        {
            get
            {
                int c = 0;
                foreach (var d in AliveDepots()) c += d.Capacity;
                return c;
            }
        }

        public bool LineHasDepot(MetroLine l)
        {
            foreach (int sid in l.stops)
            {
                var st = GetStation(sid);
                if (st != null && st.depotId >= 0) return true;
            }
            return false;
        }

        /// <summary>Total km of track, counting every line's own pair of tracks separately.</summary>
        public void TrackTotals(out float tunnelKm, out float bridgeKm)
        {
            tunnelKm = 0f;
            bridgeKm = 0f;
            foreach (var e in edges.Values)
            {
                int k = Mathf.Max(1, e.lines.Count);
                tunnelKm += e.LandLength * k;
                bridgeKm += e.waterLength * k;
            }
        }

        // ------------------------------------------------------------------ bends

        public void SetBend(int from, int to, bool diagonalFirstFromStart)
        {
            bendPrefs[Key(from, to)] = from < to ? diagonalFirstFromStart : !diagonalFirstFromStart;
            var e = GetEdge(from, to);
            if (e != null) e.geometryValid = false;
        }

        public bool TryGetBend(int from, int to, out bool diagonalFirstFromStart)
        {
            if (bendPrefs.TryGetValue(Key(from, to), out bool v))
            {
                diagonalFirstFromStart = from < to ? v : !v;
                return true;
            }
            diagonalFirstFromStart = false;
            return false;
        }

        // ------------------------------------------------------------------ creation

        public Station CreateStation(Vector2 pos, float day)
        {
            var s = new Station { id = stations.Count, pos = pos, builtDay = day, district = city.DistrictAt(pos) };
            s.name = NameBank.StationName(city, this, pos);
            stations.Add(s);
            return s;
        }

        public MetroLine CreateLine(IList<int> stops, bool loop, int colorIndex, float day)
        {
            var l = new MetroLine
            {
                id = lines.Count,
                colorIndex = colorIndex,
                loop = loop && stops.Count >= 3,
                builtDay = day
            };
            l.name = LinePalette.Names[colorIndex % LinePalette.Count] + " Line";
            l.stops.AddRange(stops);
            lines.Add(l);
            return l;
        }

        public Depot CreateDepot(int stationId, float day)
        {
            var d = new Depot { id = depots.Count, stationId = stationId, builtDay = day };
            d.iconDir = FindOpenDirection(stationId);
            depots.Add(d);
            return d;
        }

        public int NextFreeColor()
        {
            var counts = new int[LinePalette.Count];
            foreach (var l in AliveLines()) counts[l.colorIndex % LinePalette.Count]++;
            int best = 0;
            for (int i = 0; i < counts.Length; i++)
                if (counts[i] < counts[best]) best = i;
            return best;
        }

        /// <summary>A direction around a station that is free of track, used to place depot icons.</summary>
        public Vector2 FindOpenDirection(int stationId)
        {
            var st = stations[stationId];
            Vector2 best = Vector2.up;
            float bestScore = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                float ang = i * Mathf.PI / 4f + Mathf.PI / 8f;
                Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                float score = 0f;
                foreach (var e in edges.Values)
                {
                    if (e.a != stationId && e.b != stationId) continue;
                    Vector2 other = e.a == stationId ? e.path[Mathf.Min(1, e.path.Count - 1)] : e.path[Mathf.Max(0, e.path.Count - 2)];
                    Vector2 d = (other - st.pos).normalized;
                    score -= Mathf.Max(0f, Vector2.Dot(d, dir));
                }
                if (city.water.IsWater(st.pos + dir * 0.3f)) score -= 0.5f;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = dir;
                }
            }
            return best;
        }

        // ------------------------------------------------------------------ derived data

        /// <summary>Recomputes edges, lane assignments, station line lists and line paths.</summary>
        public void Rebuild()
        {
            foreach (var s in stations)
            {
                if (s == null) continue;
                s.lines.Clear();
                s.depotId = -1;
                s.laneExtent = 0f;
            }

            foreach (var l in lines)
            {
                if (l == null || !l.alive) continue;
                for (int i = l.stops.Count - 1; i >= 0; i--)
                    if (GetStation(l.stops[i]) == null) l.stops.RemoveAt(i);
                for (int i = l.stops.Count - 1; i > 0; i--)
                    if (l.stops[i] == l.stops[i - 1]) l.stops.RemoveAt(i);
                if (l.loop && l.stops.Count > 1 && l.stops[0] == l.stops[l.stops.Count - 1]) l.stops.RemoveAt(l.stops.Count - 1);
                if (l.loop && l.stops.Count < 3) l.loop = false;
                if (l.stops.Count < 2) l.alive = false;
            }

            var used = new HashSet<long>();
            foreach (var l in AliveLines())
            {
                int n = l.stops.Count;
                for (int k = 0; k < l.SegmentCount; k++)
                {
                    int u = l.stops[k], v = l.stops[(k + 1) % n];
                    long key = Key(u, v);
                    used.Add(key);
                    if (!edges.ContainsKey(key)) edges[key] = new TrackEdge(Mathf.Min(u, v), Mathf.Max(u, v));
                }
                foreach (int sid in l.stops)
                {
                    var st = stations[sid];
                    if (!st.lines.Contains(l.id)) st.lines.Add(l.id);
                }
            }

            var dead = new List<long>();
            foreach (var k in edges.Keys)
                if (!used.Contains(k)) dead.Add(k);
            foreach (var k in dead) edges.Remove(k);

            foreach (var kv in edges)
            {
                var e = kv.Value;
                e.lines.Clear();
                bool diag = bendPrefs.TryGetValue(kv.Key, out bool b) ? b : true;
                if (e.geometryValid && diag == e.diagonalFirst) continue;
                e.diagonalFirst = diag;
                e.path = LineGeometry.OctilinearPath(stations[e.a].pos, stations[e.b].pos, diag);
                e.cum.Clear();
                float acc = 0f;
                e.cum.Add(0f);
                for (int i = 1; i < e.path.Count; i++)
                {
                    acc += Vector2.Distance(e.path[i - 1], e.path[i]);
                    e.cum.Add(acc);
                }
                e.length = acc;
                e.waterLength = city.water.WaterRanges(e.path, e.waterRanges);
                e.geometryValid = true;
            }

            foreach (var l in AliveLines())
            {
                for (int k = 0; k < l.SegmentCount; k++)
                {
                    var e = GetEdge(l.stops[k], l.stops[(k + 1) % l.stops.Count]);
                    if (e != null && !e.lines.Contains(l.id)) e.lines.Add(l.id);
                }
            }

            foreach (var e in edges.Values)
            {
                e.lines.Sort();
                float ext = (e.lines.Count - 1) * 0.5f * NetStyle.LaneSpacing;
                stations[e.a].laneExtent = Mathf.Max(stations[e.a].laneExtent, ext);
                stations[e.b].laneExtent = Mathf.Max(stations[e.b].laneExtent, ext);
            }

            foreach (var l in AliveLines())
            {
                l.path = LinePath.Build(this, l);
                l.EnsureArrays();
            }

            foreach (var d in depots)
            {
                if (d == null || !d.alive) continue;
                var st = GetStation(d.stationId);
                if (st == null)
                {
                    d.alive = false;
                    continue;
                }
                st.depotId = d.id;
            }
            version++;
        }

        /// <summary>Inserts station s between a and b on every line that runs directly between them.</summary>
        public List<MetroLine> InsertIntoEdge(int a, int b, int s)
        {
            var touched = new List<MetroLine>();
            foreach (var l in AliveLines())
            {
                int n = l.stops.Count;
                for (int k = 0; k < l.SegmentCount; k++)
                {
                    int u = l.stops[k], v = l.stops[(k + 1) % n];
                    if ((u == a && v == b) || (u == b && v == a))
                    {
                        l.stops.Insert(k + 1, s);
                        touched.Add(l);
                        break;
                    }
                }
            }
            return touched;
        }

        /// <summary>Removes a station from every line. Returns lines that changed.</summary>
        public List<MetroLine> RemoveStationFromLines(int stationId)
        {
            var touched = new List<MetroLine>();
            foreach (var l in AliveLines())
            {
                if (!l.stops.Contains(stationId)) continue;
                l.stops.RemoveAll(x => x == stationId);
                touched.Add(l);
            }
            return touched;
        }

        public List<MetroLine> LinesAt(int stationId)
        {
            var res = new List<MetroLine>();
            var st = GetStation(stationId);
            if (st == null) return res;
            foreach (int lid in st.lines)
            {
                var l = GetLine(lid);
                if (l != null) res.Add(l);
            }
            return res;
        }

        public Station NearestStation(Vector2 p, float maxDist, int exclude = -1)
        {
            Station best = null;
            float bd = maxDist * maxDist;
            foreach (var s in AliveStations())
            {
                if (s.id == exclude) continue;
                float d = (s.pos - p).sqrMagnitude;
                if (d <= bd)
                {
                    bd = d;
                    best = s;
                }
            }
            return best;
        }
    }
}
