using System;
using System.Collections.Generic;
using UnityEngine;

namespace Metro
{
    /// <summary>
    /// Draws the network in the Doodle Metro style: candy lines on ink casings (one constant width),
    /// plank bridges over water, stations that wear their mood on their face (chill, busy, squished,
    /// meltdown) with a little queue beside them, white interchanges with a mood dot, white trains with
    /// windows in their line's colour, garage depots, a highlighter under the selected line, pencil
    /// plans and the active tool's preview.
    /// </summary>
    public sealed class NetworkRenderer
    {
        readonly Transform root;
        readonly Shader shader;
        GameState gs;

        MeshLayer plans, glow, casing, lines, under, depots, stations, trains, select, preview;
        public const int QPlans = 3018, QGlow = 3019, QCasing = 3020, QLines = 3021, QUnder = 3022, QDepots = 3023,
            QStations = 3024, QTrains = 3025, QSelect = 3026, QPreview = 3027;

        readonly Dictionary<int, Dictionary<long, int>> segIndex = new Dictionary<int, Dictionary<long, int>>();
        readonly List<Vector2> tmp = new List<Vector2>();
        readonly List<Vector2> pts = new List<Vector2>();
        int lastVersion = -1;
        float lineTimer;
        float time;
        Bounds bounds;
        Rect view;

        public int selectedStation = -1, selectedLine = -1, selectedTrain = -1, selectedDepot = -1;
        public int hoverStation = -1, hoverLine = -1, hoverTrain = -1;
        int lastSelectedLine = -2, lastHoverLine = -2;

        /// <summary>Set by the Plan tool: pencil plans are drawn boldly and the hovered one turns red.</summary>
        public bool planMode;
        public int hoverPlan = -1;
        int shownPlans = -1;

        /// <summary>Tool hook: draws the tool's preview into the builder.</summary>
        public Action<VectorMeshBuilder, float> previewBuilder;

        static readonly Color32[] People =
        {
            Balance.Hex("#4FC3F7"), Balance.Hex("#FF7EB6"), Balance.Hex("#A178FF"), Balance.Hex("#5FE0B0"),
            Balance.Hex("#FFD23F"), Balance.Hex("#FF9A3D"), Balance.Hex("#4FC3F7")
        };

        public NetworkRenderer(Transform parent)
        {
            root = new GameObject("Network").transform;
            root.SetParent(parent, false);
            shader = Resources.Load<Shader>("Shaders/MetroVector");
        }

        public void Bind(GameState state)
        {
            Clear();
            gs = state;
            bounds = MeshLayer.WorldBounds(gs.city);
            plans = new MeshLayer("Plans", root, shader, QPlans);
            glow = new MeshLayer("Glow", root, shader, QGlow);
            casing = new MeshLayer("Casing", root, shader, QCasing);
            lines = new MeshLayer("Lines", root, shader, QLines);
            under = new MeshLayer("Under", root, shader, QUnder);
            depots = new MeshLayer("Depots", root, shader, QDepots);
            stations = new MeshLayer("Stations", root, shader, QStations);
            trains = new MeshLayer("Trains", root, shader, QTrains);
            select = new MeshLayer("Select", root, shader, QSelect);
            preview = new MeshLayer("Preview", root, shader, QPreview);
            lastVersion = -1;
            selectedStation = selectedLine = selectedTrain = selectedDepot = -1;
            hoverStation = hoverLine = hoverTrain = -1;
        }

        public void Clear()
        {
            foreach (var l in new[] { plans, glow, casing, lines, under, depots, stations, trains, select, preview }) l?.Destroy();
            plans = glow = casing = lines = under = depots = stations = trains = select = preview = null;
            shownPlans = -1;
            gs = null;
        }

        public void ClearSelection()
        {
            selectedStation = selectedLine = selectedTrain = selectedDepot = -1;
        }

        // ------------------------------------------------------------------ geometry helpers

        public static bool IsServed(MetroNetwork net, Station s)
        {
            foreach (int lid in s.lines)
            {
                var l = net.GetLine(lid);
                if (l != null && l.trains.Count > 0) return true;
            }
            return false;
        }

        /// <summary>Outer radius of a station marker (to the outside of its ink ring), in line half-widths.</summary>
        public static float StationRadiusUnits(Station s)
        {
            float r = NetStyle.StationRadius + 0.27f;
            if (s.IsInterchange) r = Mathf.Max(2.6f, s.laneExtent + 1.35f);
            return r;
        }

        public static float HalfWidthWorld(float pixelWorld) => NetStyle.LineWidth.Evaluate(pixelWorld);

        public static Vector2 TrainOffsetUnits(Train t)
        {
            Vector2 pathTan = t.dir > 0 ? t.heading : -t.heading;
            return Geo.Perp(pathTan) * t.lane - Geo.Perp(t.heading) * 0.45f;
        }

        public static Vector2 TrainWorldPos(Train t, float pixelWorld) => t.pos + TrainOffsetUnits(t) * HalfWidthWorld(pixelWorld);

