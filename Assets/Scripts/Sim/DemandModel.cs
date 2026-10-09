using System;
using System.Collections.Generic;
using UnityEngine;

namespace Metro
{
    /// <summary>
    /// Turns the city into station-to-station daily trip rates.
    /// 1. Catchments: each populated cell walks to its nearest served station.
    /// 2. Destination choice: gravity model over all jobs/attractions of the revealed city,
    ///    so unserved parts of the city "leak" demand and expansion pays off.
    /// 3. Mode choice: logit between metro generalised cost (walk, wait, ride, transfers, fare)
    ///    and driving, which gets slower as the city grows.
    /// </summary>
    public sealed class DemandModel
    {
        readonly GameState gs;

        public int cap;
        public float[] resAt = new float[0], jobAt = new float[0], attAt = new float[0];
        public float[] H = new float[0];   // daily commute trips home(S) -> work(X)
        public float[] O = new float[0];   // daily other trips S -> X
        public bool[] served = new bool[0];
        public int[] cellStation = new int[0];
        public readonly float[] profOut = new float[Balance.MinutesPerDay];
        public readonly float[] profBack = new float[Balance.MinutesPerDay];
        public readonly float[] profOther = new float[Balance.MinutesPerDay];
        public float dailyTrips;
        public float carSpeed = Balance.CarSpeedBase;
        public int version;
        public float lastComputeMs;

        const float ZoneSize = 2f;
        const float GridSize = 1f;
        int zw, zh;
        float[] zoneJobs = new float[0], zoneAtt = new float[0], zoneW = new float[0];
        Vector2[] zonePos = new Vector2[0], zoneSum = new Vector2[0];
        float[] zc = new float[0], zo = new float[0];
        List<int>[] sgrid;
        int sgw, sgh;
        readonly List<int> servedIds = new List<int>();

        public DemandModel(GameState gs)
        {
            this.gs = gs;
            BuildProfiles();
        }

        static float G(float h, float mu, float sigma)
        {
            float z = (h - mu) / sigma;
            return Mathf.Exp(-0.5f * z * z);
        }

        static float ServiceMask(float h)
        {
            if (h >= 5f) return Geo.SmoothStep(5f, 5.5f, h);
            if (h < 1f) return 1f - Geo.SmoothStep(0.4f, 1f, h);
            return 0f;
        }

        void BuildProfiles()
        {
            float so = 0f, sb = 0f, sx = 0f;
            for (int m = 0; m < Balance.MinutesPerDay; m++)
            {
                float h = (m + 0.5f) / 60f;
                float mask = ServiceMask(h);
                profOut[m] = mask * (0.72f * G(h, 8.0f, 0.75f) + 0.28f * G(h, 9.6f, 1.4f));
                profBack[m] = mask * (0.68f * G(h, 17.6f, 0.9f) + 0.32f * G(h, 19.6f, 1.6f));
                profOther[m] = mask * (0.18f * G(h, 10.5f, 1.6f) + 0.3f * G(h, 13.2f, 2.0f) + 0.34f * G(h, 18.6f, 2.0f)
                                       + 0.18f * G(h, 21.4f, 1.3f) + 0.06f * G(h, 23.6f, 0.8f));
                so += profOut[m];
                sb += profBack[m];
                sx += profOther[m];
            }
            for (int m = 0; m < Balance.MinutesPerDay; m++)
            {
                profOut[m] /= so;
                profBack[m] /= sb;
                profOther[m] /= sx;
            }
        }

        /// <summary>Relative total demand at a minute of the day (for the time-of-day bar).</summary>
        public float CombinedProfile(int minute, bool weekend)
        {
            minute = ((minute % Balance.MinutesPerDay) + Balance.MinutesPerDay) % Balance.MinutesPerDay;
            float c = weekend ? Balance.WeekendCommuteFactor : 1f;
            float o = weekend ? Balance.WeekendOtherFactor : 1f;
            return 0.5f * c * (profOut[minute] + profBack[minute]) + 0.55f * o * profOther[minute];
        }

        void EnsureCapacity(int n)
        {
            int want = RoutingSystem.CapacityFor(n);
            if (want != cap)
            {
                cap = want;
                H = new float[cap * cap];
                O = new float[cap * cap];
            }
            if (resAt.Length != cap)
            {
                resAt = new float[cap];
                jobAt = new float[cap];
                attAt = new float[cap];
                served = new bool[cap];
                zc = new float[cap];
                zo = new float[cap];
            }
        }

