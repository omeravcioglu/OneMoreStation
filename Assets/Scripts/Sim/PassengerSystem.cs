using System.Collections.Generic;
using UnityEngine;

namespace Metro
{
    /// <summary>Passengers on board a train, grouped by the station where they will get off.</summary>
    public sealed class CargoHold
    {
        readonly Dictionary<int, Dictionary<int, float>> byAlight = new Dictionary<int, Dictionary<int, float>>();
        static readonly Stack<Dictionary<int, float>> Pool = new Stack<Dictionary<int, float>>();
        public float total;

        public int GroupCount => byAlight.Count;

        public void Add(int alight, int dest, float n)
        {
            if (n <= 0f) return;
            if (!byAlight.TryGetValue(alight, out var d))
            {
                d = Pool.Count > 0 ? Pool.Pop() : new Dictionary<int, float>();
                byAlight[alight] = d;
            }
            d.TryGetValue(dest, out float cur);
            d[dest] = cur + n;
            total += n;
        }

        /// <summary>Removes and returns the group alighting at a station (caller must Release it).</summary>
        public Dictionary<int, float> Take(int alight)
        {
            if (!byAlight.TryGetValue(alight, out var d)) return null;
            byAlight.Remove(alight);
            foreach (var v in d.Values) total -= v;
            if (total < 0.001f) total = 0f;
            return d;
        }

        public static void Release(Dictionary<int, float> d)
        {
            d.Clear();
            Pool.Push(d);
        }

        public void CollectKeys(List<int> into)
        {
            into.Clear();
            foreach (var k in byAlight.Keys) into.Add(k);
        }

        public void Clear()
        {
            foreach (var d in byAlight.Values) Release(d);
            byAlight.Clear();
            total = 0f;
        }
    }

    /// <summary>
    /// Aggregated passenger flows: people waiting at each station for each destination,
    /// spawned from the demand model by time of day, boarding trains along their routed legs.
    /// </summary>
    public sealed class PassengerSystem
    {
        readonly GameState gs;

        public int cap;
        public float[] waiting = new float[0];
        public float[] waitingTotal = new float[0];

        // Per-minute accumulators, consumed by StatsTracker.
        public float mEntries, mTurnedAway, mAbandoned, mBoardings, mDenied, mDelivered, mTransfers, mFares;

        readonly List<int> tmpKeys = new List<int>();
        readonly List<int> aliveIds = new List<int>();
        int aliveVersion = -1;

        public PassengerSystem(GameState gs)
        {
            this.gs = gs;
        }

        public void EnsureCapacity()
        {
            int want = RoutingSystem.CapacityFor(gs.net.stations.Count);
            if (want == cap) return;
            var nw = new float[want * want];
            var nt = new float[want];
            int m = Mathf.Min(cap, want);
            for (int s = 0; s < m; s++)
            {
                for (int x = 0; x < m; x++) nw[s * want + x] = waiting[s * cap + x];
                nt[s] = waitingTotal[s];
            }
            waiting = nw;
            waitingTotal = nt;
            cap = want;
        }

        void RefreshAlive()
        {
            if (aliveVersion == gs.net.version) return;
            aliveIds.Clear();
            foreach (var s in gs.net.AliveStations()) aliveIds.Add(s.id);
            aliveVersion = gs.net.version;
        }

        public float TotalWaiting
        {
            get
            {
                float t = 0f;
                foreach (int s in aliveIds)
                    if (s < cap) t += waitingTotal[s];
                return t;
            }
        }

        public void MinuteTick()
        {
            EnsureCapacity();
            RefreshAlive();
            Spawn();
            gs.gov.SpawnEventTrips(this);
            Abandon();
            foreach (int s in aliveIds) gs.net.stations[s].waiting = s < cap ? waitingTotal[s] : 0f;
        }

