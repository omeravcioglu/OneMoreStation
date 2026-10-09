using System;
using System.IO;
using Metro.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Metro
{
    public enum AppMode { Menu, Playing }
    public enum SelKind { None, Station, Line, Train, Depot, District }
    public enum WindowKind { None, Finance, Stats, City, CityHall }

    public struct Selection
    {
        public SelKind kind;
        public int id;
    }

    /// <summary>
    /// The single scene object. Owns the camera, renderers, UI and the current GameState,
    /// and runs the frame loop: simulation → tools → camera → rendering → UI.
    /// </summary>
    public sealed class GameRoot : MonoBehaviour
    {
        public static GameRoot I { get; private set; }

        public CameraController CamCtl { get; private set; }
        public MapRenderer Map { get; private set; }
        public NetworkRenderer NetView { get; private set; }
        public GameUI UI { get; private set; }
        public ToolController Tools { get; private set; }
        public TutorialController Tutorial { get; private set; }
        public GameSettings Settings { get; private set; }
        public GameState State { get; private set; }
        public AppMode Mode { get; private set; }
        public Selection Sel { get; private set; } = new Selection { kind = SelKind.None, id = -1 };
        public bool Paused { get; private set; } = true;
        public int SpeedIndex { get; private set; }
        public DataOverlay Overlay => Map.overlay;
        public bool ScreenshotHideLabels;
        public bool HideHudForTutorial => false;

        UIRoot uiRoot;
        Transform world;
        float menuTime;
        int lastAutosaveDay = -1;
        bool pausedBeforeMenu;
        float chimeCooldown;

        void Awake()
        {
            if (I != null && I != this)
            {
                Destroy(gameObject);
                return;
            }
            I = this;
            QualitySettings.vSyncCount = 1;
            QualitySettings.antiAliasing = 4;
            Application.targetFrameRate = 120;
            Application.runInBackground = true;
            Application.logMessageReceived += OnLog;
            // Steam first (it may relaunch the game through Steam), then cloud saves, then achievements.
            Metro.Platform.PlatformServices.Init();
            Metro.Platform.CloudSync.PullAll();
            Metro.Platform.Achievements.Init();
            Settings = GameSettings.Load();
            Sfx.Init(gameObject);
            MusicPlayer.Create(gameObject);
            SetupCamera();
            world = new GameObject("World").transform;
            world.SetParent(transform, false);
            Map = new MapRenderer(world);
            NetView = new NetworkRenderer(world);
            uiRoot = new UIRoot(transform);
            Tools = new ToolController(this);
            UI = new GameUI(this, uiRoot);
            Tutorial = new TutorialController(this);
            Settings.Apply();
            Metro.Platform.Achievements.Unlocked += a => UI.AchievementToast(a);
            Challenges.Finished += OnChallengeFinished;
            UndoHistory.Recorded += OnConstructionRecorded;
            Metro.Platform.PlatformServices.Current.OverlayChanged += OnOverlay;
            if (!AutoTest.TryStart(this)) StartMenu();
        }

        // ------------------------------------------------------------------ platform & app lifecycle

        bool pausedByOverlay, pausedByFocus;
        float presenceTimer, errorToastCooldown;

        void OnOverlay(bool active)
        {
            if (Mode != AppMode.Playing) return;
            if (active && !Paused)
            {
                Paused = true;
                pausedByOverlay = true;
            }
            else if (!active && pausedByOverlay)
            {
                pausedByOverlay = false;
                Paused = false;
            }
        }

        void OnApplicationFocus(bool focus)
        {
            if (Settings == null || Mode != AppMode.Playing || !Settings.pauseOnFocusLoss) return;
            if (!focus && !Paused)
            {
                Paused = true;
                pausedByFocus = true;
            }
            else if (focus && pausedByFocus)
            {
                pausedByFocus = false;
                Paused = false;
            }
        }

        void OnApplicationQuit()
        {
            // Never lose more than the current session: save the city on the way out.
            try
            {
                if (Mode == AppMode.Playing && State != null && State.challenge == null && !State.gov.gameOver && !Tutorial.Active && Settings.autosave && !AutoTest.Running)
                    SaveSystem.Save(State, SaveSystem.AutosaveSlot);
                Metro.Platform.Achievements.Flush();
            }
            catch (Exception e)
            {
                Debug.LogError("[Metro] Save on quit failed: " + e);
            }
            Metro.Platform.PlatformServices.Shutdown();
        }

        void OnLog(string message, string stack, LogType type)
        {
            if (type != LogType.Exception || uiRoot == null || errorToastCooldown > 0f) return;
            errorToastCooldown = 30f;
            uiRoot.Toast("Something went wrong. The game keeps running; details are in the log file.", ToastKind.Error, 4f);
        }

        void TickPlatform(float dt)
        {
            Metro.Platform.PlatformServices.Tick();
            errorToastCooldown -= dt;
            if (Mode == AppMode.Playing) Metro.Platform.Achievements.Tick(dt);
            if (MusicPlayer.I != null && State != null) MusicPlayer.I.SetMood(MusicPlayer.MoodFor(State.clock, Mode == AppMode.Menu));
            presenceTimer -= dt;
            if (presenceTimer <= 0f)
            {
                presenceTimer = 5f;
                var plat = Metro.Platform.PlatformServices.Current;
                if (Mode == AppMode.Menu || State == null) plat.SetPresence("Menu", "", "", "");
                else plat.SetPresence(Tutorial.Active ? "Tutorial" : State.challenge != null ? "Challenge" : "Playing", State.city.name, State.progress.Current.name, State.clock.DayNumber.ToString());
            }
        }

        void SetupCamera()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = go.AddComponent<Camera>();
            }
            cam.orthographic = true;
            cam.orthographicSize = 7f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = MapPalette.Background;
            cam.transform.SetPositionAndRotation(new Vector3(0f, 0f, -10f), Quaternion.identity);
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 100f;
            cam.allowMSAA = true;
            cam.allowHDR = false;
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None)) l.enabled = false;
            CamCtl = new CameraController(cam);
        }

        // ------------------------------------------------------------------ app flow

        void BindState(GameState gs)
        {
            if (State != null)
            {
                State.DistrictRevealed -= OnDistrictRevealed;
                State.advisor.Posted -= OnMessage;
                State.progress.TierReached -= OnTierReached;
                State.gov.SessionHeld -= OnSessionHeld;
                State.gov.LicenceRevoked -= OnLicenceRevoked;
                State.gov.BailoutGranted -= OnBailout;
            }
            UI.ClearPendingModals();
            State = gs;
            SkippingToDawn = false;
            Map.Bind(gs);
            NetView.Bind(gs);
            CamCtl.Bind(gs.city);
            shownArea = gs.city.playArea;
            Map.SetFrame(shownArea);
            Tools.Reset();
            Sel = new Selection { kind = SelKind.None, id = -1 };
            NetView.ClearSelection();
            Map.overlay = DataOverlay.None;
            gs.DistrictRevealed += OnDistrictRevealed;
            gs.advisor.Posted += OnMessage;
            gs.progress.TierReached += OnTierReached;
            gs.gov.SessionHeld += OnSessionHeld;
            gs.gov.LicenceRevoked += OnLicenceRevoked;
            gs.gov.BailoutGranted += OnBailout;
            lastAutosaveDay = gs.clock.Day;
            gs.EnsureFresh();
            gs.trains.RefreshAllPoses();
        }

        /// <summary>Is a mechanic available yet? Shows why not when it isn't.</summary>
        public bool CheckUnlocked(Feature f, bool explain = true)
        {
            if (State == null || State.Has(f)) return true;
            if (explain) uiRoot.Toast(ProgressionSystem.LockedMessage(f), ToastKind.Info, 3f);
            return false;
        }

        public void StartMenu()
        {
            Mode = AppMode.Menu;
            if (Tutorial.Active) Tutorial = new TutorialController(this);
            int[] seeds = { 4242, 1337, 2024, 777, 31415, 8080, 1984, 5150 };
            int seed = seeds[UnityEngine.Random.Range(0, seeds.Length)];
            var gs = new GameState(new NewGameOptions
            {
                seed = seed,
                cityName = NameBank.RandomCityName(new Rng(seed)),
                demo = true,
                startTier = ProgressionSystem.Tiers.Length - 1
            });
            gs.advisorEnabled = false;
            gs.governmentEnabled = false;
            gs.growthEnabled = false;
            gs.growth.RevealSilently(20);
            AutoPlanner.BuildNetwork(gs, 7, true, 0.42f, 5);
            gs.econ.money = 1e9;
            gs.clock.totalMinutes = 7 * 60 + 20;
            gs.EnsureFresh();
            gs.Advance(45f);
            BindState(gs);
            Paused = false;
            SpeedIndex = 0;
            CamCtl.inputEnabled = false;
            var rb = gs.city.RevealedBounds();
            float size = CamCtl.SizeToFit(rb, 0.82f);
            CamCtl.FocusOn(rb.center - new Vector2(size * CamCtl.cam.aspect * 0.28f, 0f), size, true);
            menuTime = 0f;
            UI.ShowMainMenu();
        }

        public void StartNewGame(NewGameOptions opts)
        {
            UI.CloseModal();
            var gs = new GameState(opts);
            EnterGame(gs);
            GameEvents.RaiseCityFounded(gs);
            var area = gs.city.playArea;
            CamCtl.FocusOn(area.center, CamCtl.SizeToFit(area, 1.02f), true);
            if (opts.tutorial)
            {
                Tutorial = new TutorialController(this);
                Tutorial.Begin();
            }
            else
            {
                string size = gs.progress.tier == 0 ? "a small town with big plans" : "a " + gs.progress.Current.name.ToLowerInvariant();
                uiRoot.Toast($"Welcome to {gs.city.name}, {size}. Start with the Line tool ({Keybinds.Label(BindAction.Line)}).", ToastKind.Info, 6f);
            }
        }

        // ------------------------------------------------------------------ challenges

        public void StartChallenge(ChallengeDef def)
        {
            UI.CloseModal();
            UI.CloseWindow();
            UI.tutorial.Hide();
            Tutorial = new TutorialController(this);
            var gs = Challenges.Create(def);
            EnterGame(gs);
            var area = gs.city.playArea;
            CamCtl.FocusOn(area.center, CamCtl.SizeToFit(area, 1.02f), true);
            // No welcome toast: the challenge banner shows the goal and the top bar says it's paused.
        }

        void OnChallengeFinished(ChallengeRun run)
        {
            if (Mode != AppMode.Playing || State == null || State.challenge != run) return;
            bool better = run.won && Metro.Platform.Achievements.Profile.RecordChallenge(run.def.id, run.stars);
            if (better) Metro.Platform.Achievements.SaveProfile();
            GameEvents.RaiseChallengeFinished(run);
            SkippingToDawn = false;
            if (run.won) Sfx.Reveal();
            else Sfx.Error();
            UI.ShowWhenFree(() =>
            {
                Paused = true;
                UI.ShowChallengeResult(run, better);
            });
        }

        // ------------------------------------------------------------------ undo

        static bool undoHintShown;

        void OnConstructionRecorded()
        {
            if (undoHintShown || Mode != AppMode.Playing || Tutorial.Active || AutoTest.Running) return;
            undoHintShown = true;
            uiRoot.Toast("Changed your mind? Ctrl+Z undoes construction.", ToastKind.Info, 3.5f);
        }

        /// <summary>Ctrl+Z: takes back the last construction (refunded in full).</summary>
        public void UndoConstruction()
        {
            if (State == null || Mode != AppMode.Playing || State.gov.gameOver) return;
            if (Tools.Active == ToolKind.Line && Tools.Line.PlannedCount > 0)
            {
                uiRoot.Toast("Backspace removes the last planned stop. Esc drops the plan.", ToastKind.Info, 3f);
                return;
            }
            var undo = State.build.Undo;
            if (!undo.Undo(out string msg))
            {
                uiRoot.Toast(msg, ToastKind.Info, 3f);
                Sfx.Error();
                return;
            }
            if (Tools.Active == ToolKind.Line && Tools.Line.IsExtending) Tools.Cancel();
            bool gone = Sel.kind == SelKind.Train
                        || (Sel.kind == SelKind.Station && State.net.GetStation(Sel.id) == null)
                        || (Sel.kind == SelKind.Line && State.net.GetLine(Sel.id) == null)
                        || (Sel.kind == SelKind.Depot && State.net.GetDepot(Sel.id) == null);
            if (gone) ClearSelection();
            Sfx.Click();
            uiRoot.Toast(msg, ToastKind.Good, 3f);
        }

        // ------------------------------------------------------------------ growth

        Rect shownArea;
        bool pausedBeforeTierUp;
        bool growFocus;

        void OnTierReached(TierReachedInfo info)
        {
            if (Mode != AppMode.Playing) return;
            SkippingToDawn = false;
            pausedBeforeTierUp = Paused;
            Paused = true;
            Sfx.Reveal();
            growFocus = true;
            ClearSelection();
            Tools.Select(ToolKind.Select);
            UI.ShowWhenFree(() => UI.ShowTierUp(info, () =>
            {
                Paused = pausedBeforeTierUp;
                UI.PulseUnlocks(ProgressionSystem.Tiers[info.tier].unlocks);
            }));
        }

        // ------------------------------------------------------------------ City Hall

        void OnSessionHeld(CouncilReport rep)
        {
            if (Mode != AppMode.Playing || State.gov.gameOver) return;
            SkippingToDawn = false;
            Sfx.Chime();
            UI.ShowWhenFree(() =>
            {
                bool was = Paused;
                Paused = true;
                UI.ShowCouncilSession(rep, () => Paused = was);
            });
        }

        void OnBailout(float grant)
        {
            if (Mode != AppMode.Playing) return;
            SkippingToDawn = false;
            Sfx.Error();
            UI.ShowWhenFree(() =>
            {
                bool was = Paused;
                Paused = true;
                UI.ShowBailout(grant, () => Paused = was);
            });
        }

        void OnLicenceRevoked(string reason)
        {
            if (Mode != AppMode.Playing) return;
            SkippingToDawn = false;
            Paused = true;
            Tools.Select(ToolKind.Select);
            ClearSelection();
            Sfx.Error();
            GameEvents.RaiseGameOver(State, reason);
            UI.ShowGameOver(reason);
        }

        void AnimateArea(float dt)
        {
            var target = State.city.playArea;
            if (shownArea == target)
            {
                growFocus = false;
                return;
            }
            float k = 1f - Mathf.Exp(-dt * 1.6f);
            shownArea = Rect.MinMaxRect(
                Mathf.Lerp(shownArea.xMin, target.xMin, k), Mathf.Lerp(shownArea.yMin, target.yMin, k),
                Mathf.Lerp(shownArea.xMax, target.xMax, k), Mathf.Lerp(shownArea.yMax, target.yMax, k));
            if (Mathf.Abs(shownArea.xMin - target.xMin) + Mathf.Abs(shownArea.yMin - target.yMin)
                + Mathf.Abs(shownArea.xMax - target.xMax) + Mathf.Abs(shownArea.yMax - target.yMax) < 0.01f) shownArea = target;
            Map.SetFrame(shownArea);
            var limits = Rect.MinMaxRect(Mathf.Max(0f, shownArea.xMin), Mathf.Max(0f, shownArea.yMin),
                Mathf.Min(State.city.width, shownArea.xMax), Mathf.Min(State.city.height, shownArea.yMax));
            CamCtl.SetLimits(limits);
            if (growFocus) CamCtl.FocusOn(limits.center, CamCtl.SizeToFitArea(limits, 1.04f));
        }

        public void EnterGame(GameState gs)
        {
            if (Mode == AppMode.Playing && State != null) GameEvents.RaiseCityLeft(State);
            BindState(gs);
            Mode = AppMode.Playing;
            Paused = true;
            SpeedIndex = 0;
            CamCtl.inputEnabled = true;
            UI.ShowGame();
            GameEvents.RaiseCityEntered(gs);
        }

        public bool SaveGame(string slot)
        {
            if (State == null || Mode != AppMode.Playing) return false;
            if (State.challenge != null)
            {
                uiRoot.Toast("Challenges can't be saved \u2014 they're short, so play them in one go.", ToastKind.Info, 3f);
                return false;
            }
            bool ok = SaveSystem.Save(State, slot);
            if (!ok) uiRoot.Toast("Saving failed — see the log.", ToastKind.Error);
            return ok;
        }

        public void LoadGame(string slot)
        {
            var gs = SaveSystem.Load(slot);
            if (gs == null)
            {
                uiRoot.Toast("That save couldn't be loaded.", ToastKind.Error);
                return;
            }
            UI.CloseModal();
            Tutorial = new TutorialController(this);
            EnterGame(gs);
            CamCtl.FocusOn(gs.city.center, 8f, true);
            uiRoot.Toast($"Loaded {gs.city.name}, day {gs.clock.DayNumber}. Paused — press Space to continue.", ToastKind.Info, 4f);
        }

        public void ExitToMenu()
        {
            if (Mode == AppMode.Playing && State != null) GameEvents.RaiseCityLeft(State);
            UI.CloseModal();
            UI.CloseWindow();
            Tutorial = new TutorialController(this);
            UI.tutorial.Hide();
            StartMenu();
        }

        public void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ------------------------------------------------------------------ time

        public void SetPaused(bool p)
        {
            Paused = p;
            if (p) SkippingToDawn = false;
        }

        public void SetSpeed(int idx)
        {
            SpeedIndex = Mathf.Clamp(idx, 0, Balance.Speeds.Length - 1);
            Paused = false;
            SkippingToDawn = false;
        }

        public void TogglePause()
        {
            Paused = !Paused;
            if (Paused) SkippingToDawn = false;
        }

        /// <summary>Once every goal is met, runs the clock fast until the city grows at dawn.</summary>
        public bool SkippingToDawn { get; private set; }
        double skipUntil;

        public void SkipToDawn()
        {
            if (State == null) return;
            var clock = State.clock;
            double dayStart = clock.Day * (double)Balance.MinutesPerDay;
            skipUntil = dayStart + Balance.NightEnd;
            if (clock.totalMinutes >= skipUntil) skipUntil += Balance.MinutesPerDay;
            SkippingToDawn = true;
            Paused = false;
        }

        public void ShowPauseMenu(bool show)
        {
            if (!show)
            {
                UI.CloseModal();
                return;
            }
            pausedBeforeMenu = Paused;
            Paused = true;
            UI.ShowPause();
        }

        public void OnPauseMenuClosed()
        {
            Paused = pausedBeforeMenu;
        }

        // ------------------------------------------------------------------ selection & camera

        public void Select(SelKind kind, int id)
        {
            Sel = new Selection { kind = kind, id = id };
            NetView.selectedStation = kind == SelKind.Station ? id : -1;
            NetView.selectedLine = kind == SelKind.Line ? id : -1;
            NetView.selectedTrain = kind == SelKind.Train ? id : -1;
            NetView.selectedDepot = kind == SelKind.Depot ? id : -1;
        }

        public void ClearSelection() => Select(SelKind.None, -1);

        public void FocusOn(Vector2 p, float size = -1f) => CamCtl.FocusOn(p, size);

        public void FocusLine(int lineId)
        {
            var l = State?.net.GetLine(lineId);
            if (l == null) return;
            var pts = new System.Collections.Generic.List<Vector2>();
            foreach (int s in l.stops) pts.Add(State.net.stations[s].pos);
            var r = Geo.Bounds(pts);
            CamCtl.FocusOn(r.center, CamCtl.SizeToFit(Geo.Expand(r, 1.5f), 1.15f));
            Select(SelKind.Line, lineId);
        }

        public void FocusDistrict(int id)
        {
            if (State == null || id < 0 || id >= State.city.districts.Count) return;
            var d = State.city.districts[id];
            CamCtl.FocusOn(d.labelPos, Mathf.Max(5f, Mathf.Min(CamCtl.Size, 8f)));
            Select(SelKind.District, id);
        }

        public void FocusMessage(AdvisorMessage m)
        {
            if (State == null) return;
            if (m.stationId >= 0 && State.net.GetStation(m.stationId) != null)
            {
                var s = State.net.stations[m.stationId];
                CamCtl.FocusOn(s.pos, Mathf.Min(CamCtl.Size, 4.5f));
                Select(SelKind.Station, s.id);
            }
            else if (m.lineId >= 0 && State.net.GetLine(m.lineId) != null) FocusLine(m.lineId);
            else if (m.districtId >= 0) FocusDistrict(m.districtId);
        }

        public void ToggleOverlay(DataOverlay o)
        {
            if (o != DataOverlay.None && Map.overlay != o && !CheckUnlocked(Feature.Overlays)) return;
            Map.overlay = Map.overlay == o ? DataOverlay.None : o;
        }

        public void ToggleOverview()
        {
            if (State == null) return;
            var r = State.city.RevealedBounds();
            foreach (var s in State.net.AliveStations())
            {
                r.xMin = Mathf.Min(r.xMin, s.pos.x - 1f);
                r.yMin = Mathf.Min(r.yMin, s.pos.y - 1f);
                r.xMax = Mathf.Max(r.xMax, s.pos.x + 1f);
                r.yMax = Mathf.Max(r.yMax, s.pos.y + 1f);
            }
            CamCtl.ToggleOverview(r);
        }

        public void OpenWindow(WindowKind k)
        {
            if (State == null || Mode != AppMode.Playing) return;
            if (UI.OpenWindowKind != k)
            {
                if (k == WindowKind.Finance && !CheckUnlocked(Feature.Finance)) return;
                if (k == WindowKind.Stats && !CheckUnlocked(Feature.Stats)) return;
                if (k == WindowKind.City && !CheckUnlocked(Feature.CityWindow)) return;
                if (k == WindowKind.CityHall && !CheckUnlocked(Feature.Council)) return;
            }
            UI.OpenWindow(k);
            if (k == WindowKind.Finance && UI.OpenWindowKind == WindowKind.Finance) Tutorial.OnFinanceOpened();
        }

        public void CloseWindow() => UI.CloseWindow();

        public void ConfirmAction(string title, string body, Action onYes) => UI.Confirm(title, body, onYes);

        // ------------------------------------------------------------------ events

        void OnDistrictRevealed(District d)
        {
            Map.MarkFogDirty();
            // Districts that open with a new city size are presented by the growth card instead.
            if (Mode == AppMode.Playing && !State.progress.Growing)
            {
                Sfx.Reveal();
                uiRoot.Toast($"New district: {d.name}", ToastKind.Info, 4f);
            }
        }

        void OnMessage(AdvisorMessage m)
        {
            if (Mode != AppMode.Playing) return;
            if ((m.severity == MsgSeverity.Critical || m.severity == MsgSeverity.Warning) && chimeCooldown <= 0f)
            {
                Sfx.Chime();
                chimeCooldown = 4f;
            }
            if (m.severity == MsgSeverity.Good) Sfx.Coin();
        }

        // ------------------------------------------------------------------ frame loop

        /// <summary>
        /// This frame's time step: real time (capped at 0.1 s), or exactly one frame while a fixed-rate
        /// capture runs (Time.captureFramerate, used to record the trailer), so footage plays back evenly.
        /// </summary>
        public static float FrameDt => Time.captureFramerate > 0 ? 1f / Time.captureFramerate : Mathf.Min(Time.unscaledDeltaTime, 0.1f);

        void Update()
        {
            InputX.BeginFrame();
            float dt = FrameDt;
            chimeCooldown -= dt;
            TickPlatform(dt);
            if (State == null) return;
            bool overUI = uiRoot.IsPointerOverUI(InputX.MousePos);
            if (Mode == AppMode.Menu)
            {
                float mult = State.clock.IsNight ? Balance.NightBoost : 1f;
                State.Advance(dt * Balance.MinutesPerRealSecond * mult);
            }
            else
            {
                HandleHotkeys();
                if (!Paused && !UI.ModalOpen && !State.gov.gameOver)
                {
                    float mult = Balance.Speeds[SpeedIndex] * (State.clock.IsNight ? Balance.NightBoost : 1f);
                    if (SkippingToDawn)
                    {
                        mult = Mathf.Max(mult, Balance.SkipToDawnSpeed);
                        // Never overshoot dawn: that's when the city grows.
                        float left = (float)(skipUntil - State.clock.totalMinutes);
                        float step = Mathf.Min(dt * Balance.MinutesPerRealSecond * mult, Mathf.Max(left, 0f));
                        State.Advance(step);
                        if (State.clock.totalMinutes >= skipUntil - 1e-4) SkippingToDawn = false;
                    }
                    else State.Advance(dt * Balance.MinutesPerRealSecond * mult);
                }
                if (!UI.ModalOpen && !UI.WindowOpen) Tools.Tick(dt, overUI);
                else uiRoot.HideCursorHint();
                Autosave();
            }
            UI.Tick(dt);
            Tutorial.Tick(dt);
        }

        void LateUpdate()
        {
            float dt = FrameDt;
            if (State == null) return;
            if (Mode == AppMode.Menu)
            {
                menuTime += dt;
                var rb = State.city.RevealedBounds();
                float size = CamCtl.SizeToFit(rb, 0.82f);
                CamCtl.Drift(dt, rb.center - new Vector2(size * CamCtl.cam.aspect * 0.28f, 0f), 2.2f, size, menuTime);
            }
            else
            {
                AnimateArea(dt);
                bool blocked = UI.ModalOpen || UI.WindowOpen;
                bool overUI = uiRoot.IsPointerOverUI(InputX.MousePos);
                CamCtl.keyboardEnabled = !TextFieldFocused();
                CamCtl.Tick(dt, overUI || blocked, Tools.AllowsLeftPan && !blocked);
            }
            Map.Tick(dt);
            NetView.Tick(dt, CamCtl.PixelWorldSize);
            UI.LateTick();
        }

        bool TextFieldFocused()
        {
            var f = uiRoot.Root.panel?.focusController?.focusedElement as VisualElement;
            return f != null && (f is TextField || f.GetFirstAncestorOfType<TextField>() != null);
        }

        void HandleHotkeys()
        {
            if ((!InputX.simulate && Keyboard.current == null) || TextFieldFocused() || UI.CapturingKey) return;
            if (InputX.KeyDown(Key.Escape))
            {
                HandleEscape();
                return;
            }
            if (UI.ModalOpen) return;
            if (InputX.Ctrl)
            {
                // Ctrl+key never triggers the plain hotkey as well.
                if (InputX.KeyDown(Key.Z)) UndoConstruction();
                return;
            }
            bool drawing = Tools.Active == ToolKind.Line || Tools.Active == ToolKind.Plan;
            // The line and plan tools use F to flip a bend: whatever else is on F waits while drawing.
            bool Hot(BindAction a) => Keybinds.Down(a) && !(drawing && Keybinds.For(a) == Key.F);
            if (Hot(BindAction.Help))
            {
                UI.ShowHelp();
                return;
            }
            if (Hot(BindAction.Pause)) TogglePause();
            if (Hot(BindAction.Speed1)) SetSpeed(0);
            if (Hot(BindAction.Speed2)) SetSpeed(1);
            if (Hot(BindAction.Speed3)) SetSpeed(2);
            if (Hot(BindAction.Overview)) ToggleOverview();
            if (Hot(BindAction.Inspect)) Tools.Select(ToolKind.Select);
            if (Hot(BindAction.Line)) Tools.Select(ToolKind.Line);
            if (Hot(BindAction.Plan)) Tools.Select(ToolKind.Plan);
            if (Hot(BindAction.Insert)) Tools.Select(ToolKind.Insert);
            if (Hot(BindAction.Depot)) Tools.Select(ToolKind.Depot);
            if (Hot(BindAction.Demolish)) Tools.Select(ToolKind.Demolish);
            if (Hot(BindAction.Population)) ToggleOverlay(DataOverlay.Population);
            if (Hot(BindAction.Jobs)) ToggleOverlay(DataOverlay.Jobs);
            if (Hot(BindAction.Coverage)) ToggleOverlay(DataOverlay.Coverage);
            if (Hot(BindAction.Finance)) OpenWindow(WindowKind.Finance);
            if (Hot(BindAction.Stats)) OpenWindow(WindowKind.Stats);
            if (Hot(BindAction.City)) OpenWindow(WindowKind.City);
            if (Hot(BindAction.CityHall)) OpenWindow(WindowKind.CityHall);
            if (Hot(BindAction.HideUI)) UI.SetHudVisible(!UI.HudVisible);
            if (Hot(BindAction.Screenshot)) TakeScreenshot();
        }

        void HandleEscape()
        {
            if (UI.ModalOpen)
            {
                if (!UI.ModalLocked) UI.CloseModal();
                return;
            }
            if (UI.WindowOpen)
            {
                UI.CloseWindow();
                return;
            }
            if (Tools.Cancel()) return;
            if (Sel.kind != SelKind.None)
            {
                ClearSelection();
                return;
            }
            if (!UI.HudVisible)
            {
                UI.SetHudVisible(true);
                return;
            }
            ShowPauseMenu(true);
        }

        void Autosave()
        {
            if (!Settings.autosave || Tutorial.Active || State.gov.gameOver || State.challenge != null) return;
            int day = State.clock.Day;
            if (day != lastAutosaveDay && State.clock.MinuteOfDay >= Balance.NightEnd)
            {
                lastAutosaveDay = day;
                if (SaveGame(SaveSystem.AutosaveSlot) && !UI.ModalOpen) uiRoot.Toast("Autosaved", ToastKind.Info, 1.5f);
            }
        }

        public string TakeScreenshot()
        {
            string dir = Path.Combine(Application.persistentDataPath, "Screenshots");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "metro_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png");
            ScreenCapture.CaptureScreenshot(path);
            uiRoot.Toast("Screenshot saved to " + path, ToastKind.Good, 3f);
            return path;
        }
    }

    public static class GameBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (UnityEngine.Object.FindFirstObjectByType<GameRoot>() != null) return;
            var go = new GameObject("GameRoot");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<GameRoot>();
        }
    }
}
