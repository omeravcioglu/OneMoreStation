using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace Metro.UI
{
    /// <summary>Shared building blocks for showing City Hall requests.</summary>
    public static class RequestView
    {
        public static readonly Color MandateColor = Doodle.Tomato;
        public static readonly Color HallColor = Doodle.Grape;

        public static IconKind IconFor(RequestKind k)
        {
            switch (k)
            {
                case RequestKind.ConnectDistrict: return IconKind.City;
                case RequestKind.StationNear: return IconKind.Pin;
                case RequestKind.Ridership: return IconKind.People;
                case RequestKind.Satisfaction: return IconKind.Smile;
                case RequestKind.ServedResidents: return IconKind.Coverage;
                case RequestKind.FareCap:
                case RequestKind.NoSurcharge: return IconKind.Finance;
                case RequestKind.Accessibility: return IconKind.Station;
                case RequestKind.Interchanges: return IconKind.Line;
                case RequestKind.CrowdRelief: return IconKind.Warning;
                case RequestKind.ProfitDay: return IconKind.Stats;
                case RequestKind.Event: return IconKind.Calendar;
                default: return IconKind.Hall;
            }
        }

        /// <summary>Moves the camera to whatever the request is about.</summary>
        public static bool CanFocus(GameState gs, CouncilRequest r) =>
            gs.gov.LandmarkById(r.landmarkId) != null || gs.net.GetStation(r.stationId) != null || r.districtId >= 0;

        public static void Focus(GameRoot g, CouncilRequest r)
        {
            var gs = g.State;
            if (gs == null) return;
            g.CloseWindow();
            var lm = gs.gov.LandmarkById(r.landmarkId);
            if (lm != null)
            {
                g.FocusOn(lm.pos, 4.5f);
                return;
            }
            var st = gs.net.GetStation(r.stationId);
            if (st != null)
            {
                g.FocusOn(st.pos, 4f);
                g.Select(SelKind.Station, st.id);
                return;
            }
            if (r.districtId >= 0) g.FocusDistrict(r.districtId);
        }

        public sealed class Handle
        {
            public bool compact;
            public CouncilRequest r;
            public VisualElement root, fill, bar;
            public Label status, time, pct, due;
        }

        /// <summary>A request card. Compact cards fit the goals panel; full cards show the letter and terms.</summary>
        public static Handle Build(GameRoot g, VisualElement parent, CouncilRequest r, bool compact)
        {
            var gs = g.State;
            var h = new Handle { r = r };
            var card = UIK.Col(parent, compact ? "goal" : "request-card");
            if (r.state == RequestState.Proposed) card.AddToClassList("request-proposed");
            h.root = card;
            var top = UIK.Row(card);
            top.style.alignItems = Align.FlexStart;
            var ic = new IconElement(IconFor(r.kind), compact ? 18f : 22f);
            if (r.mandate) ic.Color = MandateColor;
            ic.style.marginRight = 6f;
            ic.style.marginTop = 1f;
            top.Add(ic);
            var title = UIK.Text(top, r.title, compact ? "goal-title" : "h3");
            // The title takes the free width (measured at that width, so a wrapped title grows the row).
            title.style.flexBasis = 0f;
            title.style.flexGrow = 1f;
            title.style.flexShrink = 1f;
            title.style.whiteSpace = WhiteSpace.Normal;
            if (r.mandate)
            {
                var tag = UIK.Text(top, "MANDATE", "request-tag");
                tag.style.backgroundColor = MandateColor;
            }
            else if (r.state == RequestState.Proposed)
            {
                var tag = UIK.Text(top, "NEW", "request-tag");
                tag.style.backgroundColor = HallColor;
            }
            h.compact = compact;
            h.pct = UIK.Text(top, "", "goal-count");
            h.time = UIK.Text(top, "", "tiny", "faint");
            h.time.style.marginLeft = 6f;
            h.time.style.flexShrink = 0f;
            if (compact)
            {
                // In the goals panel the time joins the status line, and the map link is a small pin.
                UIK.Show(h.time, false);
                if (CanFocus(gs, r))
                {
                    var pin = UIK.IconButton(top, IconKind.Pin, () => Focus(g, r), "Show on map", "icon-btn", 16f);
                    pin.AddToClassList("icon-btn-xs");
                    pin.style.marginLeft = 4f;
                }
            }
            if (!compact && !string.IsNullOrEmpty(r.letter))
            {
                var letter = UIK.Text(card, r.letter, "small", "request-letter");
                letter.style.whiteSpace = WhiteSpace.Normal;
            }
            // On the goals note a proposal's answer comes first, right under its title, so Accept and
            // Decline stay in view even when the note is short of room; there is no progress to show yet.
            VisualElement answerRow = null;
            if (compact && r.state == RequestState.Proposed)
                answerRow = UIK.Row(card, "mt-4");
            var (bar, fill) = UIK.Bar(card, r.mandate ? MandateColor : HallColor);
            h.bar = bar;
            h.fill = fill;
            if (answerRow != null) UIK.Show(bar, false);
            h.status = UIK.Text(card, "", "goal-meta");
            h.status.style.whiteSpace = WhiteSpace.Normal;
            if (compact)
            {
                // What's at stake, at a glance: a clock with the time left, the grant and the fine.
                var meta = UIK.Row(card, "request-meta");
                var clock = new IconElement(IconKind.Clock, 16f);
                clock.style.marginRight = 4f;
                meta.Add(clock);
                h.due = UIK.Text(meta, "", "request-due");
                UIK.Spacer(meta);
                if (r.reward > 0f)
                {
                    var chip = UIK.Text(meta, "+" + Fmt.Money(r.reward), "money-chip");
                    UIK.Display(chip);
                }
                if (r.fine > 0f)
                {
                    var chip = UIK.Text(meta, "fine " + Fmt.Money(r.fine), "money-chip", "money-chip-fine");
                    UIK.Display(chip);
                }
            }
            if (!compact)
            {
                var terms = UIK.Text(card, gs.gov.RewardText(r), "small", "accent", "mt-4");
                terms.style.whiteSpace = WhiteSpace.Normal;
            }
            var buttons = answerRow ?? UIK.Row(card, "mt-4");
            buttons.style.flexWrap = Wrap.Wrap;
            bool any = false;
            if (r.state == RequestState.Proposed && !r.mandate)
            {
                var accept = UIK.Button(buttons, "Accept", () =>
                {
                    gs.gov.Accept(r);
                    Sfx.Coin();
                }, "btn-small", "btn-primary");
                accept.name = "accept-request";
                accept.style.marginRight = 6f;
                var decline = UIK.Button(buttons, $"Decline (−{gs.gov.DeclineCost(r)})", () =>
                {
                    gs.gov.Decline(r);
                    Sfx.Click();
                }, "btn-small");
                decline.name = "decline-request";
                UIK.Tooltip(decline, "Turning City Hall down costs approval, more if it's what the mayor cares about.");
                decline.style.marginRight = 6f;
                any = true;
            }
            else if (r.state == RequestState.Proposed && r.mandate)
            {
                var ok = UIK.Button(buttons, "Understood", () => gs.gov.Accept(r), "btn-small", "btn-primary");
                ok.style.marginRight = 6f;
                any = true;
            }
            if (r.state == RequestState.Active && r.IsPayment && !r.paid)
            {
                var pay = UIK.Button(buttons, "Pay", () =>
                {
                    if (gs.gov.Pay(r)) Sfx.Coin();
                    else UIRoot.I.Toast("No cash to pay City Hall right now.", ToastKind.Error);
                }, "btn-small", "btn-primary");
                pay.name = "pay-request";
                pay.style.marginRight = 6f;
                UIK.Tooltip(pay, () => $"Pays {Fmt.Money(Mathf.Min(GovernmentSystem.Remaining(r), Mathf.Max(0f, (float)gs.econ.money)))} now: the rest of what's owed, or as much as cash allows.");
                any = true;
            }
            if (!compact && CanFocus(gs, r))
            {
                UIK.Button(buttons, "Show on map", () => Focus(g, r), "btn-small", "btn-ghost");
                any = true;
            }
            UIK.Show(buttons, any);
            Update(gs, h);
            return h;
        }

        public static void Update(GameState gs, Handle h)
        {
            var r = h.r;
            UIK.SetBar(h.fill, r.progress);
            string time = r.IsOpen ? gs.gov.TimeLeft(r) : "";
            string pct = r.state == RequestState.Active && !r.IsHold ? Mathf.FloorToInt(Mathf.Clamp01(r.progress) * 100f) + "%" : "";
            if (h.pct.text != pct) h.pct.text = pct;
            if (h.compact)
            {
                if (h.status.text != r.status) h.status.text = r.status;
                UIK.Show(h.status, !string.IsNullOrEmpty(r.status));
                double end = r.state == RequestState.Proposed ? r.answerBy : r.deadline;
                double left = end - gs.clock.totalMinutes;
                bool hurry = r.state == RequestState.Active ? left < 1.5 * Balance.MinutesPerDay : r.state == RequestState.Proposed && left < 120.0;
                string due = time.Length == 0 ? "" : hurry && r.state == RequestState.Active ? time + ", hurry!" : time + (char.IsDigit(time[0]) ? " left" : "");
                if (h.due.text != due) h.due.text = due;
                h.due.EnableInClassList("bad", hurry);
            }
            else if (h.time.text != time) h.time.text = time;
        }
    }

    /// <summary>City Hall (M): the mayor, approval and what it does, requests, elections and the history.</summary>
    public sealed class CityHallWindow : WindowBase
    {
        readonly MayorPortrait portrait;
        readonly Label mayorName, agendaLabel, agendaBlurb, kpi, approvalValue, standing, effects, session, ultimatum;
        readonly VisualElement approvalFill, electionBox, requestList, historyList, agendaPill;
        readonly MiniChart chart;
        readonly List<RequestView.Handle> handles = new List<RequestView.Handle>();
        string sig = "";

        public CityHallWindow(GameRoot g, VisualElement layer) : base(g, layer, "City Hall", 920f, "head-grape", WindowKind.CityHall)
        {
            var cols = UIK.Row(body);
            cols.style.alignItems = Align.FlexStart;
            var left = UIK.Col(cols);
            left.style.width = 320f;
            left.style.marginRight = 16f;
            left.style.flexShrink = 0f;
            var right = UIK.Col(cols);
            right.style.flexGrow = 1f;
            right.style.flexShrink = 1f;

            var mc = Card(left, "THE MAYOR");
            var mr = UIK.Row(mc, "mt-4");
            portrait = new MayorPortrait(72f);
            portrait.style.marginRight = 12f;
            mr.Add(portrait);
            var mcol = UIK.Col(mr);
            mcol.style.flexShrink = 1f;
            mayorName = UIK.Text(mcol, "", "h2");
            agendaPill = UIK.Row(mcol, "mt-4");
            agendaLabel = UIK.Text(agendaPill, "", "pill");
            agendaBlurb = Hint(mc, "");
            kpi = UIK.Text(mc, "", "small", "mt-4");
            kpi.style.whiteSpace = WhiteSpace.Normal;

            var ac = Card(left, "APPROVAL");
            var ar = UIK.Row(ac);
            ar.style.alignItems = Align.FlexEnd;
            approvalValue = UIK.Text(ar, "", "h1");
            standing = UIK.Text(ar, "", "small", "muted");
            standing.style.marginLeft = 8f;
            standing.style.marginBottom = 4f;
            var (_, fill) = UIK.Bar(ac, RequestView.HallColor);
            approvalFill = fill;
            ultimatum = UIK.Text(ac, "", "small", "request-ultimatum", "mt-4");
            ultimatum.style.whiteSpace = WhiteSpace.Normal;
            effects = UIK.Text(ac, "", "small", "mt-4");
            effects.style.whiteSpace = WhiteSpace.Normal;
            chart = new MiniChart(60f, "Approval history appears after the first night.") { color = RequestView.HallColor, bars = false, forcedMax = 100f };
            chart.style.marginTop = 8f;
            ac.Add(chart);
            Hint(ac, "Every night approval drifts towards how the city feels about the metro: rider satisfaction, plus what the mayor cares about. Requests move it right away.");

            // Sessions and elections sit on top of the requests, so the candidates are in view when a vote is on.
            var ec = Card(right, "ELECTIONS & SESSIONS");
            session = UIK.Text(ec, "", "small");
            session.style.whiteSpace = WhiteSpace.Normal;
            electionBox = UIK.Col(ec, "mt-4");

            var rc = Card(right, "REQUESTS");
            requestList = UIK.Col(rc);
            var hc = Card(right, "RECENTLY");
            historyList = UIK.Col(hc);
            Refresh();
        }

        protected override void Refresh()
        {
            var gov = S.gov;
            float mood = (gov.approval - 50f) / 40f;
            portrait.Set(gov.mayor, mood);
            mayorName.text = "Mayor " + gov.mayor.name;
            agendaLabel.text = "Agenda: " + GovernmentSystem.AgendaName(gov.mayor.agenda);
            agendaLabel.style.backgroundColor = MayorPortrait.AgendaColor(gov.mayor.agenda);
            agendaBlurb.text = GovernmentSystem.AgendaBlurb(gov.mayor.agenda);
            kpi.text = "What the mayor watches: " + gov.AgendaKpi();
            approvalValue.text = gov.started ? $"{gov.approval:0}%" : "—";
            standing.text = gov.started ? gov.Standing : "The council forms at Market town size";
            UIK.SetBar(approvalFill, gov.approval / 100f);
            UIK.Show(ultimatum, gov.ultimatum);
            ultimatum.text = $"FINAL WARNING: raise approval above {Balance.UltimatumApproval:0}% by Monday's session or the council revokes the metro's licence.";
            string permits = gov.PermitFactor < 1f ? $"fast-tracked permits: construction {Fmt.Pct(1f - gov.PermitFactor)} cheaper"
                : gov.PermitFactor > 1f ? $"red tape: construction {Fmt.Pct(gov.PermitFactor - 1f)} dearer" : "permits at normal cost";
            effects.text = gov.started
                ? $"Subsidy ×{gov.SubsidyFactor:0.00} ({Fmt.Fare(S.pax.Subsidy)} per trip) · {permits}."
                : "Before the council forms, the subsidy and permits are at their standard rates.";
            chart.values = gov.approvalHistory.ToArray();
            chart.MarkDirtyRepaint();

            int toSession = gov.DaysToSession();
            var sb = new StringBuilder();
            sb.Append(toSession == 0 ? "Council session this Monday at 06:00." : $"Next council session: Monday, in {toSession} day{(toSession == 1 ? "" : "s")}.");
            if (gov.mandatesEnabled && gov.nextElectionDay >= 0)
            {
                int d = gov.nextElectionDay - S.clock.Day;
                sb.Append(d <= 0 ? " Election at this session." : $" Next mayoral election in {d} days ({GameClock.DayNameOf(gov.nextElectionDay)}).");
            }
            else if (!gov.mandatesEnabled) sb.Append(" Elections start once the town is a City.");
            sb.Append($"\nRequests done: {gov.completed} · failed: {gov.failed} · declined: {gov.declined}");
            if (gov.bailouts > 0) sb.Append($" · bailouts: {gov.bailouts}");
            session.text = sb.ToString();

            string s = Signature(gov);
            if (s != sig)
            {
                sig = s;
                Rebuild(gov);
            }
            foreach (var h in handles) RequestView.Update(S, h);
        }

        string Signature(GovernmentSystem gov)
        {
            var sb = new StringBuilder();
            foreach (var r in gov.open) sb.Append(r.id).Append(':').Append((int)r.state).Append(r.paid ? "p" : "").Append(',');
            sb.Append('|').Append(gov.history.Count).Append('|').Append(gov.challenger != null ? gov.challenger.name : "-").Append(gov.endorsed);
            return sb.ToString();
        }

        void Rebuild(GovernmentSystem gov)
        {
            handles.Clear();
            requestList.Clear();
            var ordered = new List<CouncilRequest>(gov.open);
            ordered.Sort((a, b) => (a.state == RequestState.Proposed ? 0 : 1).CompareTo(b.state == RequestState.Proposed ? 0 : 1));
            foreach (var r in ordered) handles.Add(RequestView.Build(g, requestList, r, false));
            if (ordered.Count == 0)
                Hint(requestList, gov.started ? "No requests right now. City Hall will be in touch — usually within a day." : "City Hall starts sending requests when your town becomes a Market town.");

            historyList.Clear();
            int shown = 0;
            for (int i = gov.history.Count - 1; i >= 0 && shown < 8; i--, shown++)
            {
                var r = gov.history[i];
                var row = UIK.Row(historyList, "mt-4");
                bool good = r.state == RequestState.Done;
                var ic = new IconElement(good ? IconKind.Check : IconKind.Close, 16f);
                if (!good) ic.Color = r.state == RequestState.Failed ? Doodle.Bad : Doodle.Ink3;
                ic.style.marginRight = 6f;
                row.Add(ic);
                string what = r.state == RequestState.Done ? "done" : r.state == RequestState.Failed ? "failed" : r.state == RequestState.Declined ? "declined" : "expired";
                var t = UIK.Text(row, $"{r.title} — {what}", "small", good ? "value" : "muted");
                t.style.flexShrink = 1f;
                t.style.whiteSpace = WhiteSpace.Normal;
            }
            if (gov.history.Count == 0) Hint(historyList, "Nothing yet.");

            electionBox.Clear();
            if (gov.challenger != null)
            {
                UIK.Text(electionBox, "THE CANDIDATES", "caps");
                Candidate(gov, gov.mayor, 0, "incumbent");
                Candidate(gov, gov.challenger, 1, "challenger");
                Hint(electionBox, $"Endorse a candidate if you like: back the winner and approval rises by {Balance.EndorseSwing:0}, back the loser and it falls by as much. Happier riders help the incumbent.");
            }
        }

        void Candidate(GovernmentSystem gov, Mayor m, int who, string role)
        {
            var row = UIK.Row(electionBox, "upgrade-row");
            var pic = new MayorPortrait(40f);
            pic.Set(m, 0.5f);
            pic.style.marginRight = 8f;
            row.Add(pic);
            var col = UIK.Col(row);
            col.style.flexGrow = 1f;
            col.style.flexShrink = 1f;
            UIK.Text(col, m.name, "h3");
            UIK.Text(col, $"{role} · {GovernmentSystem.AgendaName(m.agenda)}", "small", "muted");
            bool on = gov.endorsed == who;
            var b = UIK.Button(row, on ? "Endorsed" : "Endorse", () =>
            {
                gov.Endorse(who);
                sig = "";
            }, "btn-small", on ? "btn-primary" : "btn-ghost");
            b.name = "endorse-" + who;
        }
    }
}
