using System.Collections.Generic;
using UnityEngine;

namespace Metro
{
    public enum TrainState { Dwelling, Moving }

    public sealed class Train
    {
        public int id, lineId;
        public bool alive = true;
        public int stopIndex;
        public int nextStop = -1;
        public int atStation = -1;
        public int nextStation = -1;
        public int dir = 1;
        public TrainState state = TrainState.Dwelling;
        public float timer;
        public float runTime, runLength, sFrom, sTo, s;
        public int segment;
        public float holdTotal;
        public int capacity;
        public readonly CargoHold cargo = new CargoHold();

        // Pose for rendering/picking.
        public Vector2 pos, heading = Vector2.right;
        public float lane;

        public float LoadFactor => capacity > 0 ? cargo.total / capacity : 0f;
    }

    /// <summary>Moves trains along their line paths, stops them at stations and keeps headways even.</summary>
    public sealed class TrainSystem
    {
        readonly GameState gs;
        public readonly List<Train> trains = new List<Train>();

        public TrainSystem(GameState gs)
        {
            this.gs = gs;
        }

        public Train Get(int id) => id >= 0 && id < trains.Count && trains[id].alive ? trains[id] : null;

        public int AliveCount
        {
            get
            {
                int c = 0;
                foreach (var t in trains)
                    if (t.alive) c++;
                return c;
            }
        }

        public float TotalOnboard
        {
            get
            {
                float s = 0f;
                foreach (var t in trains)
                    if (t.alive) s += t.cargo.total;
                return s;
            }
        }

        // ------------------------------------------------------------------ fleet management

        public Train AddTrain(MetroLine line)
        {
            var t = new Train { id = trains.Count, lineId = line.id, capacity = line.Capacity };
            PlaceInLargestGap(t, line);
            trains.Add(t);
            line.trains.Add(t.id);
            UpdatePose(t, line);
            return t;
        }

        /// <summary>Recreates a saved train dwelling at a given stop.</summary>
        public Train RestoreTrain(MetroLine line, int stopIndex, int dir)
        {
            var t = new Train { id = trains.Count, lineId = line.id, capacity = line.Capacity };
            t.stopIndex = Mathf.Clamp(stopIndex, 0, Mathf.Max(0, line.stops.Count - 1));
            t.dir = dir >= 0 ? 1 : -1;
            if (!line.loop)
            {
                if (t.stopIndex == line.stops.Count - 1) t.dir = -1;
                else if (t.stopIndex == 0) t.dir = 1;
            }
            trains.Add(t);
            line.trains.Add(t.id);
            if (line.path != null && line.stops.Count >= 2)
            {
                SetDwelling(t, line, 0.3f);
                UpdatePose(t, line);
            }
            return t;
        }

        public bool RemoveTrain(MetroLine line)
        {
            Train pick = null;
            foreach (int id in line.trains)
            {
                var t = Get(id);
                if (t == null) continue;
                if (pick == null || t.cargo.total < pick.cargo.total) pick = t;
            }
            if (pick == null) return false;
            Retire(pick, line);
            return true;
        }

        public void RemoveAll(MetroLine line)
        {
            var ids = new List<int>(line.trains);
            foreach (int id in ids)
            {
                var t = Get(id);
                if (t != null) Retire(t, line);
            }
            line.trains.Clear();
        }

        void Retire(Train t, MetroLine line)
        {
            int st = t.state == TrainState.Moving && t.nextStation >= 0 ? t.nextStation : t.atStation;
            gs.pax.DumpCargo(t, st);
            t.alive = false;
            line.trains.Remove(t.id);
        }

        /// <summary>Re-seats a line's trains after its stops changed; passengers get off to re-route.</summary>
        public void OnLineChanged(MetroLine line)
        {
            if (!line.alive)
            {
                RemoveAll(line);
                return;
            }
            line.EnsureArrays();
            int n = line.stops.Count;
            foreach (int id in new List<int>(line.trains))
            {
                var t = Get(id);
                if (t == null) continue;
                int target = t.state == TrainState.Moving && t.nextStation >= 0 ? t.nextStation : t.atStation;
                int idx = line.stops.IndexOf(target);
                if (idx < 0)
                {
                    float bd = float.MaxValue;
                    for (int i = 0; i < n; i++)
                    {
                        float d = (gs.net.stations[line.stops[i]].pos - t.pos).sqrMagnitude;
                        if (d < bd)
                        {
                            bd = d;
                            idx = i;
                        }
                    }
                }
                idx = Mathf.Clamp(idx, 0, n - 1);
                gs.pax.DumpCargo(t, line.stops[idx]);
                t.stopIndex = idx;
                t.capacity = line.Capacity;
                if (!line.loop)
                {
                    if (idx == n - 1) t.dir = -1;
                    else if (idx == 0) t.dir = 1;
                }
                SetDwelling(t, line, 0.4f);
                UpdatePose(t, line);
            }
        }