        /// <summary>Where a depot's little garage sits, next to its station.</summary>
        public static Vector2 DepotWorldPos(MetroNetwork net, Depot d, float pixelWorld)
        {
            var st = net.stations[d.stationId];
            return st.pos + d.iconDir * ((StationRadiusUnits(st) + 4.4f) * HalfWidthWorld(pixelWorld));
        }

        /// <summary>0 chill (up to 40% of the platform), 1 busy (to 70%), 2 squished (to 90%), 3 meltdown.</summary>
        public static int MoodOf(float crowding) => crowding <= 0.4f ? 0 : crowding <= 0.7f ? 1 : crowding < 0.9f ? 2 : 3;

        static void SubPath(List<Vector2> path, List<float> cum, float s0, float s1, List<Vector2> into)
        {
            into.Clear();
            float total = cum[cum.Count - 1];
            s0 = Mathf.Clamp(s0, 0f, total);
            s1 = Mathf.Clamp(s1, 0f, total);
            if (s1 - s0 < 1e-4f) return;
            into.Add(Geo.SampleAt(path, cum, s0, out _));
            for (int i = 1; i < path.Count - 1; i++)
                if (cum[i] > s0 && cum[i] < s1) into.Add(path[i]);
            into.Add(Geo.SampleAt(path, cum, s1, out _));
        }

        // ------------------------------------------------------------------ picking

        public Station PickStation(Vector2 world, float pixelWorld, float extraPx = 7f)
        {
            if (gs == null) return null;
            float hw = HalfWidthWorld(pixelWorld);
            Station best = null;
            float bestD = float.MaxValue;
            foreach (var s in gs.net.AliveStations())
            {
                float r = (StationRadiusUnits(s) + 0.3f) * hw + extraPx * pixelWorld;
                float d = Vector2.Distance(world, s.pos);
                if (d <= r && d < bestD)
                {
                    bestD = d;
                    best = s;
                }
            }
            return best;
        }

        public Train PickTrain(Vector2 world, float pixelWorld)
        {
            if (gs == null) return null;
            float hw = HalfWidthWorld(pixelWorld);
            Train best = null;
            float bestD = 9f * pixelWorld + 2f * hw;
            foreach (var t in gs.trains.trains)
            {
                if (!t.alive) continue;
                float d = Vector2.Distance(world, t.pos + TrainOffsetUnits(t) * hw);
                if (d < bestD)
                {
                    bestD = d;
                    best = t;
                }
            }
            return best;
        }

        public Depot PickDepot(Vector2 world, float pixelWorld)
        {
            if (gs == null) return null;
            float hw = HalfWidthWorld(pixelWorld);
            foreach (var d in gs.net.AliveDepots())
                if (Vector2.Distance(world, DepotWorldPos(gs.net, d, pixelWorld)) < 3f * hw + 5f * pixelWorld) return d;
            return null;
        }

        /// <summary>Nearest track edge (and the line drawn closest to the cursor on it).</summary>
        public TrackEdge PickEdge(Vector2 world, float pixelWorld, out MetroLine line, float maxPx = 8f)
        {
            line = null;
            if (gs == null) return null;
            float hw = HalfWidthWorld(pixelWorld);
            TrackEdge best = null;
            float bestD = float.MaxValue;
            foreach (var e in gs.net.edges.Values)
            {
                float extent = ((e.lines.Count - 1) * 0.5f * NetStyle.LaneSpacing + 1.2f) * hw + maxPx * pixelWorld;
                float d = Geo.DistToPolyline(world, e.path, out int seg, out float t);
                if (d > extent || d >= bestD) continue;
                bestD = d;
                best = e;
                // choose the lane nearest to the cursor
                Vector2 a = e.path[seg], b = e.path[seg + 1];
                Vector2 n = Geo.Perp((b - a).normalized);
                float side = Vector2.Dot(world - Vector2.Lerp(a, b, t), n) / Mathf.Max(hw, 1e-6f);
                float bestLane = float.MaxValue;
                line = null;
                foreach (int lid in e.lines)
                {
                    float dl = Mathf.Abs(e.LaneUnits(lid) - side);
                    if (dl < bestLane)
                    {
                        bestLane = dl;
                        line = gs.net.GetLine(lid);
                    }
                }
            }
            return best;
        }

        // ------------------------------------------------------------------ frame update

