using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace Metro.UI
{
    /// <summary>
    /// Top bar: two cream "paper" strips. Left: the city sticker, the alarm clock (with a "Rush hour!"
    /// burst during the rushes), candy speed buttons and the day bar. Right: the headline numbers as stickers.
    /// </summary>
    public sealed class TopBar
    {
        readonly GameRoot g;
        public readonly VisualElement root;
        readonly Label cityName, dayLabel, sizeBadge, clock, period, money, moneySub, riders, ridersSub, sat, satSub, approval, approvalSub;
        readonly IconElement satIcon, hallIcon;
        readonly VisualElement hallBlock, grumpyRing;
        readonly Starburst rush;
        readonly Label pausedChip;
        readonly IconElement coinIcon;
        readonly DayBar dayBar;
        readonly VisualElement[] speedBtns = new VisualElement[4];
        int shownSpeed = -2;

        public TopBar(GameRoot g, VisualElement parent)
        {
            this.g = g;
            root = UIK.Div(parent, "topbar");
            root.pickingMode = PickingMode.Ignore;

            var left = UIK.Row(root, "panel", "topbar-group");
            var cityCol = UIK.Col(left, "city-sticker");
            cityName = UIK.Text(cityCol, "City", "city-name");
            dayLabel = UIK.Text(cityCol, "Day 1 · Mon", "city-sub");
            sizeBadge = UIK.Text(cityCol, "1/6", "size-badge");
            sizeBadge.pickingMode = PickingMode.Ignore;
            UIK.Tooltip(cityCol, () => CityTooltip());

            var clockBox = UIK.Row(left, "clock-sticker");
            var alarm = new IconElement(IconKind.Clock, 30f);
            alarm.style.marginRight = 6f;
            clockBox.Add(alarm);
            var clockCol = UIK.Col(clockBox);
            clock = UIK.Text(clockCol, "05:30", "clock");
            period = UIK.Text(clockCol, "EARLY", "period");
            UIK.Tooltip(clockBox, () => ClockTooltip());
            rush = new Starburst(62f, "RUSH\nHOUR!");
            rush.AddToClassList("rush-burst");
            clockBox.Add(rush);
            UIK.Show(rush, false);

            var speedRow = UIK.Row(left, "speed-row");
            pausedChip = UIK.Text(speedRow, "Paused!", "paused-chip");
            pausedChip.pickingMode = PickingMode.Ignore;
            UIK.Display(pausedChip);
            IconKind[] icons = { IconKind.Pause, IconKind.Play, IconKind.Fast, IconKind.Faster };
            string[] tipNames = { "Pause", "Normal speed", "Fast", "Fastest" };
            BindAction[] tipKeys = { BindAction.Pause, BindAction.Speed1, BindAction.Speed2, BindAction.Speed3 };
            string[] tints = { "c-sun", "c-mint", "c-sky", "c-grape" };
            float[] sizes = { 18f, 18f, 20f, 22f };
            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                speedBtns[i] = UIK.IconButton(speedRow, icons[i], () =>
                {
                    if (idx == 0) g.SetPaused(true);
                    else g.SetSpeed(idx - 1);
                }, null, "speed-btn", sizes[i]);
                UIK.Tooltip(speedBtns[i], () => $"{tipNames[idx]} ({Keybinds.Label(tipKeys[idx])})");
                speedBtns[i].AddToClassList(tints[i]);
                UIK.Ring(speedBtns[i]);
            }

            var barWrap = UIK.Div(left, "daybar-wrap");
            dayBar = new DayBar();
            dayBar.profile = (m, we) => g.State != null ? g.State.demand.CombinedProfile(m, we) : 0f;
            barWrap.Add(dayBar);
            UIK.Tooltip(barWrap, "Demand through the day. The pink stripes are rush hours: mornings flow into the centre, evenings flow back out. The little train is now.");

            UIK.Spacer(root).pickingMode = PickingMode.Ignore;

            var right = UIK.Row(root, "panel", "topbar-group", "topbar-right");
            var mBlock = StatBlock(right, IconKind.Coin, out money, out moneySub, "tilt-l");
            mBlock.name = "money";
            money.AddToClassList("stat-money");
            coinIcon = mBlock.Q<IconElement>();
            mBlock.AddManipulator(new Clickable(() => g.OpenWindow(WindowKind.Finance)));
            UIK.Tooltip(mBlock, () => $"Cash on hand. Click for finances ({Keybinds.Label(BindAction.Finance)}).");
            var rBlock = StatBlock(right, IconKind.People, out riders, out ridersSub, "tilt-r");
            rBlock.AddManipulator(new Clickable(() => g.OpenWindow(WindowKind.Stats)));
            UIK.Tooltip(rBlock, () => $"Trips taken today. Click for statistics ({Keybinds.Label(BindAction.Stats)}).");
            var sBlock = StatBlock(right, IconKind.Smile, out sat, out satSub, "tilt-l", "stat-sun");
            satIcon = sBlock.Q<IconElement>();
            sBlock.AddManipulator(new Clickable(() => g.OpenWindow(WindowKind.Stats)));
            UIK.TooltipBars(sBlock, () => SatTooltip());
            // When riders turn grumpy, a red ring pulses around their face: hover it to see why.
            grumpyRing = UIK.Div(sBlock, "grumpy-ring");
            grumpyRing.pickingMode = PickingMode.Ignore;
            hallBlock = StatBlock(right, IconKind.Hall, out approval, out approvalSub, "tilt-r", "stat-grape");
            hallBlock.name = "approval";
            hallIcon = hallBlock.Q<IconElement>();
            hallBlock.AddManipulator(new Clickable(() => g.OpenWindow(WindowKind.CityHall)));
            UIK.Tooltip(hallBlock, () => HallTooltip());
            UIK.Show(hallBlock, false);

            var menuBtn = UIK.IconButton(right, IconKind.Menu, () => g.ShowPauseMenu(true), "Menu (Esc)", "icon-btn", 30f);
            menuBtn.AddToClassList("icon-btn-lg");
            menuBtn.AddToClassList("c-gum-solid");
            menuBtn.style.marginLeft = 12f;
        }

        static VisualElement StatBlock(VisualElement parent, IconKind icon, out Label value, out Label caption, params string[] classes)
        {
            var b = UIK.Row(parent, "stat-block");
            foreach (var c in classes) b.AddToClassList(c);
            var ic = new IconElement(icon, 38f);
            ic.style.marginRight = 8f;
            b.Add(ic);
            var col = UIK.Col(b);
            value = UIK.Text(col, "—", "stat-value");
            caption = UIK.Text(col, "", "stat-caption");
            return b;
        }

        string CityTooltip()
        {
            var gs = g.State;
            if (gs == null) return "";
            var p = gs.progress;
            var a = gs.city.playArea;
            int open = 0;
            foreach (var d in gs.city.districts)
                if (d.revealed) open++;
            string next = p.Next != null ? $"\nNext size: {p.Next.name} ({p.GoalsDone}/{p.goals.Count} goals done)" : "\nThe whole region is open.";
            string lines = p.MaxLines >= 99 ? "unlimited" : p.MaxLines.ToString();
            return $"{gs.city.name}: {p.Current.name} (size {p.tier + 1} of {ProgressionSystem.Tiers.Length})\n" +
                   $"Map {a.width:0} × {a.height:0} km · {open} of {gs.city.districts.Count} districts open\n" +
                   $"Line licence: {lines}{next}";
        }

        static readonly string[] Grumbles =
        {
            "Shorter waits (more trains) would help most.",
            "Roomier trains (more trains or wagons) would help most.",
            "Less crowded platforms (more trains, bigger stations) would help most.",
            "More reliable service would help most.",
            "A lower ticket price would help most.",
            "Nicer stations (elevators, fewer ads) would help most."
        };

        /// <summary>"Why so grumpy?": the six things riders care about as little bars, and the one to fix first.</summary>
        (string title, (string label, float value)[] rows, string foot) SatTooltip()
        {
            var s = g.State?.satisfaction;
            if (s == null) return ("", null, null);
            var rows = new[]
            {
                ("Waiting time", s.wait), ("Train comfort", s.comfort), ("Platform crowding", s.crowding),
                ("Reliability", s.reliability), ("Ticket price", s.price), ("Amenities", s.amenities)
            };
            int worst = 0;
            for (int i = 1; i < rows.Length; i++)
                if (rows[i].Item2 < rows[worst].Item2) worst = i;
            string title = s.Percent >= 70 ? $"Riders are happy: {s.Percent}%" : $"Why so grumpy? {s.Percent}%";
            string foot = rows[worst].Item2 < 0.85f ? $"Biggest grumble: {rows[worst].Item1.ToLowerInvariant()}. {Grumbles[worst]}" : "Nothing big to grumble about. Keep it up!";
            return (title, rows, foot);
        }

        string ClockTooltip()
        {
            var c = g.State?.clock;
            if (c == null) return "";
            switch (c.Period)
            {
                case DayPeriod.MorningRush: return "Morning rush\nEverybody heads to work until 09:30.";
                case DayPeriod.EveningRush: return "Evening rush\nEverybody heads home until 19:00.";
                case DayPeriod.EarlyMorning: return c.IsWeekend ? "Early bird\nNo rush hours at the weekend." : "Early bird\nThe morning rush starts at 07:00.";
                case DayPeriod.Midday: return c.IsWeekend ? "Weekend!\nNo rush hours today: a calm, steady day." : "Midday\nThe next rush starts at 16:30.";
                case DayPeriod.Evening: return "Evening\nThings wind down; service stops at 01:00.";
                default: return "Night · zzz\nTrains run again at 05:00.";
            }
        }

        public void Refresh()
        {
            var gs = g.State;
            if (gs == null) return;
            var c = gs.clock;
            cityName.text = gs.city.name;
            dayLabel.text = $"{gs.progress.Current.name} · Day {c.DayNumber} · {c.DayName}";
            sizeBadge.text = $"{gs.progress.tier + 1}/{ProgressionSystem.Tiers.Length}";
            clock.text = c.TimeString;
            var per = c.Period;
            string pt;
            switch (per)
            {
                case DayPeriod.MorningRush: pt = "MORNING RUSH"; break;
                case DayPeriod.EveningRush: pt = "EVENING RUSH"; break;
                case DayPeriod.EarlyMorning: pt = "EARLY BIRD"; break;
                case DayPeriod.Midday: pt = c.IsWeekend ? "WEEKEND!" : "MIDDAY"; break;
                case DayPeriod.Evening: pt = "EVENING"; break;
                default: pt = "NIGHT · ZZZ"; break;
            }
            period.text = pt;
            bool isRush = per == DayPeriod.MorningRush || per == DayPeriod.EveningRush;
            period.EnableInClassList("period-rush", isRush);
            UIK.Show(rush, isRush);
            if (isRush) rush.style.rotate = new Rotate(12f + Mathf.Sin(Time.unscaledTime * 3f) * 5f);
            dayBar.SetTime(c.MinuteOfDay, c.IsWeekend);

            UIK.Show(pausedChip, g.Paused);
            if (g.Paused) pausedChip.style.rotate = new Rotate(Mathf.Sin(Time.unscaledTime * 4f) * 4f - 6f);
            int speed = g.Paused ? 0 : g.SpeedIndex + 1;
            if (speed != shownSpeed)
            {
                shownSpeed = speed;
                for (int i = 0; i < 4; i++) speedBtns[i].EnableInClassList("speed-btn-on", i == speed);
            }

            var e = gs.econ;
            money.text = Fmt.Money(e.money);
            money.EnableInClassList("bad", e.money < 0);
            float net = e.today.OperatingNet;
            bool red = e.money < 0;
            moneySub.text = red ? "in the red!" : Fmt.MoneySigned(net) + " today";
            moneySub.EnableInClassList("good", !red && net > 0);
            moneySub.EnableInClassList("bad", red || net < 0);
            if (coinIcon != null) coinIcon.Color = red ? Doodle.Tomato : Doodle.Coin;

            riders.text = Fmt.Count(gs.stats.ridersToday);
            ridersSub.text = $"trips today · {Fmt.Count(gs.stats.HourEntries)}/h";

            int sp = gs.satisfaction.Percent;
            sat.text = sp + "%";
            float d = gs.satisfaction.target - gs.satisfaction.score;
            string mood = sp >= 80 ? "happy" : sp >= 65 ? "happy-ish" : sp >= 45 ? "meh" : sp >= 30 ? "grumpy" : "furious";
            satSub.text = mood + (d > 0.01f ? " ↑" : d < -0.01f ? " ↓" : "");
            sat.style.color = sp >= 45 ? Doodle.Ink : Doodle.Bad;
            bool grumpy = sp < 45;
            UIK.Show(grumpyRing, grumpy);
            if (grumpy)
            {
                float k = 1f + 0.035f * Mathf.Sin(Time.unscaledTime * 4f);
                grumpyRing.style.scale = new Scale(new Vector3(k, k, 1f));
            }
            if (satIcon != null)
            {
                satIcon.Color = sp >= 70 ? Doodle.MoodGood : sp >= 55 ? Doodle.MoodOk : sp >= 40 ? Doodle.MoodMeh : Doodle.MoodBad;
                satIcon.Mood = Mathf.Clamp((sp - 55f) / 25f, -1f, 1f);
            }

            var gov = gs.gov;
            UIK.Show(hallBlock, gov.started);
            if (gov.started)
            {
                approval.text = $"{gov.approval:0}%";
                int pending = 0;
                foreach (var r in gov.open)
                    if (r.state == RequestState.Proposed) pending++;
                approvalSub.text = gov.ultimatum ? "FINAL WARNING" : pending > 0 ? $"{pending} new request{(pending > 1 ? "s" : "")}" : "City Hall";
                approvalSub.EnableInClassList("bad", gov.ultimatum);
                approval.style.color = gov.approval >= 35f ? Doodle.Ink : Doodle.Bad;
                if (hallIcon != null) hallIcon.Color = gov.approval >= 60f ? Doodle.Grape : gov.approval >= 35f ? Doodle.Tang : Doodle.Tomato;
            }
        }

        string HallTooltip()
        {
            var gov = g.State?.gov;
            if (gov == null) return "";
            string permits = gov.PermitFactor < 1f ? $"permits fast-tracked (−{Fmt.Pct(1f - gov.PermitFactor)} construction)"
                : gov.PermitFactor > 1f ? $"red tape (+{Fmt.Pct(gov.PermitFactor - 1f)} construction)" : "permits at normal cost";
            return $"City Hall approval {gov.approval:0}% · {gov.Standing}\nMayor {gov.mayor.name} ({GovernmentSystem.AgendaName(gov.mayor.agenda)})\n" +
                   $"Subsidy ×{gov.SubsidyFactor:0.00} · {permits}\nClick for City Hall (M).";
        }
    }

    /// <summary>
    /// Left tool palette with tools, data overlays and windows: round candy buttons with a hotkey badge.
    /// Locked items turn to sand and wear a padlock instead.
    /// </summary>
    public sealed class Toolbar
    {
        readonly GameRoot g;
        public readonly VisualElement root;
        readonly Dictionary<ToolKind, VisualElement> toolBtns = new Dictionary<ToolKind, VisualElement>();
        readonly Dictionary<DataOverlay, VisualElement> overlayBtns = new Dictionary<DataOverlay, VisualElement>();
        readonly Dictionary<Feature, List<VisualElement>> featureBtns = new Dictionary<Feature, List<VisualElement>>();
        readonly List<(VisualElement btn, Feature f, VisualElement badge, VisualElement key)> lockables = new List<(VisualElement, Feature, VisualElement, VisualElement)>();
        readonly VisualElement overviewBtn;
        readonly List<(Label badge, BindAction action)> keyBadges = new List<(Label, BindAction)>();
        ToolKind shownTool = (ToolKind)(-1);
        DataOverlay shownOverlay = (DataOverlay)(-1);
        bool shownOverview;
        int shownTier = -1;
        GameState shownState;

        public Toolbar(GameRoot g, VisualElement parent)
        {
            this.g = g;
            root = UIK.Div(parent, "panel", "toolbar");
            Tool(ToolKind.Select, IconKind.Select, BindAction.Inspect, "c-sky", "Inspect", "Click stations, lines and trains to see details.");
            Tool(ToolKind.Line, IconKind.Line, BindAction.Line, "c-gum", "Build line", "Click to place stations; click existing stations to connect. Right-click or Enter to finish.");
            Tool(ToolKind.Plan, IconKind.Pencil, BindAction.Plan, "c-sun", "Plan in pencil", "Sketch future lines across the whole city, even out in the clouds. Free, and no trains run on pencil.");
            Tool(ToolKind.Insert, IconKind.Insert, BindAction.Insert, "c-mint", "Add station to a line", "Click on a track to insert a station.");
            Tool(ToolKind.Depot, IconKind.Depot, BindAction.Depot, "c-tang", "Build depot", "Trains are stored at depots. Click a station.");
            Tool(ToolKind.Demolish, IconKind.Demolish, BindAction.Demolish, "c-tomato", "Demolish", "Remove a station or depot.");
            Separator();
            Overlay(DataOverlay.Population, IconKind.Population, BindAction.Population, "Population density", "Where people live.");
            Overlay(DataOverlay.Jobs, IconKind.Jobs, BindAction.Jobs, "Jobs density", "Where people work.");
            Overlay(DataOverlay.Coverage, IconKind.Coverage, BindAction.Coverage, "Coverage", "Green: within walking distance of a served station. Red: no access.");
            Separator();
            overviewBtn = UIK.IconButton(root, IconKind.Overview, () => g.ToggleOverview(), null, "icon-btn", 26f);
            overviewBtn.AddToClassList("c-sky");
            UIK.Ring(overviewBtn);
            KeyBadge(overviewBtn, BindAction.Overview);
            UIK.Tooltip(overviewBtn, () => $"Zoom out to the whole map ({Keybinds.Label(BindAction.Overview)})");
            Separator();
            Window(WindowKind.Finance, IconKind.Finance, Feature.Finance, BindAction.Finance, "c-sun", "Finances & fares");
            Window(WindowKind.Stats, IconKind.Stats, Feature.Stats, BindAction.Stats, "c-gum", "Statistics");
            Window(WindowKind.City, IconKind.City, Feature.CityWindow, BindAction.City, "c-mint", "City overview");
            Window(WindowKind.CityHall, IconKind.Hall, Feature.Council, BindAction.CityHall, "c-grape", "City Hall: the mayor, approval and requests");
        }

        /// <summary>The hotkey badge follows the player's key bindings (Settings → Controls).</summary>
        Label KeyBadge(VisualElement b, BindAction action)
        {
            var badge = UIK.KeyBadge(b, Keybinds.Label(action));
            keyBadges.Add((badge, action));
            return badge;
        }

        void Separator()
        {
            var s = new Squiggle();
            s.AddToClassList("tool-sep");
            root.Add(s);
        }

        VisualElement Button(IconKind icon, System.Action onClick, string tint)
        {
            var b = UIK.IconButton(root, icon, onClick, null, "icon-btn", 26f);
            b.AddToClassList(tint);
            UIK.Ring(b);
            return b;
        }

        void Lockable(VisualElement b, Feature f, BindAction key, string name, string body)
        {
            var keyBadge = KeyBadge(b, key);
            var badge = UIK.Div(b, "lock-badge");
            badge.pickingMode = PickingMode.Ignore;
            badge.Add(new IconElement(IconKind.Lock, 14f));
            UIK.Show(badge, false);
            lockables.Add((b, f, badge, keyBadge));
            if (!featureBtns.TryGetValue(f, out var list)) featureBtns[f] = list = new List<VisualElement>();
            list.Add(b);
            UIK.Tooltip(b, () =>
            {
                string title = $"{name} ({Keybinds.Label(key)})";
                return g.State != null && !g.State.Has(f)
                    ? $"{title}\nLocked: unlocks when your city becomes a {ProgressionSystem.Tiers[ProgressionSystem.UnlockTier(f)].name}."
                    : body != null ? $"{title}\n{body}" : title;
            });
        }

        void Tool(ToolKind k, IconKind icon, BindAction key, string tint, string name, string body)
        {
            var b = Button(icon, () => g.Tools.Select(k), tint);
            b.name = "tool-" + k.ToString().ToLowerInvariant();
            toolBtns[k] = b;
            var f = ToolController.FeatureFor(k);
            if (f.HasValue) Lockable(b, f.Value, key, name, body);
            else
            {
                KeyBadge(b, key);
                UIK.Tooltip(b, () => $"{name} ({Keybinds.Label(key)})\n{body}");
            }
        }

        void Overlay(DataOverlay o, IconKind icon, BindAction key, string title, string body)
        {
            var b = Button(icon, () => g.ToggleOverlay(o), "c-white");
            b.AddToClassList("overlay-btn");
            b.name = "overlay-" + o.ToString().ToLowerInvariant();
            overlayBtns[o] = b;
            Lockable(b, Feature.Overlays, key, title, body);
        }

        void Window(WindowKind w, IconKind icon, Feature f, BindAction key, string tint, string title)
        {
            var b = Button(icon, () => g.OpenWindow(w), tint);
            b.name = "window-" + w.ToString().ToLowerInvariant();
            Lockable(b, f, key, title, null);
        }

        static void SetOn(VisualElement b, bool on) => b.EnableInClassList("icon-btn-on", on);

        public VisualElement ButtonFor(ToolKind k) => toolBtns.TryGetValue(k, out var b) ? b : null;

        public List<VisualElement> ElementsFor(Feature f) => featureBtns.TryGetValue(f, out var l) ? l : null;

        public void Refresh()
        {
            foreach (var (badge, action) in keyBadges)
            {
                string k = Keybinds.Label(action);
                if (badge.text != k) badge.text = k;
            }
            var gs = g.State;
            if (gs != null && (gs.progress.tier != shownTier || gs != shownState))
            {
                shownTier = gs.progress.tier;
                shownState = gs;
                foreach (var (btn, f, badge, key) in lockables)
                {
                    bool locked = !gs.Has(f);
                    UIK.Show(badge, locked);
                    UIK.Show(key, !locked);
                    btn.EnableInClassList("icon-btn-locked", locked);
                    var ic = UIK.IconOf(btn);
                    if (ic != null) ic.style.opacity = locked ? 0.35f : 1f;
                }
            }
            if (g.Tools.Active != shownTool)
            {
                shownTool = g.Tools.Active;
                foreach (var kv in toolBtns) SetOn(kv.Value, kv.Key == shownTool);
            }
            if (g.Overlay != shownOverlay)
            {
                shownOverlay = g.Overlay;
                foreach (var kv in overlayBtns) SetOn(kv.Value, kv.Key == shownOverlay);
            }
            bool ov = g.CamCtl.InOverview;
            if (ov != shownOverview)
            {
                shownOverview = ov;
                SetOn(overviewBtn, ov);
            }
        }
    }

    /// <summary>Bottom-left row of gumball line chips plus a "new line" button.</summary>
    public sealed class LinesBar
    {
        readonly GameRoot g;
        public readonly VisualElement root;
        readonly VisualElement chips, ghost;
        readonly Label count, empty, ghostText;
        readonly DashedDisc ghostDisc;
        readonly VisualElement add;
        string signature = "";
        readonly List<(int id, VisualElement chip)> chipList = new List<(int, VisualElement)>();
        int shownSelected = -2;

        public LinesBar(GameRoot g, VisualElement parent)
        {
            this.g = g;
            root = UIK.Div(parent, "panel", "linesbar");
            UIK.Text(root, "Lines:", "lines-title");
            count = UIK.Text(root, "", "lines-count");
            empty = UIK.Text(root, "No lines yet!", "lines-empty");
            UIK.Tooltip(count, () => g.State != null ? LicenceText(g.State) : "");
            chips = UIK.Row(root);
            // While a new line is being drawn: a dashed gumball in its colour, waiting to be filled in.
            ghost = UIK.Row(root, "line-ghost");
            ghostDisc = new DashedDisc(44f);
            ghost.Add(ghostDisc);
            ghostText = UIK.Text(ghost, "", "line-ghost-text");
            ghost.pickingMode = PickingMode.Ignore;
            UIK.Show(ghost, false);
            add = UIK.Button(root, "+ New line", () => g.Tools.Select(ToolKind.Line), "btn-small");
            add.name = "new-line";
            add.style.marginLeft = 4f;
            UIK.Tooltip(add, () => g.State?.build.LineLicenceProblem() ?? $"Draw a new line ({Keybinds.Label(BindAction.Line)})");
        }

        static string LicenceText(GameState gs)
        {
            var p = gs.progress;
            if (p.MaxLines >= 99) return "Line licence: unlimited.";
            string next = p.Next != null ? $" A {p.Next.name} may run {p.Next.maxLines}." : "";
            return $"Line licence: {p.MaxLines} line{(p.MaxLines == 1 ? "" : "s")}.{next}";
        }

        public void Refresh()
        {
            var gs = g.State;
            if (gs == null) return;
            int max = gs.progress.MaxLines, n = gs.net.LineCount;
            count.text = max >= 99 ? "" : $"{n}/{max}";
            add.EnableInClassList("btn-dim", n >= max);
            bool drawing = g.Tools.Active == ToolKind.Line && !g.Tools.Line.IsExtending && g.Tools.Line.PlannedCount > 0;
            UIK.Show(ghost, drawing);
            if (drawing)
            {
                ghostDisc.Fill = LinePalette.Colors[g.Tools.Line.ColorIndex % LinePalette.Count];
                ghostText.text = n == 0 ? "your first line, almost!" : "almost!";
            }
            // No lines yet: the New line button gets big and wiggly.
            UIK.Show(empty, n == 0 && !drawing);
            add.EnableInClassList("btn-primary", n == 0);
            add.style.rotate = n == 0 ? new Rotate(Mathf.Sin(Time.unscaledTime * 3f) * 3f) : new Rotate(0f);
            var sb = new StringBuilder();
            foreach (var l in gs.net.AliveLines()) sb.Append(l.id).Append(':').Append(l.colorIndex).Append(',');
            string sig = sb.ToString();
            if (sig != signature)
            {
                signature = sig;
                chips.Clear();
                chipList.Clear();
                foreach (var l in gs.net.AliveLines())
                {
                    var line = l;
                    var chip = UIK.Div(chips, "line-chip");
                    chip.style.backgroundColor = (Color)l.Color;
                    var lab = UIK.Text(chip, l.Letter, "line-chip-label");
                    lab.pickingMode = PickingMode.Ignore;
                    // A gumball: a glint of light, and a sunny ring when it's the selected line.
                    UIK.Div(chip, "chip-shine").pickingMode = PickingMode.Ignore;
                    UIK.Div(chip, "chip-ring-outer").pickingMode = PickingMode.Ignore;
                    UIK.Div(chip, "chip-ring").pickingMode = PickingMode.Ignore;
                    chip.AddManipulator(new Clickable(() =>
                    {
                        Sfx.Click();
                        g.Select(SelKind.Line, line.id);
                    }));
                    UIK.Tooltip(chip, () => $"{line.name}\n{line.stops.Count} stations · {line.trains.Count} trains\n{Fmt.Count(line.boardingsToday)} boardings today");
                    chipList.Add((l.id, chip));
                }
                shownSelected = -2;
            }
            int sel = g.Sel.kind == SelKind.Line ? g.Sel.id : -1;
            if (sel != shownSelected)
            {
                shownSelected = sel;
                foreach (var (id, chip) in chipList) chip.EnableInClassList("line-chip-on", id == sel);
            }
        }
    }

    /// <summary>
    /// A sticky note pinned to the screen: what it takes for the city to grow to its next size (with candy
    /// progress tubes), and City Hall's targets once the city is big enough for them: what's at stake, and
    /// how the last ones went.
    /// </summary>
    public sealed class GoalsWidget
    {
        static readonly Color GrowColor = Doodle.Tang;
        static readonly Color DoneColor = Doodle.Mint;
        static readonly Color OpenColor = Doodle.Paper;

        readonly GameRoot g;
        public readonly VisualElement root;
        readonly Label growHead, growCount, growBlurb, skipTime, noTargets;
        readonly VisualElement growList, targetsBox, list, skipRow, skipBtn, skipStrip, shelf;
        readonly IconElement skipClock;
        string growSig = "", signature = "";
        readonly List<(Goal goal, IconElement check, Label title, Label pct, VisualElement bar, VisualElement fill, Label meta)> growRows =
            new List<(Goal, IconElement, Label, Label, VisualElement, VisualElement, Label)>();
        readonly List<RequestView.Handle> rows = new List<RequestView.Handle>();

        public GoalsWidget(GameRoot g, VisualElement parent)
        {
            this.g = g;
            root = UIK.Div(parent, "panel", "goals");
            root.name = "goals";
            var pin = new Pushpin();
            pin.AddToClassList("goals-pin");
            root.Add(pin);
            var head = UIK.Row(root, "goals-head");
            var star = new IconElement(IconKind.Star, 20f);
            star.style.marginRight = 6f;
            head.Add(star);
            growHead = UIK.Text(head, "", "caps", "goals-title");
            growHead.style.flexShrink = 1f;
            growHead.style.whiteSpace = WhiteSpace.Normal;
            UIK.Spacer(head);
            growCount = UIK.Text(head, "", "goal-count");
            growBlurb = UIK.Text(root, "", "goal-meta");
            growBlurb.style.whiteSpace = WhiteSpace.Normal;
            growBlurb.style.marginTop = 3f;
            skipRow = UIK.Row(root, "mt-4");
            skipBtn = UIK.Button(skipRow, "Skip to dawn", () => g.SkipToDawn(), "btn-small", "btn-primary");
            skipBtn.name = "skip-to-dawn";
            var sun = new IconElement(IconKind.Sunrise, 20f);
            sun.style.marginLeft = 6f;
            skipBtn.Add(sun);
            UIK.Tooltip(skipBtn, () => "Runs the clock fast until 05:00, when the town grows. Pause or pick a speed to stop.");
            // While skipping: a little night strip with a whizzing clock.
            skipStrip = UIK.Row(skipRow, "skip-strip");
            skipClock = new IconElement(IconKind.Clock, 18f);
            skipStrip.Add(skipClock);
            skipTime = UIK.Text(skipStrip, "", "skip-strip-text");
            UIK.Show(skipRow, false);
            growList = UIK.Col(root);
            // At the top size: a shelf with all six sizes and a trophy.
            shelf = UIK.Row(root, "trophy-shelf", "mt-8");
            for (int i = 0; i < ProgressionSystem.Tiers.Length; i++)
            {
                var pip = UIK.Text(shelf, (i + 1).ToString(), "shelf-pip");
                UIK.Display(pip);
            }
            shelf.Add(new IconElement(IconKind.Trophy, 28f));
            UIK.Text(shelf, "all grown up!", "shelf-text");
            UIK.Show(shelf, false);

            targetsBox = UIK.Col(root);
            targetsBox.name = "city-hall-requests";
            UIK.Div(targetsBox, "divider");
            var th = UIK.Row(targetsBox);
            var hall = new IconElement(IconKind.Hall, 20f);
            hall.style.marginRight = 6f;
            th.Add(hall);
            UIK.Text(th, "CITY HALL", "caps");
            UIK.Spacer(th);
            var more = UIK.Text(th, $"open ({Keybinds.Label(BindAction.CityHall)})", "tiny", "accent");
            more.AddManipulator(new Clickable(() => g.OpenWindow(WindowKind.CityHall)));
            more.schedule.Execute(() => more.text = $"open ({Keybinds.Label(BindAction.CityHall)})").Every(1000);
            // The requests scroll inside the note rather than pushing it down over the lines bar.
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.AddToClassList("goals-list");
            targetsBox.Add(scroll);
            list = scroll;
            noTargets = UIK.Text(targetsBox, "zzz… No targets right now. City Hall will be in touch.", "goal-meta", "mt-4");
            noTargets.style.whiteSpace = WhiteSpace.Normal;
            root.pickingMode = PickingMode.Position;
        }

        static string UnlockSummary(TierDef t)
        {
            var parts = new List<string> { "a bigger map", t.maxLines >= 99 ? "unlimited lines" : $"{t.maxLines} lines" };
            foreach (var f in t.unlocks) parts.Add(ProgressionSystem.FeatureTitle(f).ToLowerInvariant());
            return string.Join(", ", parts);
        }

        /// <summary>Goals that are only judged when the day ends wear a little moon.</summary>
        static bool JudgedAtDayEnd(GoalKind k) => k == GoalKind.DailyProfit || k == GoalKind.DaySatisfaction;

        void RefreshGrowth(GameState gs)
        {
            var p = gs.progress;
            string sig = p.tier + ":" + p.goals.Count + ":" + gs.GetHashCode();
            if (sig != growSig)
            {
                growSig = sig;
                growList.Clear();
                growRows.Clear();
                if (p.AtDemoEnd) growHead.text = "DEMO FINALE: GROW INTO A " + p.Next.name.ToUpperInvariant();
                else if (p.Next != null) growHead.text = "GROW INTO A " + p.Next.name.ToUpperInvariant();
                else
                {
                    growHead.text = p.Current.name.ToUpperInvariant();
                    growBlurb.text = "The whole region is open. Keep millions moving.";
                }
                foreach (var goal in p.goals)
                {
                    var row = UIK.Col(growList, "goal");
                    var tr = UIK.Row(row);
                    tr.style.alignItems = Align.FlexStart;
                    var check = new IconElement(IconKind.Station, 18f) { Color = OpenColor };
                    check.style.marginRight = 7f;
                    tr.Add(check);
                    var title = UIK.Text(tr, goal.title, "goal-title");
                    // A zero basis makes the layout measure the wrapped height at the width it really gets.
                    title.style.flexBasis = 0f;
                    title.style.flexShrink = 1f;
                    title.style.flexGrow = 1f;
                    title.style.whiteSpace = WhiteSpace.Normal;
                    if (JudgedAtDayEnd(goal.kind))
                    {
                        var moon = new IconElement(IconKind.Moon, 16f);
                        moon.style.marginLeft = 4f;
                        tr.Add(moon);
                        UIK.Tooltip(moon, "Judged when the day ends, at midnight.");
                    }
                    var pct = UIK.Text(tr, "", "goal-count");
                    var (bar, fill) = UIK.Bar(row, GrowColor);
                    bar.AddToClassList("goal-bar");
                    var meta = UIK.Text(row, "", "goal-meta", "goal-indent");
                    if (goal.districtId >= 0)
                    {
                        var gg = goal;
                        title.AddToClassList("goal-link");
                        row.AddManipulator(new Clickable(() => g.FocusDistrict(gg.districtId)));
                        UIK.Tooltip(row, "Click to fly there.");
                    }
                    growRows.Add((goal, check, title, pct, bar, fill, meta));
                }
            }
            growCount.text = p.Next != null ? $"{p.GoalsDone}/{p.goals.Count}" : "";
            bool ready = p.Next != null && p.Ready;
            root.EnableInClassList("goals-ready", ready);
            UIK.Show(shelf, p.Next == null);
            if (p.Next != null)
            {
                string blurb;
                if (ready)
                {
                    int m = Mathf.FloorToInt(gs.clock.MinuteOfDay);
                    int until = m < Balance.NightEnd ? Balance.NightEnd - m : Balance.MinutesPerDay - m + Balance.NightEnd;
                    blurb = p.AtDemoEnd
                        ? $"All goals met! The demo ends at dawn (in ~{Mathf.Max(1, Mathf.CeilToInt(until / 60f))}h)."
                        : $"All goals met! {gs.city.name} grows into a {p.Next.name} at dawn (in ~{Mathf.Max(1, Mathf.CeilToInt(until / 60f))}h).";
                }
                else if (p.AtDemoEnd) blurb = "The last goals of the demo. Meet them all and the demo ends at the next dawn.";
                else blurb = "Unlocks " + UnlockSummary(p.Next) + $". Meet every goal and the {(p.tier <= 2 ? "town" : "city")} grows overnight.";
                if (growBlurb.text != blurb) growBlurb.text = blurb;
                growBlurb.EnableInClassList("good", ready);
            }
            bool canSkip = ready && !p.holdTierUps;
            UIK.Show(skipRow, canSkip);
            if (canSkip)
            {
                bool skipping = g.SkippingToDawn;
                UIK.Show(skipBtn, !skipping);
                UIK.Show(skipStrip, skipping);
                if (skipping)
                {
                    skipClock.style.rotate = new Rotate(Time.unscaledTime * 540f % 360f);
                    skipTime.text = "Skipping to dawn… " + gs.clock.TimeString;
                }
            }
            foreach (var (goal, check, title, pct, bar, fill, meta) in growRows)
            {
                check.Kind = goal.done ? IconKind.Check : IconKind.Station;
                check.Color = goal.done ? DoneColor : OpenColor;
                UIK.SetBar(fill, goal.progress);
                UIK.Show(bar, !goal.done);
                pct.text = goal.done ? "" : Mathf.FloorToInt(Mathf.Clamp01(goal.progress) * 100f) + "%";
                string t = goal.done ? "<s>" + goal.title + "</s>" : goal.title;
                if (title.text != t) title.text = t;
                meta.text = goal.done ? "" : goal.status;
                UIK.Show(meta, !goal.done);
                title.EnableInClassList("faint", goal.done);
            }
        }

        public void Refresh()
        {
            var gs = g.State;
            if (gs == null) return;
            RefreshGrowth(gs);
            var gov = gs.gov;
            var open = gov.open;
            var sb = new StringBuilder();
            foreach (var r in open) sb.Append(r.id).Append(':').Append((int)r.state).Append(r.paid ? "p" : "").Append(',');
            sb.Append('|').Append(gov.history.Count).Append('|').Append(gs.clock.Day);
            string sig = sb.ToString();
            int recentCount = 0;
            if (sig != signature)
            {
                signature = sig;
                list.Clear();
                rows.Clear();
                // Proposals waiting for an answer first, then the work in progress (at most three in the note).
                var ordered = new List<CouncilRequest>(open);
                ordered.Sort((a, b) => (a.state == RequestState.Proposed ? 0 : 1).CompareTo(b.state == RequestState.Proposed ? 0 : 1));
                for (int i = 0; i < ordered.Count && i < 3; i++) rows.Add(RequestView.Build(g, list, ordered[i], true));
                if (ordered.Count > 3) UIK.Text(list, $"+{ordered.Count - 3} more in City Hall ({Keybinds.Label(BindAction.CityHall)})", "tiny", "faint");
                // Targets met or missed in the last day stay on the note as rubber stamps.
                double now = gs.clock.totalMinutes;
                for (int i = gov.history.Count - 1; i >= 0; i--)
                {
                    var r = gov.history[i];
                    if (now - r.resolvedAt > Balance.MinutesPerDay) break;
                    if (r.state != RequestState.Done && r.state != RequestState.Failed) continue;
                    bool met = r.state == RequestState.Done;
                    var row = UIK.Row(list, "goal-outcome");
                    var stamp = UIK.Text(row, met ? "MET" : "MISSED", "stamp", met ? "stamp-met" : "stamp-missed");
                    UIK.Display(stamp);
                    var col = UIK.Col(row);
                    col.style.flexShrink = 1f;
                    var t = UIK.Text(col, r.title, "goal-meta");
                    t.style.whiteSpace = WhiteSpace.Normal;
                    string money = met ? (r.reward > 0f ? "grant paid +" + Fmt.Money(r.reward) : "approval +" + r.approvalReward)
                                       : (r.fine > 0f ? "fined −" + Fmt.Money(r.fine) : "approval −" + r.approvalPenalty);
                    UIK.Text(col, money, "tiny", met ? "good" : "bad");
                    recentCount++;
                }
                UIK.Show(noTargets, open.Count == 0 && recentCount == 0);
            }
            UIK.Show(targetsBox, gov.started);
            UIK.Show(root, !g.HideHudForTutorial);
            foreach (var h in rows) RequestView.Update(gs, h);
        }
    }
}