        public void RefreshCapacity(MetroLine line)
        {
            foreach (int id in line.trains)
            {
                var t = Get(id);
                if (t != null) t.capacity = line.Capacity;
            }
        }

        static int SegmentForStop(MetroLine l, int i, int dir)
        {
            int n = l.stops.Count;
            int segs = l.SegmentCount;
            if (segs == 0) return 0;
            int seg;
            if (dir > 0) seg = l.loop || i < n - 1 ? i : i - 1;
            else seg = l.loop ? (i - 1 + n) % n : i - 1;
            return Mathf.Clamp(seg, 0, segs - 1);
        }

        void SetDwelling(Train t, MetroLine l, float time)
        {
            t.state = TrainState.Dwelling;
            t.timer = time;
            t.holdTotal = 0f;
            t.atStation = l.stops[t.stopIndex];
            t.nextStation = -1;
            t.s = l.path != null ? l.path.stopS[t.stopIndex] : 0f;
            t.segment = SegmentForStop(l, t.stopIndex, t.dir);
        }

        float Phase(Train o, MetroLine l)
        {
            float L = l.path.length;
            if (l.loop) return o.dir > 0 ? o.s : L - o.s;
            return o.dir > 0 ? o.s : 2f * L - o.s;
        }

        void PlaceInLargestGap(Train t, MetroLine l)
        {
            var path = l.path;
            int n = l.stops.Count;
            if (path == null || n < 2)
            {
                t.stopIndex = 0;
                t.dir = 1;
                if (n > 0 && path != null) SetDwelling(t, l, 0.3f);
                return;
            }
            float L = path.length;
            if (l.loop)
            {
                int fwd = 0, bwd = 0;
                foreach (int id in l.trains)
                {
                    var o = Get(id);
                    if (o == null) continue;
                    if (o.dir > 0) fwd++;
                    else bwd++;
                }
                t.dir = fwd <= bwd ? 1 : -1;
            }
            else t.dir = 1;

            float cycle = l.loop ? L : 2f * L;
            var phases = new List<float>();
            foreach (int id in l.trains)
            {
                var o = Get(id);
                if (o == null || (l.loop && o.dir != t.dir)) continue;
                phases.Add(Phase(o, l));
            }
            float target = 0f;
            if (phases.Count > 0)
            {
                phases.Sort();
                float bestGap = -1f;
                for (int i = 0; i < phases.Count; i++)
                {
                    float a = phases[i], b = i + 1 < phases.Count ? phases[i + 1] : phases[0] + cycle;
                    if (b - a > bestGap)
                    {
                        bestGap = b - a;
                        target = a + (b - a) * 0.5f;
                    }
                }
                target = Mathf.Repeat(target, cycle);
            }

            float s;
            int dir;
            if (l.loop)
            {
                dir = t.dir;
                s = dir > 0 ? target : L - target;
            }
            else if (target <= L)
            {
                dir = 1;
                s = target;
            }
            else
            {
                dir = -1;
                s = 2f * L - target;
            }

            int best = 0;
            float bd = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                float d = Mathf.Abs(path.stopS[i] - s);
                if (l.loop) d = Mathf.Min(d, L - d);
                if (d < bd)
                {
                    bd = d;
                    best = i;
                }
            }
            t.dir = dir;
            if (!l.loop)
            {
                if (best == n - 1) t.dir = -1;
                if (best == 0) t.dir = 1;
            }
            t.stopIndex = best;
            SetDwelling(t, l, 0.3f);
        }

        // ------------------------------------------------------------------ simulation

        public void Tick(float dt)
        {
            var net = gs.net;
            for (int i = 0; i < trains.Count; i++)
            {
                var t = trains[i];
                if (!t.alive) continue;
                var l = net.GetLine(t.lineId);
                if (l == null || l.path == null || l.stops.Count < 2) continue;
                float rem = dt;
                for (int guard = 0; rem > 1e-6f && guard < 24; guard++)
                {
                    if (t.state == TrainState.Dwelling)
                    {
                        if (t.timer > rem)
                        {
                            t.timer -= rem;
                            rem = 0f;
                            break;
                        }
                        rem -= t.timer;
                        t.timer = 0f;
                        if (ShouldHold(t, l))
                        {
                            t.timer = 0.15f;
                            t.holdTotal += 0.15f;
                            continue;
                        }
                        Depart(t, l);
                    }
                    else
                    {
                        float left = t.runTime - t.timer;
                        if (left > rem)
                        {
                            t.timer += rem;
                            rem = 0f;
                        }
                        else
                        {
                            rem -= left;
                            t.timer = t.runTime;
                            Arrive(t, l);
                        }
                    }
                }
                UpdatePose(t, l);
            }
        }