        void Spawn()
        {
            var demand = gs.demand;
            if (demand.cap == 0) return;
            var clock = gs.clock;
            var econ = gs.econ;
            var net = gs.net;
            int m = (Mathf.RoundToInt(clock.MinuteOfDay) - 1 + Balance.MinutesPerDay) % Balance.MinutesPerDay;
            bool weekend = clock.IsWeekend;
            float fOut = demand.profOut[m] * (weekend ? Balance.WeekendCommuteFactor : 1f);
            float fBack = demand.profBack[m] * (weekend ? Balance.WeekendCommuteFactor : 1f);
            float fOther = demand.profOther[m] * (weekend ? Balance.WeekendOtherFactor : 1f);
            bool peak = !weekend && GameClock.IsPeakMinute(m);
            float fare = econ.CurrentFare(peak);
            float peakFactor = peak && econ.peakSurcharge > 0f
                ? Mathf.Exp(-Balance.ModeShareSlope * 0.6f * econ.baseFare * econ.peakSurcharge / Balance.ValueOfTime)
                : 1f;
            float subsidy = Subsidy;
            int dc = demand.cap;
            var H = demand.H;
            var O = demand.O;
            var served = demand.ServedStations;
            float totalEntries = 0f, totalTurned = 0f;
            for (int si = 0; si < served.Count; si++)
            {
                int s = served[si];
                if (s >= cap) continue;
                var st = net.stations[s];
                if (st == null || !st.alive) continue;
                float crowd = waitingTotal[s] / Mathf.Max(1, st.Capacity);
                float gate = crowd < 1f ? 1f : Mathf.Clamp01(1f - (crowd - 1f) * 3f);
                int row = s * dc, prow = s * cap;
                float spawned = 0f, wanted = 0f;
                for (int xi = 0; xi < served.Count; xi++)
                {
                    int x = served[xi];
                    if (x == s || x >= cap) continue;
                    float r = H[row + x] * fOut + H[x * dc + s] * fBack + O[row + x] * fOther;
                    if (r <= 0f) continue;
                    r *= peakFactor;
                    wanted += r;
                    float a = r * gate;
                    waiting[prow + x] += a;
                    spawned += a;
                }
                waitingTotal[s] += spawned;
                st.entriesToday += spawned;
                st.turnedAwayToday += wanted - spawned;
                totalEntries += spawned;
                totalTurned += wanted - spawned;
            }
            mEntries += totalEntries;
            mTurnedAway += totalTurned;
            if (totalEntries > 0f)
            {
                econ.Earn(totalEntries * fare, LedgerCat.Fares);
                econ.Earn(totalEntries * subsidy, LedgerCat.Subsidy);
                mFares += totalEntries * fare;
            }
        }

        /// <summary>Per-rider operating subsidy: scaled by satisfaction and by how City Hall sees the metro.</summary>
        public float Subsidy => Balance.SubsidyPerRider * Mathf.Lerp(0.5f, 1.2f, gs.satisfaction.score) * gs.gov.SubsidyFactor;

        /// <summary>
        /// Adds extra trips from one station to another (special events). Crowded origin platforms turn
        /// some people away, exactly like regular demand. Returns how many actually entered the metro.
        /// </summary>
        public float AddTrips(int origin, int dest, float n)
        {
            if (n <= 0f || origin == dest) return 0f;
            EnsureCapacity();
            var net = gs.net;
            var st = net.GetStation(origin);
            if (st == null || net.GetStation(dest) == null || origin >= cap || dest >= cap) return 0f;
            var routing = gs.routing;
            int rc = routing.cap;
            if (origin >= rc || dest >= rc || routing.legLineDir[origin * rc + dest] < 0) return 0f;
            float crowd = waitingTotal[origin] / Mathf.Max(1, st.Capacity);
            float gate = crowd < 1f ? 1f : Mathf.Clamp01(1f - (crowd - 1f) * 3f);
            float a = n * gate;
            waiting[origin * cap + dest] += a;
            waitingTotal[origin] += a;
            st.entriesToday += a;
            st.turnedAwayToday += n - a;
            mEntries += a;
            mTurnedAway += n - a;
            float fare = gs.econ.CurrentFare(gs.clock.IsPeak);
            gs.econ.Earn(a * fare, LedgerCat.Fares);
            gs.econ.Earn(a * Subsidy, LedgerCat.Subsidy);
            mFares += a * fare;
            return a;
        }

        void Abandon()
        {
            var net = gs.net;
            var routing = gs.routing;
            int rc = routing.cap;
            int n = Mathf.Min(net.stations.Count, cap);
            float total = 0f;
            foreach (int s in aliveIds)
            {
                if (s >= cap) continue;
                float wt = waitingTotal[s];
                if (wt <= 0.01f)
                {
                    waitingTotal[s] = Mathf.Max(0f, wt);
                    continue;
                }
                var st = net.stations[s];
                float crowd = wt / Mathf.Max(1, st.Capacity);
                float baseRate = Balance.BaseAbandonPerMinute + Balance.CrowdAbandonPerMinute * Mathf.Max(0f, crowd - 0.9f);
                int prow = s * cap, rrow = s * rc;
                float lost = 0f, newTotal = 0f;
                for (int x = 0; x < n; x++)
                {
                    float w = waiting[prow + x];
                    if (w <= 0f) continue;
                    float rate = baseRate;
                    int leg = s < rc && x < rc ? routing.legLineDir[rrow + x] : -1;
                    if (leg < 0) rate = 0.2f;
                    else
                    {
                        var l = net.GetLine(RoutingSystem.KeyLine(leg));
                        float hw = l == null ? float.PositiveInfinity : ((leg & 1) == 0 ? l.headwayFwd : l.headwayBwd);
                        if (float.IsInfinity(hw)) rate = Balance.NoServiceAbandonPerMinute;
                    }
                    float a = w * rate;
                    w -= a;
                    if (w < 0.01f)
                    {
                        a += w;
                        w = 0f;
                    }
                    waiting[prow + x] = w;
                    lost += a;
                    newTotal += w;
                }
                waitingTotal[s] = newTotal;
                st.abandonedToday += lost;
                total += lost;
            }
            mAbandoned += total;
        }

        bool IsOnLine(int stationId, MetroLine line)
        {
            var st = gs.net.GetStation(stationId);
            return st != null && st.lines.Contains(line.id);
        }