        public void Tick(float dt, float pixelWorld)
        {
            if (gs == null) return;
            time += dt;
            float k = 1f - Mathf.Exp(-dt * 2.5f);
            foreach (var s in gs.net.AliveStations()) s.crowdDisplay += (s.Crowding - s.crowdDisplay) * k;

            // What the camera sees (with a margin): stations and trains are only built there.
            var cam = Camera.main;
            if (cam != null)
            {
                float h = cam.orthographicSize, w = h * cam.aspect;
                Vector2 c = cam.transform.position;
                float m = Mathf.Max(1f, h * 0.15f);
                view = Rect.MinMaxRect(c.x - w - m, c.y - h - m, c.x + w + m, c.y + h + m);
            }
            else view = new Rect(-1e5f, -1e5f, 2e5f, 2e5f);

            lineTimer -= dt;
            if (gs.net.version != lastVersion || lineTimer <= 0f || selectedLine != lastSelectedLine || hoverLine != lastHoverLine)
            {
                if (gs.net.version != lastVersion) RebuildIndex();
                lastVersion = gs.net.version;
                lastSelectedLine = selectedLine;
                lastHoverLine = hoverLine;
                lineTimer = 0.25f;
                RebuildLines(pixelWorld);
            }
            RebuildPlans(pixelWorld);
            RebuildMarkers(pixelWorld);
            RebuildTrains(pixelWorld);
            RebuildSelection(pixelWorld);
            preview.b.Clear();
            previewBuilder?.Invoke(preview.b, pixelWorld);
            preview.Commit(bounds);
        }

        void RebuildIndex()
        {
            segIndex.Clear();
            foreach (var l in gs.net.AliveLines())
            {
                var map = new Dictionary<long, int>();
                int n = l.stops.Count;
                for (int k = 0; k < l.SegmentCount; k++) map[MetroNetwork.Key(l.stops[k], l.stops[(k + 1) % n])] = k;
                segIndex[l.id] = map;
            }
        }

        /// <summary>Things off the selected line fade back into the paper.</summary>
        static Color32 Dim(Color32 c, bool dim) => dim ? MapPalette.Lerp(c, MapPalette.Land, 0.62f) : c;

        void RebuildLines(float pw)
        {
            var net = gs.net;
            var lw = NetStyle.LineWidth;
            float refPx = pw * StrokeWidth.PxScale;
            casing.b.Clear();
            glow.b.Clear();
            lines.b.Clear();
            foreach (var kv in net.edges)
            {
                var e = kv.Value;
                int n = e.lines.Count;
                if (n == 0) continue;
                bool edgeDim = selectedLine >= 0 && !e.lines.Contains(selectedLine);
                // Wooden plank bridges where the track crosses water.
                if (e.waterRanges.Count > 0)
                {
                    float half = (n - 1) * 0.5f * NetStyle.LaneSpacing + 2.27f;
                    foreach (var r in e.waterRanges)
                    {
                        SubPath(e.path, e.cum, r.x - 0.06f, r.y + 0.06f, tmp);
                        if (tmp.Count < 2) continue;
                        casing.b.AddPolyline(tmp, lw, Dim(MapPalette.BridgeEdge, edgeDim), 0f, false, false, half + 0.4f);
                        casing.b.AddPolyline(tmp, lw, Dim(MapPalette.BridgeCasing, edgeDim), 0f, false, false, half);
                        casing.b.AddDashed(tmp, lw, Dim(MapPalette.BridgePlank, edgeDim), 2f * refPx, 6f * refPx, half);
                    }
                }
                foreach (int lid in e.lines)
                {
                    var l = net.GetLine(lid);
                    if (l == null) continue;
                    float lane = e.LaneUnits(lid);
                    bool dim = selectedLine >= 0 && lid != selectedLine;
                    // The selected line gets a highlighter stripe underneath; a hovered one a fainter one.
                    if (lid == selectedLine)
                        glow.b.AddPolyline(e.path, lw, MapPalette.WithAlpha(MapPalette.Highlighter, 0.9f), lane, false, true, 3.07f);
                    else if (lid == hoverLine && selectedLine < 0)
                        glow.b.AddPolyline(e.path, lw, MapPalette.WithAlpha(MapPalette.Highlighter, 0.6f), lane, false, true, 2.6f);
                    casing.b.AddPolyline(e.path, lw, Dim(MapPalette.Ink, dim), lane, false, true, NetStyle.CasingUnits);
                    bool running = l.trains.Count > 0;
                    Color32 c = running ? l.Color : MapPalette.Lerp(l.Color, MapPalette.White, 0.45f);
                    lines.b.AddPolyline(e.path, lw, Dim(c, dim), lane, false, true, 1f);
                    // Built but no trains yet: white dashes down the middle.
                    if (!running) lines.b.AddDashed(e.path, lw, Dim(MapPalette.White, dim), 9f * refPx, 7f * refPx, 0.3f, lane);
                }
            }
            casing.Commit(bounds);
            glow.Commit(bounds);
            lines.Commit(bounds);
        }

        // ------------------------------------------------------------------ stations & depots

