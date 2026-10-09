using System.Collections.Generic;
using System.Text;
using Metro.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Metro
{
    public enum ToolKind { Select, Line, Insert, Depot, Demolish, Plan }

    /// <summary>Routes map input to the active tool and exposes the tool's preview to the renderer.</summary>
    public sealed class ToolController
    {
        readonly GameRoot g;
        public ToolKind Active { get; private set; } = ToolKind.Select;
        MapTool current;
        readonly SelectTool selectTool;
        readonly LineTool lineTool;
        readonly InsertTool insertTool;
        readonly DepotTool depotTool;
        readonly DemolishTool demolishTool;
        readonly PlanTool planTool;

        public ToolController(GameRoot g)
        {
            this.g = g;
            selectTool = new SelectTool(g);
            lineTool = new LineTool(g);
            insertTool = new InsertTool(g);
            depotTool = new DepotTool(g);
            demolishTool = new DemolishTool(g);
            planTool = new PlanTool(g);
            current = selectTool;
        }

        public LineTool Line => lineTool;
        public PlanTool Plan => planTool;

        MapTool For(ToolKind k)
        {
            switch (k)
            {
                case ToolKind.Line: return lineTool;
                case ToolKind.Insert: return insertTool;
                case ToolKind.Depot: return depotTool;
                case ToolKind.Demolish: return demolishTool;
                case ToolKind.Plan: return planTool;
            }
            return selectTool;
        }

        public static Feature? FeatureFor(ToolKind k)
        {
            switch (k)
            {
                case ToolKind.Line: return Feature.BuildLine;
                case ToolKind.Insert: return Feature.InsertStation;
                case ToolKind.Depot: return Feature.Depot;
                case ToolKind.Demolish: return Feature.Demolish;
            }
            return null;
        }

        public void Select(ToolKind k)
        {
            var feature = FeatureFor(k);
            if (feature.HasValue && !g.CheckUnlocked(feature.Value)) return;
            if (k == ToolKind.Line && Active != ToolKind.Line && g.State != null)
            {
                string licence = g.State.build.LineLicenceProblem();
                if (licence != null)
                {
                    UIRoot.I?.Toast(licence + " You can still extend your lines: select one and press Extend.", ToastKind.Info, 4.5f);
                    return;
                }
            }
            if (Active == k && k != ToolKind.Select && !(k == ToolKind.Line && lineTool.IsExtending))
            {
                Select(ToolKind.Select);
                return;
            }
            current?.Exit();
            Active = k;
            current = For(k);
            current.Enter();
            g.NetView.previewBuilder = current.DrawPreview;
            UIRoot.I?.HideCursorHint();
        }

        public void StartExtend(MetroLine l, bool atEnd)
        {
            current?.Exit();
            Active = ToolKind.Line;
            current = lineTool;
            lineTool.Enter();
            lineTool.BeginExtend(l, atEnd);
            g.NetView.previewBuilder = current.DrawPreview;
        }

        public void Reset()
        {
            current?.Exit();
            Active = ToolKind.Select;
            current = selectTool;
            current.Enter();
            if (g.NetView != null) g.NetView.previewBuilder = current.DrawPreview;
        }

        public bool AllowsLeftPan => Active == ToolKind.Select && selectTool.CanPan;

        public void Tick(float dt, bool overUI)
        {
            if (g.State == null) return;
            Vector2 world = g.CamCtl.ScreenToWorld(InputX.MousePos);
            current.Tick(dt, world, overUI);
        }

        /// <summary>Esc handling: returns true if the tool consumed it.</summary>
        public bool Cancel()
        {
            if (current.Cancel()) return true;
            if (Active != ToolKind.Select)
            {
                Select(ToolKind.Select);
                return true;
            }
            return false;
        }
    }

    public abstract class MapTool
    {
        protected readonly GameRoot g;
        protected GameState S => g.State;
        protected float PW => g.CamCtl.PixelWorldSize;
        Vector2 downPos;
        bool leftDown, rightDown;

        protected MapTool(GameRoot g)
        {
            this.g = g;
        }

        public virtual void Enter() { }

        public virtual void Exit()
        {
            UIRoot.I?.HideCursorHint();
            g.NetView.hoverStation = -1;
            g.NetView.hoverLine = -1;
        }

        public virtual bool Cancel() => false;
        public virtual void DrawPreview(VectorMeshBuilder b, float pw) { }
        public abstract void Tick(float dt, Vector2 world, bool overUI);

        /// <summary>True on the frame the left button is released without dragging (a click).</summary>
        protected bool LeftClicked(bool overUI)
        {
            Vector2 m = InputX.MousePos;
            if (InputX.LeftDown)
            {
                leftDown = !overUI;
                downPos = m;
            }
            if (InputX.LeftUp && leftDown)
            {
                leftDown = false;
                return (m - downPos).magnitude < 6f;
            }
            return false;
        }

        protected bool RightClicked(bool overUI)
        {
            Vector2 m = InputX.MousePos;
            if (InputX.RightDown)
            {
                rightDown = !overUI;
                downPos = m;
            }
            if (InputX.RightUp && rightDown)
            {
                rightDown = false;
                return (m - downPos).magnitude < 6f;
            }
            return false;
        }

        protected void Fail(string msg) => UIRoot.I?.Toast(msg, ToastKind.Error);
    }

    // ====================================================================== Select / inspect

    public sealed class SelectTool : MapTool
    {
        public bool CanPan = true;
        float lastClickTime;
        int lastClickStation = -1;

        public SelectTool(GameRoot g) : base(g) { }

        public override void Tick(float dt, Vector2 world, bool overUI)
        {
            var nv = g.NetView;
            Station hs = null;
            Train ht = null;
            MetroLine hl = null;
            Depot hd = null;
            if (!overUI)
            {
                hs = nv.PickStation(world, PW);
                if (hs == null) ht = nv.PickTrain(world, PW);
                if (hs == null && ht == null) hd = nv.PickDepot(world, PW);
                if (hs == null && ht == null && hd == null) nv.PickEdge(world, PW, out hl);
            }
            nv.hoverStation = hs != null ? hs.id : -1;
            nv.hoverLine = hl != null ? hl.id : -1;
            CanPan = true;

            // Hovering the map: ink bubbles with a dot in the thing's colour (a station's mood, a line's colour).
            if (hs != null)
            {
                bool served = hs.lines.Count > 0 && NetworkRenderer.IsServed(S.net, hs);
                UIRoot.I.SetCursorBubble(hs.name, StationHint(hs), served ? (Color)MapPalette.CrowdColor(hs.Crowding) : Doodle.Paper);
            }
            else if (ht != null)
            {
                var l = S.net.GetLine(ht.lineId);
                UIRoot.I.SetCursorBubble(l != null ? l.name + " train" : "Train", $"{Fmt.Int(ht.cargo.total)} on board · {Fmt.Pct(ht.LoadFactor)} full",
                    l != null ? (Color)l.Color : Doodle.Paper);
            }
            else if (hd != null) UIRoot.I.SetCursorBubble("Depot", $"{hd.Capacity} train spaces", Doodle.Tang);
            else if (hl != null) UIRoot.I.SetCursorBubble(hl.name, $"{hl.trains.Count} trains · busiest bit {Fmt.Pct(hl.MaxSegLoad)} full", (Color)hl.Color);
            else UIRoot.I.HideCursorHint();

            if (LeftClicked(overUI))
            {
                if (hs != null)
                {
                    if (lastClickStation == hs.id && Time.unscaledTime - lastClickTime < 0.35f) g.FocusOn(hs.pos, Mathf.Min(g.CamCtl.Size, 4f));
                    lastClickStation = hs.id;
                    lastClickTime = Time.unscaledTime;
                    g.Select(SelKind.Station, hs.id);
                }
                else if (ht != null) g.Select(SelKind.Train, ht.id);
                else if (hd != null) g.Select(SelKind.Depot, hd.id);
                else if (hl != null) g.Select(SelKind.Line, hl.id);
                else
                {
                    var d = S.city.DistrictObjAt(world);
                    if (d != null && S.city.InBounds(world) && g.Sel.kind == SelKind.None && d.revealed && g.CamCtl.Size > 6f)
                        g.Select(SelKind.District, d.id);
                    else g.ClearSelection();
                }
            }
        }

        string StationHint(Station s)
        {
            if (s.lines.Count == 0) return "Not on any line";
            if (!NetworkRenderer.IsServed(S.net, s)) return "No trains yet";
            return $"{Fmt.Int(s.waiting)} waiting · {Fmt.Pct(s.Crowding)} of capacity";
        }
    }

    // ====================================================================== Line drawing

    public sealed class LineTool : MapTool
    {
        readonly List<PlannedStop> stops = new List<PlannedStop>();
        MetroLine extendLine;
        bool extendAtEnd;
        bool flip;
        bool closingLoop;
        bool cursorValid;
        string cursorReason;
        PlannedStop cursorStop;
        bool hasCursor;
        float planCost, cursorCost;
        int colorIndex;

        public LineTool(GameRoot g) : base(g) { }

        public bool IsExtending => extendLine != null;
        public int PlannedCount => stops.Count;
        public int ColorIndex => colorIndex;

        public override void Enter()
        {
            stops.Clear();
            extendLine = null;
            flip = false;
            colorIndex = S != null ? S.net.NextFreeColor() : 0;
        }

        public override void Exit()
        {
            base.Exit();
            stops.Clear();
            extendLine = null;
        }

        public void BeginExtend(MetroLine l, bool atEnd)
        {
            stops.Clear();
            extendLine = l;
            extendAtEnd = atEnd;
            colorIndex = l.colorIndex;
            g.Select(SelKind.Line, l.id);
            var anchor = S.net.stations[Anchor()];
            g.FocusOn(anchor.pos);
            UIRoot.I.Toast($"Extending the {l.name}: click to add stations, right-click or Enter to build.", ToastKind.Info, 3.5f);
        }

        int Anchor() => extendLine == null ? -1 : (extendAtEnd ? extendLine.stops[extendLine.stops.Count - 1] : extendLine.stops[0]);

        Vector2 LastPos()
        {
            if (stops.Count > 0) return stops[stops.Count - 1].pos;
            int a = Anchor();
            return a >= 0 ? S.net.stations[a].pos : Vector2.zero;
        }

        int LastId()
        {
            if (stops.Count > 0) return stops[stops.Count - 1].stationId;
            return Anchor();
        }

        bool HasPrev => stops.Count > 0 || extendLine != null;

        /// <summary>Direction in which the line currently arrives at its last stop (for smooth bends).</summary>
        Vector2 IncomingDir()
        {
            if (stops.Count >= 2)
            {
                var a = stops[stops.Count - 2];
                var b = stops[stops.Count - 1];
                return LineGeometry.EndDirection(a.pos, b.pos, b.diagonalFirst);
            }
            if (stops.Count == 1 && extendLine != null)
            {
                var a = S.net.stations[Anchor()];
                return LineGeometry.EndDirection(a.pos, stops[0].pos, stops[0].diagonalFirst);
            }
            if (extendLine != null && extendLine.stops.Count >= 2)
            {
                int n = extendLine.stops.Count;
                int a = extendAtEnd ? extendLine.stops[n - 2] : extendLine.stops[1];
                int b = Anchor();
                var e = S.net.GetEdge(a, b);
                if (e != null && e.path.Count >= 2)
                {
                    bool fromB = e.a == b;
                    Vector2 p0 = fromB ? e.path[1] : e.path[e.path.Count - 2];
                    Vector2 p1 = fromB ? e.path[0] : e.path[e.path.Count - 1];
                    return (p1 - p0).normalized;
                }
            }
            return Vector2.zero;
        }

        bool InPlan(int stationId)
        {
            foreach (var s in stops)
                if (s.stationId == stationId) return true;
            return extendLine != null && extendLine.stops.Contains(stationId);
        }

        public override bool Cancel()
        {
            if (stops.Count > 0)
            {
                stops.Clear();
                return true;
            }
            if (extendLine != null)
            {
                extendLine = null;
                g.Tools.Select(ToolKind.Select);
                return true;
            }
            return false;
        }

        public override void Tick(float dt, Vector2 world, bool overUI)
        {
            if (InputX.KeyDown(Key.F)) flip = !flip;
            if (InputX.KeyDown(Key.Backspace) && stops.Count > 0) stops.RemoveAt(stops.Count - 1);

            var nv = g.NetView;
            hasCursor = !overUI;
            closingLoop = false;
            cursorReason = null;
            if (hasCursor)
            {
                var snap = nv.PickStation(world, PW, 9f);
                nv.hoverStation = snap != null ? snap.id : -1;
                if (snap != null)
                {
                    cursorStop = new PlannedStop { stationId = snap.id, pos = snap.pos };
                    int first = extendLine != null ? (extendAtEnd ? extendLine.stops[0] : extendLine.stops[extendLine.stops.Count - 1]) : (stops.Count > 0 ? stops[0].stationId : -2);
                    int planLen = stops.Count + (extendLine != null ? extendLine.stops.Count : 0);
                    if (snap.id == first && planLen >= 3 && snap.id >= 0)
                    {
                        closingLoop = true;
                        cursorValid = true;
                    }
                    else if (snap.id == LastId())
                    {
                        cursorValid = false;
                        cursorReason = "Already the last stop.";
                    }
                    else if (InPlan(snap.id))
                    {
                        cursorValid = false;
                        cursorReason = "The line already stops here.";
                    }
                    else cursorValid = true;
                }
                else if (extendLine == null && stops.Count >= 3 && stops[0].IsNew &&
                         Vector2.Distance(world, stops[0].pos) < (NetStyle.StationRadius + 1f) * NetworkRenderer.HalfWidthWorld(PW) + 9f * PW)
                {
                    cursorStop = stops[0];
                    closingLoop = true;
                    cursorValid = true;
                }
                else
                {
                    cursorStop = new PlannedStop { stationId = -1, pos = world };
                    cursorValid = S.build.ValidStationSite(world, out cursorReason);
                    if (cursorValid)
                    {
                        foreach (var s in stops)
                        {
                            if (s.IsNew && Vector2.Distance(s.pos, world) < Balance.MinStationSpacing)
                            {
                                cursorValid = false;
                                cursorReason = "Too close to a planned station.";
                            }
                        }
                    }
                }
                if (HasPrev)
                {
                    bool auto = LineGeometry.BestBend(LastPos(), cursorStop.pos, IncomingDir());
                    if (cursorStop.stationId >= 0 && LastId() >= 0 && S.net.TryGetBend(LastId(), cursorStop.stationId, out bool existing) && S.net.GetEdge(LastId(), cursorStop.stationId) != null)
                        auto = existing;
                    cursorStop.diagonalFirst = flip ? !auto : auto;
                    if (Vector2.Distance(LastPos(), cursorStop.pos) < Balance.MinStationSpacing && cursorStop.stationId < 0)
                    {
                        cursorValid = false;
                        cursorReason = "Too close to the previous stop.";
                    }
                }
            }

            planCost = S.build.PlanCost(stops, Anchor(), false, false, out _, out _);
            cursorCost = 0f;
            float segKm = 0f, segWater = 0f, segCost = 0f;
            bool shared = false;
            if (hasCursor && cursorValid)
            {
                if (HasPrev)
                {
                    segCost = S.build.SegmentCost(LastPos(), cursorStop.pos, cursorStop.diagonalFirst, LastId(), cursorStop.stationId, out segKm, out segWater);
                    shared = LastId() >= 0 && cursorStop.stationId >= 0 && S.net.GetEdge(LastId(), cursorStop.stationId) != null;
                }
                // Closing a loop onto a planned station doesn't build that station twice.
                cursorCost = segCost + (cursorStop.IsNew && !closingLoop ? S.build.StationCost : 0f);
            }
            UpdateHint(segKm, segWater, segCost, shared);

            if (LeftClicked(overUI))
            {
                if (!cursorValid)
                {
                    Fail(cursorReason ?? "Can't place a station there.");
                }
                else if (closingLoop)
                {
                    Finish(true);
                }
                else
                {
                    stops.Add(cursorStop);
                    flip = false;
                    Sfx.Click();
                }
            }
            if (RightClicked(overUI) || InputX.KeyDown(Key.Enter) || InputX.KeyDown(Key.NumpadEnter))
            {
                if (stops.Count > 0) Finish(false);
                else if (extendLine != null) Cancel();
            }
        }

        /// <summary>
        /// The receipt riding along with the cursor: what this click builds (tunnel, bridge and station,
        /// each with its price), the plan's running total and the keys that finish it.
        /// </summary>
        void UpdateHint(float segKm, float segWater, float segCost, bool shared)
        {
            if (!hasCursor)
            {
                UIRoot.I.HideCursorHint();
                return;
            }
            var r = UIRoot.I.Receipt();
            bool planning = stops.Count > 0;
            if (!cursorValid)
            {
                r.title = "Can't build here";
                r.icon = IconKind.Warning;
                r.paper = Doodle.TomatoL;
                r.note = cursorReason ?? "Can't place a station there.";
                r.noteBad = true;
                if (r.note.StartsWith("Too close"))
                    r.help = $"stations need {Mathf.RoundToInt(Balance.MinStationSpacing * 1000f)} m of elbow room";
                if (planning) r.Key("right-click", "build what you have");
                UIRoot.I.ShowReceipt();
                return;
            }
            if (closingLoop)
            {
                r.title = "Close the loop";
                r.icon = IconKind.Line;
                r.paper = Doodle.SkyL;
                r.help = "click to join the ends into a circle line";
            }
            else if (cursorStop.stationId >= 0)
            {
                var st = S.net.stations[cursorStop.stationId];
                r.title = "Connect to " + st.name;
                r.icon = IconKind.Station;
                r.paper = Doodle.MintL;
                r.help = st.lines.Count > 0 ? "snaps to the existing station, makes an interchange" : "snaps to the existing station";
            }
            else r.title = HasPrev ? "New station!" : "Start a line here";

            if (HasPrev && segKm > 0f)
            {
                float land = Mathf.Max(0f, segKm - segWater);
                if (shared) r.Row($"{Fmt.Km(segKm)} shared track", "+" + Fmt.Money(segCost));
                else if (segWater < 0.05f) r.Row($"{Fmt.Km(segKm)} tunnel", "+" + Fmt.Money(segCost));
                else if (land < 0.05f) r.Row($"{Fmt.Km(segKm)} bridge", "+" + Fmt.Money(segCost));
                else
                {
                    // Split the price the way it's charged: tunnels and bridges cost different amounts per km.
                    float lw = land * Balance.TunnelPerKm, ww = segWater * Balance.BridgePerKm;
                    r.Row($"{Fmt.Km(land)} tunnel", "+" + Fmt.Money(segCost * lw / (lw + ww)));
                    r.Row($"{Fmt.Km(segWater)} bridge", "+" + Fmt.Money(segCost * ww / (lw + ww)));
                }
            }
            if (cursorStop.IsNew && !closingLoop) r.Row(HasPrev ? "the station" : "New station", (HasPrev ? "+" : "") + Fmt.Money(S.build.StationCost));

            float total = planCost + cursorCost;
            bool affordable = S.econ.CanAfford(total);
            if (planning)
            {
                int n = stops.Count + (closingLoop ? 0 : 1);
                r.totalLabel = extendLine != null ? $"{n} new stop{(n == 1 ? "" : "s")}" : $"{n} stops planned";
                r.totalValue = Fmt.Money(total);
                r.totalBad = !affordable;
            }
            if (!affordable)
            {
                r.note = "Not enough cash for all of this.";
                r.noteBad = true;
            }
            if (planning)
            {
                r.Key("right-click", "build");
                r.Key("Backspace", "undo");
            }
            else if (extendLine != null) r.Key("right-click", "stop extending");
            if (HasPrev) r.Key("F", "flip bend");
            UIRoot.I.ShowReceipt();
        }

        void Finish(bool closeLoop)
        {
            BuildResult r;
            MetroLine built = null;
            bool closingDiag = cursorStop.diagonalFirst;
            if (extendLine != null)
            {
                r = S.build.ExtendLine(extendLine, extendAtEnd, stops, closeLoop, closingDiag);
                built = extendLine;
            }
            else
            {
                if (stops.Count + (closeLoop ? 0 : 0) < 2)
                {
                    Fail("Place at least two stations.");
                    return;
                }
                r = S.build.BuildLine(stops, closeLoop, closingDiag, out built);
            }
            if (!r.ok)
            {
                Fail(r.error);
                return;
            }
            Sfx.Build();
            string what = extendLine != null ? $"{built.name} extended" : $"{built.name} opened";
            UIRoot.I.Toast($"{what}: {built.stops.Count} stations · {Fmt.Money(r.cost)}", ToastKind.Good, 3f);
            stops.Clear();
            extendLine = null;
            g.Tools.Select(ToolKind.Select);
            g.Select(SelKind.Line, built.id);
        }

        readonly List<List<Vector2>> previewSegs = new List<List<Vector2>>();

        /// <summary>
        /// The plan so far: ink-cased track in a pale version of the line's colour with white dashes, a
        /// marching rubber band to the cursor, and a ghost stop there with its walking catchment.
        /// </summary>
        public override void DrawPreview(VectorMeshBuilder b, float pw)
        {
            if (S == null) return;
            var lw = NetStyle.LineWidth;
            float refPx = pw * StrokeWidth.PxScale;
            Color32 lineColor = LinePalette.Colors[colorIndex % LinePalette.Count];
            Color32 pale = MapPalette.Lerp(lineColor, MapPalette.White, 0.4f);
            Color32 ink = MapPalette.Ink;
            float R = NetStyle.StationRadius + 0.27f;

            Vector2 prev = Vector2.zero;
            bool hasPrev = false;
            int a = Anchor();
            if (a >= 0)
            {
                prev = S.net.stations[a].pos;
                hasPrev = true;
            }
            previewSegs.Clear();
            foreach (var s in stops)
            {
                if (hasPrev) previewSegs.Add(LineGeometry.OctilinearPath(prev, s.pos, s.diagonalFirst));
                prev = s.pos;
                hasPrev = true;
            }
            foreach (var p in previewSegs) b.AddPolyline(p, lw, ink, 0f, false, true, NetStyle.CasingUnits);
            foreach (var p in previewSegs) b.AddPolyline(p, lw, pale, 0f, false, true, 1f);
            foreach (var p in previewSegs) b.AddDashed(p, lw, MapPalette.White, 8f * refPx, 7f * refPx, 0.26f);
            if (hasCursor && hasPrev)
            {
                var path = LineGeometry.OctilinearPath(prev, cursorStop.pos, cursorStop.diagonalFirst);
                float march = Time.unscaledTime / 0.8f * 18f * refPx;
                b.AddDashed(path, StrokeWidth.Pixels(2f), cursorValid ? ink : MapPalette.Invalid, 10f * refPx, 8f * refPx, 1f, 0f, -march);
            }
            foreach (var s in stops)
            {
                if (s.IsNew)
                {
                    NetworkRenderer.AddWorldDisc(b, s.pos, Balance.WalkRadius, MapPalette.WithAlpha(MapPalette.CatchmentFill, 0.07f));
                    NetworkRenderer.AddWorldCircle(b, s.pos, Balance.WalkRadius, 0.8f, MapPalette.WithAlpha(MapPalette.Catchment, 0.35f), true);
                    b.AddDisc(s.pos, lw, R, ink);
                    b.AddDisc(s.pos, lw, R - 0.53f, MapPalette.White);
                }
                else NetworkRenderer.AddRing(b, s.pos, NetworkRenderer.StationRadiusUnits(S.net.stations[s.stationId]) + 0.8f, 0.5f, lineColor, Vector2.zero);
            }
            if (hasCursor)
            {
                Color32 rc = cursorValid ? ink : MapPalette.Invalid;
                if (cursorStop.stationId >= 0)
                    NetworkRenderer.AddRing(b, cursorStop.pos, NetworkRenderer.StationRadiusUnits(S.net.stations[cursorStop.stationId]) + 0.9f, 0.55f, cursorValid ? lineColor : MapPalette.Invalid, Vector2.zero);
                else
                {
                    if (cursorValid)
                    {
                        NetworkRenderer.AddWorldDisc(b, cursorStop.pos, Balance.WalkRadius, MapPalette.WithAlpha(MapPalette.CatchmentFill, 0.1f));
                        NetworkRenderer.AddWorldCircle(b, cursorStop.pos, Balance.WalkRadius, 1.25f, MapPalette.WithAlpha(MapPalette.Catchment, 0.7f), true);
                    }
                    b.AddDisc(cursorStop.pos, lw, R - 0.2f, MapPalette.WithAlpha(MapPalette.White, 0.65f));
                    NetworkRenderer.AddDashedRing(b, cursorStop.pos, R - 0.2f, 0.25f, rc, 9, 0f);
                }
            }
        }
    }

    // ====================================================================== Insert station on a track

    public sealed class InsertTool : MapTool
    {
        TrackEdge edge;
        MetroLine edgeLine;
        Vector2 point;
        bool valid;
        string reason;

        public InsertTool(GameRoot g) : base(g) { }

        public override void Tick(float dt, Vector2 world, bool overUI)
        {
            edge = null;
            if (!overUI)
            {
                edge = g.NetView.PickEdge(world, PW, out edgeLine, 12f);
                if (edge != null)
                {
                    Geo.DistToPolyline(world, edge.path, out int seg, out float t);
                    point = Vector2.Lerp(edge.path[seg], edge.path[seg + 1], t);
                    valid = S.build.ValidStationSite(point, out reason);
                }
            }
            if (edge == null)
            {
                if (overUI) UIRoot.I.HideCursorHint();
                else UIRoot.I.SetCursorHint("Add a station", "Hover over a track and click.", false);
            }
            else if (!valid)
            {
                var r = UIRoot.I.Receipt();
                r.title = "Can't build here";
                r.icon = IconKind.Warning;
                r.paper = Doodle.TomatoL;
                r.note = reason;
                r.noteBad = true;
                if (reason != null && reason.StartsWith("Too close"))
                    r.help = $"stations need {Mathf.RoundToInt(Balance.MinStationSpacing * 1000f)} m of elbow room";
                UIRoot.I.ShowReceipt();
            }
            else
            {
                var names = new StringBuilder();
                foreach (int lid in edge.lines)
                {
                    var l = S.net.GetLine(lid);
                    if (l == null) continue;
                    if (names.Length > 0) names.Append(", ");
                    names.Append(l.name);
                }
                bool ok = S.econ.CanAfford(S.build.StationCost);
                var r = UIRoot.I.Receipt();
                r.title = "New station!";
                r.Row("the station", "+" + Fmt.Money(S.build.StationCost));
                r.help = "served by the " + names;
                if (!ok)
                {
                    r.note = "Not enough cash right now.";
                    r.noteBad = true;
                }
                r.Key("click", "build it");
                UIRoot.I.ShowReceipt();
            }
            if (LeftClicked(overUI) && edge != null)
            {
                if (!valid)
                {
                    Fail(reason);
                    return;
                }
                var r = S.build.InsertStation(edge.a, edge.b, point, out var st);
                if (!r.ok) Fail(r.error);
                else
                {
                    Sfx.Build();
                    g.Select(SelKind.Station, st.id);
                }
            }
        }

        public override void DrawPreview(VectorMeshBuilder b, float pw)
        {
            if (edge == null) return;
            var lw = NetStyle.LineWidth;
            Color32 c = valid ? MapPalette.Ink : MapPalette.Invalid;
            if (valid)
            {
                NetworkRenderer.AddWorldDisc(b, point, Balance.WalkRadius, MapPalette.WithAlpha(MapPalette.CatchmentFill, 0.1f));
                NetworkRenderer.AddWorldCircle(b, point, Balance.WalkRadius, 1.25f, MapPalette.WithAlpha(MapPalette.Catchment, 0.7f), true);
            }
            float r = Mathf.Max(NetStyle.StationRadius + 0.27f, (edge.lines.Count - 1) * 0.5f * NetStyle.LaneSpacing + 1.35f);
            b.AddDisc(point, lw, r, c);
            b.AddDisc(point, lw, r - 0.53f, MapPalette.WithAlpha(MapPalette.White, 0.85f));
        }
    }

    // ====================================================================== Depot

    public sealed class DepotTool : MapTool
    {
        Station target;

        public DepotTool(GameRoot g) : base(g) { }

        public override void Tick(float dt, Vector2 world, bool overUI)
        {
            target = overUI ? null : g.NetView.PickStation(world, PW, 9f);
            g.NetView.hoverStation = target != null ? target.id : -1;
            if (target == null)
            {
                if (overUI) UIRoot.I.HideCursorHint();
                else UIRoot.I.SetCursorHint("Build a depot", "Click a station on a line. Every train needs depot space.", false);
            }
            else if (target.depotId >= 0) UIRoot.I.SetCursorHint(target.name, "Already has a depot.", true);
            else if (target.lines.Count == 0) UIRoot.I.SetCursorHint(target.name, "Not on a line yet.", true);
            else
            {
                bool ok = S.econ.CanAfford(S.build.DepotCost);
                var r = UIRoot.I.Receipt();
                r.title = "Depot at " + target.name;
                r.icon = IconKind.Depot;
                r.Row("the depot", "+" + Fmt.Money(S.build.DepotCost));
                r.help = $"room for {Balance.DepotCapacityPerLevel} trains, for any line";
                if (!ok)
                {
                    r.note = "Not enough cash right now.";
                    r.noteBad = true;
                }
                r.Key("click", "build it");
                UIRoot.I.ShowReceipt();
            }

            if (LeftClicked(overUI) && target != null)
            {
                var r = S.build.BuildDepot(target.id);
                if (!r.ok) Fail(r.error);
                else
                {
                    Sfx.Build();
                    UIRoot.I.Toast("Depot built at " + target.name, ToastKind.Good, 2f);
                    g.Tools.Select(ToolKind.Select);
                    g.Select(SelKind.Station, target.id);
                }
            }
        }
    }

    // ====================================================================== Pencil plans

    /// <summary>
    /// Plan in pencil: sketch future lines anywhere, even out past the town limit and into the clouds.
    /// Free, and no trains run on pencil. Click to add points (they snap to stations), right-click or
    /// Enter to finish, Backspace to undo a point, F to flip a bend. With no sketch going, right-click
    /// a pencil line to rub it out.
    /// </summary>
    public sealed class PlanTool : MapTool
    {
        readonly List<Vector2> pts = new List<Vector2>();
        readonly List<bool> diags = new List<bool>();
        bool hasCursor, flip, cursorDiag;
        Vector2 cursor;
        int hoverPlan = -1;

        public PlanTool(GameRoot g) : base(g) { }

        public int HoverPlan => hoverPlan;
        public bool Drawing => pts.Count > 0;

        public override void Enter()
        {
            pts.Clear();
            diags.Clear();
            flip = false;
            g.NetView.planMode = true;
        }

        public override void Exit()
        {
            base.Exit();
            pts.Clear();
            diags.Clear();
            hoverPlan = -1;
            g.NetView.planMode = false;
            g.NetView.hoverPlan = -1;
        }

        public override bool Cancel()
        {
            if (pts.Count == 0) return false;
            pts.Clear();
            diags.Clear();
            return true;
        }

        public override void Tick(float dt, Vector2 world, bool overUI)
        {
            if (InputX.KeyDown(Key.F)) flip = !flip;
            if (InputX.KeyDown(Key.Backspace) && pts.Count > 0)
            {
                pts.RemoveAt(pts.Count - 1);
                diags.RemoveAt(diags.Count - 1);
            }
            hasCursor = !overUI;
            cursor = world;
            var snap = hasCursor ? g.NetView.PickStation(world, PW, 9f) : null;
            g.NetView.hoverStation = snap != null ? snap.id : -1;
            if (snap != null) cursor = snap.pos;
            if (pts.Count > 0)
            {
                Vector2 last = pts[pts.Count - 1];
                Vector2 inDir = pts.Count >= 2 ? LineGeometry.EndDirection(pts[pts.Count - 2], last, diags[diags.Count - 1]) : Vector2.zero;
                bool auto = LineGeometry.BestBend(last, cursor, inDir);
                cursorDiag = flip ? !auto : auto;
            }
            hoverPlan = pts.Count == 0 && hasCursor ? PickPlan(world) : -1;
            g.NetView.hoverPlan = hoverPlan;
            UpdateHint();

            if (LeftClicked(overUI) && (pts.Count == 0 || Vector2.Distance(pts[pts.Count - 1], cursor) > 0.05f))
            {
                pts.Add(cursor);
                diags.Add(pts.Count > 1 && cursorDiag);
                flip = false;
                Sfx.Click();
            }
            if (RightClicked(overUI) || InputX.KeyDown(Key.Enter) || InputX.KeyDown(Key.NumpadEnter))
            {
                if (pts.Count >= 2) Finish();
                else if (pts.Count == 1)
                {
                    pts.Clear();
                    diags.Clear();
                }
                else if (hoverPlan >= 0) Erase(hoverPlan);
            }
        }

        /// <summary>The pencil plan under the cursor, or -1.</summary>
        int PickPlan(Vector2 world)
        {
            float best = 9f * PW;
            int found = -1;
            for (int i = 0; i < S.plans.Count; i++)
            {
                var plan = S.plans[i];
                for (int k = 1; k < plan.points.Count; k++)
                {
                    bool diag = k < plan.diagonalFirst.Count && plan.diagonalFirst[k];
                    float d = Geo.DistToPolyline(world, LineGeometry.OctilinearPath(plan.points[k - 1], plan.points[k], diag), out _, out _);
                    if (d < best)
                    {
                        best = d;
                        found = i;
                    }
                }
            }
            return found;
        }

        /// <summary>What a sketch would roughly cost to build today: track, plus stations where there are none yet.</summary>
        public static float Estimate(GameState gs, IList<Vector2> points, IList<bool> diag, int count = -1)
        {
            int n = count < 0 ? points.Count : count;
            float cost = 0f;
            for (int i = 0; i < n; i++)
            {
                if (gs.net.NearestStation(points[i], 0.02f) == null) cost += gs.build.StationCost;
                if (i > 0) cost += gs.build.SegmentCost(points[i - 1], points[i], diag != null && i < diag.Count && diag[i], -1, -1, out _, out _);
            }
            return cost;
        }

        /// <summary>A sketch is named after where it's heading: "Harbour Express?".</summary>
        public static string NameFor(GameState gs, Vector2 end)
        {
            var d = gs.city.DistrictObjAt(end);
            return d != null ? d.name + " Express?" : "Someday Line?";
        }

        void Finish()
        {
            var plan = new PencilPlan { name = NameFor(S, pts[pts.Count - 1]) };
            plan.points.AddRange(pts);
            plan.diagonalFirst.AddRange(diags);
            S.plans.Add(plan);
            Sfx.Click();
            UIRoot.I.Toast($"Pencilled in: {plan.name} Free, and no trains run on pencil.", ToastKind.Info, 3f);
            pts.Clear();
            diags.Clear();
        }

        void Erase(int i)
        {
            string name = S.plans[i].name;
            S.plans.RemoveAt(i);
            hoverPlan = -1;
            Sfx.Click();
            UIRoot.I.Toast($"Rubbed out: {name}", ToastKind.Info, 2f);
        }

        void UpdateHint()
        {
            if (!hasCursor)
            {
                UIRoot.I.HideCursorHint();
                return;
            }
            var r = UIRoot.I.Receipt();
            if (pts.Count == 0 && hoverPlan >= 0)
            {
                r.title = $"Rub out {S.plans[hoverPlan].name}";
                r.icon = IconKind.Pencil;
                r.Key("right-click", "rub it out");
                UIRoot.I.ShowReceipt();
                return;
            }
            r.title = pts.Count == 0 ? "Plan in pencil" : "Pencil plan";
            r.icon = IconKind.Pencil;
            r.paper = Doodle.SunL;
            if (pts.Count == 0)
            {
                r.note = "Sketch a line you'd like someday, even out in the clouds.";
                r.help = "free · no trains run on pencil";
                r.Key("click", "start sketching");
            }
            else
            {
                int n = pts.Count + 1;
                pts.Add(cursor);
                diags.Add(cursorDiag);
                float cost = Estimate(S, pts, diags);
                pts.RemoveAt(pts.Count - 1);
                diags.RemoveAt(diags.Count - 1);
                r.Row($"{n} stops", "about " + Fmt.Money(cost));
                r.help = "if you built it today · free to sketch";
                r.Key("right-click", "finish");
                r.Key("Backspace", "undo");
                r.Key("F", "flip bend");
            }
            UIRoot.I.ShowReceipt();
        }

        public override void DrawPreview(VectorMeshBuilder b, float pw)
        {
            if (S == null || pts.Count == 0) return;
            pts.Add(cursor);
            diags.Add(cursorDiag);
            NetworkRenderer.AddPencil(b, pts, diags, pw, MapPalette.Pencil, true, hasCursor ? pts.Count : pts.Count - 1);
            pts.RemoveAt(pts.Count - 1);
            diags.RemoveAt(diags.Count - 1);
        }
    }

    // ====================================================================== Demolish

    public sealed class DemolishTool : MapTool
    {
        Station st;
        Depot dep;

        public DemolishTool(GameRoot g) : base(g) { }

        public override void Tick(float dt, Vector2 world, bool overUI)
        {
            st = null;
            dep = null;
            if (!overUI)
            {
                dep = g.NetView.PickDepot(world, PW);
                if (dep == null) st = g.NetView.PickStation(world, PW, 8f);
            }
            g.NetView.hoverStation = st != null ? st.id : -1;
            if (st != null)
            {
                var r = UIRoot.I.Receipt();
                r.title = $"Demolish {st.name}?";
                r.icon = IconKind.Demolish;
                r.note = $"Refund {Fmt.Money(S.build.StationCost * Balance.DemolishRefund)}. Lines reconnect around it.";
                r.help = "People waiting there will leave.";
                UIRoot.I.ShowReceipt();
            }
            else if (dep != null)
            {
                var r = UIRoot.I.Receipt();
                r.title = "Remove depot?";
                r.icon = IconKind.Demolish;
                r.note = $"Refund {Fmt.Money(S.build.DepotCost * Balance.DemolishRefund)}.";
                r.help = "Less room for trains across the network.";
                UIRoot.I.ShowReceipt();
            }
            else if (overUI) UIRoot.I.HideCursorHint();
            else UIRoot.I.SetCursorHint("Demolish", "Click a station or depot.", false);

            if (LeftClicked(overUI))
            {
                if (dep != null)
                {
                    var r = S.build.RemoveDepot(dep.id);
                    if (!r.ok) Fail(r.error);
                    else Sfx.Click();
                }
                else if (st != null)
                {
                    var target = st;
                    g.UI.Confirm($"Demolish {target.name}?", "Lines through it will reconnect around it. Waiting passengers will leave.", () =>
                    {
                        var r = S.build.DemolishStation(target.id);
                        if (!r.ok) Fail(r.error);
                        else Sfx.Click();
                    }, "Demolish", S.build.StationCost * Balance.DemolishRefund);
                }
            }
        }

        public override void DrawPreview(VectorMeshBuilder b, float pw)
        {
            if (st != null) NetworkRenderer.AddRing(b, st.pos, NetworkRenderer.StationRadiusUnits(st) + 0.9f, 0.6f, MapPalette.Invalid, Vector2.zero);
            if (dep != null)
            {
                var ds = S.net.stations[dep.stationId];
                NetworkRenderer.AddRing(b, ds.pos, 3.6f, 0.6f, MapPalette.Invalid, dep.iconDir * (NetworkRenderer.StationRadiusUnits(ds) + 4.4f));
            }
        }
    }
}
