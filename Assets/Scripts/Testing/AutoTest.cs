using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

namespace Metro
{
    /// <summary>
    /// Scripted runs for automated verification: launch the player with
    /// "-autotest shots -shotdir &lt;dir&gt;" and it walks through scenarios, saving screenshots and a log.
    /// </summary>
    public sealed partial class AutoTest : MonoBehaviour
    {
        string outDir;
        string scenario;
        StreamWriter log;

        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }

        /// <summary>True while a scripted test drives the game (no saving on quit, no Steam).</summary>
        public static bool Running { get; private set; }

        public static bool TryStart(GameRoot g)
        {
            var args = Environment.GetCommandLineArgs();
            int idx = Array.IndexOf(args, "-autotest");
            if (idx < 0) return false;
            Running = true;
            var t = g.gameObject.AddComponent<AutoTest>();
            t.scenario = idx + 1 < args.Length && !args[idx + 1].StartsWith("-") ? args[idx + 1] : "shots";
            t.outDir = Arg("-shotdir") ?? Path.Combine(Application.persistentDataPath, "autotest");
            Directory.CreateDirectory(t.outDir);
            t.log = new StreamWriter(Path.Combine(t.outDir, "autotest_log.txt"), false) { AutoFlush = true };
            Application.logMessageReceived += t.OnLog;
            t.StartCoroutine(t.Run(g));
            return true;
        }