        public float ModeShare(float metroCost, float d, float fare)
        {
            float metro = Balance.AccessWalk + metroCost + Balance.EgressWalk + fare / Balance.ValueOfTime;
            float road = d * 1.3f;
            float car = Balance.CarOverhead + road / Mathf.Max(5f, carSpeed) * 60f
                        + (Balance.CarCostPerKm * road + Balance.CarFixedCost) / Balance.ValueOfTime;
            float s = Balance.MaxModeShare / (1f + Mathf.Exp(Balance.ModeShareSlope * (metro - car)));
            return s * Geo.SmoothStep(0.5f, 2.2f, d);
        }

        public void Recompute()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var city = gs.city;
            var net = gs.net;
            int n = net.stations.Count;
            EnsureCapacity(n);
            Array.Clear(resAt, 0, resAt.Length);
            Array.Clear(jobAt, 0, jobAt.Length);
            Array.Clear(attAt, 0, attAt.Length);
            Array.Clear(served, 0, served.Length);
            Array.Clear(H, 0, H.Length);
            Array.Clear(O, 0, O.Length);

            foreach (var l in net.AliveLines())
            {
                if (l.trains.Count == 0) continue;
                foreach (int s in l.stops) served[s] = true;
            }
            servedIds.Clear();
            for (int i = 0; i < n; i++)
            {
                var st = net.stations[i];
                if (served[i] && st != null && st.alive) servedIds.Add(i);
                else served[i] = false;
            }
            BuildStationGrid(city);

            // Catchments.
            int cells = city.CellCount;
            if (cellStation.Length != cells) cellStation = new int[cells];
            float r2 = Balance.WalkRadius * Balance.WalkRadius;
            for (int i = 0; i < cells; i++)
            {
                cellStation[i] = -1;
                city.cellAccess[i] = 0f;
                if (!city.IsCellActive(i)) continue;
                float r = city.res[i], j = city.jobs[i], a = city.att[i];
                if (r + j + a <= 0f) continue;
                int best = NearestServed(city.CellCenter(i), r2, out float d2);
                if (best < 0) continue;
                float w = 1f - 0.55f * d2 / r2;
                cellStation[i] = best;
                city.cellAccess[i] = w;
                resAt[best] += r * w;
                jobAt[best] += j * w;
                attAt[best] += a * w;
            }
            city.RefreshAggregates();

            BuildZones(city);
            int nz = zw * zh;
            foreach (int s in servedIds)
            {
                Vector2 p = net.stations[s].pos;
                float sc = 0f, so = 0f;
                for (int z = 0; z < nz; z++)
                {
                    if (zoneW[z] <= 0f) continue;
                    float d = Vector2.Distance(p, zonePos[z]);
                    sc += zoneJobs[z] * Mathf.Exp(-d / Balance.CommuteDecayKm);
                    so += zoneAtt[z] * Mathf.Exp(-d / Balance.OtherDecayKm);
                }
                zc[s] = sc;
                zo[s] = so;
            }

            float fare = gs.econ.baseFare;
            carSpeed = Balance.CarSpeedBase / (1f + city.revealedPopulation / Balance.CarCongestionPop);
            float satMul = Mathf.Lerp(0.6f, 1.25f, gs.satisfaction.score);
            var routing = gs.routing;
            float total = 0f;
            foreach (int s in servedIds)
            {
                Vector2 ps = net.stations[s].pos;
                float R = resAt[s];
                float other = R * Balance.OtherTripsPerResident + jobAt[s] * Balance.OtherTripsPerJob;
                float invZc = zc[s] > 1f ? 1f / zc[s] : 0f;
                float invZo = zo[s] > 1f ? 1f / zo[s] : 0f;
                int row = s * cap;
                foreach (int x in servedIds)
                {
                    if (x == s) continue;
                    float c = routing.Cost(s, x);
                    if (float.IsInfinity(c)) continue;
                    float d = Vector2.Distance(ps, net.stations[x].pos);
                    float share = Mathf.Min(0.95f, ModeShare(c, d, fare) * satMul);
                    if (share <= 1e-4f) continue;
                    float pw = jobAt[x] * Mathf.Exp(-d / Balance.CommuteDecayKm) * invZc;
                    float po = (attAt[x] + 0.5f * jobAt[x]) * Mathf.Exp(-d / Balance.OtherDecayKm) * invZo;
                    float h = R * Balance.CommuteTripsPerResident * pw * share;
                    float o = other * po * share;
                    H[row + x] = h;
                    O[row + x] = o;
                    total += 2f * h + o;
                }
            }
            dailyTrips = total;
            version++;
            lastComputeMs = (float)sw.Elapsed.TotalMilliseconds;
        }

