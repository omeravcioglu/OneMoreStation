using System;
using System.Collections.Generic;
using UnityEngine;

namespace Metro
{
    /// <summary>
    /// Computes the best route between every pair of stations on a graph of
    /// station nodes and per-line/per-direction platform nodes. Waiting time is half
    /// the headway, transfers carry a penalty. For each origin/destination pair it stores
    /// the first leg (line, direction, alight station) so passengers know which train to board.
    /// </summary>
    public sealed class RoutingSystem
    {
        readonly GameState gs;

        public int cap;
        public float[] cost = new float[0];
        public int[] legLineDir = new int[0];
        public int[] legAlight = new int[0];
        public byte[] transfers = new byte[0];
        public Dictionary<int, List<int>>[] boardGroups = new Dictionary<int, List<int>>[0];
        public int version;
        public float lastComputeMs;

        // Graph (CSR).
        int nodeCount;
        int[] adjStart = new int[0];
        int[] adjTo = new int[0];
        float[] adjCost = new float[0];
        byte[] adjKind = new byte[0];
        int[] nodeLineDir = new int[0];
        int[] nodeStation = new int[0];

        // Dijkstra scratch.
        float[] dist = new float[0];
        int[] dLeg = new int[0];
        int[] dAlight = new int[0];
        byte[] dTr = new byte[0];
        readonly MinHeap heap = new MinHeap();

        const byte KindBoard = 0, KindRide = 1, KindAlight = 2;

        public RoutingSystem(GameState gs)
        {
            this.gs = gs;
        }

        public static int DirBit(int dir) => dir > 0 ? 0 : 1;
        public static int LineDirKey(int lineId, int dir) => lineId * 2 + DirBit(dir);
        public static int KeyLine(int key) => key >> 1;
        public static int KeyDir(int key) => (key & 1) == 0 ? 1 : -1;

        public float Cost(int s, int x) => s < cap && x < cap ? cost[s * cap + x] : float.PositiveInfinity;

        public List<int> BoardList(int station, int lineDirKey)
        {
            if (station < 0 || station >= boardGroups.Length) return null;
            var g = boardGroups[station];
            if (g == null) return null;
            return g.TryGetValue(lineDirKey, out var list) ? list : null;
        }

        public static int CapacityFor(int n) => Mathf.Max(64, ((n + 31) / 32) * 32);

        void EnsureCapacity(int n)
        {
            int want = CapacityFor(n);
            if (want == cap) return;
            cap = want;
            int sz = cap * cap;
            cost = new float[sz];
            legLineDir = new int[sz];
            legAlight = new int[sz];
            transfers = new byte[sz];
        }

        public static float TransferPenalty(Station s) => s.concourse ? Balance.TransferPenaltyConcourse : Balance.TransferPenalty;

        /// <summary>Cycle time and headway of every line from its geometry and train count.</summary>
        public void ComputeServiceLevels()
        {
            foreach (var l in gs.net.AliveLines())
            {
                if (l.path == null || l.SegmentCount == 0)
                {
                    l.cycleTime = 0f;
                    l.headway = l.headwayFwd = l.headwayBwd = float.PositiveInfinity;
                    continue;
                }
                float run = 0f;
                for (int k = 0; k < l.path.SegmentCount; k++) run += l.path.segTime[k] + Balance.DwellEstimate;
                int fwd = 0, bwd = 0;
                foreach (int tid in l.trains)
                {
                    var t = gs.trains.Get(tid);
                    if (t == null) continue;
                    if (t.dir > 0) fwd++;
                    else bwd++;
                }
                int total = fwd + bwd;
                if (!l.loop)
                {
                    l.cycleTime = 2f * run + 2f * Balance.Turnaround;
                    float h = total > 0 ? l.cycleTime / total : float.PositiveInfinity;
                    l.headwayFwd = l.headwayBwd = h;
                }
                else
                {
                    l.cycleTime = run;
                    l.headwayFwd = fwd > 0 ? run / fwd : float.PositiveInfinity;
                    l.headwayBwd = bwd > 0 ? run / bwd : float.PositiveInfinity;
                }
                l.headway = Mathf.Min(l.headwayFwd, l.headwayBwd);
            }
        }

        public void Recompute()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var net = gs.net;
            int n = net.stations.Count;
            EnsureCapacity(n);
            ComputeServiceLevels();
            BuildGraph();