        /// <summary>Handles alighting and boarding when a train stops. Returns passengers exchanged.</summary>
        public float OnTrainArrive(Train t, MetroLine line, int stopIndex, bool terminal)
        {
            EnsureCapacity();
            var net = gs.net;
            var routing = gs.routing;
            int S = line.stops[stopIndex];
            var st = net.stations[S];
            int prow = S * cap;
            float alighted = 0f, delivered = 0f, transferred = 0f;

            var group = t.cargo.Take(S);
            if (group != null)
            {
                foreach (var kv in group) Unload(S, prow, kv.Key, kv.Value, ref alighted, ref delivered, ref transferred);
                CargoHold.Release(group);
            }

            t.cargo.CollectKeys(tmpKeys);
            foreach (int a in tmpKeys)
            {
                if (!terminal && IsOnLine(a, line)) continue;
                var g = t.cargo.Take(a);
                if (g == null) continue;
                foreach (var kv in g) Unload(S, prow, kv.Key, kv.Value, ref alighted, ref delivered, ref transferred);
                CargoHold.Release(g);
            }

            float boarded = 0f, denied = 0f;
            var dests = routing.BoardList(S, RoutingSystem.LineDirKey(line.id, t.dir));
            if (dests != null)
            {
                float want = 0f;
                foreach (int x in dests)
                    if (x < cap) want += waiting[prow + x];
                if (want > 0.01f)
                {
                    float room = Mathf.Max(0f, t.capacity - t.cargo.total);
                    float f = want <= room ? 1f : room / want;
                    if (f > 0f)
                    {
                        int rrow = S * routing.cap;
                        foreach (int x in dests)
                        {
                            if (x >= cap) continue;
                            float w = waiting[prow + x];
                            if (w <= 0f) continue;
                            float b = w * f;
                            waiting[prow + x] = w - b;
                            int al = routing.legAlight[rrow + x];
                            if (al < 0) al = x;
                            t.cargo.Add(al, x, b);
                            boarded += b;
                        }
                    }
                    denied = Mathf.Max(0f, want - boarded);
                }
            }

            waitingTotal[S] = Mathf.Max(0f, waitingTotal[S] + transferred - boarded);
            st.waiting = waitingTotal[S];
            st.boardingsToday += boarded;
            st.transfersToday += transferred;
            line.boardingsToday += boarded;
            line.deniedToday += denied;
            mBoardings += boarded;
            mDenied += denied;
            mDelivered += delivered;
            mTransfers += transferred;
            if (boarded > 0f) gs.econ.Earn(boarded * Balance.AdRevenuePerBoarding[gs.econ.adLevel], LedgerCat.Ads);

            int idx = stopIndex * 2 + RoutingSystem.DirBit(t.dir);
            if (idx < line.lastArrival.Length)
            {
                double now = gs.clock.totalMinutes;
                double last = line.lastArrival[idx];
                if (last >= 0)
                {
                    float interval = Mathf.Min((float)(now - last), 60f);
                    float people = boarded + denied;
                    float wsum = boarded * interval * 0.5f + denied * interval;
                    line.waitSum += wsum;
                    line.waitWeight += people;
                    float obs = people > 0.5f ? wsum / people : interval * 0.5f;
                    line.avgWaitRecent = Mathf.Lerp(line.avgWaitRecent, obs, 0.04f);
                }
                line.lastArrival[idx] = now;
            }
            return alighted + boarded;
        }

        void Unload(int S, int prow, int dest, float n, ref float alighted, ref float delivered, ref float transferred)
        {
            alighted += n;
            if (dest == S)
            {
                delivered += n;
                return;
            }
            if (dest >= cap || gs.net.GetStation(dest) == null) return; // destination closed: trip lost
            waiting[prow + dest] += n;
            transferred += n;
        }

        /// <summary>Puts everyone on a train back on the platform of a station (line changed or train removed).</summary>
        public void DumpCargo(Train t, int stationId)
        {
            EnsureCapacity();
            var st = gs.net.GetStation(stationId);
            if (st == null)
            {
                t.cargo.Clear();
                return;
            }
            int prow = stationId * cap;
            t.cargo.CollectKeys(tmpKeys);
            float alighted = 0f, delivered = 0f, transferred = 0f;
            foreach (int a in tmpKeys)
            {
                var g = t.cargo.Take(a);
                if (g == null) continue;
                foreach (var kv in g) Unload(stationId, prow, kv.Key, kv.Value, ref alighted, ref delivered, ref transferred);
                CargoHold.Release(g);
            }
            waitingTotal[stationId] += transferred;
            st.waiting = waitingTotal[stationId];
        }

        public void ClearStation(int s)
        {
            if (s >= cap) return;
            int prow = s * cap;
            for (int x = 0; x < cap; x++) waiting[prow + x] = 0f;
            waitingTotal[s] = 0f;
        }

        public void ClearAll()
        {
            System.Array.Clear(waiting, 0, waiting.Length);
            System.Array.Clear(waitingTotal, 0, waitingTotal.Length);
        }
    }
}