        void RebuildMarkers(float pw)
        {
            var lw = NetStyle.LineWidth;
            var net = gs.net;
            float u = HalfWidthWorld(pw);
            float unitPx = u / Mathf.Max(pw, 1e-6f);
            // Faces and queues only once a station is big enough on screen to read them.
            bool faces = unitPx * NetStyle.StationRadius >= 9.5f;
            float pulse = 0.5f + 0.5f * Mathf.Sin(time * 5.5f);
            stations.b.Clear();
            under.b.Clear();
            depots.b.Clear();
            foreach (var s in net.AliveStations())
            {
                if (!view.Contains(s.pos)) continue;
                bool dim = selectedLine >= 0 && !s.lines.Contains(selectedLine);
                bool served = IsServed(net, s);
                float crowd = s.crowdDisplay;
                int mood = served ? MoodOf(crowd) : -1;
                Color32 moodC = MapPalette.CrowdColor(crowd);
                Color32 ink = Dim(s.lines.Count == 0 ? MapPalette.InkSoft : MapPalette.Ink, dim);
                float grow = s.id == hoverStation ? 0.22f : 0f;
                float R = StationRadiusUnits(s) + grow + (mood == 3 && !s.IsInterchange ? 0.3f : 0f);

                // Meltdown: a pulsing red halo behind the station.
                if (mood == 3)
                {
                    float hr = R + 0.6f + 0.55f * pulse;
                    under.b.AddDisc(s.pos, lw, hr + 0.35f, MapPalette.WithAlpha(MapPalette.CrowdRed, 0.18f + 0.12f * (1f - pulse)));
                    AddDashedRing(under.b, s.pos, hr, 0.26f, Dim(MapPalette.CrowdRed, dim), 12, time * 0.6f);
                }

                if (s.IsInterchange)
                {
                    // A white interchange with a mood dot (or a whole face up close).
                    stations.b.AddDisc(s.pos, lw, R, ink);
                    stations.b.AddDisc(s.pos, lw, R - 0.6f, Dim(MapPalette.White, dim));
                    if (served && faces)
                    {
                        Face(stations.b, s.pos, Mathf.Min(R * 0.55f, 1.7f), mood, u, dim);
                        Vector2 badge = new Vector2(0.7f, -0.7f) * (R - 0.35f);
                        stations.b.AddDisc(s.pos, lw, 0.62f, ink, badge);
                        stations.b.AddDisc(s.pos, lw, 0.46f, Dim(moodC, dim), badge);
                    }
                    else if (served)
                    {
                        stations.b.AddDisc(s.pos, lw, 1.07f, ink);
                        stations.b.AddDisc(s.pos, lw, 0.8f, Dim(moodC, dim));
                    }
                }
                else
                {
                    float fill = R - 0.53f;
                    if (!served && s.lines.Count > 0 && faces)
                    {
                        // No trains yet: a dashed outline and a snoozing face.
                        stations.b.AddDisc(s.pos, lw, fill + 0.05f, Dim(MapPalette.White, dim));
                        AddDashedRing(stations.b, s.pos, R - 0.27f, 0.27f, ink, 9, 0f);
                    }
                    else
                    {
                        stations.b.AddDisc(s.pos, lw, R, ink);
                        stations.b.AddDisc(s.pos, lw, fill, Dim(served ? moodC : MapPalette.White, dim));
                    }
                    if (faces && s.lines.Count > 0) Face(stations.b, s.pos, fill, mood, u, dim);
                    if (faces && !served && s.lines.Count > 0) Snooze(stations.b, s.pos, R, u, dim);
                }

                if (faces && served)
                {
                    // A little queue of riders beside the station, longer as it fills up.
                    int queue = mood == 0 ? 1 : mood == 1 ? 3 : mood == 2 ? 5 : 7;
                    Queue(stations.b, s.pos, R, queue, dim);
                    if (mood == 2) Sweat(stations.b, s.pos, R, dim);
                    if (mood == 3) Steam(stations.b, s.pos, R, dim);
                }
            }

            foreach (var d in net.AliveDepots())
            {
                var st = net.stations[d.stationId];
                if (!view.Contains(st.pos)) continue;
                bool dim = selectedLine >= 0 && !st.lines.Contains(selectedLine);
                Garage(depots.b, st, d, u, pw, dim);
            }
            stations.Commit(bounds);
            under.Commit(bounds);
            depots.Commit(bounds);
        }