        bool ShouldHold(Train t, MetroLine l)
        {
            if (l.trains.Count < 2 || t.holdTotal >= 1.5f) return false;
            float L = l.path.length;
            float cycle = l.loop ? L : 2f * L;
            if (cycle <= 0f) return false;
            float my = Phase(t, l);
            float best = cycle;
            int group = 0;
            foreach (int id in l.trains)
            {
                var o = Get(id);
                if (o == null || o == t || (l.loop && o.dir != t.dir)) continue;
                group++;
                float g = Mathf.Repeat(Phase(o, l) - my, cycle);
                if (g > 1e-3f && g < best) best = g;
            }
            if (group == 0) return false;
            return best < cycle / (group + 1) * 0.5f;
        }

        void Depart(Train t, MetroLine l)
        {
            int next = RoutingSystem.NextStop(l, t.stopIndex, t.dir);
            if (next < 0)
            {
                t.dir = -t.dir;
                next = RoutingSystem.NextStop(l, t.stopIndex, t.dir);
                if (next < 0)
                {
                    t.timer = 1f;
                    return;
                }
            }
            int seg = Mathf.Clamp(t.dir > 0 ? t.stopIndex : next, 0, l.path.SegmentCount - 1);
            float a = l.path.SegStart(seg), b = l.path.SegEnd(seg);
            t.segment = seg;
            t.nextStop = next;
            t.nextStation = l.stops[next];
            if (t.dir > 0)
            {
                t.sFrom = a;
                t.sTo = b;
            }
            else
            {
                t.sFrom = b;
                t.sTo = a;
            }
            t.runLength = Mathf.Abs(b - a);
            t.runTime = Mathf.Max(0.05f, LineGeometry.TravelTime(t.runLength));
            t.timer = 0f;
            t.state = TrainState.Moving;
            t.holdTotal = 0f;

            float lf = t.LoadFactor;
            if (seg < l.segLoadFwd.Length)
            {
                if (t.dir > 0) l.segLoadFwd[seg] = Mathf.Lerp(l.segLoadFwd[seg], lf, 0.3f);
                else l.segLoadBwd[seg] = Mathf.Lerp(l.segLoadBwd[seg], lf, 0.3f);
            }
            if (t.cargo.total > 0f)
            {
                l.loadSum += lf * t.cargo.total;
                l.loadWeight += t.cargo.total;
            }
            l.avgLoadRecent = Mathf.Lerp(l.avgLoadRecent, lf, 0.03f);
            if (lf > l.peakLoadToday) l.peakLoadToday = lf;
        }

        void Arrive(Train t, MetroLine l)
        {
            t.stopIndex = t.nextStop;
            t.state = TrainState.Dwelling;
            t.atStation = l.stops[t.stopIndex];
            t.nextStation = -1;
            t.s = t.sTo;
            bool terminal = !l.loop && (t.stopIndex == 0 || t.stopIndex == l.stops.Count - 1);
            if (terminal) t.dir = t.stopIndex == 0 ? 1 : -1;
            float exchanged = gs.pax.OnTrainArrive(t, l, t.stopIndex, terminal);
            float dwell = Balance.DwellBase + exchanged / (Mathf.Max(1, l.wagons) * Balance.PaxPerWagonPerMinute);
            dwell = Mathf.Min(dwell, Balance.DwellMax);
            if (terminal) dwell += Balance.Turnaround;
            t.timer = dwell;
        }

        public void UpdatePose(Train t, MetroLine l)
        {
            if (l.path == null || l.path.points.Count < 2) return;
            if (t.state == TrainState.Moving)
            {
                float x = LineGeometry.DistanceAt(t.runLength, t.timer);
                t.s = t.sFrom + (t.sTo >= t.sFrom ? x : -x);
            }
            t.pos = l.path.Evaluate(t.s, out Vector2 tan);
            t.heading = t.dir > 0 ? tan : -tan;
            int seg = t.state == TrainState.Moving ? t.segment : SegmentForStop(l, t.stopIndex, t.dir);
            t.lane = seg >= 0 && seg < l.path.segLane.Count ? l.path.segLane[seg] : 0f;
        }

        public void RefreshAllPoses()
        {
            foreach (var t in trains)
            {
                if (!t.alive) continue;
                var l = gs.net.GetLine(t.lineId);
                if (l != null) UpdatePose(t, l);
            }
        }
    }
}
