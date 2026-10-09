using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Metro.UI
{
    /// <summary>Screen manager: HUD, windows, modals, main menu and map labels.</summary>
    public sealed partial class GameUI
    {
        readonly GameRoot g;
        public readonly UIRoot root;
        public readonly VisualElement hud;
        public readonly TopBar top;
        public readonly Toolbar toolbar;
        public readonly LinesBar lines;
        public readonly Minimap minimap;
        public readonly MapKey mapKey;
        public readonly GoalsWidget goals;
        public readonly FeedView feed;
        public readonly Inspector inspector;
        public readonly LabelLayer labels;
        public readonly TutorialView tutorial;
        MainMenu menu;
        WindowBase window;
        public WindowKind OpenWindowKind { get; private set; }
        VisualElement modal;
        Action onModalClosed;
        bool hudVisible = true;

        public GameUI(GameRoot g, UIRoot root)
        {
            this.g = g;
            this.root = root;
            labels = new LabelLayer(g, root.labelsLayer);
            hud = UIK.Div(root.hudLayer, "layer");
            hud.pickingMode = PickingMode.Ignore;
            top = new TopBar(g, hud);
            toolbar = new Toolbar(g, hud);
            goals = new GoalsWidget(g, hud);
            lines = new LinesBar(g, hud);
            minimap = new Minimap(g, hud);
            mapKey = new MapKey(g, hud);
            feed = new FeedView(g, hud);
            inspector = new Inspector(g, hud);
            tutorial = new TutorialView(g, root.overlayLayer);
            InitScreens();
            UIK.Show(hud, false);
        }

        public bool ModalOpen => modal != null;
        public bool WindowOpen => window != null;
        public bool MenuOpen => menu != null;

        // ------------------------------------------------------------------ modes

        public void ShowMainMenu()
        {
            pendingModals.Clear();
            CloseWindow();
            ModalLocked = false;
            CloseModal();
            UIK.Show(hud, false);
            tutorial.Hide();
            labels.enabled = true;
            menu?.root.RemoveFromHierarchy();
            menu = new MainMenu(g, root.modalLayer);
        }

        public void ShowGame()
        {
            menu?.root.RemoveFromHierarchy();
            menu = null;
            CloseModal();
            CloseWindow();
            SetHudVisible(true);
            inspector.ForceRebuild();
        }

        public void SetHudVisible(bool v)
        {
            hudVisible = v;
            UIK.Show(hud, v && menu == null);
        }

        public bool HudVisible => hudVisible;

        // ------------------------------------------------------------------ windows

        public void OpenWindow(WindowKind k)
        {
            if (OpenWindowKind == k)
            {
                CloseWindow();
                return;
            }
            CloseWindow();
            OpenWindowKind = k;
            switch (k)
            {
                case WindowKind.Finance: window = new FinanceWindow(g, root.windowLayer); break;
                case WindowKind.Stats: window = new StatsWindow(g, root.windowLayer); break;
                case WindowKind.City: window = new CityWindow(g, root.windowLayer); break;
                case WindowKind.CityHall: window = new CityHallWindow(g, root.windowLayer); break;
                default: OpenWindowKind = WindowKind.None; break;
            }
        }

        public void CloseWindow()
        {
            window?.Close();
            window = null;
            OpenWindowKind = WindowKind.None;
        }

        // ------------------------------------------------------------------ modals

        public VisualElement OpenModal(float width, Action onClosed = null, bool closeOnBackdrop = true)
        {
            CloseModal();
            ModalLocked = false;
            var back = UIK.Div(root.modalLayer, "modal-backdrop");
            if (closeOnBackdrop)
                back.RegisterCallback<PointerDownEvent>(e =>
                {
                    if (e.target == back) CloseModal();
                });
            // Dialogs drop in with a little bounce over the dimmed map.
            var panel = UIK.Div(back, "panel", "window", "modal-enter");
            panel.style.width = width;
            panel.schedule.Execute(() => panel.RemoveFromClassList("modal-enter")).StartingIn(16);
            modal = back;
            onModalClosed = onClosed;
            return panel;
        }

        /// <summary>A locked modal can only be closed by its own buttons (game over).</summary>
        public bool ModalLocked { get; private set; }

        public void CloseModal()
        {
            if (modal == null) return;
            modal.RemoveFromHierarchy();
            modal = null;
            ModalLocked = false;
            var cb = onModalClosed;
            onModalClosed = null;
            cb?.Invoke();
        }

        readonly Queue<Action> pendingModals = new Queue<Action>();

        /// <summary>Shows a modal now, or as soon as the current one closes (growth cards, council sessions...).</summary>
        public void ShowWhenFree(Action show)
        {
            if (modal == null && menu == null) show();
            else pendingModals.Enqueue(show);
        }

        public void ClearPendingModals() => pendingModals.Clear();

        /// <summary>Enter presses a dialog's main button: the first candy one that isn't greyed out.</summary>
        void PressPrimary()
        {
            if (modal == null) return;
            VisualElement found = null;
            modal.Query(className: "btn-primary").ForEach(e =>
            {
                if (found == null && e.userData is Action && !e.ClassListContains("btn-disabled") && e.resolvedStyle.display != DisplayStyle.None)
                    found = e;
            });
            (found?.userData as Action)?.Invoke();
        }

        /// <summary>A dialog's coloured header band with its title and a close button.</summary>
        static VisualElement ModalHead(VisualElement panel, string title, Action close, string tint = "head-sky")
        {
            var head = UIK.Div(panel, "window-head", tint);
            UIK.Text(head, title, "h1");
            UIK.Spacer(head);
            if (close != null) UIK.IconButton(head, IconKind.Close, close, "Close", "icon-btn", 20f);
            return head;
        }

        /// <summary>The tilted paper sticker at the top of a big card: an icon and a few shouty words.</summary>
        static Label Kicker(VisualElement panel, IconKind icon, string text, Color? iconColor = null)
        {
            var row = UIK.Row(panel, "tierup-kicker-row");
            var ic = new IconElement(icon, 22f);
            if (iconColor.HasValue) ic.Color = iconColor.Value;
            ic.style.marginRight = 7f;
            row.Add(ic);
            return UIK.Text(row, text, "tierup-kicker");
        }

        /// <summary>A fact card for the big cards: a number in the display face over a short caption.</summary>
        static void FactCard(VisualElement facts, string v, string k)
        {
            var f = UIK.Col(facts, "tierup-fact", "fact-n" + facts.childCount % 4);
            UIK.Text(f, v, "tierup-fact-v");
            UIK.Text(f, k, "tierup-fact-k");
        }

        // ------------------------------------------------------------------ city growth

        static readonly Color GrowOrange = Doodle.Tang;

        public static IconKind IconFor(Feature f)
        {
            switch (f)
            {
                case Feature.BuildLine: return IconKind.Line;
                case Feature.Depot: return IconKind.Depot;
                case Feature.Demolish: return IconKind.Demolish;
                case Feature.InsertStation: return IconKind.Insert;
                case Feature.Wagons: return IconKind.Wagon;
                case Feature.CityGrowth: return IconKind.City;
                case Feature.CityWindow: return IconKind.City;
                case Feature.Finance: return IconKind.Finance;
                case Feature.Overlays: return IconKind.Population;
                case Feature.Stats: return IconKind.Stats;
                case Feature.Upgrades: return IconKind.Station;
                case Feature.Surcharge: return IconKind.Finance;
                case Feature.Loans: return IconKind.Finance;
                case Feature.CityTargets: return IconKind.Ballot;
                case Feature.DepotExpansion: return IconKind.Depot;
                case Feature.Council: return IconKind.Hall;
            }
            return IconKind.Star;
        }

        /// <summary>
        /// The "your city grew" card at dawn: a sunburst band with the new size in big letters, the mascot
        /// jumping for joy, a "Size up!" burst and confetti, then what changed: the map, the line licence,
        /// the grant, new districts and new tools, explained one by one.
        /// </summary>
        public void ShowTierUp(TierReachedInfo info, Action onClosed)
        {
            var gs = g.State;
            var def = ProgressionSystem.Tiers[info.tier];
            var prev = ProgressionSystem.Tiers[Mathf.Max(0, info.tier - 1)];
            var p = OpenModal(720f, onClosed, false);
            p.AddToClassList("tierup");
            modal.AddToClassList("modal-backdrop-light");

            var band = UIK.Div(p, "tierup-band");
            var rays = new SunRays();
            band.Add(rays);
            Kicker(band, IconKind.Star, $"CITY SIZE {info.tier + 1} OF {ProgressionSystem.Tiers.Length}");
            UIK.Text(band, $"{gs.city.name} is now a", "tierup-lead");
            UIK.Text(band, def.name + "!", "tierup-title");
            UIK.Text(band, def.blurb, "tierup-blurb");
            var c1 = new Confetti(70f, 3, 9);
            c1.AddToClassList("tierup-confetti-l");
            band.Add(c1);
            var c2 = new Confetti(70f, 11, 9);
            c2.AddToClassList("tierup-confetti-r");
            band.Add(c2);

            var body = UIK.Col(p, "tierup-body");
            var facts = UIK.Div(body, "tierup-facts");
            void Fact(string v, string k) => FactCard(facts, v, k);
            Fact($"{info.oldArea.width:0}×{info.oldArea.height:0} → {info.newArea.width:0}×{info.newArea.height:0} km", "the map grew");
            Fact(def.maxLines >= 99 ? "Unlimited" : $"{prev.maxLines} → {def.maxLines}", "line licence");
            if (info.grant > 0f) Fact("+" + Fmt.Money(info.grant), "expansion grant");

            if (info.newDistricts.Count > 0)
            {
                var row = UIK.Row(body, "tierup-districts");
                UIK.Text(row, "New districts", "tierup-section");
                int n = 0;
                foreach (var d in info.newDistricts)
                {
                    var pill = UIK.Row(row, "tierup-district");
                    pill.style.rotate = new Rotate(n++ % 2 == 0 ? -2f : 2f);
                    pill.Add(new IconElement(Doodle.DistrictIcon(d.type), 18f));
                    UIK.Text(pill, d.name, "tierup-district-name");
                }
            }

            if (def.unlocks.Length > 0)
            {
                UIK.Text(body, "New tools & rules", "tierup-section", "mt-12");
                var grid = UIK.Row(body, "tierup-rules");
                Color[] discs = { Doodle.MintL, Doodle.SunL, Doodle.SkyL, Doodle.GumL, Doodle.GrapeL, Doodle.TangL };
                int k = 0;
                foreach (var f in def.unlocks)
                {
                    var r = UIK.Row(grid, "tierup-rule");
                    var box = UIK.Div(r, "tierup-rule-icon");
                    box.style.backgroundColor = discs[k++ % discs.Length];
                    box.Add(new IconElement(IconFor(f), 24f));
                    var col = UIK.Col(r);
                    col.style.flexShrink = 1f;
                    var tr = UIK.Row(col);
                    UIK.Text(tr, ProgressionSystem.FeatureTitle(f), "tierup-rule-title").style.whiteSpace = WhiteSpace.Normal;
                    string key = ProgressionSystem.FeatureHotkey(f);
                    if (key != null) UIK.Text(tr, key, "tierup-rule-key");
                    var bt = UIK.Text(col, ProgressionSystem.FeatureBody(f), "tierup-rule-body");
                    bt.style.whiteSpace = WhiteSpace.Normal;
                }
            }
            else
            {
                var t = UIK.Text(body, info.tier >= ProgressionSystem.Tiers.Length - 1
                    ? "Every district of the region can now appear. City Hall will keep you busy."
                    : "More room, more districts and a bigger licence. The old core will start to feel the strain.", "tierup-blurb", "mt-12");
                t.style.whiteSpace = WhiteSpace.Normal;
            }
            var foot = UIK.Row(body, "tierup-foot");
            var next = UIK.Text(foot, gs.progress.IsMax ? "The whole region is yours. Keep millions moving!" : $"Next size: {gs.progress.Next.name}. Your new goals are in the top-left panel.", "tierup-next");
            next.style.whiteSpace = WhiteSpace.Normal;
            UIK.Button(foot, "Keep building", CloseModal, "btn-primary", "btn-mint");

            // Outside the card: the mascot jumping for joy and a "Size up!" burst.
            var jump = new Mascot(124f, 130f);
            jump.AddToClassList("tierup-mascot");
            p.Add(jump);
            var burst = new Starburst(128f, "SIZE\nUP!");
            burst.Fill = Doodle.Gum;
            burst.label.AddToClassList("tierup-burst-text");
            burst.AddToClassList("tierup-burst");
            p.Add(burst);
            jump.schedule.Execute(() =>
            {
                float t = Time.unscaledTime;
                jump.style.translate = new Translate(0f, -Mathf.Abs(Mathf.Sin(t * 3.2f)) * 16f);
                jump.Wave = Mathf.Sin(t * 6f) * 18f;
                burst.style.rotate = new Rotate(12f + Mathf.Sin(t * 2.2f) * 4f);
            }).Every(16);
        }

        readonly List<(VisualElement el, VisualElement sticker, float until)> pulses = new List<(VisualElement, VisualElement, float)>();

        /// <summary>Makes newly unlocked toolbar buttons glow for a few seconds, wearing a "new!" sticker.</summary>
        public void PulseUnlocks(Feature[] features)
        {
            foreach (var f in features)
            {
                var els = toolbar.ElementsFor(f);
                if (els == null) continue;
                foreach (var e in els)
                {
                    e.AddToClassList("tut-highlight");
                    var sticker = UIK.Text(e, "new!", "new-sticker");
                    sticker.pickingMode = PickingMode.Ignore;
                    UIK.Display(sticker);
                    pulses.Add((e, sticker, Time.unscaledTime + 9f));
                }
            }
        }

        void TickPulses()
        {
            for (int i = pulses.Count - 1; i >= 0; i--)
            {
                var (e, sticker, until) = pulses[i];
                if (Time.unscaledTime > until)
                {
                    e.RemoveFromClassList("tut-highlight");
                    e.style.borderTopColor = e.style.borderBottomColor = e.style.borderLeftColor = e.style.borderRightColor = StyleKeyword.Null;
                    sticker.RemoveFromHierarchy();
                    pulses.RemoveAt(i);
                    continue;
                }
                float a = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5f);
                var c = new Color(GrowOrange.r, GrowOrange.g, GrowOrange.b, 0.35f + 0.65f * a);
                e.style.borderTopColor = e.style.borderBottomColor = e.style.borderLeftColor = e.style.borderRightColor = c;
                sticker.style.rotate = new Rotate(12f + Mathf.Sin(Time.unscaledTime * 6f) * 6f);
            }
        }

        // ------------------------------------------------------------------ City Hall

        /// <summary>The weekly council session: approval, the mayor's verdict, warnings and elections.</summary>
        public void ShowCouncilSession(CouncilReport rep, Action onClosed)
        {
            var gs = g.State;
            var gov = gs.gov;
            var p = OpenModal(620f, onClosed, false);
            p.AddToClassList("tierup");
            p.name = "council-session";
            Kicker(p, IconKind.Hall, $"COUNCIL SESSION · WEEK {rep.week}");

            var mr = UIK.Row(p, "mt-8");
            mr.style.alignItems = Align.FlexStart;
            var pic = new MayorPortrait(64f);
            pic.Set(gov.mayor, (rep.approvalNow - 50f) / 40f);
            pic.style.marginRight = 12f;
            mr.Add(pic);
            var mc = UIK.Col(mr);
            mc.style.flexShrink = 1f;
            UIK.Text(mc, $"Mayor {gov.mayor.name}", "h2");
            var q = UIK.Text(mc, "“" + rep.quote + "”", "tierup-blurb");
            q.style.whiteSpace = WhiteSpace.Normal;

            var facts = UIK.Div(p, "tierup-facts");
            void Fact(string v, string k) => FactCard(facts, v, k);
            float d = rep.approvalNow - rep.approvalBefore;
            Fact($"{rep.approvalNow:0}% ({(d >= 0 ? "+" : "")}{d:0})", "approval · " + rep.verdict.ToLowerInvariant());
            Fact($"{rep.done} done · {rep.failed} failed", "requests this week");
            Fact($"×{rep.subsidyFactor:0.00}", "subsidy next week");

            if (rep.ultimatumIssued)
            {
                var w = UIK.Text(p, $"FINAL WARNING: the council will revoke the metro's licence if approval is still below {Balance.UltimatumApproval:0}% at next Monday's session." +
                                    (gs.options.difficulty == 0 ? " (Relaxed difficulty: they'll only grumble.)" : ""), "small", "request-ultimatum", "mt-8");
                w.style.whiteSpace = WhiteSpace.Normal;
            }
            else if (rep.ultimatumLifted)
                UIK.Text(p, "The council has withdrawn its final warning.", "small", "good", "mt-8");

            if (rep.election != null)
            {
                var e = rep.election;
                UIK.Text(p, "ELECTION RESULT", "caps", "mt-12");
                string res = e.incumbentWon
                    ? $"Mayor {e.winner.name} is re-elected with {Fmt.Pct(e.incumbentShare)} of the vote."
                    : $"{e.winner.name} beats Mayor {e.loser.name} with {Fmt.Pct(1f - e.incumbentShare)} and takes over City Hall. New agenda: {GovernmentSystem.AgendaName(e.winner.agenda)} — {GovernmentSystem.AgendaBlurb(e.winner.agenda).ToLowerInvariant()}";
                if (e.endorsed >= 0) res += e.endorsedWon ? $" You backed the winner: approval +{Balance.EndorseSwing:0}." : $" You backed the loser: approval −{Balance.EndorseSwing:0}.";
                var t = UIK.Text(p, res, "small");
                t.style.whiteSpace = WhiteSpace.Normal;
            }
            if (rep.challengerAnnounced != null)
            {
                UIK.Text(p, "ELECTION NEXT WEEK", "caps", "mt-12");
                var c = rep.challengerAnnounced;
                var t = UIK.Text(p, $"{c.name} ({GovernmentSystem.AgendaName(c.agenda)}) challenges Mayor {gov.mayor.name}. You can endorse a candidate in City Hall (M).", "small");
                t.style.whiteSpace = WhiteSpace.Normal;
            }
            var btnRow = UIK.Row(p, "mt-12");
            UIK.Spacer(btnRow);
            UIK.Button(btnRow, "Open City Hall", () =>
            {
                CloseModal();
                g.OpenWindow(WindowKind.CityHall);
            }).style.marginRight = 8f;
            UIK.Button(btnRow, "Back to work", CloseModal, "btn-primary");
        }

        public void ShowBailout(float grant, Action onClosed)
        {
            var gs = g.State;
            var p = OpenModal(580f, onClosed, false);
            p.AddToClassList("tierup");
            Kicker(p, IconKind.Hall, "CITY HALL STEPS IN", RequestView.MandateColor);
            UIK.Text(p, "The metro went bankrupt", "tierup-title");
            var body = UIK.Text(p, $"City Hall covered our debts with {Fmt.Money(grant)} so trains keep running. The money must be repaid within {Balance.BailoutRepayDays} days, in instalments if you like: use Pay in the goals panel or City Hall (M). Approval fell by {Balance.BailoutApprovalCost:0}.", "tierup-blurb");
            body.style.whiteSpace = WhiteSpace.Normal;
            var warn = UIK.Text(p, gs.options.difficulty == 0
                ? "On Relaxed difficulty City Hall will keep bailing you out, but every rescue costs approval."
                : "There won't be a second rescue: go bankrupt again, or miss the repayment, and the city takes the metro away.", "small", "request-ultimatum", "mt-8");
            warn.style.whiteSpace = WhiteSpace.Normal;
            var tips = UIK.Text(p, "Tips: remove trains on quiet lines, raise the fare a little, sell advertising, and stop building until the books are back in the black.", "small", "muted", "mt-8");
            tips.style.whiteSpace = WhiteSpace.Normal;
            var row = UIK.Row(p, "mt-12");
            UIK.Spacer(row);
            UIK.Button(row, "Understood", CloseModal, "btn-primary");
        }

        /// <summary>The end of a city: the council or the bank took the metro away.</summary>
        public void ShowGameOver(string reason)
        {
            var gs = g.State;
            CloseWindow();
            pendingModals.Clear();
            var p = OpenModal(620f, null, false);
            ModalLocked = true;
            p.AddToClassList("tierup");
            p.name = "game-over";
            Kicker(p, IconKind.Warning, "OPERATING LICENCE REVOKED", Doodle.Tomato).style.color = Doodle.Bad;
            UIK.Text(p, $"{gs.city.name}'s metro has a new operator", "tierup-title");
            var body = UIK.Text(p, reason, "tierup-blurb");
            body.style.whiteSpace = WhiteSpace.Normal;
            var facts = UIK.Div(p, "tierup-facts");
            void Fact(string v, string k) => FactCard(facts, v, k);
            Fact(gs.clock.DayNumber.ToString(), "days in charge");
            Fact(gs.progress.Current.name, "city size reached");
            Fact(Fmt.Count(gs.stats.recordRiders), "best day (trips)");
            Fact(gs.gov.completed.ToString(), "City Hall requests done");
            var row = UIK.Row(p, "mt-12");
            UIK.Spacer(row);
            var auto = SaveSystem.Info(SaveSystem.AutosaveSlot);
            if (auto.exists && auto.city == gs.city.name)
                UIK.Button(row, "Load this morning's autosave", () =>
                {
                    ModalLocked = false;
                    CloseModal();
                    g.LoadGame(SaveSystem.AutosaveSlot);
                }).style.marginRight = 8f;
            UIK.Button(row, "Main menu", () =>
            {
                ModalLocked = false;
                CloseModal();
                g.ExitToMenu();
            }, "btn-primary");
        }

        /// <summary>
        /// "Are you sure?": a doodle that says what's at stake (a bomb for demolishing, a door for leaving,
        /// the mascot for skipping the guide), the question, an optional refund chip, Cancel and the scary
        /// button in tomato on the right.
        /// </summary>
        public void Confirm(string title, string body, Action onYes, string yes = "Confirm", float refund = 0f)
        {
            var p = OpenModal(440f);
            p.AddToClassList("confirm-card");
            var head = UIK.Row(p, "confirm-head");
            VisualElement art;
            if (yes == "Leave" || yes == "Quit") art = new DoorDoodle();
            else if (yes == "Skip") art = new Mascot(74f, 76f);
            else if (yes == "Demolish" || yes == "Delete" || yes == "Remove") art = new BombDoodle();
            else art = new IconElement(IconKind.Warning, 60f);
            art.AddToClassList("confirm-art");
            head.Add(art);
            var tl = UIK.Text(head, title, "confirm-title");
            UIK.Display(tl);
            var t = UIK.Text(p, body, "confirm-body");
            t.style.whiteSpace = WhiteSpace.Normal;
            if (refund >= 1f)
            {
                var chip = UIK.Row(p, "confirm-refund");
                chip.Add(new IconElement(IconKind.Coin, 20f));
                var ct = UIK.Text(chip, "+" + Fmt.Money(refund) + " refund", "confirm-refund-text");
                UIK.Display(ct);
            }
            var row = UIK.Row(p, "confirm-buttons");
            UIK.Button(row, "Cancel", CloseModal, "confirm-cancel");
            UIK.Button(row, yes, () =>
            {
                CloseModal();
                onYes?.Invoke();
            }, "btn-danger", "confirm-yes");
        }

        /// <summary>
        /// The pause menu: a wooden PAUSED sign swinging over a cream card, Resume (Esc) in mint, the
        /// everyday buttons in white with their icons, and the ways out as wavy-underlined links.
        /// </summary>
        public void ShowPause()
        {
            var p = OpenModal(400f, () => g.OnPauseMenuClosed());
            p.AddToClassList("pause-card");
            var sign = new PausedSign();
            sign.AddToClassList("pause-sign");
            p.Add(sign);
            var gs = g.State;
            var c = gs.clock;
            UIK.Text(p, $"{gs.city.name} · Day {c.DayNumber} · {c.DayNameLong} {c.TimeString}", "pause-status");
            var col = UIK.Col(p, "pause-list");
            // Labels stay the buttons' first child; the icon sits after it and row-reverse shows it first.
            VisualElement B(VisualElement parent, string label, IconKind icon, Action a, params string[] cls)
            {
                var b = UIK.Button(parent, label, a, cls);
                b.AddToClassList("pause-btn");
                b.Add(new IconElement(icon, 24f));
                return b;
            }
            var resume = UIK.Button(col, "Resume", CloseModal, "btn-primary", "pause-resume");
            var play = new IconElement(IconKind.Play, 20f) { Color = Doodle.Ink };
            play.style.marginRight = 10f;
            resume.Insert(0, play);
            var esc = UIK.Text(resume, "Esc", "pause-esc");
            esc.pickingMode = PickingMode.Ignore;
            if (gs.challenge != null)
            {
                // Challenges can't be saved; start the same one over instead.
                var def = gs.challenge.def;
                B(col, "Restart challenge", IconKind.Faster, () =>
                {
                    CloseModal();
                    g.StartChallenge(def);
                });
            }
            else B(col, "Save game", IconKind.Floppy, () => ShowSaveLoad(true));
            B(col, "Load game", IconKind.Folder, () => ShowSaveLoad(false));
            B(col, "Settings", IconKind.Gear, ShowSettings);
            var pair = UIK.Row(col, "pause-pair");
            B(pair, "Help", IconKind.Info, ShowHelp).AddToClassList("pause-btn-narrow");
            B(pair, "Achievements", IconKind.Trophy, ShowAchievements).AddToClassList("pause-btn-wide");
            var dash = new Squiggle(Doodle.Sand, 3f);
            dash.style.flexGrow = 1f;
            UIK.Div(p, "pause-dash").Add(dash);
            var links = UIK.Row(p, "pause-links");
            Link(links, "Main menu", Doodle.Gum, false, () => Confirm("Leave this city?", "Unsaved progress since the last save will be lost. The game autosaves every morning.", g.ExitToMenu, "Leave"));
            UIK.Spacer(links);
            Link(links, "Quit to desktop", Doodle.Tomato, true, () => Confirm("Quit the game?", "Unsaved progress since the last save will be lost.", g.Quit, "Quit"));
        }

        /// <summary>A text link with a wavy underline (the quiet way out of a dialog).</summary>
        static VisualElement Link(VisualElement parent, string text, Color wave, bool bad, Action onClick)
        {
            var l = UIK.Col(parent, "wavy-link");
            var t = UIK.Text(l, text, "wavy-link-text");
            if (bad) t.AddToClassList("bad");
            t.pickingMode = PickingMode.Ignore;
            var sq = new Squiggle(wave, 2.2f);
            sq.AddToClassList("wavy-link-line");
            l.Add(sq);
            l.AddManipulator(new Clickable(() =>
            {
                Sfx.Click();
                onClick();
            }));
            return l;
        }

        static readonly string[] DifficultyDescs =
        {
            "More starting money, cheaper construction, slower growth and lighter fines.",
            "The intended experience.",
            "Tight budget, expensive construction, faster growth and harsher fines."
        };

        /// <summary>
        /// "Found a new city": name and seed (with a dice roll), difficulty as three picture cards whose
        /// numbers show up as chips, the guided-start switch and a note on how cities grow.
        /// </summary>
        public void ShowNewGame(bool tutorialMode = false)
        {
            var opts = new NewGameOptions
            {
                seed = UnityEngine.Random.Range(1, 99999),
                difficulty = 1,
                tutorial = tutorialMode
            };
            opts.cityName = NameBank.RandomCityName(new Rng(opts.seed));
            var p = OpenModal(640f);
            ModalHead(p, "Found a new city", CloseModal, "head-mint");
            UIK.Text(p, "Every city is generated from its seed: coastline, rivers, districts and where the city will grow.", "newcity-blurb").style.whiteSpace = WhiteSpace.Normal;

            var fields = UIK.Row(p, "newcity-fields");
            var nameCol = UIK.Col(fields, "newcity-name");
            UIK.Text(nameCol, "City name", "newcity-label");
            var nameField = new TextField { value = opts.cityName, maxLength = 28 };
            nameCol.Add(nameField);
            var seedCol = UIK.Col(fields, "newcity-seed");
            UIK.Text(seedCol, "Seed", "newcity-label");
            var seedRow = UIK.Row(seedCol);
            var seedField = new TextField { value = opts.seed.ToString(), maxLength = 9 };
            seedField.style.flexGrow = 1f;
            seedRow.Add(seedField);
            var roll = UIK.Button(seedRow, "Roll", () =>
            {
                int s = UnityEngine.Random.Range(1, 99999);
                seedField.value = s.ToString();
                nameField.value = NameBank.RandomCityName(new Rng(s));
            }, "newcity-roll");
            roll.Add(new IconElement(IconKind.Dice, 22f));
            UIK.Tooltip(roll, "Random city");

            UIK.Text(p, "Difficulty", "newcity-label", "mt-12");
            var cards = UIK.Row(p, "difficulty-cards");
            var desc = UIK.Text(p, DifficultyDescs[opts.difficulty], "difficulty-desc");
            desc.style.whiteSpace = WhiteSpace.Normal;
            var chips = UIK.Row(p, "difficulty-chips");
            Color[] bgs = { Doodle.MintL, Doodle.SunL, Doodle.TangL };
            float[] moods = { 1f, 0.35f, -0.45f };
            var cardEls = new List<VisualElement>();
            void ShowChips(int d)
            {
                chips.Clear();
                void Chip(string v, string k, bool cash = false)
                {
                    var c = UIK.Row(chips, "difficulty-chip");
                    if (cash) c.AddToClassList("difficulty-chip-cash");
                    var vl = UIK.Text(c, v, "difficulty-chip-v");
                    UIK.Display(vl);
                    UIK.Text(c, k, "difficulty-chip-k");
                }
                string Pct(float m) => m < 0.995f ? "−" + Mathf.RoundToInt((1f - m) * 100f) + "%" : m > 1.005f ? "+" + Mathf.RoundToInt((m - 1f) * 100f) + "%" : "normal";
                Chip(Fmt.Money(Balance.StartMoney(d)), "starting cash", true);
                Chip(Pct(Balance.CostMultiplier(d)), "construction");
                Chip(Balance.GrowthPace(d) < 0.99f ? "slower" : Balance.GrowthPace(d) > 1.01f ? "faster" : "normal", "growth pace");
                Chip(Balance.FineMultiplier(d) < 0.99f ? "lighter" : Balance.FineMultiplier(d) > 1.01f ? "harsher" : "normal", "fines");
            }
            void Pick(int d)
            {
                opts.difficulty = d;
                desc.text = DifficultyDescs[d];
                for (int i = 0; i < cardEls.Count; i++)
                {
                    bool on = i == d;
                    cardEls[i].EnableInClassList("difficulty-card-on", on);
                    cardEls[i].style.backgroundColor = on ? bgs[i] : Doodle.Paper;
                }
                ShowChips(d);
            }
            for (int i = 0; i < Balance.DifficultyNames.Length; i++)
            {
                int d = i;
                var card = UIK.Col(cards, "difficulty-card");
                card.name = "difficulty-" + i;
                var face = new IconElement(IconKind.Smile, 46f) { Color = new[] { Doodle.Mint, Doodle.Sun, Doodle.Tang }[i], Mood = moods[i] };
                face.pickingMode = PickingMode.Ignore;
                card.Add(face);
                var lab = UIK.Text(card, Balance.DifficultyNames[i], "difficulty-card-label");
                UIK.Display(lab);
                lab.pickingMode = PickingMode.Ignore;
                card.AddManipulator(new Clickable(() =>
                {
                    Sfx.Click();
                    Pick(d);
                }));
                cardEls.Add(card);
            }
            Pick(opts.difficulty);

            var guided = UIK.Row(p, "newcity-guided");
            guided.Add(new Mascot(40f, 42f));
            var guidedCol = UIK.Col(guided);
            guidedCol.style.flexGrow = 1f;
            guidedCol.style.flexShrink = 1f;
            UIK.Text(guidedCol, "Guided start", "newcity-guided-title");
            var gtext = UIK.Text(guidedCol, "Step-by-step help while you build your first line. Recommended for your first city.", "newcity-guided-text");
            gtext.style.whiteSpace = WhiteSpace.Normal;
            UIK.Switch(guided, opts.tutorial, v => opts.tutorial = v);
            var how = UIK.Row(p, "newcity-how");
            how.Add(new IconElement(IconKind.City, 30f));
            var howText = UIK.Text(how, "Every city starts as a small town on a small map with a few tools. Hit its goals and it grows overnight: the map expands, new districts appear and new tools unlock, all the way to a 48-district metropolis.", "newcity-how-text");
            howText.style.whiteSpace = WhiteSpace.Normal;

            var row = UIK.Row(p, "newcity-buttons");
            UIK.Button(row, "Cancel", CloseModal);
            UIK.Spacer(row);
            UIK.Button(row, "Found city!", () =>
            {
                opts.cityName = string.IsNullOrWhiteSpace(nameField.value) ? "Metropolis" : nameField.value.Trim();
                if (!int.TryParse(seedField.value, out opts.seed)) opts.seed = Mathf.Abs(seedField.value.GetHashCode()) % 99999 + 1;
                CloseModal();
                g.StartNewGame(opts);
            }, "btn-primary", "newcity-go");
        }

        /// <summary>
        /// Saving and loading: each slot is a polaroid taped to the page, with a doodled snapshot of its city,
        /// the slot's name, the city and day, and a Save here / Load button.
        /// </summary>
        public void ShowSaveLoad(bool save)
        {
            var p = OpenModal(620f);
            p.AddToClassList("saveload-card");
            var head = UIK.Row(p, "saveload-head");
            head.Add(new IconElement(save ? IconKind.Floppy : IconKind.Folder, 40f));
            var title = UIK.Text(head, save ? "Save game" : "Load game", "saveload-title");
            UIK.Display(title);
            UIK.Spacer(head);
            UIK.IconButton(head, IconKind.Close, CloseModal, "Close", "icon-btn", 20f);
            if (Metro.Platform.CloudSync.Enabled)
            {
                var cloud = UIK.Row(p, "saveload-cloud");
                cloud.Add(new IconElement(IconKind.Cloud, 22f) { Color = Doodle.SkyL });
                UIK.Text(cloud, "Saves sync with Steam Cloud.", "small", "muted");
            }
            var grid = UIK.Row(p, "polaroids");
            int n = 0;
            Color[] tapes = { Doodle.Gum, Doodle.Sky, Doodle.Mint, Doodle.Sun };
            foreach (var slot in SaveSystem.Slots)
            {
                var info = SaveSystem.Info(slot);
                if (save && slot == SaveSystem.AutosaveSlot) continue;
                var card = UIK.Col(grid, "polaroid");
                card.style.rotate = new Rotate(n % 2 == 0 ? -2f : 1.6f);
                var tape = UIK.Div(card, "polaroid-tape");
                tape.style.backgroundColor = tapes[n % tapes.Length];
                tape.style.rotate = new Rotate(n % 2 == 0 ? 4f : -5f);
                tape.pickingMode = PickingMode.Ignore;
                var photo = new PolaroidPhoto(info.exists ? (info.city + slot).GetHashCode() : 0, info.exists);
                photo.AddToClassList("polaroid-photo");
                card.Add(photo);
                var labelRow = UIK.Row(card, "polaroid-label");
                var tag = UIK.Text(labelRow, slot == SaveSystem.AutosaveSlot ? "AUTO" : SaveSystem.SlotLabel(slot).ToUpperInvariant(), "polaroid-tag");
                UIK.Display(tag);
                UIK.Text(labelRow, info.exists ? info.city : "Empty", "polaroid-city");
                string size = string.IsNullOrEmpty(info.size) ? "" : info.size + " · ";
                UIK.Text(card, info.exists ? $"{size}Day {info.day} · {Fmt.Money(info.money)} · {info.savedAt}" : "Nothing saved here yet.", "polaroid-meta").style.whiteSpace = WhiteSpace.Normal;
                var actions = UIK.Row(card, "polaroid-actions");
                string s = slot;
                if (info.exists)
                {
                    var del = UIK.IconButton(actions, IconKind.Demolish, () => Confirm("Delete this save?",
                        $"{SaveSystem.SlotLabel(s)} ({info.city}, day {info.day}) will be deleted{(Metro.Platform.CloudSync.Enabled ? " here and from Steam Cloud" : "")}. This can't be undone.",
                        () =>
                        {
                            SaveSystem.Delete(s);
                            ShowSaveLoad(save);
                        }, "Delete"), "Delete this save", "icon-btn", 18f);
                    del.AddToClassList("icon-btn-sm");
                }
                UIK.Spacer(actions);
                if (save)
                {
                    UIK.Button(actions, "Save here", () =>
                    {
                        if (g.SaveGame(s))
                        {
                            UIRoot.I.Toast("Game saved", ToastKind.Good, 2f);
                            CloseModal();
                        }
                    }, "btn-small", "btn-primary");
                }
                else if (info.exists)
                {
                    UIK.Button(actions, "Load", () =>
                    {
                        CloseModal();
                        g.LoadGame(s);
                    }, "btn-small", "btn-primary", "btn-mint");
                }
                n++;
            }
        }

        // ------------------------------------------------------------------ frame

        public void Tick(float dt)
        {
            root.Tick(dt);
            root.SetToastsLow(modal != null || window != null);
            if (modal != null && (InputX.KeyDown(UnityEngine.InputSystem.Key.Enter) || InputX.KeyDown(UnityEngine.InputSystem.Key.NumpadEnter)))
                PressPrimary();
            TickScreens(dt);
            if (modal == null && menu == null && pendingModals.Count > 0) pendingModals.Dequeue()();
            if (menu != null)
            {
                menu.Tick(dt);
                return;
            }
            if (g.State == null) return;
            if (UIK.Shown(hud))
            {
                top.Refresh();
                toolbar.Refresh();
                lines.Refresh();
                minimap.Refresh();
                mapKey.Refresh();
                goals.Refresh();
                feed.Refresh();
                inspector.Refresh(dt);
                // An open inspector gets the room under the feed; the feed shortens to make space.
                feed.root.EnableInClassList("feed-short", UIK.Shown(inspector.root));
            }
            window?.Tick(dt);
            tutorial.Tick(dt);
            TickPulses();
        }

        public void LateTick()
        {
            labels.Update();
        }
    }

    /// <summary>
    /// Title screen over a living demo city: a cream card with the candy "One More Station" logo, a
    /// tagline, a stack of chunky, slightly wonky buttons (New city the biggest, with an arrow pointing at
    /// it), the train mascot waving from the card's edge and a "Live demo city!" sticker.
    /// </summary>
    public sealed class MainMenu
    {
        readonly GameRoot g;
        public readonly VisualElement root;
        readonly Mascot mascot;
        readonly VisualElement mascotBob, demoSticker;

        public MainMenu(GameRoot g, VisualElement layer)
        {
            this.g = g;
            root = UIK.Div(layer, "layer");
            root.pickingMode = PickingMode.Ignore;
            var card = UIK.Div(root, "menu-side");
            var logoTex = Resources.Load<Texture2D>("UI/MenuLogo");
            if (logoTex != null)
            {
                var logo = UIK.Div(card, "menu-logo");
                logo.style.backgroundImage = new StyleBackground(logoTex);
                logo.pickingMode = PickingMode.Ignore;
            }
            else
            {
                // No logo texture: the name in candy letters.
                var title = UIK.Row(card, "menu-title");
                Color[] letterColors = { Doodle.Sky, Doodle.Grape, Doodle.Tang, Doodle.Gum, Doodle.Tomato, Doodle.Sun, Doodle.Mint };
                const string word = "ONE MORE STATION";
                for (int i = 0; i < word.Length; i++)
                {
                    if (word[i] == ' ')
                    {
                        UIK.Div(title, "menu-letter-gap");
                        continue;
                    }
                    var l = UIK.Text(title, word[i].ToString(), "menu-letter");
                    l.style.color = letterColors[i % letterColors.Length];
                    l.style.rotate = new Rotate(i % 2 == 0 ? -5f : 4f);
                }
            }
            UIK.Text(card, "a metro network management sim", "menu-tagline");
            UIK.Text(card, "No story, just a city that keeps growing and people who need to get around.", "menu-blurb");

            var nav = UIK.Col(card, "menu-nav");
            var auto = SaveSystem.Info(SaveSystem.AutosaveSlot);
            if (auto.exists)
                MenuButton(nav, IconKind.Play, "Continue", $"{auto.city} · Day {auto.day}", Doodle.Mint, 0, () => g.LoadGame(SaveSystem.AutosaveSlot));
            var hero = MenuButton(nav, IconKind.City, "New city", "Start small, grow a metropolis", Doodle.Sun, 1, () => g.UI.ShowNewGame(!auto.exists));
            hero.AddToClassList("menu-btn-hero");
            var arrow = new CurlyArrow();
            arrow.AddToClassList("menu-hero-arrow");
            hero.Add(arrow);
            MenuButton(nav, IconKind.Info, "Tutorial", "A guided first city", Doodle.Sky, 2, () => g.UI.ShowNewGame(true));
            MenuButton(nav, IconKind.Folder, "Load game", "Four slots, incl. the autosave", Doodle.Gum, 3, () => g.UI.ShowSaveLoad(false));
            var pair = UIK.Row(nav, "menu-pair");
            var ch = MenuButton(pair, IconKind.Faster, "Challenges", null, Doodle.SkyL, 0, () => g.UI.ShowChallenges(), true);
            Chip(ch, Metro.Challenges.MenuSummary().Replace(" of ", "/").Replace(" stars", ""), IconKind.Star);
            UIK.Tooltip(ch, () => "Short puzzles with a goal and a clock: " + Metro.Challenges.MenuSummary() + ".");
            var ach = MenuButton(pair, IconKind.Trophy, "Achievements", null, Doodle.Tang, 1, () => g.UI.ShowAchievements(), true);
            Chip(ach, $"{Metro.Platform.Achievements.UnlockedCount}/{Metro.Platform.Achievements.All.Length}", null);
            var pair2 = UIK.Row(nav, "menu-pair");
            MenuButton(pair2, IconKind.Gear, "Settings", null, Doodle.GrapeL, 2, () => g.UI.ShowSettings(), true);
            MenuButton(pair2, IconKind.Star, "Credits", null, Doodle.Paper, 3, () => g.UI.ShowCredits(), true);
            MenuButton(pair2, IconKind.Door, "Quit", null, Doodle.TomatoL, 0, () => g.Quit(), true).AddToClassList("menu-btn-quit");
            string player = Metro.Platform.PlatformServices.Current.PlayerName;
            UIK.Text(card, $"Version {Application.version} · {Metro.Platform.PlatformServices.Status}{(string.IsNullOrEmpty(player) ? "" : " · " + player)}", "menu-footer");

            // The mascot peeks out from behind the card and says hello.
            var buddy = UIK.Div(root, "menu-mascot");
            buddy.pickingMode = PickingMode.Ignore;
            var bubble = UIK.Div(buddy, "menu-bubble");
            UIK.Text(bubble, "Ready to move some people?", "menu-bubble-text");
            var tail = new BubbleTail();
            tail.AddToClassList("menu-bubble-tail");
            bubble.Add(tail);
            mascotBob = UIK.Div(buddy, "menu-mascot-bob");
            mascot = new Mascot();
            mascotBob.Add(mascot);
            NoPick(buddy);

            demoSticker = new Starburst(132f, "LIVE\nDEMO\nCITY!");
            ((Starburst)demoSticker).Fill = Doodle.Sky;
            ((Starburst)demoSticker).label.AddToClassList("menu-demo-text");
            demoSticker.AddToClassList("menu-demo");
            root.Add(demoSticker);
        }

        static void NoPick(VisualElement e)
        {
            e.pickingMode = PickingMode.Ignore;
            foreach (var c in e.Children()) NoPick(c);
        }

        static VisualElement MenuButton(VisualElement parent, IconKind icon, string label, string desc, Color color, int index, Action onClick, bool small = false)
        {
            var b = UIK.Div(parent, "menu-btn", "menu-tilt-" + index % 4);
            if (small) b.AddToClassList("menu-btn-small");
            b.style.backgroundColor = color;
            var dot = UIK.Div(b, "menu-btn-dot");
            dot.pickingMode = PickingMode.Ignore;
            dot.Add(new IconElement(icon, small ? 22f : 28f));
            var col = UIK.Col(b);
            col.pickingMode = PickingMode.Ignore;
            col.style.flexShrink = 1f;
            var l = UIK.Text(col, label, "menu-btn-label");
            l.pickingMode = PickingMode.Ignore;
            if (desc != null)
            {
                var d = UIK.Text(col, desc, "menu-btn-desc");
                d.pickingMode = PickingMode.Ignore;
            }
            b.AddManipulator(new Clickable(() =>
            {
                Sfx.Click();
                onClick();
            }));
            return b;
        }

        static void Chip(VisualElement button, string text, IconKind? icon)
        {
            UIK.Spacer(button).pickingMode = PickingMode.Ignore;
            var chip = UIK.Row(button, "menu-btn-chip");
            chip.pickingMode = PickingMode.Ignore;
            if (icon.HasValue) chip.Add(new IconElement(icon.Value, 15f));
            var c = UIK.Text(chip, text, "menu-btn-chip-text");
            c.pickingMode = PickingMode.Ignore;
            UIK.Display(c);
        }

        public void Tick(float dt)
        {
            float t = Time.unscaledTime;
            mascotBob.style.translate = new Translate(0f, -3f - 3f * Mathf.Sin(t * Mathf.PI));
            mascot.Wave = Mathf.Lerp(-8f, 14f, 0.5f + 0.5f * Mathf.Sin(t * 2f * Mathf.PI / 1.4f));
            demoSticker.style.rotate = new Rotate(-10f + Mathf.Sin(t * 1.3f) * 2f);
        }
    }
}