        /// <summary>Dot eyes and a mouth that tells how the platform feels (in half-width units around c).</summary>
        void Face(VectorMeshBuilder b, Vector2 c, float r, int mood, float u, bool dim)
        {
            var lw = NetStyle.LineWidth;
            Color32 ink = Dim(MapPalette.Ink, dim);
            float k = r / 1.33f;
            pts.Clear();
            switch (mood)
            {
                case -1:
                    // Asleep: closed eyes and a small smile.
                    Stroke(b, c, u, ink, 0.12f * k, new Vector2(-0.73f, 0.18f) * k, new Vector2(-0.27f, 0.18f) * k);
                    Stroke(b, c, u, ink, 0.12f * k, new Vector2(0.27f, 0.18f) * k, new Vector2(0.73f, 0.18f) * k);
                    Curve(new Vector2(-0.27f, -0.55f), new Vector2(0f, -0.72f), new Vector2(0.27f, -0.55f), k);
                    break;
                case 0:
                    Eyes(b, c, k, ink);
                    Curve(new Vector2(-0.64f, -0.32f), new Vector2(0f, -0.95f), new Vector2(0.64f, -0.32f), k);
                    break;
                case 1:
                    Eyes(b, c, k, ink);
                    pts.Add(new Vector2(-0.55f, -0.5f) * k);
                    pts.Add(new Vector2(0.55f, -0.5f) * k);
                    break;
                default:
                    // Squeezed ">  <" eyes when squished, angry brows in a meltdown.
                    float lift = mood == 3 ? 0.1f : 0f;
                    Stroke(b, c, u, ink, 0.13f * k, new Vector2(-0.73f, 0.53f + lift) * k, new Vector2(-0.22f, 0.3f) * k);
                    Stroke(b, c, u, ink, 0.13f * k, new Vector2(0.73f, 0.53f + lift) * k, new Vector2(0.22f, 0.3f) * k);
                    Curve(new Vector2(-0.6f, -0.73f), new Vector2(0f, -0.25f), new Vector2(0.6f, -0.73f), k);
                    break;
            }
            for (int i = 0; i < pts.Count; i++) pts[i] = c + pts[i] * u;
            if (pts.Count >= 2) b.AddPolyline(pts, lw, ink, 0f, false, true, 0.12f * k);
        }

        static void Eyes(VectorMeshBuilder b, Vector2 c, float k, Color32 ink)
        {
            b.AddDisc(c, NetStyle.LineWidth, 0.2f * k, ink, new Vector2(-0.47f, 0.27f) * k);
            b.AddDisc(c, NetStyle.LineWidth, 0.2f * k, ink, new Vector2(0.47f, 0.27f) * k);
        }

        void Curve(Vector2 a, Vector2 ctrl, Vector2 z, float k)
        {
            for (int i = 0; i <= 6; i++)
            {
                float t = i / 6f;
                Vector2 p = (1 - t) * (1 - t) * a + 2 * (1 - t) * t * ctrl + t * t * z;
                pts.Add(p * k);
            }
        }

        static readonly List<Vector2> seg2 = new List<Vector2>(2) { Vector2.zero, Vector2.zero };

        static void Stroke(VectorMeshBuilder b, Vector2 c, float u, Color32 col, float halfUnits, Vector2 a, Vector2 z)
        {
            seg2[0] = c + a * u;
            seg2[1] = c + z * u;
            b.AddPolyline(seg2, NetStyle.LineWidth, col, 0f, false, true, halfUnits);
        }

        /// <summary>A queue of tiny riders (heads and shoulders) to the upper right of a station.</summary>
        static void Queue(VectorMeshBuilder b, Vector2 c, float R, int count, bool dim)
        {
            var lw = NetStyle.LineWidth;
            Color32 ink = Dim(MapPalette.Ink, dim);
            float x0 = R + 0.55f, y = R * 0.55f;
            for (int i = 0; i < count; i++)
            {
                Vector2 body = new Vector2(x0 + i * 0.9f, y);
                Color32 col = Dim(People[i % People.Length], dim);
                b.AddCapsule(c, Vector2.right, 0.52f, 0.42f, lw, ink, body);
                b.AddCapsule(c, Vector2.right, 0.4f, 0.3f, lw, col, body);
                b.AddDisc(c, lw, 0.34f, ink, body + new Vector2(0f, 0.78f));
            }
        }

        static void Sweat(VectorMeshBuilder b, Vector2 c, float R, bool dim)
        {
            var lw = NetStyle.LineWidth;
            Vector2 at = new Vector2(R * 0.55f, R * 1.05f);
            b.AddDisc(c, lw, 0.42f, Dim(MapPalette.Ink, dim), at);
            b.AddDisc(c, lw, 0.3f, Dim(MapPalette.Sweat, dim), at);
            b.AddCapsule(c, Vector2.up, 0.42f, 0.2f, lw, Dim(MapPalette.Ink, dim), at + new Vector2(0f, 0.32f));
            b.AddCapsule(c, Vector2.up, 0.3f, 0.09f, lw, Dim(MapPalette.Sweat, dim), at + new Vector2(0f, 0.3f));
        }

        /// <summary>White puffs of steam rising off a station in a meltdown.</summary>
        void Steam(VectorMeshBuilder b, Vector2 c, float R, bool dim)
        {
            var lw = NetStyle.LineWidth;
            for (int i = 0; i < 4; i++)
            {
                float side = i < 2 ? -1f : 1f;
                bool big = i % 2 == 0;
                float bob = Mathf.Sin(time * 3f + i * 1.7f) * 0.18f;
                Vector2 at = new Vector2(side * (big ? R * 0.95f : R * 1.5f), (big ? R * 1.55f : R * 2.2f) + bob);
                float r = big ? 0.67f : 0.47f;
                b.AddDisc(c, lw, r + 0.2f, Dim(MapPalette.Ink, dim), at);
                b.AddDisc(c, lw, r, Dim(MapPalette.White, dim), at);
            }
        }