        void BuildStationGrid(CityModel city)
        {
            int w = Mathf.CeilToInt(city.width / GridSize) + 1, h = Mathf.CeilToInt(city.height / GridSize) + 1;
            if (sgrid == null || sgw != w || sgh != h)
            {
                sgw = w;
                sgh = h;
                sgrid = new List<int>[w * h];
                for (int i = 0; i < sgrid.Length; i++) sgrid[i] = new List<int>();
            }
            else
            {
                foreach (var l in sgrid) l.Clear();
            }
            foreach (int s in servedIds)
            {
                Vector2 p = gs.net.stations[s].pos;
                int gx = Mathf.Clamp(Mathf.FloorToInt(p.x / GridSize), 0, sgw - 1);
                int gy = Mathf.Clamp(Mathf.FloorToInt(p.y / GridSize), 0, sgh - 1);
                sgrid[gy * sgw + gx].Add(s);
            }
        }

        int NearestServed(Vector2 p, float maxD2, out float bestD2)
        {
            int gx = Mathf.FloorToInt(p.x / GridSize), gy = Mathf.FloorToInt(p.y / GridSize);
            int best = -1;
            bestD2 = maxD2;
            for (int dy = -1; dy <= 1; dy++)
            {
                int y = gy + dy;
                if (y < 0 || y >= sgh) continue;
                for (int dx = -1; dx <= 1; dx++)
                {
                    int x = gx + dx;
                    if (x < 0 || x >= sgw) continue;
                    var bucket = sgrid[y * sgw + x];
                    for (int k = 0; k < bucket.Count; k++)
                    {
                        int id = bucket[k];
                        float d2 = (gs.net.stations[id].pos - p).sqrMagnitude;
                        if (d2 < bestD2)
                        {
                            bestD2 = d2;
                            best = id;
                        }
                    }
                }
            }
            return best;
        }

        void BuildZones(CityModel city)
        {
            zw = Mathf.CeilToInt(city.width / ZoneSize);
            zh = Mathf.CeilToInt(city.height / ZoneSize);
            int nz = zw * zh;
            if (zoneJobs.Length != nz)
            {
                zoneJobs = new float[nz];
                zoneAtt = new float[nz];
                zoneW = new float[nz];
                zonePos = new Vector2[nz];
                zoneSum = new Vector2[nz];
            }
            Array.Clear(zoneJobs, 0, nz);
            Array.Clear(zoneAtt, 0, nz);
            Array.Clear(zoneW, 0, nz);
            Array.Clear(zoneSum, 0, nz);
            int cells = city.CellCount;
            for (int i = 0; i < cells; i++)
            {
                if (!city.IsCellActive(i)) continue;
                float j = city.jobs[i], a = city.att[i], r = city.res[i];
                float w = j + a + r;
                if (w <= 0f) continue;
                Vector2 p = city.CellCenter(i);
                int zx = Mathf.Min(zw - 1, Mathf.FloorToInt(p.x / ZoneSize));
                int zy = Mathf.Min(zh - 1, Mathf.FloorToInt(p.y / ZoneSize));
                int z = zy * zw + zx;
                zoneJobs[z] += j;
                zoneAtt[z] += a + 0.5f * j;
                zoneW[z] += w;
                zoneSum[z] += p * w;
            }
            for (int z = 0; z < nz; z++)
                zonePos[z] = zoneW[z] > 0f ? zoneSum[z] / zoneW[z] : Vector2.zero;
        }

        /// <summary>Expected daily trips starting or ending at a station.</summary>
        public float StationDailyTrips(int s)
        {
            if (s < 0 || s >= cap || !served[s]) return 0f;
            float t = 0f;
            int row = s * cap;
            foreach (int x in servedIds)
            {
                t += H[row + x] + O[row + x];
                t += H[x * cap + s] + O[x * cap + s];
            }
            return t;
        }

        public IReadOnlyList<int> ServedStations => servedIds;
    }
}
