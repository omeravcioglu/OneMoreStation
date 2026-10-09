using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Metro.UI
{
    /// <summary>
    /// Bottom-right card for the current selection. Its frame takes the colour of what's selected (a line's
    /// colour, a station's mood, the depot's orange, the district's kind); the title row and the action buttons
    /// stay put while the details in between scroll.
    /// </summary>
    public sealed class Inspector
    {
        readonly GameRoot g;
        public readonly VisualElement root;
        readonly ScrollView scroll;
        readonly VisualElement top, card, footer;
        Selection shown = new Selection { kind = SelKind.None, id = -1 };
        int shownVersion = -1;
        Action refresh;
        float timer;
        bool follow;                // the camera rides along with the selected train
        VisualElement followBtn;
        readonly List<(VisualElement btn, float cost)> priced = new List<(VisualElement, float)>();

        public Inspector(GameRoot g, VisualElement parent)
        {
            this.g = g;
            root = UIK.Div(parent, "panel", "inspector");
            root.name = "inspector";
            // The title row sits on the coloured frame; the details and the action buttons live on a paper card inside.
            top = UIK.Div(root, "insp-top");
            card = UIK.Col(root, "insp-card");
            scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.AddToClassList("insp-scroll");
            card.Add(scroll);
            footer = UIK.Col(card, "insp-footer");
            UIK.Show(root, false);
        }

        GameState S => g.State;
        ConstructionService B => g.State.build;

        public void Refresh(float dt)
        {
            if (S == null)
            {
                UIK.Show(root, false);
                return;
            }
            var sel = g.Sel;
            bool structural = S.net.version != shownVersion && (sel.kind == SelKind.Line || sel.kind == SelKind.Station || sel.kind == SelKind.Depot);
            if (sel.kind != shown.kind || sel.id != shown.id || structural)
            {
                if (sel.kind != shown.kind || sel.id != shown.id) follow = false;
                shown = sel;
                shownVersion = S.net.version;
                Rebuild();
                timer = 0f;
            }
            if (follow) FollowTrain();
            timer -= dt;
            if (timer <= 0f)
            {
                timer = 0.25f;
                refresh?.Invoke();
                foreach (var (btn, cost) in priced) UIK.SetAffordable(btn, S.econ.CanAfford(cost));
            }
        }

        public void ForceRebuild() => shown = new Selection { kind = SelKind.None, id = -2 };

        void Rebuild()
        {
            var body = scroll.contentContainer;
            body.Clear();
            top.Clear();
            footer.Clear();
            priced.Clear();
            refresh = null;
            followBtn = null;
            root.style.backgroundColor = StyleKeyword.Null;
            bool any = true;
            switch (shown.kind)
            {
                case SelKind.Station:
                    var st = S.net.GetStation(shown.id);
                    if (st != null) BuildStation(body, st);
                    else any = false;
                    break;
                case SelKind.Line:
                    var l = S.net.GetLine(shown.id);
                    if (l != null) BuildLine(body, l);
                    else any = false;
                    break;
                case SelKind.Train:
                    var t = S.trains.Get(shown.id);
                    if (t != null) BuildTrain(body, t);
                    else any = false;
                    break;
                case SelKind.Depot:
                    var d = S.net.GetDepot(shown.id);
                    if (d != null) BuildDepot(body, d);
                    else any = false;
                    break;
                case SelKind.District:
                    if (shown.id >= 0 && shown.id < S.city.districts.Count) BuildDistrict(body, S.city.districts[shown.id]);
                    else any = false;
                    break;
                default:
                    any = false;
                    break;
            }
            UIK.Show(footer, footer.childCount > 0);
            UIK.Show(root, any);
            if (!any && shown.kind != SelKind.None) g.ClearSelection();
        }

        // ------------------------------------------------------------------ shared pieces

        void Frame(Color c) => root.style.backgroundColor = c;

        Label Head(string title, VisualElement badge, Action focus = null)
        {
            var head = UIK.Div(top, "insp-head");
            if (badge != null) head.Add(badge);
            var titleLabel = UIK.Text(head, title, "h2", "insp-title");
            titleLabel.style.flexShrink = 1f;
            UIK.Spacer(head);
            if (focus != null)
            {
                var show = UIK.IconButton(head, IconKind.Pin, focus, "Show on map", "icon-btn", 18f);
                show.AddToClassList("icon-btn-sm");
                show.style.marginRight = 6f;
            }
            var close = UIK.IconButton(head, IconKind.Close, () => g.ClearSelection(), "Close (Esc)", "icon-btn", 18f);
            close.AddToClassList("icon-btn-sm");
            return titleLabel;
        }

        /// <summary>A white disc with the line's letter in its colour, like the gumball chips.</summary>
        static Label LetterChip(MetroLine l)
        {
            var chip = UIK.Text(null, l.Letter, "insp-letter");
            chip.style.color = (Color)l.Color;
            UIK.Display(chip);
            return chip;
        }

        static VisualElement IconBadge(IconKind kind, Color? color = null)
        {
            var b = UIK.Div(null, "insp-icon-badge");
            var ic = new IconElement(kind, 28f);
            if (color.HasValue) ic.Color = color.Value;
            b.Add(ic);
            return b;
        }

        /// <summary>A row of little pips: filled up to `filled`, empty (dashed-looking) up to `total`.</summary>
        static void Pips(VisualElement row, int filled, int total, Color fill)
        {
            row.Clear();
            for (int i = 0; i < total; i++)
            {
                var pip = UIK.Div(row, "pip");
                if (i < filled) pip.style.backgroundColor = fill;
                else pip.AddToClassList("pip-empty");
            }
        }

        VisualElement Priced(VisualElement parent, string verb, float cost, Action onClick, params string[] classes)
        {
            var b = UIK.PriceButton(parent, verb, cost, onClick, classes);
            if (cost > 0f)
            {
                priced.Add((b, cost));
                UIK.Tooltip(b, () => S != null && !S.econ.CanAfford(cost) ? "Not enough cash right now." : null);
            }
            return b;
        }

        /// <summary>Applies a construction result: an error toast, or a cheerful receipt with what it cost.</summary>
        void Result(BuildResult r, string success = null, IconKind? icon = null)
        {
            if (!r.ok)
            {
                UIRoot.I.Toast(r.error, ToastKind.Error);
                return;
            }
            Sfx.Build();
            if (!string.IsNullOrEmpty(success)) UIRoot.I.Toast(success, ToastKind.Good, 2.5f, -r.cost, icon);
            ForceRebuild();
        }

        static VisualElement Pill(VisualElement parent, string text, Color c)
        {
            var p = UIK.Text(parent, text, "pill");
            p.style.backgroundColor = c;
            return p;
        }

        // ------------------------------------------------------------------ station

        void BuildStation(VisualElement body, Station s)
        {
            var face = new IconElement(IconKind.Smile, 40f);
            face.AddToClassList("insp-face");
            Head(s.name, face, () => g.FocusOn(s.pos, 3.5f));
            var d = S.city.districts[s.district];
            UIK.Text(body, $"{d.name} · {d.TypeLabel}", "small", "muted");
            var linesRow = UIK.Row(body, "mt-8");
            linesRow.style.flexWrap = Wrap.Wrap;
            foreach (var l in S.net.LinesAt(s.id))
            {
                var line = l;
                var pill = Pill(linesRow, l.name, (Color)l.Color);
                pill.AddManipulator(new Clickable(() => g.Select(SelKind.Line, line.id)));
            }
            if (s.IsInterchange) Pill(linesRow, "INTERCHANGE", Doodle.Ink);
            if (s.lines.Count == 0) UIK.Text(linesRow, "Not on any line yet", "small", "warn");

            // How full the platform is, at a glance: a big number, a word and a four-zone meter.
            var busy = UIK.Row(body, "insp-busy", "mt-8");
            var busyPct = UIK.Text(busy, "0%", "insp-busy-value");
            UIK.Display(busyPct);
            var busyWord = UIK.Text(busy, "", "small", "muted");
            var meter = new ZoneMeter();
            meter.AddToClassList("mt-4");
            body.Add(meter);
            var turned = UIK.Text(body, "", "small", "bad", "mt-4");
            turned.style.whiteSpace = WhiteSpace.Normal;

            var grid = UIK.Div(body, "grid2", "mt-8");
            var waiting = UIK.Tile(grid, "waiting on platform");
            var entries = UIK.Tile(grid, "entries today");
            var boards = UIK.Tile(grid, "boardings today");
            var transfers = UIK.Tile(grid, "transfers today");
            var catchment = UIK.Text(body, "", "small", "muted", "mt-8");
            catchment.style.whiteSpace = WhiteSpace.Normal;

            UIK.Divider(body);
            UIK.Text(body, S.Has(Feature.Upgrades) ? "UPGRADES" : "STATION", "caps");
            if (S.Has(Feature.Upgrades))
            {
                UpgradeRow(body, s, StationUpgrade.Platform, IconKind.Platform, "Platforms", s.platformLevel, 3,
                    $"Level {s.platformLevel} · trains up to {s.MaxWagons} wagons can stop here.", s.platformLevel >= 3, "Lengthen");
                UpgradeRow(body, s, StationUpgrade.Capacity, IconKind.Exit, "Exits & concourse", s.capacityLevel, 3,
                    $"Level {s.capacityLevel} · holds {Fmt.Int(s.Capacity)} waiting passengers before it gets dangerous.", s.capacityLevel >= 3, "Expand");
                UpgradeRow(body, s, StationUpgrade.Elevators, IconKind.Elevator, "Elevators", -1, 0,
                    s.elevators ? "Step-free access for everyone." : "Step-free access. Riders are happier.", s.elevators, "Install");
                if (s.IsInterchange)
                    UpgradeRow(body, s, StationUpgrade.Concourse, IconKind.Concourse, "Transfer concourse", -1, 0,
                        s.concourse ? "Quick transfers between lines." : "Cuts transfer time between lines; more capacity.", s.concourse, "Build");
            }
            else LockedRow(body, Feature.Upgrades, $"Holds {Fmt.Int(s.Capacity)} waiting passengers; trains up to {s.MaxWagons} wagons fit.");
            DepotRow(body, s);

            var actions = UIK.Row(footer);
            UIK.Spacer(actions);
            var demo = UIK.HoldButton(actions, "Hold to demolish", () =>
            {
                var r = B.DemolishStation(s.id);
                if (r.ok) g.ClearSelection();
                Result(r, $"{s.name} demolished", IconKind.Demolish);
            }, "btn-small", "btn-danger");
            var refund = UIK.Text(demo, "+" + Fmt.Money(B.StationCost * Balance.DemolishRefund), "price-chip", "price-chip-refund");
            refund.pickingMode = PickingMode.Ignore;
            UIK.Display(refund);
            UIK.Tooltip(demo, "Hold to demolish: lines through it reconnect around it, and waiting passengers leave.");

            refresh = () =>
            {
                float c = s.Crowding;
                Color mood = MapPalette.CrowdColor(c);
                Frame(mood);
                face.Color = mood;
                face.Mood = Doodle.MoodFromCrowding(c);
                busyPct.text = Fmt.Pct(c);
                busyWord.text = "of platform · " + Doodle.CrowdWord(c);
                meter.Value = c;
                turned.text = s.turnedAwayToday >= 1f ? $"{Fmt.Count(s.turnedAwayToday)} riders turned away at the gates today." : "";
                UIK.Show(turned, s.turnedAwayToday >= 1f);
                waiting.value.text = Fmt.Int(s.waiting);
                entries.value.text = Fmt.Count(s.entriesToday);
                boards.value.text = Fmt.Count(s.boardingsToday);
                transfers.value.text = Fmt.Count(s.transfersToday);
                var dm = S.demand;
                if (s.id < dm.resAt.Length && dm.served.Length > s.id && dm.served[s.id])
                    catchment.text = $"Within walking distance: {Fmt.Count(dm.resAt[s.id])} residents, {Fmt.Count(dm.jobAt[s.id])} jobs. Expected {Fmt.Count(dm.StationDailyTrips(s.id))} trips/day.";
                else
                    catchment.text = "No trains stop here yet, so nobody uses this station.";
            };
        }

        /// <summary>A greyed row explaining when a mechanic unlocks.</summary>
        void LockedRow(VisualElement body, Feature f, string now)
        {
            var row = UIK.Div(body, "upgrade-row");
            var ib = UIK.Div(row, "upgrade-icon", "upgrade-icon-locked");
            ib.Add(new IconElement(IconKind.Lock, 22f));
            var col = UIK.Col(row);
            col.style.flexShrink = 1f;
            UIK.Text(col, ProgressionSystem.FeatureTitle(f), "h3", "faint");
            var t = UIK.Text(col, (now != null ? now + " " : "") + $"Unlocks when your city becomes a {ProgressionSystem.Tiers[ProgressionSystem.UnlockTier(f)].name}.", "small", "muted");
            t.style.whiteSpace = WhiteSpace.Normal;
        }

        void UpgradeRow(VisualElement body, Station s, StationUpgrade u, IconKind icon, string title, int level, int maxLevel, string desc, bool maxed, string verb)
        {
            var row = UIK.Div(body, "upgrade-row");
            var ib = UIK.Div(row, "upgrade-icon");
            ib.Add(new IconElement(icon, 24f));
            var col = UIK.Col(row);
            col.style.flexGrow = 1f;
            col.style.flexShrink = 1f;
            var tr = UIK.Row(col);
            UIK.Text(tr, title, "h3");
            if (level > 0)
            {
                var pips = UIK.Row(tr, "pips");
                pips.style.marginLeft = 8f;
                Pips(pips, level, maxLevel, Doodle.Sky);
            }
            var dl = UIK.Text(col, desc, "small", "muted");
            dl.style.whiteSpace = WhiteSpace.Normal;
            if (maxed)
            {
                var done = UIK.Row(row, "installed-sticker");
                done.Add(new IconElement(IconKind.Check, 16f));
                UIK.Text(done, maxLevel > 0 ? "Maxed" : "Installed", "sticker-text");
                return;
            }
            float cost = B.UpgradeCost(s, u);
            var btn = Priced(row, verb, cost, () => Result(B.Upgrade(s.id, u), $"{title} upgraded at {s.name}", icon), "btn-small");
            btn.style.marginLeft = 8f;
        }

        void DepotRow(VisualElement body, Station s)
        {
            var row = UIK.Div(body, "upgrade-row");
            var ib = UIK.Div(row, "upgrade-icon");
            ib.Add(new IconElement(IconKind.Depot, 24f));
            var col = UIK.Col(row);
            col.style.flexGrow = 1f;
            col.style.flexShrink = 1f;
            UIK.Text(col, "Train depot", "h3");
            var dep = S.net.GetDepot(s.depotId);
            if (dep != null)
            {
                UIK.Text(col, $"Stores up to {dep.Capacity} trains (network: {S.trains.AliveCount}/{S.net.DepotCapacity}).", "small", "muted").style.whiteSpace = WhiteSpace.Normal;
                var b = UIK.Button(row, "Details", () => g.Select(SelKind.Depot, dep.id), "btn-small");
                b.style.marginLeft = 8f;
            }
            else
            {
                UIK.Text(col, s.lines.Count > 0 ? "Lines through here could store trains." : "Needs a line first.", "small", "muted");
                var b = Priced(row, "Build", B.DepotCost, () => Result(B.BuildDepot(s.id), "Depot built at " + s.name, IconKind.Depot), "btn-small");
                b.style.marginLeft = 8f;
            }
        }

        // ------------------------------------------------------------------ line

        void BuildLine(VisualElement body, MetroLine l)
        {
            Frame(l.Color);
            Head(l.name, LetterChip(l), () => g.FocusLine(l.id));
            var sub = UIK.Text(body, "", "small", "muted");

            Action refreshStats = null;
            if (!S.net.LineHasDepot(l))
            {
                // Nothing runs without a depot, so the card is just the to-do.
                var note = UIK.Col(body, "insp-note", "mt-8");
                UIK.Text(note, "No depot yet!", "h3");
                var t = UIK.Text(note, "Trains live in depots. Build one at any station on this line, then add trains.", "small");
                t.style.whiteSpace = WhiteSpace.Normal;
                var b = Priced(note, "Build a depot", B.DepotCost, () =>
                {
                    g.Tools.Select(ToolKind.Depot);
                    UIRoot.I.Toast($"Now click any station on the {l.name}.", ToastKind.Info, 3f);
                }, "btn-small", "btn-primary");
                b.style.alignSelf = Align.FlexStart;
                b.style.marginTop = 8f;
            }
            else
            {
                var warn = UIK.Text(body, "", "small", "warn", "mt-4");
                warn.style.whiteSpace = WhiteSpace.Normal;

                // The steppers come first: they're what you touch most, so they never hide below the fold.
                var trainsRow = UIK.Row(body, "mt-8");
                var tcol = UIK.Col(trainsRow);
                tcol.style.flexGrow = 1f;
                tcol.style.flexShrink = 1f;
                var ttitle = UIK.Row(tcol);
                UIK.Text(ttitle, "Trains", "h3");
                var dots = UIK.Row(ttitle, "train-dots");
                var tdesc = UIK.Text(tcol, "", "small", "muted");
                tdesc.style.whiteSpace = WhiteSpace.Normal;
                UIK.IconButton(trainsRow, IconKind.Minus, () => Result(B.RemoveTrain(l), $"Train sold from the {l.name}", IconKind.Train),
                    $"Sell a train (+{Fmt.Money(B.TrainCost(l.wagons) * Balance.TrainResale)})");
                var tval = UIK.Text(trainsRow, "0", "stepper-val");
                var tplus = UIK.IconButton(trainsRow, IconKind.Plus, () => Result(B.AddTrain(l), $"Choo choo! Train added to the {l.name}", IconKind.Train),
                    $"Buy a train ({Fmt.Money(B.TrainCost(l.wagons))})");
                tplus.name = "add-train";

                var wagonsRow = UIK.Row(body, "mt-8");
                var wcol = UIK.Col(wagonsRow);
                wcol.style.flexGrow = 1f;
                wcol.style.flexShrink = 1f;
                bool wagonsOpen = S.Has(Feature.Wagons);
                var wtitle = UIK.Row(wcol);
                UIK.Text(wtitle, "Wagons per train", "h3");
                var wpips = UIK.Row(wcol, "pips", "mt-4");
                var wdesc = UIK.Text(wcol, "", "small", "muted");
                wdesc.style.whiteSpace = WhiteSpace.Normal;
                Label wval;
                if (wagonsOpen)
                {
                    UIK.IconButton(wagonsRow, IconKind.Minus, () => Result(B.SetWagons(l, l.wagons - 1), $"Shorter trains on the {l.name}", IconKind.Wagon), "Remove a wagon from every train");
                    wval = UIK.Text(wagonsRow, "0", "stepper-val");
                    UIK.IconButton(wagonsRow, IconKind.Plus, () => Result(B.SetWagons(l, l.wagons + 1), $"Longer trains on the {l.name}", IconKind.Wagon), $"Add a wagon to every train ({Fmt.Money(B.WagonCost)} each)");
                }
                else
                {
                    wagonsRow.Add(new IconElement(IconKind.Lock, 22f));
                    wval = UIK.Text(wagonsRow, "0", "stepper-val");
                }
                string wagonsTier = ProgressionSystem.Tiers[ProgressionSystem.UnlockTier(Feature.Wagons)].name;

                UIK.Divider(body);
                var grid = UIK.Div(body, "grid2");
                var headway = UIK.Tile(grid, "between trains");
                var riders = UIK.Tile(grid, "boardings today");
                var load = UIK.Tile(grid, "busiest section load");
                var wait = UIK.Tile(grid, "average wait");
                var loadFace = new IconElement(IconKind.Smile, 24f) { Color = Doodle.MoodBad, Mood = -1f };
                loadFace.AddToClassList("tile-face");
                load.value.parent.Add(loadFace);

                UIK.Divider(body);
                var loadHead = UIK.Row(body);
                UIK.Text(loadHead, "HOW PACKED IS IT?", "caps");
                UIK.Spacer(loadHead);
                var ends = UIK.Text(loadHead, "", "tiny", "faint");
                var chart = new MiniChart(52f, "No passengers on board yet.") { color = (Color)l.Color, forcedMax = 1f, fullLine = 1f, peakFace = true };
                chart.style.marginTop = 6f;
                body.Add(chart);

                int shownTrains = -1, shownWagons = -1, shownMax = -1;
                refreshStats = () =>
                {
                    string w = l.trains.Count == 0 ? "No trains yet. Add trains so people can ride." : null;
                    warn.text = w ?? "";
                    UIK.Show(warn, w != null);
                    headway.value.text = l.trains.Count > 0 ? Fmt.Minutes(l.headway) : "—";
                    riders.value.text = Fmt.Count(l.boardingsToday);
                    float mx = l.MaxSegLoad;
                    load.value.text = Fmt.Pct(mx);
                    load.value.style.color = (Color)MapPalette.CrowdColor(mx);
                    load.caption.text = mx >= 0.9f ? "busiest bit, squished!" : "busiest section load";
                    UIK.Show(loadFace, mx >= 0.9f);
                    wait.value.text = l.trains.Count > 0 ? Fmt.Minutes(l.avgWaitRecent) : "—";
                    tval.text = l.trains.Count.ToString();
                    float perDay = l.trains.Count * (Balance.DriverSalaryPerTrain + l.wagons * Balance.TrainMaintenancePerWagon);
                    tdesc.text = l.trains.Count > 0 ? $"About {Fmt.Minutes(l.headway)} apart · {Fmt.Money(perDay)} a day" : $"{Fmt.Money(B.TrainCost(l.wagons))} each";
                    if (l.trains.Count != shownTrains)
                    {
                        shownTrains = l.trains.Count;
                        dots.Clear();
                        int show = Mathf.Min(shownTrains, 8);
                        for (int i = 0; i < show; i++) dots.Add(new IconElement(IconKind.Train, 17f) { Color = (Color)l.Color });
                        if (shownTrains > show) UIK.Text(dots, "+" + (shownTrains - show), "tiny", "muted");
                    }
                    wval.text = l.wagons.ToString();
                    int maxW = Mathf.Max(l.wagons, B.MaxWagonsForLine(l));
                    if (l.wagons != shownWagons || maxW != shownMax)
                    {
                        shownWagons = l.wagons;
                        shownMax = maxW;
                        Pips(wpips, l.wagons, maxW, (Color)l.Color);
                    }
                    int spare = maxW - l.wagons;
                    wdesc.text = !wagonsOpen ? $"Longer trains unlock when your city becomes a {wagonsTier}."
                        : spare > 0 ? $"Room for {spare} more with the current platforms!" : "As long as the platforms allow.";
                    int segs = l.SegmentCount;
                    var vals = new float[segs];
                    for (int i = 0; i < segs; i++) vals[i] = l.SegLoad(i);
                    chart.SetData(vals);
                    if (l.stops.Count >= 2)
                    {
                        var a = S.net.stations[l.stops[0]];
                        var b = S.net.stations[l.stops[l.stops.Count - 1]];
                        ends.text = l.loop ? "around the loop" : $"{a.name} → {b.name}";
                    }
                };
            }

            var row1 = UIK.Row(footer);
            if (!l.loop)
            {
                UIK.Button(row1, "Extend start", () => g.Tools.StartExtend(l, false), "btn-small").style.marginRight = 6f;
                UIK.Button(row1, "Extend end", () => g.Tools.StartExtend(l, true), "btn-small").style.marginRight = 6f;
            }
            UIK.Button(row1, "Recolour", () =>
            {
                B.SetLineColor(l, l.colorIndex + 1);
                ForceRebuild();
            }, "btn-small");
            var row2 = UIK.Row(footer, "mt-8");
            if (!l.loop && l.stops.Count > 2)
            {
                var cutA = UIK.Button(row2, "Cut start", () => Result(B.TruncateLine(l, false)), "btn-small");
                cutA.style.marginRight = 6f;
                UIK.Tooltip(cutA, "Remove the first stop from this line.");
                var cutB = UIK.Button(row2, "Cut end", () => Result(B.TruncateLine(l, true)), "btn-small");
                UIK.Tooltip(cutB, "Remove the last stop from this line.");
            }
            UIK.Spacer(row2);
            var del = UIK.HoldButton(row2, "Hold to delete", () =>
            {
                var r = B.DeleteLine(l);
                if (r.ok) g.ClearSelection();
                Result(r, $"The {l.name} is gone", IconKind.Demolish);
            }, "btn-small", "btn-danger");
            UIK.Tooltip(del, "Hold to delete the line: its trains are sold, stations stay in place.");

            refresh = () =>
            {
                float km = l.path != null ? l.path.length : 0f;
                sub.text = $"{l.stops.Count} stations · {Fmt.Km(km)}{(l.loop ? " · circle line" : "")} · fits {Fmt.Int(l.Capacity)} per train";
                refreshStats?.Invoke();
            };
        }

        // ------------------------------------------------------------------ train

        void BuildTrain(VisualElement body, Train t)
        {
            var l = S.net.GetLine(t.lineId);
            if (l == null) return;
            Frame(l.Color);
            Head($"{l.name} train", LetterChip(l), () => g.FocusOn(NetworkRenderer.TrainWorldPos(t, g.CamCtl.PixelWorldSize)));
            var headline = UIK.Text(body, "", "insp-headline");
            var status = UIK.Text(body, "", "small", "muted");
            var wagons = UIK.Row(body, "wagon-load", "mt-8");
            var fills = new List<VisualElement>();
            for (int i = 0; i < l.wagons; i++)
            {
                var w = UIK.Div(wagons, "wagon-shape");
                var f = UIK.Div(w, "wagon-fill");
                fills.Add(f);
            }
            var squeeze = UIK.Text(body, "", "small", "mt-4");
            var grid = UIK.Div(body, "grid2", "mt-8");
            var pax = UIK.Tile(grid, "passengers on board");
            var lf = UIK.Tile(grid, "of capacity");

            var row = UIK.Row(footer);
            followBtn = UIK.Button(row, follow ? "Following" : "Follow", () => SetFollow(!follow), "btn-small");
            followBtn.EnableInClassList("btn-on", follow);
            UIK.Tooltip(followBtn, "The camera rides along with this train. Pan the map to stop.");
            followBtn.style.marginRight = 6f;
            UIK.Button(row, "Show line", () => g.Select(SelKind.Line, l.id), "btn-small");

            refresh = () =>
            {
                if (!t.alive) return;
                pax.value.text = Fmt.Int(t.cargo.total);
                lf.value.text = Fmt.Pct(t.LoadFactor);
                lf.value.style.color = (Color)MapPalette.CrowdColor(t.LoadFactor);
                Color fillC = MapPalette.CrowdColor(t.LoadFactor);
                foreach (var f in fills)
                {
                    f.style.width = Length.Percent(Mathf.Clamp01(t.LoadFactor) * 100f);
                    f.style.backgroundColor = fillC;
                }
                squeeze.text = $"{Fmt.Int(t.cargo.total)} of {Fmt.Int(l.Capacity)} riders squeezed in";
                string towards;
                if (l.loop) towards = t.dir > 0 ? "clockwise" : "anticlockwise";
                else towards = "towards " + S.net.stations[l.stops[t.dir > 0 ? l.stops.Count - 1 : 0]].name;
                headline.text = t.state == TrainState.Dwelling
                    ? "Stopped at " + S.net.stations[t.atStation].name
                    : "Next stop: " + (t.nextStation >= 0 ? S.net.stations[t.nextStation].name : "—");
                status.text = $"{towards} · {l.wagons} wagons";
            };
        }

        void SetFollow(bool on)
        {
            follow = on;
            if (followBtn == null) return;
            UIK.SetButtonText(followBtn, on ? "Following" : "Follow");
            followBtn.EnableInClassList("btn-on", on);
        }

        void FollowTrain()
        {
            var t = shown.kind == SelKind.Train ? S.trains.Get(shown.id) : null;
            if (t == null || !t.alive || g.CamCtl.UserPanned)
            {
                SetFollow(false);
                return;
            }
            g.CamCtl.FocusOn(NetworkRenderer.TrainWorldPos(t, g.CamCtl.PixelWorldSize));
        }

        // ------------------------------------------------------------------ depot

        void BuildDepot(VisualElement body, Depot d)
        {
            var st = S.net.stations[d.stationId];
            Frame(Doodle.Tang);
            Head("Depot at " + st.name, IconBadge(IconKind.Depot), () => g.FocusOn(st.pos, 3.5f));
            var lvl = UIK.Text(body, $"LEVEL {d.level}", "level-sticker");
            UIK.Display(lvl);
            var desc = UIK.Text(body, "", "small", "muted", "mt-8");
            desc.style.whiteSpace = WhiteSpace.Normal;
            UIK.Text(body, "NETWORK DEPOT SPACE", "caps", "mt-8");
            var space = UIK.Row(body, "space-stickers", "mt-4");
            var spaceText = UIK.Text(body, "", "small", "muted");
            var grid = UIK.Div(body, "grid2", "mt-8");
            var cap = UIK.Tile(grid, "trains this depot holds");
            var used = UIK.Tile(grid, "network trains / depot space");

            var row = UIK.Row(footer);
            if (d.level < Balance.MaxDepotLevel)
            {
                if (S.Has(Feature.DepotExpansion)) Priced(row, "Expand", B.DepotExpandCost, () => Result(B.ExpandDepot(d.id), "Depot expanded at " + st.name, IconKind.Depot), "btn-small");
                else UIK.Text(row, "Bigger depots unlock at City size.", "small", "muted");
            }
            UIK.Spacer(row);
            UIK.HoldButton(row, "Hold to remove", () =>
            {
                var r = B.RemoveDepot(d.id);
                if (r.ok) g.ClearSelection();
                Result(r, "Depot removed", IconKind.Depot);
            }, "btn-small", "btn-danger");

            int shownTrains = -1, shownCap = -1;
            refresh = () =>
            {
                desc.text = $"Lines through {st.name} can take trains from here. Every train in the network needs depot space somewhere.";
                cap.value.text = d.Capacity.ToString();
                int trains = S.trains.AliveCount, capacity = S.net.DepotCapacity;
                used.value.text = $"{trains} / {capacity}";
                used.value.EnableInClassList("bad", trains >= capacity);
                spaceText.text = trains >= capacity ? "Full! Build or expand a depot before buying more trains." : $"{capacity - trains} train spaces free.";
                if (trains == shownTrains && capacity == shownCap) return;
                shownTrains = trains;
                shownCap = capacity;
                // One sticker per two trains, in the colours of the lines that use the space; dashed ones are free.
                var colors = new List<Color>();
                foreach (var l in S.net.AliveLines())
                    for (int i = 0; i < l.trains.Count; i++) colors.Add(l.Color);
                space.Clear();
                int stickers = Mathf.Min(30, Mathf.CeilToInt(capacity / 2f));
                for (int i = 0; i < stickers; i++)
                {
                    var stk = UIK.Div(space, "space-sticker");
                    int ti = i * 2;
                    if (ti < colors.Count) stk.style.backgroundColor = colors[ti];
                    else stk.AddToClassList("space-sticker-free");
                }
            };
        }

        // ------------------------------------------------------------------ district

        void BuildDistrict(VisualElement body, District d)
        {
            if (!d.revealed)
            {
                Frame(Doodle.SkyL);
                Head("Unexplored district", IconBadge(IconKind.Cloud));
                var clouds = UIK.Row(body, "insp-clouds");
                clouds.Add(new IconElement(IconKind.Cloud, 54f));
                clouds.Add(new IconElement(IconKind.Cloud, 38f));
                var t = UIK.Text(body, "This part of the region is still in the clouds. Districts open as the city grows.", "small", "mt-8");
                t.style.whiteSpace = WhiteSpace.Normal;
                int hidden = 0;
                foreach (var x in S.city.districts)
                    if (!x.revealed) hidden++;
                UIK.Text(body, $"{hidden} still in the clouds", "chip", "mt-8");
                double at = S.growth.nextRevealAt;
                if (at < double.MaxValue / 2)
                    UIK.Text(body, $"Next new district expected around Day {(int)(at / Balance.MinutesPerDay) + 1}.", "small", "muted", "mt-8");
                refresh = null;
                return;
            }
            Frame(Doodle.DistrictColor(d.type));
            Head(d.name, IconBadge(Doodle.DistrictIcon(d.type)), () => g.FocusDistrict(d.id));
            UIK.Text(body, d.TypeLabel, "small", "muted");
            var grid = UIK.Div(body, "grid2", "mt-8");
            var pop = UIK.Tile(grid, "residents");
            var jobs = UIK.Tile(grid, "jobs");
            var cov = UIK.Tile(grid, "residents near a served station");
            var stations = UIK.Tile(grid, "stations");
            var growRow = UIK.Row(body, "mt-8");
            var growText = UIK.Text(growRow, "", "small", "muted");
            var (growBar, growFill) = UIK.Bar(body, Doodle.Tang);
            var ask = UIK.Row(body, "insp-callout", "mt-8");
            ask.Add(new IconElement(IconKind.Hall, 22f));
            var askText = UIK.Text(ask, $"{d.name} is asking City Hall for metro access: trains, please!", "small");
            askText.style.whiteSpace = WhiteSpace.Normal;
            askText.style.flexShrink = 1f;
            askText.style.marginLeft = 8f;

            var row = UIK.Row(footer);
            var build = UIK.Button(row, "Build a line here (L)", () =>
            {
                g.FocusDistrict(d.id);
                g.Tools.Select(ToolKind.Line);
            }, "btn-small", "btn-primary");

            refresh = () =>
            {
                pop.value.text = Fmt.Count(d.population);
                jobs.value.text = Fmt.Count(d.jobs);
                cov.value.text = Fmt.Pct(d.Coverage);
                cov.value.EnableInClassList("bad", d.Coverage < 0.15f);
                int n = 0;
                foreach (var s in S.net.AliveStations())
                    if (s.district == d.id && s.lines.Count > 0) n++;
                stations.value.text = n.ToString();
                stations.caption.text = n == 0 ? "stations yet" : n == 1 ? "station" : "stations";
                stations.root.EnableInClassList("tile-empty", n == 0);
                bool growing = d.maturity < 0.99f;
                growText.text = growing ? $"Still growing: {Fmt.Pct(d.maturity)} of its eventual size" : "Fully grown and settled.";
                UIK.Show(growBar, growing);
                UIK.SetBar(growFill, d.maturity);
                UIK.Show(ask, d.Coverage < 0.15f && d.population > 1000f);
            };
        }
    }
}