        /// <summary>"z z z" drifting up from a station with no trains.</summary>
        void Snooze(VectorMeshBuilder b, Vector2 c, float R, float u, bool dim)
        {
            Color32 col = Dim(MapPalette.InkSoft, dim);
            for (int i = 0; i < 3; i++)
            {
                float s = 0.32f + 0.1f * i;
                Vector2 o = new Vector2(R + 0.4f + i * 0.75f, R * 0.7f + i * 0.85f + Mathf.Sin(time * 1.5f + i) * 0.1f);
                pts.Clear();
                pts.Add(c + (o + new Vector2(-s, s)) * u);
                pts.Add(c + (o + new Vector2(s, s)) * u);
                pts.Add(c + (o + new Vector2(-s, -s)) * u);
                pts.Add(c + (o + new Vector2(s, -s)) * u);
                b.AddPolyline(pts, NetStyle.LineWidth, col, 0f, false, true, 0.12f + 0.02f * i);
            }
        }

        static readonly int[] QuadTris = { 0, 1, 2, 0, 2, 3 };

        /// <summary>A depot: a little garage with an orange roof and a yellow door, on a dashed stub from its station.</summary>
        void Garage(VectorMeshBuilder b, Station st, Depot d, float u, float pw, bool dim)
        {
            var lw = NetStyle.LineWidth;
            Color32 ink = Dim(MapPalette.Ink, dim);
            Vector2 hc = DepotWorldPos(gs.net, d, pw);
            float k = 1f + 0.12f * (d.level - 1);
            // Dashed stub from the station to the garage.
            tmp.Clear();
            tmp.Add(st.pos + d.iconDir * (StationRadiusUnits(st) * u));
            tmp.Add(hc - d.iconDir * (2.1f * k * u));
            b.AddDashed(tmp, lw, ink, 0.45f * u, 0.65f * u, 0.2f);
            Vector2 P(float x, float y) => hc + new Vector2(x, y) * (k * u);
            // Body.
            pts.Clear();
            pts.Add(P(-2.0f, -1.45f));
            pts.Add(P(2.0f, -1.45f));
            pts.Add(P(2.0f, 1.2f));
            pts.Add(P(-2.0f, 1.2f));
            b.AddFill(pts, QuadTris, Dim(MapPalette.White, dim));
            b.AddPolyline(pts, lw, ink, 0f, true, false, 0.21f * k);
            // Garage door with slats.
            pts.Clear();
            pts.Add(P(-1.0f, -1.45f));
            pts.Add(P(1.0f, -1.45f));
            pts.Add(P(1.0f, 0.42f));
            pts.Add(P(-1.0f, 0.42f));
            b.AddFill(pts, QuadTris, Dim(MapPalette.DepotDoor, dim));
            b.AddPolyline(pts, lw, ink, 0f, true, false, 0.17f * k);
            Stroke(b, hc, u * k, ink, 0.12f * k, new Vector2(-1f, -0.2f), new Vector2(1f, -0.2f));
            Stroke(b, hc, u * k, ink, 0.12f * k, new Vector2(-1f, -0.82f), new Vector2(1f, -0.82f));
            // Roof: a fat orange stroke with an ink line on top.
            pts.Clear();
            pts.Add(P(-2.4f, 1.0f));
            pts.Add(P(0f, 2.75f));
            pts.Add(P(2.4f, 1.0f));
            b.AddPolyline(pts, lw, ink, 0f, false, true, 0.75f * k);
            b.AddPolyline(pts, lw, Dim(MapPalette.DepotRoof, dim), 0f, false, true, 0.55f * k);
            b.AddPolyline(pts, lw, ink, 0f, false, true, 0.17f * k);
        }

        // ------------------------------------------------------------------ trains

