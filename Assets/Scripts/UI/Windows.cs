using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Metro.UI
{
    public abstract class WindowBase
    {
        protected readonly GameRoot g;
        public readonly VisualElement backdrop;
        protected readonly VisualElement win;
        protected readonly VisualElement body;
        float timer;

        static readonly (WindowKind kind, string label, BindAction key, Feature feature, string tint)[] TabDefs =
        {
            (WindowKind.Finance, "Finances", BindAction.Finance, Feature.Finance, "tab-sun"),
            (WindowKind.Stats, "Statistics", BindAction.Stats, Feature.Stats, "tab-gum"),
            (WindowKind.City, "City", BindAction.City, Feature.CityWindow, "tab-mint"),
            (WindowKind.CityHall, "City Hall", BindAction.CityHall, Feature.Council, "tab-grape"),
        };

        protected WindowBase(GameRoot g, VisualElement layer, string title, float width = 880f, string tint = "head-sky", WindowKind kind = WindowKind.None)
        {
            this.g = g;
            backdrop = UIK.Div(layer, "modal-backdrop");
            backdrop.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.target == backdrop) g.CloseWindow();
            });
            win = UIK.Div(backdrop, "panel", "window");
            win.style.width = width;
            var head = UIK.Div(win, "window-head", tint);
            UIK.Text(head, title, "h1");
            UIK.Spacer(head);
            UIK.IconButton(head, IconKind.Close, () => g.CloseWindow(), "Close (Esc)", "icon-btn", 20f);
            if (kind != WindowKind.None) Tabs(kind);
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.style.flexShrink = 1f;
            win.Add(scroll);
            body = scroll.contentContainer;
        }

        /// <summary>Folder tabs above the window to hop between Finances, Statistics, City and City Hall.</summary>
        void Tabs(WindowKind current)
        {
            var tabs = UIK.Row(win, "window-tabs");
            tabs.pickingMode = PickingMode.Ignore;
            foreach (var (kind, label, key, feature, tint) in TabDefs)
            {
                bool on = kind == current;
                bool locked = g.State != null && !g.State.Has(feature);
                var tab = UIK.Row(tabs, "window-tab", tint);
                tab.EnableInClassList("window-tab-on", on);
                tab.EnableInClassList("window-tab-locked", locked);
                var l = UIK.Text(tab, label, "window-tab-label");
                UIK.Display(l);
                if (locked) tab.Add(new IconElement(IconKind.Lock, 15f));
                else UIK.Text(tab, Keybinds.Label(key), "window-tab-key");
                if (on) continue;
                var k = kind;
                tab.AddManipulator(new Clickable(() =>
                {
                    Sfx.Click();
                    g.OpenWindow(k);
                }));
                if (locked)
                    UIK.Tooltip(tab, $"{label}: unlocks when your city becomes a {ProgressionSystem.Tiers[ProgressionSystem.UnlockTier(feature)].name}.");
            }
        }

        protected GameState S => g.State;

        public void Tick(float dt)
        {
            timer -= dt;
            if (timer > 0f) return;
            timer = 0.3f;
            Refresh();
        }

        protected abstract void Refresh();

        public void Close() => backdrop.RemoveFromHierarchy();

        protected static VisualElement Card(VisualElement parent, string caps = null)
        {
            var c = UIK.Col(parent, "card", "mt-8");
            if (caps != null) UIK.Text(c, caps, "caps");
            return c;
        }

        protected static Label Hint(VisualElement parent, string text)
        {
            var l = UIK.Text(parent, text, "small", "muted", "mt-4");
            l.style.whiteSpace = WhiteSpace.Normal;
            return l;
        }

        /// <summary>A small legend entry: a colour swatch (optionally striped-looking) and a word.</summary>
        protected static void Legend(VisualElement row, Color c, string text, bool striped = false)
        {
            var sw = UIK.Div(row, "legend-swatch");
            sw.style.backgroundColor = c;
            sw.EnableInClassList("legend-striped", striped);
            var l = UIK.Text(row, text, "tiny", "muted");
            l.style.marginRight = 12f;
        }

        /// <summary>A gumball letter for a line, as on the lines bar.</summary>
        protected static Label LineBadge(VisualElement parent, MetroLine l)
        {
            var b = UIK.Text(parent, l.Letter, "line-badge");
            b.style.backgroundColor = (Color)l.Color;
            UIK.Display(b);
            return b;
        }
    }

    // ====================================================================== Finance

    public sealed class FinanceWindow : WindowBase
    {
        /// <summary>Riders start to grumble about fares above this (the hint and the slider's red zone).</summary>
        const float FareGrumble = 3f;

        readonly Label cash, loanLabel, limitLabel, fareValue, fareHint, surchargeValue, adHint, running, bankNote, ledgerFoot;
        readonly Slider fare, surcharge;
        readonly List<(LedgerCat cat, Label name, Label today, Label yesterday)> rows = new List<(LedgerCat, Label, Label, Label)>();
        readonly Label netToday, netYesterday, grantTag;
        readonly DayBars chart;
        bool updatingControls;

        public FinanceWindow(GameRoot g, VisualElement layer) : base(g, layer, "Finances", 880f, "head-sun", WindowKind.Finance)
        {
            var cols = UIK.Row(body);
            cols.style.alignItems = Align.FlexStart;
            var left = UIK.Col(cols);
            left.style.width = Length.Percent(48);
            left.style.marginRight = 16f;
            var right = UIK.Col(cols);
            right.style.flexGrow = 1f;

            var cashCard = Card(left, "CASH");
            var cashRow = UIK.Row(cashCard, "mt-4");
            var piggy = new IconElement(IconKind.Finance, 56f);
            piggy.style.marginRight = 10f;
            cashRow.Add(piggy);
            var cashCol = UIK.Col(cashRow);
            cashCol.style.flexShrink = 1f;
            cash = UIK.Text(cashCol, "", "cash-value");
            UIK.Display(cash);
            loanLabel = UIK.Text(cashCol, "", "small");
            loanLabel.style.whiteSpace = WhiteSpace.Normal;
            limitLabel = UIK.Text(cashCard, "", "small", "muted", "mt-4");
            bankNote = UIK.Text(cashCard, "", "small", "bank-note", "mt-4");
            bankNote.style.whiteSpace = WhiteSpace.Normal;
            UIK.Show(bankNote, false);
            if (S.Has(Feature.Loans))
            {
                var loanRow = UIK.Row(cashCard, "mt-8");
                UIK.Button(loanRow, $"Borrow {Fmt.Money(Balance.LoanStep)}", () =>
                {
                    if (!S.econ.Borrow()) Note("The bank won't lend more right now.");
                    else
                    {
                        Sfx.Coin();
                        Note(null);
                    }
                }, "btn-small", "btn-primary").style.marginRight = 6f;
                UIK.Button(loanRow, $"Repay {Fmt.Money(Balance.LoanStep)}", () =>
                {
                    if (!S.econ.Repay()) Note("Nothing to repay, or not enough cash.");
                    else Note(null);
                }, "btn-small");
            }
            else
            {
                UIK.Show(loanLabel, false);
                UIK.Show(limitLabel, false);
                Hint(cashCard, "Loans unlock when your city becomes a " + ProgressionSystem.Tiers[ProgressionSystem.UnlockTier(Feature.Loans)].name + ".");
            }
            running = Hint(cashCard, "");

            var fareCard = Card(left, "TICKET PRICE");
            var fr = UIK.Row(fareCard, "mt-4");
            fareValue = UIK.Text(fr, "", "fare-chip");
            UIK.Display(fareValue);
            UIK.Text(fr, "per trip", "small", "muted").style.marginLeft = 8f;
            fare = new Slider(Balance.MinFare, Balance.MaxFare) { name = "fare-slider" };
            fare.RegisterValueChangedCallback(e =>
            {
                if (updatingControls) return;
                float v = Mathf.Round(e.newValue * 10f) / 10f;
                S.econ.baseFare = v;
                S.MarkDemandDirty();
                ShowFare(v);
            });
            fareCard.Add(fare);
            fareCard.Add(new FareScale(Balance.MinFare, Balance.MaxFare, FareGrumble));
            fareHint = Hint(fareCard, "");

            var peakCard = Card(left, "PEAK-HOUR SURCHARGE");
            var pr = UIK.Row(peakCard, "mt-4");
            surchargeValue = UIK.Text(pr, "", "fare-chip", "fare-chip-pink");
            UIK.Display(surchargeValue);
            surcharge = new Slider(0f, 1f);
            surcharge.RegisterValueChangedCallback(e =>
            {
                if (updatingControls) return;
                float v = Mathf.Round(e.newValue * 20f) / 20f;
                S.econ.peakSurcharge = v;
                surchargeValue.text = v < 0.01f ? "off" : "+" + Fmt.Pct(v);
            });
            peakCard.Add(surcharge);
            if (S.Has(Feature.Surcharge))
                Hint(peakCard, "Applies on weekdays 07:00–09:30 and 16:30–19:00. Pushes some trips out of the rush and earns more, but commuters grumble.");
            else
            {
                UIK.Show(surcharge, false);
                UIK.Show(pr, false);
                Hint(peakCard, "Rush-hour pricing unlocks when your city becomes a " + ProgressionSystem.Tiers[ProgressionSystem.UnlockTier(Feature.Surcharge)].name + ".");
            }

            var adCard = Card(left, "ADVERTISING");
            UIK.Segmented(adCard, Balance.AdLevelNames, S.econ.adLevel, i => S.econ.adLevel = i);
            adHint = Hint(adCard, "");

            var ledgerCard = Card(right, "LEDGER");
            ledgerCard.AddToClassList("ledger");
            var hr = UIK.Div(ledgerCard, "table-row");
            UIK.Text(hr, "", "cell").style.flexGrow = 1f;
            var h1 = UIK.Text(hr, "Today", "cell-num", "caps");
            h1.style.width = 100f;
            var h2 = UIK.Text(hr, "Yesterday", "cell-num", "caps");
            h2.style.width = 100f;
            for (int i = 0; i < DayLedger.Count; i++)
            {
                var cat = (LedgerCat)i;
                var r = UIK.Div(ledgerCard, "table-row");
                var nameCol = UIK.Row(r);
                nameCol.style.flexGrow = 1f;
                var name = UIK.Text(nameCol, DayLedger.Label(cat), "cell");
                if (cat == LedgerCat.Grants)
                {
                    grantTag = UIK.Text(nameCol, "target met!", "ledger-tag");
                    UIK.Display(grantTag);
                    UIK.Show(grantTag, false);
                }
                var t = UIK.Text(r, "", "cell-num");
                t.style.width = 100f;
                var y = UIK.Text(r, "", "cell-num");
                y.style.width = 100f;
                rows.Add((cat, name, t, y));
            }
            var tr = UIK.Div(ledgerCard, "table-row", "table-row-total");
            UIK.Text(tr, "Net", "cell", "value").style.flexGrow = 1f;
            netToday = UIK.Text(tr, "", "cell-num", "value");
            netToday.style.width = 100f;
            netYesterday = UIK.Text(tr, "", "cell-num", "value");
            netYesterday.style.width = 100f;
            ledgerFoot = UIK.Text(ledgerCard, "", "tiny", "muted", "mt-4");

            var chartCard = Card(right, "NET RESULT, LAST 14 DAYS");
            chart = new DayBars(14, 130f) { color = Doodle.Mint, format = v => Fmt.Money(v) };
            chart.style.marginTop = 8f;
            chartCard.Add(chart);
            var legend = UIK.Row(chartCard, "mt-4");
            Legend(legend, Doodle.Mint, "profit");
            Legend(legend, Doodle.Tomato, "loss");
            Legend(legend, Doodle.Mint, "today so far", true);
            Hint(chartCard, "Includes construction spending. Running costs are charged continuously through the day.");
            Refresh();
        }

        void Note(string text)
        {
            bankNote.text = text ?? "";
            UIK.Show(bankNote, !string.IsNullOrEmpty(text));
            if (!string.IsNullOrEmpty(text)) Sfx.Error();
        }

        void ShowFare(float v)
        {
            fareValue.text = Fmt.Fare(v);
            bool grumble = v > FareGrumble + 0.001f;
            fareValue.EnableInClassList("fare-chip-hot", grumble);
            fareHint.text = grumble
                ? "Riders grumble at this price: fewer of them choose the metro, and satisfaction drops."
                : "Higher fares earn more per trip but fewer people choose the metro. Riders notice prices above $3.";
            fareHint.EnableInClassList("bad", grumble);
        }

        protected override void Refresh()
        {
            var e = S.econ;
            cash.text = Fmt.Money(e.money);
            cash.EnableInClassList("bad", e.money < 0);
            loanLabel.text = e.loan > 0 ? $"Loan: {Fmt.Money(e.loan)} at {Balance.LoanDailyInterest * 100f:0.0}% per day ({Fmt.Money(e.loan * Balance.LoanDailyInterest)}/day)" : "No outstanding loan.";
            limitLabel.text = $"Credit limit {Fmt.Money(e.LoanLimit)} (grows with the network)";
            e.DailyRunningCosts(out float sal, out float maint);
            running.text = $"Running costs: {Fmt.Money(sal)}/day staff, {Fmt.Money(maint)}/day maintenance.";
            updatingControls = true;
            fare.SetValueWithoutNotify(e.baseFare);
            surcharge.SetValueWithoutNotify(e.peakSurcharge);
            updatingControls = false;
            ShowFare(e.baseFare);
            surchargeValue.text = e.peakSurcharge < 0.01f ? "off" : "+" + Fmt.Pct(e.peakSurcharge);
            float perBoard = Balance.AdRevenuePerBoarding[e.adLevel];
            adHint.text = e.adLevel == 0
                ? "No ads: no ad income, riders appreciate the clean stations."
                : $"Earns {Fmt.Fare(perBoard)} per boarding; busier stations are worth more. Riders' patience drops a little with each level.";
            var y = e.Yesterday;
            foreach (var (cat, name, t, yy) in rows)
            {
                float vt = e.today.Get(cat);
                t.text = Mathf.Abs(vt) < 1f ? "—" : Fmt.Money(vt);
                t.EnableInClassList("bad", vt < -1f);
                t.EnableInClassList("good", vt > 1f);
                float vy = y != null ? y.Get(cat) : 0f;
                yy.text = Mathf.Abs(vy) < 1f ? "—" : Fmt.Money(vy);
                yy.EnableInClassList("bad", vy < -1f);
                yy.EnableInClassList("good", vy > 1f);
            }
            // "target met!" when a City Hall request paid out today.
            bool metToday = false;
            int today = S.clock.Day;
            foreach (var r in S.gov.history)
                if (r.state == RequestState.Done && r.reward > 0f && (int)(r.resolvedAt / Balance.MinutesPerDay) == today) metToday = true;
            if (grantTag != null) UIK.Show(grantTag, metToday);
            netToday.text = Fmt.MoneySigned(e.today.Net);
            netToday.EnableInClassList("good", e.today.Net >= 0);
            netToday.EnableInClassList("bad", e.today.Net < 0);
            float ny = y != null ? y.Net : 0f;
            netYesterday.text = y != null ? Fmt.MoneySigned(ny) : "—";
            netYesterday.EnableInClassList("good", ny >= 0);
            netYesterday.EnableInClassList("bad", ny < 0);
            ledgerFoot.text = $"Today so far, {S.clock.TimeString}.";
            var hist = e.history;
            int n = Mathf.Min(13, hist.Count);
            var vals = new float[n + 1];
            for (int i = 0; i < n; i++) vals[i] = hist[hist.Count - n + i].Net;
            vals[n] = e.today.Net;
            chart.SetData(vals, S.clock.DayNumber - n);
        }
    }

    /// <summary>The ticket-price scale under the fare slider: a calm zone, then a hatched red "riders grumble" zone.</summary>
    public sealed class FareScale : VisualElement
    {
        readonly float min, max, grumble;

        public FareScale(float min, float max, float grumble)
        {
            this.min = min;
            this.max = max;
            this.grumble = grumble;
            style.height = 30f;
            style.marginLeft = 6f;
            style.marginRight = 6f;
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
            float gx = (grumble - min) / (max - min) * 100f;
            Tick(0f, Fmt.Fare(min), 0f);
            Tick(gx, $"{Fmt.Fare(grumble)}: riders grumble past here", -50f);
            Tick(100f, Fmt.Fare(max), -100f);
        }

        void Tick(float pct, string text, float shift)
        {
            var l = new Label(text);
            l.AddToClassList("fare-scale-label");
            if (pct > 0f && pct < 100f) l.AddToClassList("bad");
            l.style.position = Position.Absolute;
            l.style.left = Length.Percent(pct);
            l.style.top = 11f;
            l.style.translate = new Translate(Length.Percent(shift), 0);
            l.pickingMode = PickingMode.Ignore;
            Add(l);
        }

        void Draw(MeshGenerationContext mgc)
        {
            var r = contentRect;
            if (r.width < 20f) return;
            var p = mgc.painter2D;
            float gx = (grumble - min) / (max - min) * r.width;
            p.fillColor = Doodle.MintL;
            p.BeginPath();
            p.MoveTo(new Vector2(0f, 2f));
            p.LineTo(new Vector2(gx, 2f));
            p.LineTo(new Vector2(gx, 8f));
            p.LineTo(new Vector2(0f, 8f));
            p.ClosePath();
            p.Fill();
            p.fillColor = Doodle.TomatoL;
            p.BeginPath();
            p.MoveTo(new Vector2(gx, 2f));
            p.LineTo(new Vector2(r.width, 2f));
            p.LineTo(new Vector2(r.width, 8f));
            p.LineTo(new Vector2(gx, 8f));
            p.ClosePath();
            p.Fill();
            MiniChart.Stripes(p, gx, r.width, 2f, 8f);
            p.strokeColor = Doodle.Ink;
            p.lineWidth = 1.5f;
            foreach (float x in new[] { 0.75f, gx, r.width - 0.75f })
            {
                p.BeginPath();
                p.MoveTo(new Vector2(x, 0f));
                p.LineTo(new Vector2(x, 10f));
                p.Stroke();
            }
        }
    }

    // ====================================================================== Stats

    public sealed class StatsWindow : WindowBase
    {
        readonly (VisualElement root, Label value, Label caption)[] tiles;
        readonly Label[] chipValues = new Label[4];
        readonly MiniChart hourly;
        readonly DayBars daily;
        readonly Label hourlyCaption, satHeadline;
        readonly IconElement satFace;
        readonly List<(Label name, VisualElement fill, Label pct)> satRows = new List<(Label, VisualElement, Label)>();
        readonly VisualElement stationsList, linesList;
        int tick;

        public StatsWindow(GameRoot g, VisualElement layer) : base(g, layer, "Statistics", 880f, "head-gum", WindowKind.Stats)
        {
            var grid = UIK.Div(body, "grid2");
            tiles = new (VisualElement, Label, Label)[4];
            string[] caps = { "trips today", "trips yesterday", "average wait", "average train load" };
            for (int i = 0; i < tiles.Length; i++)
            {
                tiles[i] = UIK.Tile(grid, caps[i]);
                tiles[i].root.style.width = Length.Percent(24.9f);
            }
            // The smaller numbers, as chips; the bad ones go red when they're not zero.
            var chips = UIK.Row(body, "stat-chips", "mt-4");
            string[] chipCaps = { "transfers today", "left behind on platforms", "turned away at gates", "gave up waiting" };
            for (int i = 0; i < 4; i++)
            {
                var c = UIK.Row(chips, "stat-chip");
                chipValues[i] = UIK.Text(c, "0", "stat-chip-value");
                UIK.Display(chipValues[i]);
                UIK.Text(c, chipCaps[i], "tiny", "muted").style.marginLeft = 5f;
            }

            var cols = UIK.Row(body);
            cols.style.alignItems = Align.FlexStart;
            var left = UIK.Col(cols);
            left.style.width = Length.Percent(55);
            left.style.marginRight = 16f;
            var right = UIK.Col(cols);
            right.style.flexGrow = 1f;

            var hc = Card(left, "TRIPS PER HOUR");
            var hl = UIK.Row(hc);
            Legend(hl, Doodle.Sky, "today");
            Legend(hl, Doodle.WithAlpha(Doodle.Ink, 0.45f), "yesterday");
            Legend(hl, Doodle.WithAlpha(Doodle.Gum, 0.35f), "rush hours");
            hourly = new MiniChart(116f, "No trips yet today.") { color = Doodle.Sky, rushBands = true };
            hourly.style.marginTop = 12f;
            hc.Add(hourly);
            var hours = UIK.Div(hc, "hour-axis");
            string[] hourText = { "00", "06", "12", "18", "24" };
            for (int i = 0; i < hourText.Length; i++)
            {
                var l = UIK.Text(hours, hourText[i], "tiny", "faint");
                l.style.position = Position.Absolute;
                l.style.left = Length.Percent(i * 25f);
                l.style.translate = new Translate(Length.Percent(i == 0 ? 0 : i == 4 ? -100 : -50), 0);
            }
            hourlyCaption = UIK.Text(hc, "", "small", "mt-4");
            hourlyCaption.style.whiteSpace = WhiteSpace.Normal;

            var dc = Card(left, "TRIPS PER DAY, LAST 15 DAYS");
            daily = new DayBars(15, 120f) { color = Doodle.Grape, format = v => Fmt.Count(v) };
            daily.style.marginTop = 8f;
            dc.Add(daily);

            var sc = Card(right);
            var sh = UIK.Row(sc);
            UIK.Text(sh, "RIDER SATISFACTION", "caps");
            UIK.Spacer(sh);
            satFace = new IconElement(IconKind.Smile, 30f);
            sh.Add(satFace);
            satHeadline = UIK.Text(sh, "", "sat-headline");
            UIK.Display(satHeadline);
            string[] names = { "Waiting time", "Train comfort", "Platform crowding", "Reliability", "Ticket price", "Amenities" };
            foreach (var nm in names)
            {
                var r = UIK.Row(sc, "mt-4");
                var lab = UIK.Text(r, nm, "small");
                lab.style.width = 130f;
                var (bar, fill) = UIK.Bar(r, Doodle.Sky);
                bar.style.flexGrow = 1f;
                bar.style.marginTop = 0f;
                var pct = UIK.Text(r, "", "small", "value");
                pct.style.width = 44f;
                pct.style.unityTextAlign = TextAnchor.MiddleRight;
                satRows.Add((lab, fill, pct));
            }

            var stc = Card(right, "BUSIEST STATIONS TODAY");
            stationsList = UIK.Col(stc);
            var lc = Card(body, "LINES");
            linesList = UIK.Col(lc);
            Refresh();
        }

        protected override void Refresh()
        {
            var st = S.stats;
            var sat = S.satisfaction;
            var last = st.LastDay;
            tiles[0].value.text = Fmt.Count(st.ridersToday);
            tiles[1].value.text = last != null ? Fmt.Count(last.riders) : "—";
            tiles[2].value.text = Fmt.Minutes(sat.avgWait);
            tiles[3].value.text = Fmt.Pct(sat.avgLoad);
            float[] chipVals = { st.transfersToday, st.deniedToday, st.turnedAwayToday, st.abandonedToday };
            for (int i = 0; i < 4; i++)
            {
                chipValues[i].text = Fmt.Count(chipVals[i]);
                chipValues[i].EnableInClassList("bad", i > 0 && chipVals[i] >= 1f);
            }

            int hour = Mathf.FloorToInt(S.clock.HourOfDay);
            hourly.nowIndex = hour;
            hourly.rushBands = !S.clock.IsWeekend;
            hourly.SetData((float[])st.hourRiders.Clone(), st.days.Count > 0 ? (float[])st.yesterdayHourRiders.Clone() : null);
            if (st.days.Count > 0 && last != null)
            {
                float soFar = 0f;
                for (int h = 0; h <= hour && h < 24; h++) soFar += st.yesterdayHourRiders[h];
                hourlyCaption.text = st.ridersToday >= last.riders && last.riders > 0f
                    ? "Today already beats yesterday!"
                    : soFar > 1f ? $"{Fmt.Pct(st.ridersToday / soFar)} of what yesterday had carried by this hour." : "";
            }
            else hourlyCaption.text = "Yesterday's line appears after the first full day.";
            int n = Mathf.Min(14, st.days.Count);
            var dv = new float[n + 1];
            for (int i = 0; i < n; i++) dv[i] = st.days[st.days.Count - n + i].riders;
            dv[n] = st.ridersToday;
            daily.SetData(dv, S.clock.DayNumber - n);

            int pctAll = sat.Percent;
            satHeadline.text = pctAll + "%";
            satFace.Color = pctAll >= 70 ? Doodle.MoodGood : pctAll >= 55 ? Doodle.MoodOk : pctAll >= 40 ? Doodle.MoodMeh : Doodle.MoodBad;
            satFace.Mood = Mathf.Clamp((pctAll - 55f) / 25f, -1f, 1f);
            float[] comps = { sat.wait, sat.comfort, sat.crowding, sat.reliability, sat.price, sat.amenities };
            for (int i = 0; i < satRows.Count; i++)
            {
                var (_, fill, pct) = satRows[i];
                UIK.SetBar(fill, comps[i]);
                fill.style.backgroundColor = Doodle.Score(comps[i]);
                pct.text = Fmt.Pct(comps[i]);
            }

            if (tick++ % 5 != 0) return;
            stationsList.Clear();
            var stations = new List<Station>();
            foreach (var s in S.net.AliveStations())
                if (s.lines.Count > 0) stations.Add(s);
            stations.Sort((a, b) => b.entriesToday.CompareTo(a.entriesToday));
            for (int i = 0; i < Mathf.Min(6, stations.Count); i++)
            {
                var s = stations[i];
                var r = UIK.Row(stationsList, "table-row", "table-row-click");
                var rank = UIK.Text(r, (i + 1).ToString(), "rank-medal", i == 0 ? "rank-gold" : i == 1 ? "rank-silver" : i == 2 ? "rank-bronze" : "rank-plain");
                UIK.Display(rank);
                var face = new IconElement(IconKind.Smile, 20f) { Color = MapPalette.CrowdColor(s.Crowding), Mood = Doodle.MoodFromCrowding(s.Crowding) };
                face.style.marginRight = 8f;
                r.Add(face);
                UIK.Text(r, s.name, "cell").style.flexGrow = 1f;
                UIK.Text(r, Fmt.Count(s.entriesToday), "cell-num");
                var ss = s;
                r.AddManipulator(new Clickable(() =>
                {
                    g.CloseWindow();
                    g.Select(SelKind.Station, ss.id);
                    g.FocusOn(ss.pos, 4f);
                }));
            }
            if (stations.Count == 0) UIK.Text(stationsList, "No stations in service yet.", "small", "muted");

            linesList.Clear();
            var head = UIK.Row(linesList, "table-row");
            void H(string t, float w)
            {
                var l = UIK.Text(head, t, "caps", "cell-num");
                l.style.width = w;
            }
            UIK.Text(head, "LINE", "caps").style.flexGrow = 1f;
            H("TRAINS", 70f);
            H("HEADWAY", 80f);
            H("BOARDINGS", 90f);
            H("PEAK LOAD", 90f);
            H("WAIT", 70f);
            foreach (var l in S.net.AliveLines())
            {
                var r = UIK.Row(linesList, "table-row", "table-row-click");
                LineBadge(r, l);
                UIK.Text(r, l.name, "cell").style.flexGrow = 1f;
                void C(string t, float w, bool bad = false)
                {
                    var c = UIK.Text(r, t, "cell-num");
                    c.style.width = w;
                    c.EnableInClassList("bad", bad);
                }
                C(l.trains.Count.ToString(), 70f);
                C(l.trains.Count > 0 ? Fmt.Minutes(l.headway) : "—", 80f);
                C(Fmt.Count(l.boardingsToday), 90f);
                C(Fmt.Pct(l.MaxSegLoad), 90f, l.MaxSegLoad >= 0.9f);
                C(l.trains.Count > 0 ? Fmt.Minutes(l.avgWaitRecent) : "—", 70f);
                var ll = l;
                r.AddManipulator(new Clickable(() =>
                {
                    g.CloseWindow();
                    g.Select(SelKind.Line, ll.id);
                }));
            }
        }
    }

    // ====================================================================== City

    public sealed class CityWindow : WindowBase
    {
        enum SortBy { Order, Residents, Jobs, Coverage }

        readonly (VisualElement root, Label value, Label caption)[] tiles;
        readonly VisualElement strip, targets, recent, districts, ladder;
        readonly Label next, stripHead;
        readonly List<RequestView.Handle> handles = new List<RequestView.Handle>();
        SortBy sortBy = SortBy.Order;
        string targetSig = "";
        int tick;

        public CityWindow(GameRoot g, VisualElement layer) : base(g, layer, "City overview", 880f, "head-mint", WindowKind.City)
        {
            var grid = UIK.Div(body, "grid2");
            tiles = new (VisualElement, Label, Label)[4];
            string[] caps = { "residents in the developed city", "jobs", "residents near a served station", "districts open" };
            for (int i = 0; i < 4; i++)
            {
                tiles[i] = UIK.Tile(grid, caps[i]);
                tiles[i].root.style.width = Length.Percent(24.9f);
            }

            // Every district as a little square: coloured by kind when open, a cloud while it isn't.
            var sc = Card(body);
            var sh = UIK.Row(sc);
            stripHead = UIK.Text(sh, "", "caps");
            UIK.Spacer(sh);
            var all = UIK.Text(sh, "see the whole map", "small", "accent", "link");
            all.AddManipulator(new Clickable(() =>
            {
                g.CloseWindow();
                if (!g.CamCtl.InOverview) g.ToggleOverview();
            }));
            strip = UIK.Row(sc, "district-strip", "mt-8");
            next = Hint(sc, "");

            var cols = UIK.Row(body);
            cols.style.alignItems = Align.FlexStart;
            var left = UIK.Col(cols);
            left.style.width = Length.Percent(50);
            left.style.marginRight = 16f;
            var right = UIK.Col(cols);
            right.style.flexGrow = 1f;
            var tc = Card(left, "CITY HALL TARGETS");
            targets = UIK.Col(tc);
            var rc = Card(right, "RECENTLY");
            recent = UIK.Col(rc);

            var lc = Card(body, "CITY SIZES");
            ladder = UIK.Col(lc);

            var dc = Card(body, "DISTRICTS");
            districts = UIK.Col(dc);
            BuildLadder();
            Refresh();
        }

        /// <summary>All six city sizes on a rail: where the city has been, where it is, and what comes next.</summary>
        void BuildLadder()
        {
            ladder.Clear();
            var p = S.progress;
            var rail = UIK.Row(ladder, "size-ladder", "mt-8");
            UIK.Div(rail, "size-rail").pickingMode = PickingMode.Ignore;
            for (int i = 0; i < ProgressionSystem.Tiers.Length; i++)
            {
                var t = ProgressionSystem.Tiers[i];
                var stop = UIK.Col(rail, "size-stop");
                stop.EnableInClassList("size-stop-done", i < p.tier);
                stop.EnableInClassList("size-stop-now", i == p.tier);
                var dot = UIK.Text(stop, (i + 1).ToString(), "size-dot");
                UIK.Display(dot);
                UIK.Text(stop, t.name, "size-name");
                UIK.Text(stop, t.maxLines >= 99 ? "unlimited lines" : $"{t.maxLines} line{(t.maxLines == 1 ? "" : "s")}", "tiny", "muted");
                if (t.grant > 0f) UIK.Text(stop, "+" + Fmt.Money(t.grant) + " grant", "tiny", "good");
                foreach (var f in t.unlocks)
                {
                    var u = UIK.Text(stop, ProgressionSystem.FeatureTitle(f), "size-unlock");
                    u.style.whiteSpace = WhiteSpace.Normal;
                }
                if (i == p.tier + 1)
                    UIK.Text(stop, $"{p.GoalsDone}/{p.goals.Count} goals done", "size-goals");
                if (i == p.tier) UIK.Text(stop, "you are here", "size-here");
            }
        }

        protected override void Refresh()
        {
            var city = S.city;
            tiles[0].value.text = Fmt.Count(city.revealedPopulation);
            tiles[1].value.text = Fmt.Count(city.revealedJobs);
            tiles[2].value.text = Fmt.Pct(city.CoverageRatio);
            int open = 0;
            foreach (var d in city.districts)
                if (d.revealed) open++;
            tiles[3].value.text = $"{open} / {city.districts.Count}";
            stripHead.text = $"GREATER {city.name.ToUpperInvariant()} · {open} OF {city.districts.Count} DISTRICTS OPEN";
            double at = S.growth.nextRevealAt;
            next.text = at < double.MaxValue / 2
                ? $"The city keeps growing on its own. Next new district expected around Day {(int)(at / Balance.MinutesPerDay) + 1}. Existing districts keep densifying, faster near good metro service."
                : "Every district of the city is now open.";
            foreach (var h in handles) RequestView.Update(S, h);

            if (tick++ % 5 != 0) return;
            var gov = S.gov;
            var sig = new System.Text.StringBuilder();
            foreach (var r in gov.open) sig.Append(r.id).Append(':').Append((int)r.state).Append(',');
            sig.Append('|').Append(gov.history.Count);
            if (sig.ToString() != targetSig)
            {
                targetSig = sig.ToString();
                targets.Clear();
                handles.Clear();
                if (!gov.started)
                    Hint(targets, $"Mayor {gov.mayor.name}'s council forms when your town becomes a Market town. From then on City Hall sends requests, and its approval sets your subsidy.");
                else
                {
                    UIK.Text(targets, $"Approval {gov.approval:0}% · {gov.Standing} · subsidy ×{gov.SubsidyFactor:0.00}", "small", "muted");
                    foreach (var r in gov.open) handles.Add(RequestView.Build(g, targets, r, true));
                    if (gov.open.Count == 0) Hint(targets, "No targets right now. City Hall will be in touch, usually within a day.");
                    var b = UIK.Button(targets, "Open City Hall", () => g.OpenWindow(WindowKind.CityHall), "btn-small", "btn-primary");
                    b.style.alignSelf = Align.FlexStart;
                    b.style.marginTop = 8f;
                }
                recent.Clear();
                int shown = 0;
                for (int i = gov.history.Count - 1; i >= 0 && shown < 7; i--, shown++)
                {
                    var r = gov.history[i];
                    var row = UIK.Row(recent, "recent-row");
                    bool met = r.state == RequestState.Done;
                    string stamp = met ? "MET" : r.state == RequestState.Failed ? "MISSED" : r.state == RequestState.Declined ? "DECLINED" : "EXPIRED";
                    var st = UIK.Text(row, stamp, "stamp", met ? "stamp-met" : r.state == RequestState.Failed ? "stamp-missed" : "stamp-other");
                    UIK.Display(st);
                    var col = UIK.Col(row);
                    col.style.flexShrink = 1f;
                    col.style.flexGrow = 1f;
                    var t = UIK.Text(col, r.title, "small", "value");
                    t.style.whiteSpace = WhiteSpace.Normal;
                    int day = (int)(r.resolvedAt / Balance.MinutesPerDay);
                    string when = (day == S.clock.Day ? "today " : $"Day {day + 1} · ") + GameClock.FormatMinute((float)(r.resolvedAt % Balance.MinutesPerDay));
                    string money = met && r.reward > 0f ? " · +" + Fmt.Money(r.reward) : r.state == RequestState.Failed && r.fine > 0f ? " · −" + Fmt.Money(r.fine) : "";
                    UIK.Text(col, when + money, "tiny", "muted");
                }
                if (gov.history.Count == 0) Hint(recent, "Nothing yet.");
            }

            strip.Clear();
            var order = new List<District>(city.districts);
            order.Sort((a, b) => a.revealOrder.CompareTo(b.revealOrder));
            foreach (var d in order)
            {
                var sq = UIK.Div(strip, "district-square");
                if (d.revealed)
                {
                    sq.style.backgroundColor = Doodle.DistrictColor(d.type);
                    bool fresh = (int)d.revealedAtDay == S.clock.Day && d.revealedAtDay >= 0f;
                    sq.EnableInClassList("district-square-new", fresh);
                    var dd = d;
                    UIK.Tooltip(sq, $"{d.name} · {d.TypeLabel}\n{Fmt.Count(d.population)} residents · {Fmt.Pct(d.Coverage)} served" + (fresh ? "\nNew today!" : ""));
                    sq.AddManipulator(new Clickable(() =>
                    {
                        g.CloseWindow();
                        g.FocusDistrict(dd.id);
                    }));
                }
                else
                {
                    sq.AddToClassList("district-square-cloud");
                    sq.Add(new IconElement(IconKind.Cloud, 14f));
                    UIK.Tooltip(sq, "Still in the clouds: it opens as the city grows.");
                }
            }

            BuildLadder();
            BuildDistricts();
        }

        void BuildDistricts()
        {
            var city = S.city;
            districts.Clear();
            var list = new List<District>();
            foreach (var d in city.districts)
                if (d.revealed) list.Add(d);
            switch (sortBy)
            {
                case SortBy.Residents: list.Sort((a, b) => b.population.CompareTo(a.population)); break;
                case SortBy.Jobs: list.Sort((a, b) => b.jobs.CompareTo(a.jobs)); break;
                case SortBy.Coverage: list.Sort((a, b) => a.Coverage.CompareTo(b.Coverage)); break;
                default: list.Sort((a, b) => a.revealOrder.CompareTo(b.revealOrder)); break;
            }
            var head = UIK.Row(districts, "table-row");
            UIK.Text(head, "DISTRICT", "caps").style.flexGrow = 1f;
            void Head(string t, float w, SortBy s)
            {
                var l = UIK.Text(head, t + (sortBy == s ? " ↓" : ""), "caps", "cell-num", "sortable");
                l.style.width = w;
                l.AddManipulator(new Clickable(() =>
                {
                    sortBy = sortBy == s ? SortBy.Order : s;
                    BuildDistricts();
                }));
                UIK.Tooltip(l, "Click to sort");
            }
            var ty = UIK.Text(head, "TYPE", "caps", "cell-num");
            ty.style.width = 140f;
            Head("RESIDENTS", 100f, SortBy.Residents);
            Head("JOBS", 80f, SortBy.Jobs);
            Head("COVERAGE", 160f, SortBy.Coverage);
            foreach (var d in list)
            {
                var r = UIK.Row(districts, "table-row", "table-row-click");
                var sw = UIK.Div(r, "type-swatch");
                sw.style.backgroundColor = Doodle.DistrictColor(d.type);
                UIK.Text(r, d.name, "cell", "value");
                if ((int)d.revealedAtDay == S.clock.Day && d.revealedAtDay >= 0f)
                {
                    var nw = UIK.Text(r, "new today!", "new-badge");
                    UIK.Display(nw);
                }
                UIK.Spacer(r);
                var tl = UIK.Text(r, d.TypeLabel, "cell-num", "muted");
                tl.style.width = 140f;
                var p = UIK.Text(r, Fmt.Count(d.population), "cell-num");
                p.style.width = 100f;
                var j = UIK.Text(r, Fmt.Count(d.jobs), "cell-num");
                j.style.width = 80f;
                var covWrap = UIK.Row(r);
                covWrap.style.width = 160f;
                covWrap.style.justifyContent = Justify.FlexEnd;
                var (bar, fill) = UIK.Bar(covWrap, d.Coverage > 0.4f ? Doodle.Mint : d.Coverage > 0.15f ? Doodle.Sun : Doodle.Tomato);
                bar.style.width = 90f;
                bar.style.marginTop = 0f;
                bar.style.marginRight = 8f;
                UIK.SetBar(fill, d.Coverage);
                var cv = UIK.Text(covWrap, Fmt.Pct(d.Coverage), "cell-num");
                cv.style.width = 44f;
                cv.EnableInClassList("bad", d.Coverage < 0.15f);
                var dd = d;
                r.AddManipulator(new Clickable(() =>
                {
                    g.CloseWindow();
                    g.FocusDistrict(dd.id);
                }));
            }
            int hidden = city.districts.Count - list.Count;
            if (hidden > 0) UIK.Text(districts, $"{hidden} more districts are still in the clouds.", "small", "muted", "mt-4");
        }
    }
}