        void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert || type == LogType.Warning)
                log?.WriteLine($"[{type}] {condition}\n{stackTrace}");
        }

        void Note(string s)
        {
            log?.WriteLine(s);
            Debug.Log("[AutoTest] " + s);
        }

        IEnumerator Wait(float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end) yield return null;
        }

        IEnumerator Shot(string name)
        {
            yield return null;
            yield return new WaitForEndOfFrame();
            string path = Path.Combine(outDir, name + ".png");
            ScreenCapture.CaptureScreenshot(path);
            yield return null;
            yield return null;
            yield return Wait(0.3f);
            Note("shot " + name);
        }

        static void AdvanceTo(GameState gs, int day, float minuteOfDay)
        {
            double target = day * (double)Balance.MinutesPerDay + minuteOfDay;
            int guard = 0;
            while (gs.clock.totalMinutes < target && guard++ < 100000) gs.Advance(Mathf.Min(10f, (float)(target - gs.clock.totalMinutes) + 0.01f));
        }

        IEnumerator Run(GameRoot g)
        {
            yield return null;
            Note($"Scenario {scenario}, screen {Screen.width}x{Screen.height}");
            if (scenario == "shots") yield return Shots(g);
            else if (scenario == "draw") yield return DrawTest(g);
            else if (scenario == "play") yield return PlayTest(g);
            else if (scenario == "tutorial") yield return TutorialTest(g);
            else if (scenario == "hall") yield return HallTest(g);
            else if (scenario == "challenge") yield return ChallengeTest(g);
            else if (scenario == "ui") yield return UiTest(g);
            else if (scenario == "demo") yield return DemoTest(g);
            else if (scenario == "trailer") yield return TrailerTest(g);
            Note("done");
            Application.logMessageReceived -= OnLog;
            log?.Close();
            yield return Wait(0.5f);
            Application.Quit();
        }

        IEnumerator Shots(GameRoot g)
        {
            g.StartMenu();
            yield return Wait(3f);
            yield return Shot("01_menu");

            // A brand-new small town: small framed map, a handful of tools.
            g.StartNewGame(new NewGameOptions { seed = 2024, cityName = "Port Avalon", difficulty = 1 });
            yield return Wait(1.5f);
            var gs = g.State;
            Note($"small town: map {gs.city.playArea.width:0}x{gs.city.playArea.height:0} km, camera max size {g.CamCtl.Size:0.0}, tier {gs.progress.Current.name}");
            yield return Shot("02_new_city");

            var starter = AutoPlanner.BuildStarterLine(gs);
            if (starter != null)
            {
                gs.build.BuildDepot(starter.stops[starter.stops.Count / 2]);
                gs.build.AddTrain(starter);
                gs.build.AddTrain(starter);
            }
            Note($"starter line: {(starter != null ? starter.stops.Count + " stops" : "failed")}, lines {gs.net.LineCount}/{gs.progress.MaxLines}, money {Fmt.Money(gs.econ.money)}");
            gs.progress.holdTierUps = true;
            AdvanceTo(gs, 0, 8 * 60 + 15);
            g.SetSpeed(0);
            yield return Wait(2.5f);
            yield return Shot("03_rush_hour");
            if (starter != null) g.Select(SelKind.Line, starter.id);
            yield return Wait(1f);
            yield return Shot("03b_small_town_line");
            g.ClearSelection();

            // Grow the town: the card, then the bigger map.
            gs.progress.holdTierUps = false;
            gs.progress.TierUp();
            yield return Wait(1.2f);
            Note($"after growth: {gs.progress.Current.name}, modal open {g.UI.ModalOpen}, map {gs.city.playArea.width:0}x{gs.city.playArea.height:0}");
            yield return Shot("03c_growth_card");
            g.UI.CloseModal();
            yield return Wait(3.5f);
            yield return Shot("03d_bigger_map");
            gs.progress.TierUp();
            yield return Wait(0.6f);
            yield return Shot("03e_growth_card_market_town");
            g.UI.CloseModal();
            yield return Wait(3f);
            foreach (var d in gs.city.districts)
            {
                if (!d.revealed || d.id < 2 || !gs.city.InPlayArea(d.center) || gs.net.LineCount >= gs.progress.MaxLines) continue;
                var nl = AutoPlanner.BuildAlong(gs, new System.Collections.Generic.List<Vector2> { d.center, gs.city.districts[0].center });
                if (nl == null) continue;
                gs.build.BuildDepot(nl.stops[nl.stops.Count / 2]);
                gs.build.AddTrain(nl);
                gs.build.AddTrain(nl);
            }
            AdvanceTo(gs, 1, 8 * 60 + 15);
            g.SetSpeed(0);
            yield return Wait(2f);
            yield return Shot("03f_market_town");

            MetroLine busiest = null;
            foreach (var l in gs.net.AliveLines())
                if (busiest == null || l.MaxSegLoad > busiest.MaxSegLoad) busiest = l;
            if (busiest != null)
            {
                g.Select(SelKind.Line, busiest.id);
                yield return Wait(1f);
                yield return Shot("04_line_panel");
            }
            Station worst = null;
            foreach (var s in gs.net.AliveStations())
                if (worst == null || s.Crowding > worst.Crowding) worst = s;
            if (worst != null)
            {
                g.Select(SelKind.Station, worst.id);
                g.FocusOn(worst.pos, 3f);
                yield return Wait(1.5f);
                yield return Shot("05_station_panel");
            }
            g.ClearSelection();
            g.SetPaused(true);
            g.OpenWindow(WindowKind.Finance);
            yield return Wait(0.8f);
            yield return Shot("06_finance");
            g.OpenWindow(WindowKind.Stats);
            yield return Wait(0.8f);
            yield return Shot("07_stats");
            g.OpenWindow(WindowKind.City);
            yield return Wait(0.8f);
            yield return Shot("08_city");
            g.CloseWindow();
            g.ToggleOverlay(DataOverlay.Population);
            g.FocusOn(gs.city.center, 9f);
            yield return Wait(2.5f);
            yield return Shot("09_overlay_population");
            g.ToggleOverlay(DataOverlay.Population);

            g.SetPaused(false);
            AdvanceTo(gs, 0, 17 * 60 + 50);
            g.ToggleOverview();
            yield return Wait(2f);
            yield return Shot("10_overview_small");
            g.ToggleOverview();
            AdvanceTo(gs, 1, 2 * 60 + 30);
            g.SetPaused(true);
            g.FocusOn(gs.city.center, 6f);
            yield return Wait(1.5f);
            yield return Shot("10b_night");
            g.SetPaused(false);

            // A big, mature city for the zoom-out money shot.
            var big = new GameState(new NewGameOptions { seed = 31415, cityName = "Kestrel Bay", difficulty = 1, startTier = ProgressionSystem.Tiers.Length - 1 });
            big.growth.RevealSilently(26);
            AutoPlanner.BuildNetwork(big, 9, true, 0.45f, 5);
            big.gov.SyncWithProgress(false);
            AdvanceTo(big, 1, 8 * 60 + 20);
            g.EnterGame(big);
            g.SetSpeed(0);
            Note($"big network: {big.net.StationCount} stations, {big.net.LineCount} lines, {big.trains.AliveCount} trains, {Fmt.Count(big.stats.ridersToday)} riders so far today");
            g.ToggleOverview();
            yield return Wait(2.5f);
            g.SetSpeed(2);
            float t0 = Time.realtimeSinceStartup;
            int frames = 0;
            float worstFrame = 0f;
            while (Time.realtimeSinceStartup - t0 < 4f)
            {
                yield return null;
                frames++;
                worstFrame = Mathf.Max(worstFrame, Time.unscaledDeltaTime);
            }
            Note($"perf (big city, 4x speed, overview): {frames / (Time.realtimeSinceStartup - t0):0.0} fps avg, worst frame {worstFrame * 1000f:0} ms, routing {big.routing.lastComputeMs:0.0} ms, demand {big.demand.lastComputeMs:0.0} ms");
            g.SetSpeed(0);
            yield return Shot("11_overview_big");
            g.UI.SetHudVisible(false);
            yield return Wait(0.5f);
            yield return Shot("12_overview_clean");
            g.UI.SetHudVisible(true);
            g.ToggleOverview();
            g.FocusOn(big.city.center, 3.2f);
            yield return Wait(2f);
            yield return Shot("13_closeup");

            g.StartNewGame(new NewGameOptions { seed = 99, cityName = "Lindmere", tutorial = true });
            yield return Wait(1.5f);
            yield return Shot("14_tutorial");
        }

        // ------------------------------------------------------------------ simulated input helpers

        GameRoot game;

        IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        Vector2 ScreenOf(Vector2 world) => game.CamCtl.WorldToScreen(world);

        IEnumerator Hover(Vector2 world)
        {
            InputX.SimMouse(ScreenOf(world));
            yield return Frames(3);
        }

        IEnumerator Click(Vector2 world, bool right = false)
        {
            Vector2 s = ScreenOf(world);
            InputX.SimMouse(s);
            yield return Frames(2);
            InputX.SimMouse(s, !right, right);
            yield return Frames(2);
            InputX.SimMouse(s);
            yield return Frames(3);
        }

        IEnumerator Press(UnityEngine.InputSystem.Key k)
        {
            InputX.SimKey(k, true);
            yield return Frames(2);
            InputX.SimKey(k, false);
            yield return Frames(2);
        }

        static UnityEngine.UIElements.VisualElement FindButton(string text)
        {
            UnityEngine.UIElements.VisualElement found = null;
            Metro.UI.UIRoot.I.Root.Query<UnityEngine.UIElements.VisualElement>(className: "btn").ForEach(e =>
            {
                if (found == null && e.childCount > 0 && e[0] is UnityEngine.UIElements.Label l && l.text == text && e.panel != null) found = e;
            });
            return found;
        }

        /// <summary>Clicks a UI element through UI Toolkit's own pointer event dispatch.</summary>
        IEnumerator UiClick(UnityEngine.UIElements.VisualElement e, string what)
        {
            if (e == null)
            {
                Note("UI element not found: " + what);
                yield break;
            }
            var panel = e.panel;
            Vector2 pos = e.worldBound.center;
            var down = new Event { type = EventType.MouseDown, mousePosition = pos, button = 0, clickCount = 1 };
            using (var pe = UnityEngine.UIElements.PointerDownEvent.GetPooled(down)) panel.visualTree.SendEvent(pe);
            yield return Frames(2);
            var up = new Event { type = EventType.MouseUp, mousePosition = pos, button = 0, clickCount = 1 };
            using (var pe = UnityEngine.UIElements.PointerUpEvent.GetPooled(up)) panel.visualTree.SendEvent(pe);
            yield return Frames(3);
            Note("ui click: " + what);
        }

        /// <summary>Finds an element of a class that shows (or contains a label showing) the given text.</summary>
        static UnityEngine.UIElements.VisualElement FindText(string text, string cls)
        {
            UnityEngine.UIElements.VisualElement found = null;
            Metro.UI.UIRoot.I.Root.Query<UnityEngine.UIElements.VisualElement>(className: cls).ForEach(e =>
            {
                if (found != null || e.panel == null) return;
                if (e is UnityEngine.UIElements.Label own && own.text == text) found = e;
                else if (e.Q<UnityEngine.UIElements.Label>() is UnityEngine.UIElements.Label l && l.text == text) found = e;
            });
            return found;
        }

        /// <summary>City Hall: requests, landmarks, the window, elections, sessions, settings, achievements, bailout, game over.</summary>
        IEnumerator HallTest(GameRoot g)
        {
            game = g;
            g.StartNewGame(new NewGameOptions { seed = 2024, cityName = "Port Avalon", difficulty = 1, startTier = 2 });
            yield return Wait(1f);
            var gs = g.State;
            var gov = gs.gov;
            gs.progress.holdTierUps = true;
            AutoPlanner.BuildNetwork(gs, 3, true, 0.45f, 3);
            AdvanceTo(gs, 0, 9 * 60);
            g.SetPaused(true);
            InputX.simulate = true;
            Note($"start: {gs.progress.Current.name}, council {gov.started}, approval {gov.approval:0}, mayor {gov.mayor.name} ({GovernmentSystem.AgendaName(gov.mayor.agenda)}), lines {gs.net.LineCount}");
            var near = gov.Propose(RequestKind.StationNear);
            var ev = gov.Propose(RequestKind.Event);
            var fare = gov.Propose(RequestKind.FareCap);
            Note($"proposals: {near?.title} | {ev?.title} | {fare?.title}; open {gov.open.Count}");
            g.FocusOn(gs.city.playArea.center, 9f);
            yield return Wait(1.2f);
            yield return Shot("h01_requests_panel");
            yield return UiClick(FindButton("Accept"), "Accept");
            yield return Wait(0.4f);
            Note($"after accept: states {string.Join(",", gov.open.ConvertAll(r => r.title + "=" + r.state))}");
            var lm = near != null ? gov.LandmarkById(near.landmarkId) : null;
            if (lm != null)
            {
                g.FocusOn(lm.pos, 4.5f);
                yield return Wait(1.5f);
                yield return Shot("h02_landmark");
            }
            yield return Press(UnityEngine.InputSystem.Key.M);
            yield return Wait(0.8f);
            Note($"window after M: {g.UI.OpenWindowKind}");
            yield return Shot("h03_city_hall");
            g.CloseWindow();

            gov.AnnounceElection();
            g.OpenWindow(WindowKind.CityHall);
            yield return Wait(0.8f);
            yield return UiClick(Metro.UI.UIRoot.I.Root.Q("endorse-1"), "endorse challenger");
            yield return Wait(0.6f);
            Note($"endorsed {gov.endorsed}, challenger {gov.challenger?.name}, election day {gov.nextElectionDay}");
            yield return Shot("h04_election");
            g.CloseWindow();

            float before = gov.approval;
            AdvanceTo(gs, 7, Balance.CouncilSessionMinute + 1);
            yield return Wait(1.2f);
            Note($"session: modal {g.UI.ModalOpen}, approval {before:0} -> {gov.approval:0}, mayor {gov.mayor.name}, done {gov.completed}, failed {gov.failed}, open {gov.open.Count}");
            yield return Shot("h05_council_session");
            yield return UiClick(FindButton("Back to work"), "Back to work");
            yield return Wait(0.5f);

            g.ShowPauseMenu(true);
            yield return Wait(0.5f);
            yield return UiClick(FindButton("Achievements"), "Achievements");
            yield return Wait(0.8f);
            Note($"achievements unlocked (test profile): {Metro.Platform.Achievements.UnlockedCount}/{Metro.Platform.Achievements.All.Length}");
            yield return Shot("h06_achievements");
            g.UI.CloseModal();
            g.ShowPauseMenu(true);
            yield return Wait(0.4f);
            yield return UiClick(FindButton("Settings"), "Settings");
            yield return Wait(0.6f);
            yield return Shot("h07_settings_game");
            foreach (var (tab, shot) in new[] { ("Audio", "h08_settings_audio"), ("Display", "h09_settings_display"), ("Controls", "h10_settings_controls") })
            {
                yield return UiClick(FindText(tab, "seg-item"), tab);
                yield return Wait(0.5f);
                yield return Shot(shot);
            }
            yield return UiClick(FindButton("Done"), "Done");
            yield return Wait(0.4f);

            gs.econ.money = -(Balance.BankruptBase + gs.econ.NetworkValue() * 0.25f + 6_000_000f);
            AdvanceTo(gs, gs.clock.Day + 1, 1f);
            yield return Wait(1f);
            Note($"bailout: modal {g.UI.ModalOpen}, bailouts {gov.bailouts}, money {Fmt.Money(gs.econ.money)}, repay open {gov.open.Exists(r => r.kind == RequestKind.RepayBailout)}");
            yield return Shot("h11_bailout");
            yield return UiClick(FindButton("Understood"), "Understood");
            yield return Wait(0.4f);

            gov.RevokeLicence("The council voted to hand the metro to another operator (scripted test).");
            yield return Wait(1f);
            Note($"game over: modal {g.UI.ModalOpen}, locked {g.UI.ModalLocked}, sim stopped {gov.gameOver}");
            yield return Press(UnityEngine.InputSystem.Key.Escape);
            Note($"after Esc: modal still open {g.UI.ModalOpen}");
            yield return Shot("h12_game_over");
            yield return UiClick(FindButton("Main menu"), "Main menu");
            yield return Wait(3f);
            yield return Shot("h13_main_menu");
            yield return UiClick(FindText("Credits", "menu-btn"), "Credits");
            yield return Wait(0.8f);
            yield return Shot("h14_credits");
            yield return UiClick(FindButton("Third-party licences"), "Licences");
            yield return Wait(0.8f);
            yield return Shot("h15_licences");
            InputX.simulate = false;
        }

        /// <summary>The switch next to a settings label.</summary>
        static UnityEngine.UIElements.VisualElement SwitchFor(string label)
        {
            UnityEngine.UIElements.VisualElement found = null;
            Metro.UI.UIRoot.I.Root.Query<UnityEngine.UIElements.Label>().ForEach(l =>
            {
                if (found == null && l.panel != null && l.text == label) found = l.parent?.parent?.Q(className: "switch");
            });
            return found;
        }

        static bool Visible(string name)
        {
            var e = Metro.UI.UIRoot.I.Root.Q(name);
            return e != null && e.panel != null && e.resolvedStyle.display != UnityEngine.UIElements.DisplayStyle.None;
        }

        IEnumerator CtrlZ()
        {
            InputX.SimKey(UnityEngine.InputSystem.Key.LeftCtrl, true);
            yield return Frames(2);
            yield return Press(UnityEngine.InputSystem.Key.Z);
            InputX.SimKey(UnityEngine.InputSystem.Key.LeftCtrl, false);
            yield return Frames(2);
        }

        /// <summary>Challenges, help, undo, key rebinding, colour-blind colours and the challenge result.</summary>
        IEnumerator ChallengeTest(GameRoot g)
        {
            game = g;
            InputX.simulate = true;
            g.StartMenu();
            yield return Wait(2.5f);
            yield return Shot("c01_menu");
            yield return UiClick(FindText("Challenges", "menu-btn"), "Challenges");
            yield return Wait(0.8f);
            int cards = 0;
            foreach (var c in Challenges.All)
                if (Metro.UI.UIRoot.I.Root.Q("play-" + c.id) != null) cards++;
            Note($"challenge cards: {cards}/{Challenges.All.Length}");
            yield return Shot("c02_challenges");
            yield return UiClick(Metro.UI.UIRoot.I.Root.Q("play-boomtown"), "Play Boomtown");
            yield return Wait(1.5f);
            var gs = g.State;
            Note($"challenge: {gs.challenge?.def.id}, mode {g.Mode}, paused {g.Paused}, banner shown {Visible("challenge-banner")}, city {gs.city.name}");
            yield return Shot("c03_banner");

            // Help.
            yield return Press(UnityEngine.InputSystem.Key.F1);
            yield return Wait(0.5f);
            Note($"help open after F1: {Visible("help")}");
            yield return Shot("c04_help");
            yield return UiClick(FindButton("Got it"), "Got it");
            yield return Wait(0.3f);

            // Undo: build a line, take it back with Ctrl+Z.
            double money0 = gs.econ.money;
            AutoPlanner.BuildStarterLine(gs, 5);
            Note($"built: lines {gs.net.LineCount}, stations {gs.net.StationCount}, spent {Fmt.Money(money0 - gs.econ.money)}, can undo {gs.build.Undo.CanUndo} ({gs.build.Undo.NextLabel})");
            yield return CtrlZ();
            Note($"after Ctrl+Z: lines {gs.net.LineCount}, stations {gs.net.StationCount}, money back {Math.Abs(gs.econ.money - money0) < 1.0}, can undo {gs.build.Undo.CanUndo}");
            yield return Shot("c05_undone");
            yield return CtrlZ();
            Note($"second Ctrl+Z with nothing left: lines {gs.net.LineCount}");

            // Saving is refused during a challenge.
            Note($"save during challenge: {g.SaveGame("slot3")}, slot3 exists {SaveSystem.Info("slot3").exists}");

            // Rebind the line tool to U, use it, reset.
            g.ShowPauseMenu(true);
            yield return Wait(0.4f);
            yield return UiClick(FindButton("Settings"), "Settings");
            yield return Wait(0.5f);
            yield return UiClick(FindText("Controls", "seg-item"), "Controls");
            yield return Wait(0.4f);
            yield return UiClick(Metro.UI.UIRoot.I.Root.Q("bind-line"), "bind Build line");
            Note($"capturing: {g.UI.CapturingKey}");
            yield return Press(UnityEngine.InputSystem.Key.W);
            Note($"after W (camera key): line key {Keybinds.Label(BindAction.Line)}, still capturing {g.UI.CapturingKey}");
            yield return Press(UnityEngine.InputSystem.Key.U);
            Note($"after U: line key {Keybinds.Label(BindAction.Line)}, capturing {g.UI.CapturingKey}");
            yield return Shot("c06_controls_rebound");
            yield return UiClick(FindText("Game", "seg-item"), "Game");
            yield return Wait(0.3f);
            yield return UiClick(SwitchFor("Colour-blind friendly colours"), "colour-blind switch");
            Note($"colour-blind: setting {g.Settings.colorBlind}, palette {Metro.MapPalette.ColorBlindSafe}");
            yield return UiClick(FindButton("Done"), "Done");
            yield return Wait(0.3f);
            if (g.UI.ModalOpen) g.UI.CloseModal();
            yield return Press(UnityEngine.InputSystem.Key.U);
            Note($"tool after U: {g.Tools.Active}");
            yield return Press(UnityEngine.InputSystem.Key.Escape);
            yield return Press(UnityEngine.InputSystem.Key.L);
            Note($"tool after L (unbound now): {g.Tools.Active}");
            if (g.Tools.Active != ToolKind.Select) yield return Press(UnityEngine.InputSystem.Key.Escape);
            Keybinds.ResetDefaults();
            g.Settings.Save();

            // Win: grow to a Big city; the growth cards come first, then the result.
            for (int i = 0; i < 4; i++) gs.progress.TierUp();
            g.SetSpeed(0);
            float until = Time.realtimeSinceStartup + 10f;
            while (!gs.challenge.finished && Time.realtimeSinceStartup < until) yield return null;
            Note($"finished {gs.challenge.finished}, won {gs.challenge.won}, stars {gs.challenge.stars}, days {gs.challenge.DaysUsed:0.00}");
            for (int i = 0; i < 6 && !Visible("challenge-result"); i++)
            {
                var keep = FindButton("Keep building");
                if (keep == null) break;
                yield return UiClick(keep, "Keep building");
                yield return Wait(0.5f);
            }
            yield return Wait(0.5f);
            Note($"result shown: {Visible("challenge-result")}, best stars {Metro.Platform.Achievements.Profile.ChallengeStars("boomtown")}, ACH_CHALLENGE {Metro.Platform.Achievements.Profile.Has("ACH_CHALLENGE")}, menu summary {Challenges.MenuSummary()}");
            yield return Shot("c07_result");
            yield return UiClick(FindButton("Next challenge"), "Next challenge");
            yield return Wait(1.5f);
            gs = g.State;
            Note($"next: {gs.challenge?.def.id}, banner {Visible("challenge-banner")}, colour-blind {Metro.MapPalette.ColorBlindSafe}");
            g.SetSpeed(2);
            until = Time.realtimeSinceStartup + 25f;
            while (gs.clock.MinuteOfDay < 8 * 60 && Time.realtimeSinceStartup < until) yield return null;
            g.SetPaused(true);
            g.FocusOn(gs.city.center, 7f);
            yield return Wait(1f);
            yield return Shot("c08_thrift_colourblind");
            g.Settings.colorBlind = false;
            g.Settings.Apply();
            g.Settings.Save();
            yield return Wait(0.5f);
            yield return Shot("c09_thrift_normal");
            g.ExitToMenu();
            yield return Wait(2f);
            yield return Shot("c10_menu_after");
            InputX.simulate = false;
        }

        static int CountOpen(GameState gs)
        {
            int n = 0;
            foreach (var d in gs.city.districts)
                if (d.revealed) n++;
            return n;
        }

        Vector2 Land(GameState gs, Vector2 p)
        {
            for (int k = 0; k < 30; k++)
            {
                if (gs.build.ValidStationSite(p, out _)) return p;
                p += new Vector2(Mathf.Cos(k * 1.7f), Mathf.Sin(k * 1.7f)) * (0.2f + 0.05f * k);
            }
            return p;
        }

        IEnumerator PlayTest(GameRoot g)
        {
            game = g;
            g.StartNewGame(new NewGameOptions { seed = 2024, cityName = "Port Avalon", difficulty = 1, startTier = 3 });
            yield return Wait(1.2f);
            var gs = g.State;
            InputX.simulate = true;
            double money0 = gs.econ.money;

            // 1. Draw a line by clicking: Old Town -> Central -> the next district.
            var central = gs.city.districts[0];
            var oldTown = gs.city.districts[1];
            District third = null;
            foreach (var d in gs.city.districts)
                if (d.revealed && d.id > 1 && (third == null || Vector2.Distance(d.center, oldTown.center) > Vector2.Distance(third.center, oldTown.center))) third = d;
            var route = new System.Collections.Generic.List<Vector2>();
            Vector2 a = oldTown.center, b = central.center, c = third.center;
            for (int i = 0; i <= 2; i++) route.Add(Vector2.Lerp(a, b, i / 2f));
            for (int i = 1; i <= 3; i++) route.Add(Vector2.Lerp(b, c, i / 3f));
            for (int i = 0; i < route.Count; i++) route[i] = Land(gs, route[i]);
            g.FocusOn((a + c) * 0.5f, Vector2.Distance(a, c) * 0.55f + 2f);
            yield return Wait(1.2f);

            yield return Press(UnityEngine.InputSystem.Key.L);
            Note($"tool after L: {g.Tools.Active}");
            for (int i = 0; i < route.Count - 1; i++) yield return Click(route[i]);
            yield return Hover(route[route.Count - 1]);
            yield return Wait(0.4f);
            Note($"planned stops: {g.Tools.Line.PlannedCount}");
            yield return Shot("p01_drawing");
            yield return Click(route[route.Count - 1]);
            yield return Click(route[route.Count - 1] + new Vector2(0.01f, 0f), true);
            yield return Wait(0.3f);
            MetroLine line = null;
            foreach (var l in gs.net.AliveLines()) line = l;
            Note($"after right-click: lines={gs.net.LineCount} stations={gs.net.StationCount} spent={Fmt.Money(money0 - gs.econ.money)} tool={g.Tools.Active}");
            yield return Shot("p02_line_built");
            if (line == null) yield break;

            // 2. Depot via the depot tool.
            yield return Press(UnityEngine.InputSystem.Key.B);
            var depotStation = gs.net.stations[line.stops[line.stops.Count / 2]];
            yield return Click(depotStation.pos);
            int depots = 0;
            foreach (var _ in gs.net.AliveDepots()) depots++;
            Note($"depots after click: {depots}, tool={g.Tools.Active}");

            // 3. Select the line by clicking its track, add trains with the inspector's + button.
            yield return Press(UnityEngine.InputSystem.Key.V);
            var e0 = gs.net.GetEdge(line.stops[0], line.stops[1]);
            Vector2 mid = Geo.SampleAt(e0.path, e0.cum, e0.length * 0.5f, out _);
            yield return Click(mid);
            Note($"selection after clicking track: {g.Sel.kind} {g.Sel.id}");
            if (g.Sel.kind != SelKind.Line) g.Select(SelKind.Line, line.id);
            yield return Frames(5);
            for (int i = 0; i < 3; i++)
            {
                yield return UiClick(Metro.UI.UIRoot.I.Root.Q("add-train"), "add train");
                yield return Frames(4);
            }
            Note($"trains on line: {line.trains.Count}");
            yield return UiClick(FindButton("Extend end"), "Extend end");
            Note($"tool after Extend end: {g.Tools.Active}, extending={g.Tools.Line.IsExtending}");
            var endPos = gs.net.stations[line.stops[line.stops.Count - 1]].pos;
            var prevPos = gs.net.stations[line.stops[line.stops.Count - 2]].pos;
            Vector2 dir = (endPos - prevPos).normalized;
            Vector2 x1 = Land(gs, endPos + dir * 1.3f), x2 = Land(gs, endPos + dir * 2.6f + Geo.Perp(dir) * 0.6f);
            bool ok1 = gs.build.ValidStationSite(x1, out string why1), ok2 = gs.build.ValidStationSite(x2, out string why2);
            Note($"extension sites: {ok1} {why1} / {ok2} {why2}; money {Fmt.Money(gs.econ.money)}");
            if (gs.econ.money < 1_500_000)
            {
                // This scenario tests the extend flow, not the budget.
                gs.econ.money += 1_500_000;
                Note($"topped up money to {Fmt.Money(gs.econ.money)} for the extension");
            }
            // Keep the clicks in the middle of the screen, clear of panels and tooltips.
            g.FocusOn((endPos + x2) * 0.5f, 4f);
            yield return Wait(0.8f);
            yield return Click(x1);
            yield return Click(x2);
            yield return Press(UnityEngine.InputSystem.Key.Enter);
            Note($"after extension: stops={line.stops.Count} stations={gs.net.StationCount}");

            // 4. Run the clock into the rush.
            yield return Press(UnityEngine.InputSystem.Key.Space);
            Note($"paused after Space: {g.Paused}");
            yield return Press(UnityEngine.InputSystem.Key.Digit3);
            float until = Time.realtimeSinceStartup + 25f;
            while (gs.clock.MinuteOfDay < 8 * 60 + 15 && Time.realtimeSinceStartup < until) yield return null;
            Note($"time {gs.clock.TimeString}, riders {Fmt.Count(gs.stats.ridersToday)}, waiting {Fmt.Count(gs.stats.totalWaiting)}");
            yield return Press(UnityEngine.InputSystem.Key.Digit1);
            yield return Hover(gs.net.stations[line.stops[1]].pos);
            yield return Wait(1f);
            yield return Shot("p03_rush");

            // 5. Insert a station on the track.
            yield return Press(UnityEngine.InputSystem.Key.I);
            var e1 = gs.net.GetEdge(line.stops[2], line.stops[3]);
            Vector2 ins = Geo.SampleAt(e1.path, e1.cum, e1.length * 0.5f, out _);
            int before = gs.net.StationCount;
            yield return Hover(ins);
            yield return Shot("p04_insert_hover");
            yield return Click(ins);
            Note($"insert: stations {before} -> {gs.net.StationCount}");

            // 6. Overlay and overview by keyboard.
            yield return Press(UnityEngine.InputSystem.Key.Escape);
            yield return Press(UnityEngine.InputSystem.Key.C);
            yield return Wait(2f);
            yield return Shot("p05_coverage");
            yield return Press(UnityEngine.InputSystem.Key.C);
            yield return Press(UnityEngine.InputSystem.Key.Tab);
            yield return Wait(1.5f);
            yield return Shot("p06_overview");
            yield return Press(UnityEngine.InputSystem.Key.Tab);

            // 7. Finance window: change the fare through the slider value, close with Esc.
            yield return Press(UnityEngine.InputSystem.Key.F);
            Note($"window: {g.UI.OpenWindowKind}");
            var slider = Metro.UI.UIRoot.I.Root.Q<UnityEngine.UIElements.Slider>("fare-slider");
            if (slider != null) slider.value = 3.1f;
            yield return Frames(5);
            Note($"fare now {Fmt.Fare(gs.econ.baseFare)}");
            yield return Shot("p07_finance");
            yield return Press(UnityEngine.InputSystem.Key.Escape);
            Note($"window after Esc: {g.UI.OpenWindowKind}");

            // 8. Save from the pause menu, then load and compare.
            yield return Press(UnityEngine.InputSystem.Key.Escape);
            yield return Press(UnityEngine.InputSystem.Key.Escape);
            Note($"pause menu open: {g.UI.ModalOpen}");
            yield return Shot("p08_pause");
            yield return UiClick(FindButton("Save game"), "Save game");
            yield return UiClick(FindButton("Save here"), "Save here");
            var info = SaveSystem.Info("slot1");
            Note($"slot1 exists={info.exists} city={info.city} day={info.day}");
            int st = gs.net.StationCount, ln = gs.net.LineCount, tr = gs.trains.AliveCount;
            double money = gs.econ.money;
            g.LoadGame("slot1");
            yield return Wait(1f);
            var gs2 = g.State;
            Note($"loaded: stations {st}->{gs2.net.StationCount}, lines {ln}->{gs2.net.LineCount}, trains {tr}->{gs2.trains.AliveCount}, money {Fmt.Money(money)}->{Fmt.Money(gs2.econ.money)}, time {gs2.clock.TimeString}");
            yield return Shot("p09_loaded");

            // 9. Demolish a station through the confirm dialog.
            yield return Press(UnityEngine.InputSystem.Key.X);
            var victim = gs2.net.stations[gs2.net.lines[0].stops[0]];
            int st0 = gs2.net.StationCount;
            yield return Click(victim.pos);
            Note($"confirm open: {g.UI.ModalOpen}");
            // The danger button reads "Demolish" (older builds: "Confirm").
            yield return UiClick(FindButton("Demolish") ?? FindButton("Confirm"), "Confirm demolish");
            Note($"demolish: stations {st0} -> {gs2.net.StationCount}");
            InputX.simulate = false;
        }

        IEnumerator TutorialTest(GameRoot g)
        {
            game = g;
            g.StartNewGame(new NewGameOptions { seed = 99, cityName = "Lindmere", tutorial = true });
            yield return Wait(1f);
            var gs = g.State;
            InputX.simulate = true;
            yield return Shot("t00_welcome");
            yield return UiClick(FindButton("Let's go"), "Let's go");
            yield return UiClick(FindButton("Next"), "Next (map)");
            yield return Shot("t01_line_step");

            // Locked things explain themselves instead of opening.
            yield return Press(UnityEngine.InputSystem.Key.I);
            Note($"after I (locked insert tool): tool={g.Tools.Active}");
            yield return Press(UnityEngine.InputSystem.Key.F);
            Note($"after F (locked finances): window={g.UI.OpenWindowKind}");
            yield return Shot("t01b_locked_toast");

            var line = AutoPlanner.BuildStarterLine(gs);
            Note($"tutorial line: {(line != null ? line.stops.Count + " stops" : "failed")}");
            yield return Wait(0.5f);
            yield return Shot("t02_depot_step");
            if (line == null) yield break;
            gs.build.BuildDepot(line.stops[1]);
            yield return Wait(0.5f);
            g.Select(SelKind.Line, line.id);
            yield return Wait(0.5f);
            yield return Shot("t03_trains_step");
            yield return UiClick(Metro.UI.UIRoot.I.Root.Q("add-train"), "add train");
            yield return UiClick(Metro.UI.UIRoot.I.Root.Q("add-train"), "add train");
            yield return Wait(0.5f);
            var second = gs.build.BuildLine(new System.Collections.Generic.List<PlannedStop>
            {
                new PlannedStop { stationId = -1, pos = Land(gs, gs.city.center + new Vector2(0f, 2.5f)) },
                new PlannedStop { stationId = -1, pos = Land(gs, gs.city.center + new Vector2(0f, -2.5f)) }
            }, false, false, out _);
            Note($"second line while licence is 1: ok={second.ok} \"{second.error}\"");
            yield return Press(UnityEngine.InputSystem.Key.Space);
            float until = Time.realtimeSinceStartup + 20f;
            while (gs.clock.MinuteOfDay < Balance.StartMinute + 25 && Time.realtimeSinceStartup < until) yield return null;
            yield return Wait(0.8f);
            yield return Shot("t04_rush_step");
            Note($"tutorial clock {gs.clock.TimeString}");
            until = Time.realtimeSinceStartup + 70f;
            while (gs.clock.MinuteOfDay < 9 * 60 + 35 && Time.realtimeSinceStartup < until) yield return null;
            yield return Wait(1f);
            Note($"at {gs.clock.TimeString}: riders {Fmt.Count(gs.stats.ridersToday)}, goals {gs.progress.GoalsDone}/{gs.progress.goals.Count}, tier {gs.progress.Current.name}, held {gs.progress.holdTierUps}");
            yield return Shot("t05_grow_step");
            yield return UiClick(FindButton("Got it"), "Got it");
            g.SetSpeed(2);
            until = Time.realtimeSinceStartup + 90f;
            while (!gs.progress.Ready && Time.realtimeSinceStartup < until) yield return null;
            yield return Wait(0.6f);
            Note($"ready at {gs.clock.TimeString}: riders today {Fmt.Count(gs.stats.ridersToday)}, tier {gs.progress.Current.name}");
            yield return Shot("t06_ready_to_grow");
            yield return UiClick(FindButton("Skip to dawn"), "Skip to dawn");
            Note($"skipping: {g.SkippingToDawn}");
            until = Time.realtimeSinceStartup + 90f;
            while (gs.progress.tier == 0 && Time.realtimeSinceStartup < until) yield return null;
            yield return Wait(1.2f);
            Note($"grew: tier {gs.progress.Current.name}, card open {g.UI.ModalOpen}, clock {gs.clock.TimeString} day {gs.clock.DayNumber}, still skipping {g.SkippingToDawn}");
            yield return Shot("t07_growth_card");
            yield return UiClick(FindButton("Keep building"), "Keep building");
            yield return Wait(3.5f);
            Note($"after card: paused {g.Paused}, map {gs.city.playArea.width:0}x{gs.city.playArea.height:0}, insert tool unlocked {gs.Has(Feature.InsertStation)}, licence {gs.progress.MaxLines}, open districts {CountOpen(gs)}");
            yield return Shot("t08_growing_town");
            InputX.simulate = false;
        }

        IEnumerator DrawTest(GameRoot g)
        {
            g.StartNewGame(new NewGameOptions { seed = 2024, cityName = "Port Avalon", difficulty = 1 });
            yield return Wait(1f);
            var gs = g.State;
            var c = gs.city.center;
            var stops = new System.Collections.Generic.List<PlannedStop>
            {
                new PlannedStop { stationId = -1, pos = c + new Vector2(-4f, 1f) },
                new PlannedStop { stationId = -1, pos = c + new Vector2(-2.5f, 0.2f), diagonalFirst = false },
                new PlannedStop { stationId = -1, pos = c, diagonalFirst = true },
                new PlannedStop { stationId = -1, pos = c + new Vector2(1.6f, 1.2f), diagonalFirst = false },
                new PlannedStop { stationId = -1, pos = c + new Vector2(3.5f, 1.4f), diagonalFirst = true }
            };
            var r = gs.build.BuildLine(stops, false, false, out var line);
            Note($"BuildLine: ok={r.ok} err={r.error} cost={r.cost}");
            if (line != null)
            {
                Note($"depot: {gs.build.BuildDepot(line.stops[2]).ok}");
                for (int i = 0; i < 3; i++) Note($"train: {gs.build.AddTrain(line).ok}");
                g.Select(SelKind.Line, line.id);
            }
            g.FocusOn(c, 4f);
            g.SetSpeed(2);
            yield return Wait(4f);
            yield return Shot("draw_01");
        }
    }
}