        void RebuildTrains(float pw)
        {
            var lw = NetStyle.LineWidth;
            var net = gs.net;
            float u = HalfWidthWorld(pw);
            bool detail = u / Mathf.Max(pw, 1e-6f) >= 3.2f;
            trains.b.Clear();
            Color32 ink = MapPalette.Ink;
            foreach (var t in gs.trains.trains)
            {
                if (!t.alive || !view.Contains(t.pos)) continue;
                var l = net.GetLine(t.lineId);
                if (l == null) continue;
                bool dim = selectedLine >= 0 && t.lineId != selectedLine;
                Vector2 off = TrainOffsetUnits(t);
                Vector2 dir = t.heading;
                float halfLen = 1.75f + 0.17f * l.wagons;
                // A white gumball bus with an ink outline...
                trains.b.AddCapsule(t.pos, dir, halfLen + 0.2f, 1.27f, lw, Dim(ink, dim), off);
                trains.b.AddCapsule(t.pos, dir, halfLen, 1.07f, lw, Dim(MapPalette.White, dim), off);
                if (!detail) continue;
                // ...with windows in the line's colour (faces at the windows when it's packed).
                bool packed = t.LoadFactor > 0.95f;
                int windows = Mathf.Clamp(l.wagons - 1, 2, 4);
                float span = (halfLen - 1.05f) * 2f;
                for (int i = 0; i < windows; i++)
                {
                    float x = -span * 0.5f + span * i / (windows - 1);
                    Vector2 wo = off + dir * x;
                    trains.b.AddCapsule(t.pos, dir, 0.52f, 0.47f, lw, Dim(ink, dim), wo);
                    trains.b.AddCapsule(t.pos, dir, 0.42f, 0.37f, lw, Dim(packed ? MapPalette.White : l.Color, dim), wo);
                    if (packed)
                    {
                        trains.b.AddDisc(t.pos, lw, 0.14f, Dim(ink, dim), wo + Geo.Perp(dir) * 0.05f - dir * 0.16f);
                        trains.b.AddDisc(t.pos, lw, 0.14f, Dim(ink, dim), wo + Geo.Perp(dir) * 0.05f + dir * 0.16f);
                    }
                }
                if (packed)
                {
                    // A speech bubble shouting "!!" above a packed train.
                    Vector2 bo = off + new Vector2(1.6f, 2.6f);
                    trains.b.AddCapsule(t.pos, Vector2.right, 1.45f, 1.05f, lw, Dim(ink, dim), bo);
                    trains.b.AddCapsule(t.pos, Vector2.right, 1.27f, 0.87f, lw, Dim(MapPalette.White, dim), bo);
                    for (int i = 0; i < 2; i++)
                    {
                        Vector2 g = bo + new Vector2(i == 0 ? -0.3f : 0.3f, 0f);
                        trains.b.AddCapsule(t.pos, Vector2.up, 0.36f, 0.13f, lw, Dim(MapPalette.CrowdRed, dim), g + new Vector2(0f, 0.18f));
                        trains.b.AddDisc(t.pos, lw, 0.13f, Dim(MapPalette.CrowdRed, dim), g + new Vector2(0f, -0.42f));
                    }
                }
            }
            trains.Commit(bounds);
        }

        // ------------------------------------------------------------------ selection

        void RebuildSelection(float pixelWorld)
        {
            var net = gs.net;
            select.b.Clear();
            var st = net.GetStation(selectedStation);
            if (st != null) Halo(select.b, st.pos, StationRadiusUnits(st) + 0.75f, Vector2.zero);
            if (hoverStation >= 0 && hoverStation != selectedStation)
            {
                var hs = net.GetStation(hoverStation);
                if (hs != null) AddRing(select.b, hs.pos, StationRadiusUnits(hs) + 0.75f, 0.32f, MapPalette.WithAlpha(MapPalette.Ink, 0.45f), Vector2.zero);
            }
            var tr = gs.trains.Get(selectedTrain);
            if (tr != null) Halo(select.b, tr.pos, 3.4f + 0.17f * (net.GetLine(tr.lineId)?.wagons ?? 3), TrainOffsetUnits(tr));
            var dep = net.GetDepot(selectedDepot);
            if (dep != null)
            {
                var ds = net.stations[dep.stationId];
                Halo(select.b, ds.pos, 3.6f, dep.iconDir * (StationRadiusUnits(ds) + 4.4f));
            }
            select.Commit(bounds);
        }

        /// <summary>A highlighter-yellow ring with ink edges around whatever is selected.</summary>
        static void Halo(VectorMeshBuilder b, Vector2 center, float radiusUnits, Vector2 offsetUnits)
        {
            AddRing(b, center, radiusUnits, 0.95f, MapPalette.Ink, offsetUnits);
            AddRing(b, center, radiusUnits, 0.62f, MapPalette.Highlighter, offsetUnits);
        }

        // ------------------------------------------------------------------ pencil plans

        void RebuildPlans(float pixelWorld)
        {
            if (gs.plans.Count == 0 && shownPlans == 0) return;
            shownPlans = gs.plans.Count;
            plans.b.Clear();
            Color32 c = planMode ? MapPalette.Pencil : MapPalette.WithAlpha(MapPalette.Pencil, 0.75f);
            for (int i = 0; i < gs.plans.Count; i++)
            {
                var p = gs.plans[i];
                if (p?.points == null || p.points.Count == 0) continue;
                AddPencil(plans.b, p.points, p.diagonalFirst, pixelWorld, i == hoverPlan ? MapPalette.Invalid : c, planMode);
            }
            plans.Commit(bounds);
        }

        static readonly List<Vector2> pencilPath = new List<Vector2>();
        static readonly List<Vector2> dashPts = new List<Vector2>();

