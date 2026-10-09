using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Metro.UI
{
    /// <summary>
    /// "The whole city" in a pocket: a little map of the region (open districts in sand and town colours,
    /// the rest still in the clouds), the metro lines, pencil plans, the town limit and a dashed yellow box
    /// for the current view. Click or drag on it to fly there; + and − zoom. With the pencil out, it turns
    /// into the planning map and the sketches are drawn boldly, even out in the clouds.
    /// </summary>
    public sealed class Minimap
    {
        readonly GameRoot g;
        public readonly VisualElement root;
        readonly VisualElement map, box;
        readonly Label title, chip, here;
        readonly MapLines lines;
        Texture2D tex;
        Color32[] px;
        GameState bound;
        float texTimer;
        int shownVersion = -1, shownPlans = -1, shownRevealed = -1;
        bool shownPlanMode;
        bool dragging;
        Rect shownBox;

        const float MapWidth = 232f;

        public Minimap(GameRoot g, VisualElement parent)
        {
            this.g = g;
            root = UIK.Div(parent, "panel", "minimap");
            root.name = "minimap";
            var head = UIK.Row(root, "minimap-head");
            title = UIK.Text(head, "The whole city", "minimap-title");
            UIK.Spacer(head);
            chip = UIK.Text(head, "", "minimap-chip");
            map = UIK.Div(root, "minimap-map");
            lines = new MapLines(this);
            lines.style.position = Position.Absolute;
            lines.style.left = 0f;
            lines.style.top = 0f;
            lines.style.right = 0f;
            lines.style.bottom = 0f;
            lines.pickingMode = PickingMode.Ignore;
            map.Add(lines);
            box = new DashedBox();
            box.style.position = Position.Absolute;
            box.pickingMode = PickingMode.Ignore;
            map.Add(box);
            here = UIK.Text(map, "you're here", "minimap-here");
            here.pickingMode = PickingMode.Ignore;
            var zoom = UIK.Row(map, "minimap-zoom");
            var minus = UIK.IconButton(zoom, IconKind.Minus, () => Zoom(1.4f), "Zoom out", "icon-btn", 12f);
            minus.AddToClassList("minimap-zoom-btn");
            var plus = UIK.IconButton(zoom, IconKind.Plus, () => Zoom(1f / 1.4f), "Zoom in", "icon-btn", 12f);
            plus.AddToClassList("minimap-zoom-btn");

            map.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                dragging = true;
                map.CapturePointer(e.pointerId);
                FlyTo(e.localPosition);
                e.StopPropagation();
            });
            map.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (dragging) FlyTo(e.localPosition);
            });
            map.RegisterCallback<PointerUpEvent>(e =>
            {
                if (!dragging) return;
                dragging = false;
                map.ReleasePointer(e.pointerId);
            });
            UIK.Tooltip(map, "The whole city\nClick or drag to fly there. The yellow box is your view.");
        }

        GameState S => g.State;

        Vector2 ToWorld(Vector2 local)
        {
            var city = S.city;
            float w = map.contentRect.width, h = map.contentRect.height;
            return new Vector2(local.x / Mathf.Max(1f, w) * city.width, (1f - local.y / Mathf.Max(1f, h)) * city.height);
        }

        Vector2 ToLocal(Vector2 world)
        {
            var city = S.city;
            float w = map.contentRect.width, h = map.contentRect.height;
            return new Vector2(world.x / city.width * w, (1f - world.y / city.height) * h);
        }

        void FlyTo(Vector2 local)
        {
            if (S == null) return;
            g.CamCtl.FocusOn(ToWorld(local));
        }

        void Zoom(float k)
        {
            if (S == null) return;
            g.CamCtl.FocusOn(g.CamCtl.Position, g.CamCtl.Size * k);
        }

        void Bind(GameState gs)
        {
            bound = gs;
            if (tex != null) Object.Destroy(tex);
            tex = null;
            shownVersion = shownPlans = shownRevealed = -1;
            if (gs == null) return;
            var city = gs.city;
            tex = new Texture2D(city.gw, city.gh, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            px = new Color32[city.gw * city.gh];
            map.style.backgroundImage = new StyleBackground(tex);
            map.style.height = MapWidth * city.height / Mathf.Max(1f, city.width);
            texTimer = 0f;
        }

        /// <summary>Repaints the little map: water, sand, towns by density, the haze past the limit, clouds.</summary>
        void PaintTexture()
        {
            var city = S.city;
            Color32 cloud = new Color32(255, 255, 255, 255), water = MapPalette.Water, land = MapPalette.Land;
            Color32 haze = MapPalette.Lerp(MapPalette.Land, MapPalette.Uncharted, 0.55f);
            Color32 town = Balance.Hex("#F1CDB0"), downtown = Balance.Hex("#E2DEF2");
            float area = city.cellSize * city.cellSize;
            int revealed = 0;
            for (int y = 0; y < city.gh; y++)
            {
                for (int x = 0; x < city.gw; x++)
                {
                    int i = city.CellIndex(x, y);
                    Color32 c;
                    if (city.IsCellWater(i)) c = water;
                    else if (!city.IsCellRevealed(i)) c = cloud;
                    else
                    {
                        float v = Mathf.Clamp01((city.res[i] + 0.6f * city.jobs[i]) / (area * 16000f));
                        int di = city.cellDistrict[i];
                        bool dt = di >= 0 && (city.districts[di].type == DistrictType.Central || city.districts[di].type == DistrictType.BusinessPark);
                        c = MapPalette.Lerp(land, dt ? downtown : town, Mathf.Sqrt(v) * 0.9f);
                        if (!city.CellInArea(i)) c = MapPalette.Lerp(c, haze, 0.6f);
                    }
                    px[i] = c;
                }
            }
            foreach (var d in city.districts)
                if (d.revealed) revealed++;
            shownRevealed = revealed;
            tex.SetPixels32(px);
            tex.Apply(false);
        }

        public void Refresh()
        {
            var gs = S;
            if (gs != bound) Bind(gs);
            if (gs == null) return;
            UIK.Show(root, !g.HideHudForTutorial);
            var city = gs.city;

            texTimer -= Time.unscaledDeltaTime;
            int revealed = 0;
            foreach (var d in city.districts)
                if (d.revealed) revealed++;
            if (texTimer <= 0f || revealed != shownRevealed)
            {
                texTimer = 3f;
                PaintTexture();
            }

            bool planMode = g.Tools.Active == ToolKind.Plan;
            if (planMode != shownPlanMode)
            {
                shownPlanMode = planMode;
                root.EnableInClassList("minimap-planning", planMode);
                title.text = planMode ? "Planning" : "The whole city";
                lines.MarkDirtyRepaint();
            }
            string chipText = planMode ? $"{gs.plans.Count} pencil plan{(gs.plans.Count == 1 ? "" : "s")}" : $"{revealed} / {city.districts.Count} districts";
            if (chip.text != chipText) chip.text = chipText;
            if (gs.net.version != shownVersion || gs.plans.Count != shownPlans)
            {
                shownVersion = gs.net.version;
                shownPlans = gs.plans.Count;
                lines.MarkDirtyRepaint();
            }

            // The camera's view as a dashed yellow box.
            var cam = g.CamCtl.cam;
            float hh = cam.orthographicSize, hw = hh * cam.aspect;
            Vector2 c = g.CamCtl.Position;
            Vector2 a = ToLocal(new Vector2(c.x - hw, c.y + hh)), b = ToLocal(new Vector2(c.x + hw, c.y - hh));
            var r = Rect.MinMaxRect(a.x, a.y, b.x, b.y);
            if (Mathf.Abs(r.x - shownBox.x) > 0.2f || Mathf.Abs(r.y - shownBox.y) > 0.2f || Mathf.Abs(r.width - shownBox.width) > 0.2f || Mathf.Abs(r.height - shownBox.height) > 0.2f)
            {
                bool resized = Mathf.Abs(r.width - shownBox.width) > 0.2f || Mathf.Abs(r.height - shownBox.height) > 0.2f;
                shownBox = r;
                box.style.left = r.x;
                box.style.top = r.y;
                box.style.width = Mathf.Max(6f, r.width);
                box.style.height = Mathf.Max(6f, r.height);
                if (resized) box.MarkDirtyRepaint();
                here.style.left = Mathf.Clamp(r.x + r.width * 0.5f - 34f, 2f, MapWidth - 72f);
                // Below the box, unless that would sit on the zoom buttons or fall off the map.
                here.style.top = r.yMax + 3f > map.contentRect.height - 34f ? Mathf.Max(2f, r.y - 19f) : r.yMax + 3f;
            }
        }

        /// <summary>Lines, stations, pencil plans and the town limit, drawn over the little map.</summary>
        sealed class MapLines : VisualElement
        {
            readonly Minimap m;

            public MapLines(Minimap m)
            {
                this.m = m;
                generateVisualContent += Draw;
            }

            void Draw(MeshGenerationContext mgc)
            {
                var gs = m.S;
                if (gs == null) return;
                var p = mgc.painter2D;
                var net = gs.net;
                p.lineCap = LineCap.Round;
                p.lineJoin = LineJoin.Round;
                // The town limit.
                var area = gs.city.playArea;
                Vector2 a = m.ToLocal(new Vector2(area.xMin, area.yMax)), b = m.ToLocal(new Vector2(area.xMax, area.yMin));
                DashedRect(p, Rect.MinMaxRect(a.x, a.y, b.x, b.y), Doodle.Ink, 1.5f, 5f, 3f);
                // Metro lines on thin ink casings.
                foreach (var pass in new[] { 0, 1 })
                {
                    foreach (var e in net.edges.Values)
                    {
                        if (e.lines.Count == 0 || e.path.Count < 2) continue;
                        var l = net.GetLine(e.lines[0]);
                        if (l == null) continue;
                        p.strokeColor = pass == 0 ? Doodle.Ink : (Color)l.Color;
                        p.lineWidth = pass == 0 ? 4.2f : 2.4f;
                        p.BeginPath();
                        p.MoveTo(m.ToLocal(e.path[0]));
                        for (int i = 1; i < e.path.Count; i++) p.LineTo(m.ToLocal(e.path[i]));
                        p.Stroke();
                    }
                }
                // Pencil plans: bold in plan mode, faint otherwise.
                bool plan = m.shownPlanMode;
                Color pencil = plan ? (Color)MapPalette.Pencil : Doodle.WithAlpha(MapPalette.Pencil, 0.6f);
                foreach (var pl in gs.plans)
                {
                    if (pl?.points == null || pl.points.Count < 2) continue;
                    var path = new List<Vector2>();
                    for (int i = 1; i < pl.points.Count; i++)
                    {
                        var seg = LineGeometry.OctilinearPath(pl.points[i - 1], pl.points[i], i < pl.diagonalFirst.Count && pl.diagonalFirst[i]);
                        for (int k = path.Count == 0 ? 0 : 1; k < seg.Count; k++) path.Add(m.ToLocal(seg[k]));
                    }
                    DashedPath(p, path, pencil, plan ? 2.4f : 1.6f, 4f, 3f);
                }
            }
        }

        static void DashedPath(Painter2D p, List<Vector2> pts, Color c, float width, float dash, float gap)
        {
            p.strokeColor = c;
            p.lineWidth = width;
            float carry = 0f;
            bool on = true;
            for (int i = 1; i < pts.Count; i++)
            {
                Vector2 a = pts[i - 1], b = pts[i];
                float len = Vector2.Distance(a, b), t = 0f;
                while (t < len)
                {
                    float step = Mathf.Min((on ? dash : gap) - carry, len - t);
                    if (on)
                    {
                        p.BeginPath();
                        p.MoveTo(Vector2.Lerp(a, b, t / len));
                        p.LineTo(Vector2.Lerp(a, b, (t + step) / len));
                        p.Stroke();
                    }
                    t += step;
                    carry += step;
                    if (carry >= (on ? dash : gap) - 1e-4f)
                    {
                        carry = 0f;
                        on = !on;
                    }
                }
            }
        }

        static void DashedRect(Painter2D p, Rect r, Color c, float width, float dash, float gap)
        {
            var pts = new List<Vector2> { new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMax), new Vector2(r.xMin, r.yMax), new Vector2(r.xMin, r.yMin) };
            DashedPath(p, pts, c, width, dash, gap);
        }

        /// <summary>The view box: an ink outline with yellow dashes over it.</summary>
        sealed class DashedBox : VisualElement
        {
            public DashedBox()
            {
                generateVisualContent += Draw;
            }

            void Draw(MeshGenerationContext mgc)
            {
                var r = contentRect;
                if (r.width < 2f || r.height < 2f) return;
                var p = mgc.painter2D;
                p.lineJoin = LineJoin.Round;
                p.strokeColor = Doodle.Ink;
                p.lineWidth = 3.6f;
                p.BeginPath();
                p.MoveTo(new Vector2(1f, 1f));
                p.LineTo(new Vector2(r.width - 1f, 1f));
                p.LineTo(new Vector2(r.width - 1f, r.height - 1f));
                p.LineTo(new Vector2(1f, r.height - 1f));
                p.ClosePath();
                p.Stroke();
                DashedRect(p, Rect.MinMaxRect(1f, 1f, r.width - 1f, r.height - 1f), MapPalette.Highlighter, 2.2f, 5f, 3f);
            }
        }
    }

    /// <summary>
    /// In the zoomed-out overview: a doodled compass rose and a scale bar ("5 km"), like the corner of a
    /// paper map.
    /// </summary>
    public sealed class MapKey
    {
        readonly GameRoot g;
        public readonly VisualElement root;
        readonly ScaleBar bar;
        readonly Label label;
        float shownKm = -1f;

        public MapKey(GameRoot g, VisualElement parent)
        {
            this.g = g;
            root = UIK.Row(parent, "panel", "map-key");
            root.pickingMode = PickingMode.Ignore;
            var rose = new CompassRose();
            root.Add(rose);
            var col = UIK.Col(root, "map-key-scale");
            bar = new ScaleBar();
            col.Add(bar);
            label = UIK.Text(col, "", "map-key-label");
            UIK.Display(label);
            foreach (var c in root.Children()) c.pickingMode = PickingMode.Ignore;
            UIK.Show(root, false);
        }

        public void Refresh()
        {
            bool show = g.State != null && g.CamCtl.InOverview && !g.HideHudForTutorial;
            UIK.Show(root, show);
            if (!show) return;
            float pw = g.CamCtl.PixelWorldSize;
            float panelPerPx = UIRoot.I.PanelScale;
            float[] nice = { 0.5f, 1f, 2f, 5f, 10f, 20f, 50f };
            float km = nice[0];
            foreach (var n in nice)
            {
                km = n;
                if (n / pw * panelPerPx >= 70f) break;
            }
            float px = km / pw * panelPerPx;
            bar.style.width = px;
            if (Mathf.Abs(km - shownKm) > 0.001f)
            {
                shownKm = km;
                label.text = (km < 1f ? Mathf.RoundToInt(km * 1000f) + " m" : km.ToString("0") + " km");
                bar.MarkDirtyRepaint();
            }
        }

        sealed class CompassRose : VisualElement
        {
            public CompassRose()
            {
                style.width = 58f;
                style.height = 58f;
                style.flexShrink = 0f;
                generateVisualContent += Draw;
            }

            void Draw(MeshGenerationContext mgc)
            {
                var p = mgc.painter2D;
                Vector2 c = contentRect.center;
                p.BeginPath();
                p.Arc(c, 24f, Angle.Degrees(0f), Angle.Degrees(360f));
                p.ClosePath();
                p.fillColor = Doodle.Cream;
                p.Fill();
                p.strokeColor = Doodle.Ink;
                p.lineWidth = 2.5f;
                p.Stroke();
                void Needle(Vector2 dir, Color fill, float len)
                {
                    Vector2 n = new Vector2(-dir.y, dir.x);
                    p.BeginPath();
                    p.MoveTo(c + dir * len);
                    p.LineTo(c + n * 5f);
                    p.LineTo(c - n * 5f);
                    p.ClosePath();
                    p.fillColor = fill;
                    p.Fill();
                    p.strokeColor = Doodle.Ink;
                    p.lineWidth = 1.8f;
                    p.Stroke();
                }
                Needle(Vector2.right, Doodle.Paper, 15f);
                Needle(Vector2.left, Doodle.Paper, 15f);
                Needle(Vector2.up, Doodle.Ink2, 18f);
                Needle(Vector2.down, Doodle.Tomato, 20f);
                // "N" on top (UI y points down, so north is towards -y).
                p.strokeColor = Doodle.Ink;
                p.lineWidth = 2.2f;
                p.lineCap = LineCap.Round;
                p.lineJoin = LineJoin.Round;
                p.BeginPath();
                p.MoveTo(c + new Vector2(-4f, -27f + 9f));
                p.LineTo(c + new Vector2(-4f, -27f));
                p.LineTo(c + new Vector2(4f, -27f + 9f));
                p.LineTo(c + new Vector2(4f, -27f));
                p.Stroke();
            }
        }

        sealed class ScaleBar : VisualElement
        {
            public ScaleBar()
            {
                style.height = 12f;
                generateVisualContent += Draw;
            }

            void Draw(MeshGenerationContext mgc)
            {
                var r = contentRect;
                if (r.width < 6f) return;
                var p = mgc.painter2D;
                for (int i = 0; i < 4; i++)
                {
                    float x0 = r.width * i / 4f, x1 = r.width * (i + 1) / 4f;
                    p.BeginPath();
                    p.MoveTo(new Vector2(x0, 2f));
                    p.LineTo(new Vector2(x1, 2f));
                    p.LineTo(new Vector2(x1, r.height - 2f));
                    p.LineTo(new Vector2(x0, r.height - 2f));
                    p.ClosePath();
                    p.fillColor = i % 2 == 0 ? Doodle.Ink : Doodle.Paper;
                    p.Fill();
                }
                p.BeginPath();
                p.MoveTo(new Vector2(1f, 2f));
                p.LineTo(new Vector2(r.width - 1f, 2f));
                p.LineTo(new Vector2(r.width - 1f, r.height - 2f));
                p.LineTo(new Vector2(1f, r.height - 2f));
                p.ClosePath();
                p.strokeColor = Doodle.Ink;
                p.lineWidth = 2f;
                p.Stroke();
            }
        }
    }
}