            for (int i = 0; i < cost.Length; i++)
            {
                cost[i] = float.PositiveInfinity;
                legLineDir[i] = -1;
                legAlight[i] = -1;
                transfers[i] = 0;
            }
            if (dist.Length < nodeCount)
            {
                dist = new float[nodeCount];
                dLeg = new int[nodeCount];
                dAlight = new int[nodeCount];
                dTr = new byte[nodeCount];
            }

            foreach (var s in net.AliveStations())
            {
                if (s.lines.Count == 0) continue;
                RunDijkstra(s.id, n);
            }
            BuildBoardGroups(n);
            version++;
            lastComputeMs = (float)sw.Elapsed.TotalMilliseconds;
        }

        struct EdgeTmp
        {
            public int from, to;
            public float cost;
            public byte kind;
        }

        readonly List<EdgeTmp> tmpEdges = new List<EdgeTmp>(4096);

        void BuildGraph()
        {
            var net = gs.net;
            int n = net.stations.Count;
            tmpEdges.Clear();
            int next = n;
            var lineBase = new Dictionary<int, int>();
            foreach (var l in net.AliveLines())
            {
                if (l.path == null || l.stops.Count < 2) continue;
                lineBase[l.id] = next;
                next += l.stops.Count * 2;
            }
            nodeCount = next;
            if (nodeLineDir.Length < nodeCount)
            {
                nodeLineDir = new int[nodeCount];
                nodeStation = new int[nodeCount];
            }
            for (int i = 0; i < n; i++)
            {
                nodeLineDir[i] = -1;
                nodeStation[i] = i;
            }

            foreach (var l in net.AliveLines())
            {
                if (!lineBase.TryGetValue(l.id, out int baseNode)) continue;
                int ns = l.stops.Count;
                for (int i = 0; i < ns; i++)
                {
                    int st = l.stops[i];
                    var station = net.stations[st];
                    for (int db = 0; db < 2; db++)
                    {
                        int dir = db == 0 ? 1 : -1;
                        int node = baseNode + i * 2 + db;
                        nodeLineDir[node] = l.id * 2 + db;
                        nodeStation[node] = st;
                        int nextStop = NextStop(l, i, dir);
                        float headway = db == 0 ? l.headwayFwd : l.headwayBwd;
                        if (nextStop >= 0 && !float.IsInfinity(headway))
                        {
                            float wait = Mathf.Min(headway * 0.5f, Balance.MaxWaitCost);
                            tmpEdges.Add(new EdgeTmp { from = st, to = node, cost = wait + Balance.BoardPenalty, kind = KindBoard });
                        }
                        if (nextStop >= 0)
                        {
                            int seg = dir > 0 ? i : nextStop;
                            float ride = l.path.segTime[seg] + Balance.DwellEstimate;
                            tmpEdges.Add(new EdgeTmp { from = node, to = baseNode + nextStop * 2 + db, cost = ride, kind = KindRide });
                        }
                        tmpEdges.Add(new EdgeTmp { from = node, to = st, cost = TransferPenalty(station), kind = KindAlight });
                    }
                }
            }

            // CSR conversion.
            if (adjStart.Length < nodeCount + 1) adjStart = new int[nodeCount + 1];
            for (int i = 0; i <= nodeCount; i++) adjStart[i] = 0;
            foreach (var e in tmpEdges) adjStart[e.from + 1]++;
            for (int i = 0; i < nodeCount; i++) adjStart[i + 1] += adjStart[i];
            int m = tmpEdges.Count;
            if (adjTo.Length < m)
            {
                adjTo = new int[m];
                adjCost = new float[m];
                adjKind = new byte[m];
            }
            var fill = new int[nodeCount];
            foreach (var e in tmpEdges)
            {
                int idx = adjStart[e.from] + fill[e.from]++;
                adjTo[idx] = e.to;
                adjCost[idx] = e.cost;
                adjKind[idx] = e.kind;
            }
        }

        public static int NextStop(MetroLine l, int i, int dir)
        {
            int n = l.stops.Count;
            if (l.loop) return ((i + dir) % n + n) % n;
            int j = i + dir;
            return j >= 0 && j < n ? j : -1;
        }