        /// <summary>
        /// A pencil sketch: a dashed graphite track through hollow, dashed stop circles. Sizes are in pixels so
        /// a sketch reads the same at every zoom. count limits how many points are used (the Plan tool's preview).
        /// </summary>
        public static void AddPencil(VectorMeshBuilder b, IList<Vector2> pts, IList<bool> diag, float pw, Color32 c, bool bold, int count = -1)
        {
            int n = count < 0 ? pts.Count : Mathf.Min(count, pts.Count);
            if (n == 0) return;
            float refPx = pw * StrokeWidth.PxScale;
            pencilPath.Clear();
            for (int i = 1; i < n; i++)
            {
                var seg = LineGeometry.OctilinearPath(pts[i - 1], pts[i], diag != null && i < diag.Count && diag[i]);
                for (int k = pencilPath.Count == 0 ? 0 : 1; k < seg.Count; k++) pencilPath.Add(seg[k]);
            }
            if (pencilPath.Count >= 2) b.AddDashed(pencilPath, StrokeWidth.Pixels(bold ? 2.4f : 2f), c, 11f * refPx, 7f * refPx);
            var ring = StrokeWidth.Pixels(1.5f);
            float rad = (bold ? 8f : 7f) * refPx;
            for (int i = 0; i < n; i++)
            {
                b.AddDisc(pts[i], StrokeWidth.Pixels(bold ? 8f : 7f), 1f, MapPalette.WithAlpha(MapPalette.Cream, c.a / 255f));
                for (int d = 0; d < 8; d++)
                {
                    dashPts.Clear();
                    for (int k = 0; k <= 3; k++)
                    {
                        float a = (d * 45f + k * 9f) * Mathf.Deg2Rad;
                        dashPts.Add(pts[i] + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rad);
                    }
                    b.AddPolyline(dashPts, ring, c, 0f, false, true);
                }
            }
        }

        // ------------------------------------------------------------------ shared primitives

        static readonly List<Vector2> ringPts = new List<Vector2>();

        /// <summary>A ring whose radius is in line half-width units (scales with the network).</summary>
        public static void AddRing(VectorMeshBuilder b, Vector2 center, float radiusUnits, float thickUnits, Color32 c, Vector2 offsetUnits)
        {
            const int seg = 40;
            var lw = NetStyle.LineWidth;
            for (int i = 0; i < seg; i++)
            {
                float a0 = i / (float)seg * Mathf.PI * 2f, a1 = (i + 1) / (float)seg * Mathf.PI * 2f;
                Vector2 p0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * radiusUnits + offsetUnits;
                Vector2 p1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * radiusUnits + offsetUnits;
                Vector2 mid = (p0 + p1) * 0.5f;
                b.AddCapsule(center, p1 - p0, (p1 - p0).magnitude * 0.5f + thickUnits * 0.5f, thickUnits, lw, c, mid);
            }
        }

        /// <summary>A dashed ring in line half-width units; phase (radians) turns the dashes.</summary>
        public static void AddDashedRing(VectorMeshBuilder b, Vector2 center, float radiusUnits, float thickUnits, Color32 c, int dashes, float phase)
        {
            var lw = NetStyle.LineWidth;
            float step = Mathf.PI * 2f / dashes;
            for (int i = 0; i < dashes; i++)
            {
                float a0 = phase + i * step, a1 = a0 + step * 0.55f;
                const int sub = 3;
                for (int k = 0; k < sub; k++)
                {
                    float s0 = Mathf.Lerp(a0, a1, k / (float)sub), s1 = Mathf.Lerp(a0, a1, (k + 1) / (float)sub);
                    Vector2 p0 = new Vector2(Mathf.Cos(s0), Mathf.Sin(s0)) * radiusUnits;
                    Vector2 p1 = new Vector2(Mathf.Cos(s1), Mathf.Sin(s1)) * radiusUnits;
                    b.AddCapsule(center, p1 - p0, (p1 - p0).magnitude * 0.5f + thickUnits * 0.5f, thickUnits, lw, c, (p0 + p1) * 0.5f);
                }
            }
        }

        /// <summary>A world-space circle outline (catchment radius) with a pixel-constant stroke.</summary>
        public static void AddWorldCircle(VectorMeshBuilder b, Vector2 center, float radius, float halfPx, Color32 c, bool dashed = false)
        {
            const int seg = 64;
            ringPts.Clear();
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                ringPts.Add(center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius);
            }
            if (dashed) b.AddDashed(ringPts, StrokeWidth.Pixels(halfPx), c, radius * 0.12f, radius * 0.08f);
            else b.AddPolyline(ringPts, StrokeWidth.Pixels(halfPx), c, 0f, false, false);
        }

        static int[] discTris;

        /// <summary>A filled world-space circle (the catchment area under a planned stop).</summary>
        public static void AddWorldDisc(VectorMeshBuilder b, Vector2 center, float radius, Color32 c)
        {
            const int seg = 48;
            ringPts.Clear();
            ringPts.Add(center);
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                ringPts.Add(center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius);
            }
            if (discTris == null)
            {
                discTris = new int[seg * 3];
                for (int i = 0; i < seg; i++)
                {
                    discTris[i * 3] = 0;
                    discTris[i * 3 + 1] = i + 1;
                    discTris[i * 3 + 2] = i + 2;
                }
            }
            b.AddFill(ringPts, discTris, c);
        }
    }
}
