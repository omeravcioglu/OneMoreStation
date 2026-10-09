using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Metro.UI
{
    /// <summary>
    /// Station and district names drawn as UI labels over the map.
    /// Placement runs on an occupancy grid that already contains the track and the station
    /// markers, so names sit beside the lines instead of on top of them. Placement is
    /// recomputed when the view changes; positions are updated every frame.
    /// </summary>
    public sealed class LabelLayer
    {
        readonly GameRoot g;
        readonly VisualElement layer;
        readonly Dictionary<int, Label> stationLabels = new Dictionary<int, Label>();
        readonly Dictionary<int, int> stationSide = new Dictionary<int, int>();
        readonly List<Label> districtLabels = new List<Label>();
        readonly bool[] districtShown = new bool[256];
        readonly List<Station> cands = new List<Station>();
        readonly Dictionary<int, LandmarkView> landmarkViews = new Dictionary<int, LandmarkView>();
        readonly bool[] districtNext = new bool[256];
        readonly List<(VisualElement note, Label title, Label sub)> planNotes = new List<(VisualElement, Label, Label)>();
        readonly List<string> planSubs = new List<string>();
        readonly List<(VisualElement flag, Label pop)> accessFlags = new List<(VisualElement, Label)>();
        readonly List<Label> riverLabels = new List<Label>();
        readonly List<int> staleLandmarks = new List<int>();
        GameState bound;

        sealed class LandmarkView
        {
            public VisualElement ring, pin;
            public IconElement icon;
            public Label label;
        }
        public bool enabled = true;

        // occupancy grid in panel units
        const float Cell = 6f;
        bool[] occ = new bool[0];
        int ogw, ogh;

        float lastSize = -1f;
        Vector2 lastPos;
        int lastVersion = -1, lastSel = -2;
        float timer;

        public LabelLayer(GameRoot g, VisualElement layer)
        {
            this.g = g;
            this.layer = layer;
        }

        void Bind(GameState gs)
        {
            bound = gs;
            foreach (var l in stationLabels.Values) l.RemoveFromHierarchy();
            stationLabels.Clear();
            stationSide.Clear();
            foreach (var l in districtLabels) l.RemoveFromHierarchy();
            districtLabels.Clear();
            foreach (var v in landmarkViews.Values)
            {
                v.ring.RemoveFromHierarchy();
                v.pin.RemoveFromHierarchy();
            }
            landmarkViews.Clear();
            foreach (var n in planNotes) n.note.RemoveFromHierarchy();
            planNotes.Clear();
            planSubs.Clear();
            foreach (var f in accessFlags) f.flag.RemoveFromHierarchy();
            accessFlags.Clear();
            foreach (var r in riverLabels) r.RemoveFromHierarchy();
            riverLabels.Clear();
            lastSize = -1f;
            if (gs == null) return;
            foreach (var d in gs.city.districts)
            {
                var l = new Label(d.name.ToUpperInvariant());
                l.AddToClassList("district-label");
                UIK.Display(l);
                l.pickingMode = PickingMode.Ignore;
                l.visible = false;
                layer.Add(l);
                districtLabels.Add(l);
                // Big districts with no metro wave a little City Hall flag: "48K · trains pls!"
                var flag = new VisualElement();
                flag.AddToClassList("access-flag");
                flag.pickingMode = PickingMode.Ignore;
                flag.Add(new IconElement(IconKind.Hall, 18f));
                var pop = UIK.Text(flag, "", "access-pop");
                UIK.Display(pop);
                UIK.Text(flag, "trains pls!", "access-text");
                foreach (var c in flag.Children()) c.pickingMode = PickingMode.Ignore;
                flag.visible = false;
                layer.Add(flag);
                accessFlags.Add((flag, pop));
            }
            foreach (var r in gs.city.water.rivers)
            {
                var l = new Label(string.IsNullOrEmpty(r.name) ? "" : r.name);
                l.AddToClassList("river-label");
                l.pickingMode = PickingMode.Ignore;
                l.visible = false;
                layer.Insert(0, l);
                riverLabels.Add(l);
            }
        }

        // ------------------------------------------------------------------ occupancy grid

        void ClearGrid(float w, float h)
        {
            int nw = Mathf.CeilToInt(w / Cell) + 2, nh = Mathf.CeilToInt(h / Cell) + 2;
            if (nw * nh != occ.Length)
            {
                occ = new bool[nw * nh];
            }
            else System.Array.Clear(occ, 0, occ.Length);
            ogw = nw;
            ogh = nh;
        }

        void MarkRect(Rect r)
        {
            int x0 = Mathf.Max(0, Mathf.FloorToInt(r.xMin / Cell)), x1 = Mathf.Min(ogw - 1, Mathf.FloorToInt(r.xMax / Cell));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(r.yMin / Cell)), y1 = Mathf.Min(ogh - 1, Mathf.FloorToInt(r.yMax / Cell));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++) occ[y * ogw + x] = true;
        }

        bool Free(Rect r)
        {
            if (r.xMin < 0f || r.yMin < 0f || r.xMax > (ogw - 2) * Cell || r.yMax > (ogh - 2) * Cell) return false;
            // Test a slightly smaller rect so grid quantisation doesn't make a label collide with its own station.
            r = Rect.MinMaxRect(r.xMin + Cell * 0.5f, r.yMin + Cell * 0.5f, r.xMax - Cell * 0.5f, r.yMax - Cell * 0.5f);
            int x0 = Mathf.Max(0, Mathf.FloorToInt(r.xMin / Cell)), x1 = Mathf.Min(ogw - 1, Mathf.FloorToInt(r.xMax / Cell));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(r.yMin / Cell)), y1 = Mathf.Min(ogh - 1, Mathf.FloorToInt(r.yMax / Cell));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    if (occ[y * ogw + x]) return false;
            return true;
        }

        void MarkSegment(Vector2 a, Vector2 b, float radius)
        {
            float len = Vector2.Distance(a, b);
            int steps = Mathf.Max(1, Mathf.CeilToInt(len / (Cell * 0.75f)));
            for (int i = 0; i <= steps; i++)
            {
                Vector2 p = Vector2.Lerp(a, b, i / (float)steps);
                MarkRect(new Rect(p.x - radius, p.y - radius, radius * 2f, radius * 2f));
            }
        }

        // ------------------------------------------------------------------ update

        static float Width(Label l, float perChar, int chars)
        {
            float w = l.resolvedStyle.width;
            if (float.IsNaN(w) || w <= 1f) w = chars * perChar + 4f;
            return w;
        }

        Rect SideRect(int side, Vector2 p, float r, float w, float h)
        {
            switch (side)
            {
                case 0: return new Rect(p.x + r, p.y - h * 0.5f, w, h);
                case 1: return new Rect(p.x - r - w, p.y - h * 0.5f, w, h);
                case 2: return new Rect(p.x - w * 0.5f, p.y - r - h, w, h);
                case 3: return new Rect(p.x - w * 0.5f, p.y + r, w, h);
                case 4: return new Rect(p.x + r * 0.75f, p.y - r * 0.75f - h, w, h);
                case 5: return new Rect(p.x + r * 0.75f, p.y + r * 0.75f, w, h);
                case 6: return new Rect(p.x - r * 0.75f - w, p.y - r * 0.75f - h, w, h);
                default: return new Rect(p.x - r * 0.75f - w, p.y + r * 0.75f, w, h);
            }
        }

        public void Update()
        {
            var gs = g.State;
            if (gs != bound) Bind(gs);
            if (gs == null) return;
            var ui = UIRoot.I;
            float rw = ui.Root.layout.width, rh = ui.Root.layout.height;
            if (float.IsNaN(rw) || rw < 10f) return;
            bool show = enabled && g.Settings.labels && !g.ScreenshotHideLabels && g.UI.HudVisible;

            float size = g.CamCtl.Size;
            Vector2 pos = g.CamCtl.Position;
            int sel = g.Sel.kind == SelKind.Station || g.Sel.kind == SelKind.Line ? (int)g.Sel.kind * 100000 + g.Sel.id : -1;
            timer -= Time.unscaledDeltaTime;
            float pw = g.CamCtl.PixelWorldSize;
            float scale = ui.PanelScale;
            float panPx = (pos - lastPos).magnitude / Mathf.Max(pw, 1e-6f) * scale;
            bool replace = timer <= 0f || Mathf.Abs(size - lastSize) > lastSize * 0.015f || panPx > 140f
                           || gs.net.version != lastVersion || sel != lastSel;
            if (replace && show)
            {
                timer = 0.3f;
                lastSize = size;
                lastPos = pos;
                lastVersion = gs.net.version;
                lastSel = sel;
                Place(gs, rw, rh, pw, scale);
            }
            Position(gs, show, pw, scale);
            PositionLandmarks(gs, show, pw, scale);
            PositionPlans(gs, show, replace);
            PositionRivers(gs, show, pw);
        }

        // ------------------------------------------------------------------ pencil plans, rivers

        /// <summary>A sticky note at the end of each pencil plan: "Harbour Express? · 4 stops · about $14M · someday".</summary>
        void PositionPlans(GameState gs, bool show, bool refresh)
        {
            var ui = UIRoot.I;
            var cam = g.CamCtl.cam;
            while (planNotes.Count < gs.plans.Count)
            {
                var note = new VisualElement();
                note.AddToClassList("plan-note");
                note.pickingMode = PickingMode.Ignore;
                var t = UIK.Text(note, "", "plan-note-title");
                var sub = UIK.Text(note, "", "plan-note-sub");
                t.pickingMode = sub.pickingMode = PickingMode.Ignore;
                note.style.rotate = new Rotate(planNotes.Count % 2 == 0 ? 3f : -2.5f);
                layer.Add(note);
                planNotes.Add((note, t, sub));
                planSubs.Add(null);
            }
            for (int i = 0; i < planNotes.Count; i++)
            {
                var (note, title, sub) = planNotes[i];
                var plan = i < gs.plans.Count ? gs.plans[i] : null;
                if (!show || plan?.points == null || plan.points.Count < 2)
                {
                    note.visible = false;
                    continue;
                }
                if (refresh || planSubs[i] == null)
                {
                    int stops = plan.points.Count;
                    planSubs[i] = $"{stops} stops · about {Fmt.Money(PlanTool.Estimate(gs, plan.points, plan.diagonalFirst))} · someday";
                }
                if (title.text != plan.name) title.text = plan.name;
                if (sub.text != planSubs[i]) sub.text = planSubs[i];
                Vector2 p = ui.WorldToPanel(cam, plan.points[plan.points.Count - 1]);
                note.style.translate = new Translate(p.x + 14f, p.y - 54f);
                note.visible = true;
            }
        }

        /// <summary>Each river's name, written along the water near the middle of the view.</summary>
        void PositionRivers(GameState gs, bool show, float pw)
        {
            var ui = UIRoot.I;
            var cam = g.CamCtl.cam;
            Vector2 center = g.CamCtl.Position;
            float pxPerKm = 1f / Mathf.Max(pw, 1e-6f);
            float halfW = cam.orthographicSize * cam.aspect * 0.8f, halfH = cam.orthographicSize * 0.8f;
            for (int i = 0; i < riverLabels.Count && i < gs.city.water.rivers.Count; i++)
            {
                var lab = riverLabels[i];
                var r = gs.city.water.rivers[i];
                if (!show || string.IsNullOrEmpty(lab.text) || pxPerKm < 9f || r.points.Count < 3)
                {
                    lab.visible = false;
                    continue;
                }
                int best = -1;
                float bestD = float.MaxValue;
                for (int k = 1; k < r.points.Count - 1; k++)
                {
                    Vector2 q = r.points[k];
                    if (Mathf.Abs(q.x - center.x) > halfW || Mathf.Abs(q.y - center.y) > halfH) continue;
                    if (!gs.city.InPlayArea(q, 0.3f)) continue;
                    float d = (q - center).sqrMagnitude;
                    if (d < bestD)
                    {
                        bestD = d;
                        best = k;
                    }
                }
                if (best < 0)
                {
                    lab.visible = false;
                    continue;
                }
                Vector2 a = ui.WorldToPanel(cam, r.points[best - 1]), b = ui.WorldToPanel(cam, r.points[best + 1]);
                float ang = Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg;
                if (ang > 90f) ang -= 180f;
                if (ang < -90f) ang += 180f;
                Vector2 p = ui.WorldToPanel(cam, r.points[best]);
                float w = Width(lab, 8.5f, lab.text.Length);
                lab.style.translate = new Translate(p.x - w * 0.5f, p.y - 9f);
                lab.style.rotate = new Rotate(ang);
                lab.visible = true;
            }
        }

        // ------------------------------------------------------------------ City Hall landmarks

        /// <summary>
        /// Pins for the places City Hall asks about (a new hospital, the stadium...). Open requests also show
        /// the walking-distance circle a station has to be in.
        /// </summary>
        void PositionLandmarks(GameState gs, bool show, float pw, float scale)
        {
            var gov = gs.gov;
            var ui = UIRoot.I;
            var cam = g.CamCtl.cam;
            staleLandmarks.Clear();
            foreach (var id in landmarkViews.Keys)
                if (gov.LandmarkById(id) == null) staleLandmarks.Add(id);
            foreach (var id in staleLandmarks)
            {
                landmarkViews[id].ring.RemoveFromHierarchy();
                landmarkViews[id].pin.RemoveFromHierarchy();
                landmarkViews.Remove(id);
            }
            float pxPerKm = 1f / Mathf.Max(pw, 1e-6f) * scale;
            foreach (var lm in gov.landmarks)
            {
                if (!landmarkViews.TryGetValue(lm.id, out var v))
                {
                    v = new LandmarkView();
                    v.ring = new VisualElement();
                    v.ring.AddToClassList("landmark-ring");
                    v.ring.pickingMode = PickingMode.Ignore;
                    layer.Insert(0, v.ring);
                    v.pin = new VisualElement();
                    v.pin.AddToClassList("landmark-pin");
                    v.pin.pickingMode = PickingMode.Ignore;
                    v.icon = new IconElement(IconKind.Pin, 28f);
                    v.pin.Add(v.icon);
                    v.label = new Label(char.ToUpperInvariant(lm.name[0]) + lm.name.Substring(1));
                    v.label.AddToClassList("landmark-label");
                    v.label.pickingMode = PickingMode.Ignore;
                    v.pin.Add(v.label);
                    layer.Add(v.pin);
                    landmarkViews[lm.id] = v;
                }
                bool wanted = false, isEvent = false;
                foreach (var r in gov.open)
                    if (r.landmarkId == lm.id)
                    {
                        wanted = true;
                        isEvent |= r.kind == RequestKind.Event;
                    }
                bool visible = show && (wanted || lm.built) && pxPerKm > 6f;
                v.pin.visible = visible;
                v.ring.visible = visible && wanted;
                if (!visible) continue;
                Vector2 p = ui.WorldToPanel(cam, lm.pos);
                Color c = wanted ? (isEvent ? Doodle.Tang : RequestView.HallColor) : Doodle.Ink3;
                v.icon.Color = c;
                v.pin.style.translate = new Translate(p.x - 14f, p.y - 27f);
                v.pin.style.opacity = wanted ? 1f : 0.8f;
                if (wanted)
                {
                    float rad = Balance.WalkRadius * pxPerKm;
                    v.ring.style.width = v.ring.style.height = rad * 2f;
                    v.ring.style.borderTopLeftRadius = v.ring.style.borderTopRightRadius = v.ring.style.borderBottomLeftRadius = v.ring.style.borderBottomRightRadius = rad;
                    v.ring.style.translate = new Translate(p.x - rad, p.y - rad);
                    v.ring.style.borderTopColor = v.ring.style.borderBottomColor = v.ring.style.borderLeftColor = v.ring.style.borderRightColor = new Color(c.r, c.g, c.b, 0.75f);
                    v.ring.style.backgroundColor = new Color(c.r, c.g, c.b, 0.08f);
                }
            }
        }

        void Place(GameState gs, float rw, float rh, float pw, float scale)
        {
            var ui = UIRoot.I;
            var cam = g.CamCtl.cam;
            float pxPerKm = 1f / Mathf.Max(pw, 1e-6f);
            float hwPx = NetStyle.LineWidth.Evaluate(pw) / pw * scale;
            ClearGrid(rw, rh);

            // Track and station markers occupy space first.
            foreach (var e in gs.net.edges.Values)
            {
                float radius = ((e.lines.Count - 1) * 0.5f * NetStyle.LaneSpacing + 1.0f) * hwPx;
                Vector2 prev = ui.WorldToPanel(cam, e.path[0]);
                for (int i = 1; i < e.path.Count; i++)
                {
                    Vector2 cur = ui.WorldToPanel(cam, e.path[i]);
                    float minX = Mathf.Min(prev.x, cur.x), maxX = Mathf.Max(prev.x, cur.x);
                    float minY = Mathf.Min(prev.y, cur.y), maxY = Mathf.Max(prev.y, cur.y);
                    if (maxX >= -20f && minX <= rw + 20f && maxY >= -20f && minY <= rh + 20f) MarkSegment(prev, cur, radius);
                    prev = cur;
                }
            }
            cands.Clear();
            foreach (var s in gs.net.AliveStations())
            {
                Vector2 p = ui.WorldToPanel(cam, s.pos);
                float r = (NetworkRenderer.StationRadiusUnits(s) + 0.6f) * hwPx;
                if (p.x < -60f || p.y < -60f || p.x > rw + 60f || p.y > rh + 60f) continue;
                MarkRect(new Rect(p.x - r, p.y - r, r * 2f, r * 2f));
                if (s.lines.Count > 0 || (g.Sel.kind == SelKind.Station && g.Sel.id == s.id)) cands.Add(s);
            }
            foreach (var d in gs.net.AliveDepots())
            {
                Vector2 p = ui.WorldToPanel(cam, NetworkRenderer.DepotWorldPos(gs.net, d, pw));
                float r = 2.6f * hwPx;
                MarkRect(new Rect(p.x - r, p.y - r, r * 2f, r * 2f));
            }

            int selId = g.Sel.kind == SelKind.Station ? g.Sel.id : -1;
            int lineSel = g.Sel.kind == SelKind.Line ? g.Sel.id : -1;
            cands.Sort((a, b) => Score(b, selId, lineSel).CompareTo(Score(a, selId, lineSel)));
            int maxLabels = pxPerKm > 60f ? 600 : pxPerKm > 34f ? 90 : pxPerKm > 20f ? 36 : 14;
            stationSide.Clear();
            int count = 0;
            foreach (var s in cands)
            {
                if (count >= maxLabels) break;
                if (!stationLabels.TryGetValue(s.id, out var lab))
                {
                    lab = new Label(s.name);
                    lab.AddToClassList("station-label");
                    lab.pickingMode = PickingMode.Ignore;
                    lab.visible = false;
                    layer.Add(lab);
                    stationLabels[s.id] = lab;
                }
                if (lab.text != s.name) lab.text = s.name;
                lab.EnableInClassList("station-label-major", s.IsInterchange);
                Vector2 p = ui.WorldToPanel(cam, s.pos);
                float w = Width(lab, s.IsInterchange ? 8.6f : 7.6f, s.name.Length) + 4f;
                float h = s.IsInterchange ? 19f : 17f;
                float r = (NetworkRenderer.StationRadiusUnits(s) + 0.9f) * hwPx + 2f;
                int side = -1;
                for (int k = 0; k < 8; k++)
                {
                    if (Free(SideRect(k, p, r, w, h)))
                    {
                        side = k;
                        break;
                    }
                }
                if (side < 0 && s.id == selId) side = 0;
                if (side < 0) continue;
                MarkRect(SideRect(side, p, r, w, h));
                stationSide[s.id] = side;
                count++;
            }

            // District names last, only where free. Districts still in the clouds get a pencilled "NAME?".
            for (int i = 0; i < districtLabels.Count && i < districtShown.Length; i++)
            {
                districtShown[i] = false;
                var d = gs.city.districts[i];
                bool next = !d.revealed;
                if (pxPerKm < 7f || (!next && !gs.city.InPlayArea(d.labelPos, 0.5f))) continue;
                var lab = districtLabels[i];
                if (districtNext[i] != next || lab.text.Length == 0)
                {
                    districtNext[i] = next;
                    lab.text = d.name.ToUpperInvariant() + (next ? "?" : "");
                    lab.EnableInClassList("district-label-next", next);
                }
                Vector2 p = ui.WorldToPanel(cam, d.labelPos);
                float w = Width(lab, 12.5f, d.name.Length);
                var rc = new Rect(p.x - w * 0.5f, p.y - 10f, w, 20f);
                if (!Free(rc)) continue;
                MarkRect(rc);
                districtShown[i] = true;
            }
        }

        void Position(GameState gs, bool show, float pw, float scale)
        {
            var ui = UIRoot.I;
            var cam = g.CamCtl.cam;
            float hwPx = NetStyle.LineWidth.Evaluate(pw) / pw * scale;
            int lineSel = g.Sel.kind == SelKind.Line ? g.Sel.id : -1;
            foreach (var kv in stationLabels)
            {
                var lab = kv.Value;
                if (!show || !stationSide.TryGetValue(kv.Key, out int side))
                {
                    lab.visible = false;
                    continue;
                }
                var s = gs.net.GetStation(kv.Key);
                if (s == null)
                {
                    lab.visible = false;
                    continue;
                }
                Vector2 p = ui.WorldToPanel(cam, s.pos);
                float w = Width(lab, s.IsInterchange ? 8.6f : 7.6f, s.name.Length) + 4f;
                float h = s.IsInterchange ? 19f : 17f;
                float r = (NetworkRenderer.StationRadiusUnits(s) + 0.9f) * hwPx + 2f;
                var rc = SideRect(side, p, r, w, h);
                lab.style.translate = new Translate(rc.x + 2f, rc.y);
                lab.style.opacity = lineSel >= 0 && !s.lines.Contains(lineSel) ? 0.35f : 1f;
                lab.visible = true;
            }
            float pxPerKm = 1f / Mathf.Max(pw, 1e-6f);
            float alpha = Mathf.Clamp01((80f - pxPerKm) / 50f) * 0.5f + 0.5f;
            for (int i = 0; i < districtLabels.Count; i++)
            {
                var lab = districtLabels[i];
                var d = gs.city.districts[i];
                if (!show || i >= districtShown.Length || !districtShown[i])
                {
                    lab.visible = false;
                    continue;
                }
                Vector2 p = ui.WorldToPanel(cam, d.labelPos);
                float w = Width(lab, 12.5f, d.name.Length);
                lab.style.translate = new Translate(p.x - w * 0.5f, p.y - 10f);
                lab.style.opacity = d.revealed ? alpha * Mathf.Clamp01(d.revealAnim) : 0.85f;
                lab.visible = true;
            }
            // "trains pls!" flags over the three biggest districts nobody serves yet (once there is a metro at all).
            float third = float.MaxValue;
            if (gs.net.LineCount > 0)
            {
                float a1 = 0f, a2 = 0f, a3 = 0f;
                foreach (var d in gs.city.districts)
                {
                    if (!d.revealed || d.population < 25000f || d.Coverage >= 0.12f) continue;
                    float v = d.population;
                    if (v > a1) { a3 = a2; a2 = a1; a1 = v; }
                    else if (v > a2) { a3 = a2; a2 = v; }
                    else if (v > a3) a3 = v;
                }
                third = Mathf.Max(25000f, a3);
            }
            for (int i = 0; i < accessFlags.Count && i < gs.city.districts.Count; i++)
            {
                var (flag, pop) = accessFlags[i];
                var d = gs.city.districts[i];
                bool asking = show && i < districtShown.Length && districtShown[i] && d.revealed && d.revealAnim >= 1f
                              && d.population >= third && d.Coverage < 0.12f;
                flag.visible = asking;
                if (!asking) continue;
                string ptxt = Fmt.Count(d.population).ToUpperInvariant();
                if (pop.text != ptxt) pop.text = ptxt;
                Vector2 p = ui.WorldToPanel(cam, d.labelPos);
                flag.style.translate = new Translate(p.x - 52f, p.y - 54f);
            }
        }

        static float Score(Station s, int selId, int lineSel)
        {
            float score = s.entriesToday + s.entriesYesterday * 0.5f + s.lines.Count * 50000f;
            if (s.id == selId) score += 1e9f;
            if (lineSel >= 0 && s.lines.Contains(lineSel)) score += 1e7f;
            return score;
        }
    }
}