        void RunDijkstra(int source, int stationCount)
        {
            for (int i = 0; i < nodeCount; i++)
            {
                dist[i] = float.PositiveInfinity;
                dLeg[i] = -1;
                dAlight[i] = -1;
                dTr[i] = 0;
            }
            dist[source] = 0f;
            heap.Clear();
            heap.Push(0f, source);
            while (heap.Count > 0)
            {
                heap.Pop(out float d, out int u);
                if (d > dist[u]) continue;
                int end = adjStart[u + 1];
                for (int e = adjStart[u]; e < end; e++)
                {
                    int v = adjTo[e];
                    float nd = d + adjCost[e];
                    if (nd >= dist[v] - 1e-5f) continue;
                    dist[v] = nd;
                    byte kind = adjKind[e];
                    if (kind == KindBoard)
                    {
                        if (dLeg[u] < 0)
                        {
                            dLeg[v] = nodeLineDir[v];
                            dAlight[v] = -1;
                            dTr[v] = 0;
                        }
                        else
                        {
                            dLeg[v] = dLeg[u];
                            dAlight[v] = dAlight[u];
                            dTr[v] = (byte)Mathf.Min(255, dTr[u] + 1);
                        }
                    }
                    else if (kind == KindRide)
                    {
                        dLeg[v] = dLeg[u];
                        dAlight[v] = dAlight[u];
                        dTr[v] = dTr[u];
                    }
                    else
                    {
                        dLeg[v] = dLeg[u];
                        dAlight[v] = dAlight[u] >= 0 ? dAlight[u] : nodeStation[u];
                        dTr[v] = dTr[u];
                    }
                    heap.Push(nd, v);
                }
            }

            var net = gs.net;
            int row = source * cap;
            for (int x = 0; x < stationCount; x++)
            {
                if (x == source) continue;
                var st = net.stations[x];
                if (st == null || !st.alive || float.IsInfinity(dist[x]) || dLeg[x] < 0) continue;
                cost[row + x] = dist[x] - TransferPenalty(st);
                legLineDir[row + x] = dLeg[x];
                legAlight[row + x] = dAlight[x];
                transfers[row + x] = dTr[x];
            }
        }

        void BuildBoardGroups(int n)
        {
            if (boardGroups.Length != cap) boardGroups = new Dictionary<int, List<int>>[cap];
            else Array.Clear(boardGroups, 0, boardGroups.Length);
            for (int s = 0; s < n; s++)
            {
                var st = gs.net.stations[s];
                if (st == null || !st.alive || st.lines.Count == 0) continue;
                Dictionary<int, List<int>> dict = null;
                int row = s * cap;
                for (int x = 0; x < n; x++)
                {
                    int key = legLineDir[row + x];
                    if (key < 0) continue;
                    dict ??= new Dictionary<int, List<int>>();
                    if (!dict.TryGetValue(key, out var list)) dict[key] = list = new List<int>();
                    list.Add(x);
                }
                boardGroups[s] = dict;
            }
        }
    }

    /// <summary>Binary min-heap of (key, value) pairs with lazy deletion.</summary>
    public sealed class MinHeap
    {
        float[] keys = new float[256];
        int[] vals = new int[256];
        public int Count { get; private set; }

        public void Clear() => Count = 0;

        public void Push(float key, int val)
        {
            if (Count == keys.Length)
            {
                Array.Resize(ref keys, keys.Length * 2);
                Array.Resize(ref vals, vals.Length * 2);
            }
            int i = Count++;
            while (i > 0)
            {
                int p = (i - 1) >> 1;
                if (keys[p] <= key) break;
                keys[i] = keys[p];
                vals[i] = vals[p];
                i = p;
            }
            keys[i] = key;
            vals[i] = val;
        }

        public void Pop(out float key, out int val)
        {
            key = keys[0];
            val = vals[0];
            Count--;
            if (Count == 0) return;
            float k = keys[Count];
            int v = vals[Count];
            int i = 0;
            while (true)
            {
                int c = i * 2 + 1;
                if (c >= Count) break;
                if (c + 1 < Count && keys[c + 1] < keys[c]) c++;
                if (keys[c] >= k) break;
                keys[i] = keys[c];
                vals[i] = vals[c];
                i = c;
            }
            keys[i] = k;
            vals[i] = v;
        }
    }
}
