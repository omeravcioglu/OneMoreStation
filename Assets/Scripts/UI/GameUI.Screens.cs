using System;
using System.Collections.Generic;
using System.IO;
using Metro.Platform;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Metro.UI
{
    /// <summary>Settings, help, challenges, achievements, credits and licences.</summary>
    public sealed partial class GameUI
    {
        // ------------------------------------------------------------------ settings

        int settingsTab;

        public void ShowSettings()
        {
            var st = g.Settings;
            var p = OpenModal(600f, () => st.Save());
            p.name = "settings";
            ModalHead(p, "Settings", CloseModal, "head-sky");
            var tabs = UIK.Row(p, "settings-tabs");
            var content = UIK.Col(p, "settings-body");
            void Fill(int tab)
            {
                settingsTab = tab;
                rebinding = null;
                content.Clear();
                switch (tab)
                {
                    case 0: GeneralSettings(content, st); break;
                    case 1: AudioTab(content, st); break;
                    case 2: DisplaySettings(content, st); break;
                    default: ControlsTab(content); break;
                }
            }
            UIK.Segmented(tabs, new[] { "Game", "Audio", "Display", "Controls" }, settingsTab, Fill);
            Fill(settingsTab);
            var row = UIK.Row(p, "mt-12");
            UIK.Spacer(row);
            UIK.Button(row, "Done", CloseModal, "btn-primary");
        }

        static void SwitchRow(VisualElement parent, string label, string hint, bool value, Action<bool> set)
        {
            var r = UIK.Row(parent, "upgrade-row");
            var c = UIK.Col(r);
            c.style.flexGrow = 1f;
            c.style.flexShrink = 1f;
            UIK.Text(c, label, "h3");
            if (hint != null)
            {
                var h = UIK.Text(c, hint, "small", "muted");
                h.style.whiteSpace = WhiteSpace.Normal;
            }
            UIK.Switch(r, value, set);
        }

        static void SliderRow(VisualElement parent, string label, float min, float max, float value, Func<float, string> fmt, Action<float> set)
        {
            var r = UIK.Row(parent, "upgrade-row");
            var name = UIK.Text(r, label, "h3");
            name.style.width = 150f;
            var slider = new Slider(min, max) { value = value };
            slider.style.flexGrow = 1f;
            var lab = UIK.Text(r, fmt(value), "small", "muted");
            lab.style.width = 48f;
            lab.style.unityTextAlign = TextAnchor.MiddleRight;
            slider.RegisterValueChangedCallback(e =>
            {
                lab.text = fmt(e.newValue);
                set(e.newValue);
            });
            r.Insert(1, slider);
        }

        void GeneralSettings(VisualElement p, GameSettings st)
        {
            SwitchRow(p, "Station names on the map", null, st.labels, v =>
            {
                st.labels = v;
                st.Apply();
            });
            SwitchRow(p, "Pan at screen edges", "Move the map when the mouse touches the edge of the screen.", st.edgePan, v =>
            {
                st.edgePan = v;
                st.Apply();
            });
            SwitchRow(p, "Autosave every morning", "Saved to the autosave slot at 05:00, and when you quit.", st.autosave, v => st.autosave = v);
            SwitchRow(p, "Pause when the game loses focus", "Alt-tabbing out stops the clock.", st.pauseOnFocusLoss, v => st.pauseOnFocusLoss = v);
            SwitchRow(p, "Colour-blind friendly colours", "Crowding goes blue → yellow → orange → vermilion instead of green → red.", st.colorBlind, v =>
            {
                st.colorBlind = v;
                st.Apply();
            });
            SliderRow(p, "Interface size", 0.8f, 1.3f, st.uiScale, v => Fmt.Pct(Mathf.Round(v * 20f) / 20f), v =>
            {
                st.uiScale = Mathf.Round(v * 20f) / 20f;
                st.Apply();
            });
            var plat = UIK.Text(p, "Platform: " + PlatformServices.Status + (CloudSync.Enabled ? ". Saves and achievements sync with Steam Cloud." : ". Saves stay on this computer."), "small", "muted", "mt-12");
            plat.style.whiteSpace = WhiteSpace.Normal;
        }

        void AudioTab(VisualElement p, GameSettings st)
        {
            SliderRow(p, "Master volume", 0f, 1f, st.masterVolume, Fmt.Pct, v =>
            {
                st.masterVolume = v;
                st.Apply();
            });
            SliderRow(p, "Music", 0f, 1f, st.musicVolume, Fmt.Pct, v =>
            {
                st.musicVolume = v;
                st.Apply();
            });
            SliderRow(p, "Sound effects", 0f, 1f, st.sfxVolume, Fmt.Pct, v =>
            {
                st.sfxVolume = v;
                st.Apply();
            });
            var hint = UIK.Text(p, "The soundtrack is generated as you play and follows the time of day in your city.", "small", "muted", "mt-8");
            hint.style.whiteSpace = WhiteSpace.Normal;
        }

        void DisplaySettings(VisualElement p, GameSettings st)
        {
            UIK.Text(p, "WINDOW", "field-label", "mt-4");
            UIK.Segmented(p, GameSettings.WindowModeNames, st.windowMode, i =>
            {
                st.windowMode = i;
                st.Apply();
            });
            UIK.Text(p, "RESOLUTION", "field-label", "mt-12");
            var res = GameSettings.Resolutions();
            var choices = new List<string> { "Automatic" };
            int sel = 0;
            for (int i = 0; i < res.Count; i++)
            {
                choices.Add($"{res[i].x} × {res[i].y}");
                if (res[i].x == st.resolutionWidth && res[i].y == st.resolutionHeight) sel = i + 1;
            }
            var dd = new DropdownField(choices, sel);
            dd.RegisterValueChangedCallback(e =>
            {
                int i = choices.IndexOf(e.newValue);
                if (i <= 0)
                {
                    st.resolutionWidth = st.resolutionHeight = 0;
                }
                else
                {
                    st.resolutionWidth = res[i - 1].x;
                    st.resolutionHeight = res[i - 1].y;
                }
                st.Apply();
            });
            p.Add(dd);
            SwitchRow(p, "Vertical sync", "Smooth, tear-free frames that match your screen.", st.vsync, v =>
            {
                st.vsync = v;
                st.Apply();
            });
            UIK.Text(p, "FRAME RATE LIMIT WITH VSYNC OFF", "field-label", "mt-12");
            int fl = Array.IndexOf(GameSettings.FrameLimits, st.frameLimit);
            if (fl < 0) fl = GameSettings.FrameLimits.Length - 1;
            UIK.Segmented(p, GameSettings.FrameLimitNames, fl, i =>
            {
                st.frameLimit = GameSettings.FrameLimits[i];
                st.Apply();
            });
            var fh = UIK.Text(p, "30 or 60 saves power on laptops.", "small", "muted", "mt-4");
            fh.style.whiteSpace = WhiteSpace.Normal;
            if (Application.isEditor) UIK.Text(p, "Window and resolution changes apply in the built game.", "small", "muted", "mt-8");
        }

        static readonly (string keys, string what)[] FixedControls =
        {
            ("Right / middle drag · WASD · arrows", "Pan the map"),
            ("Mouse wheel · + −", "Zoom"),
            ("Click · right-click · Enter", "While drawing a line: place a station · build"),
            ("F · Backspace (while drawing)", "Flip the bend · remove the last stop"),
            ("Ctrl+Z", "Undo the last construction (same day, within a few hours)"),
            ("Esc", "Close, cancel, or open the menu")
        };

        // Key rebinding: the action waiting for a key press, if any.
        BindAction? rebinding;
        Action afterRebind;

        /// <summary>True while the Controls tab waits for a key (hotkeys are suspended).</summary>
        public bool CapturingKey => rebinding.HasValue;

        void ControlsTab(VisualElement p)
        {
            var hint = UIK.Text(p, "Click a key to change it, then press the new key (Esc cancels). Taking a key that's already used swaps the two.", "small", "muted");
            hint.style.whiteSpace = WhiteSpace.Normal;
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.maxHeight = 380f;
            scroll.style.marginTop = 6f;
            p.Add(scroll);
            void Build()
            {
                scroll.contentContainer.Clear();
                foreach (BindAction a in Enum.GetValues(typeof(BindAction)))
                {
                    var r = UIK.Row(scroll.contentContainer, "table-row");
                    var w = UIK.Text(r, Keybinds.Title(a), "small");
                    w.style.flexGrow = 1f;
                    var act = a;
                    bool waiting = rebinding.HasValue && rebinding.Value == a;
                    var b = UIK.Button(r, waiting ? "Press a key…" : Keybinds.Label(a), () =>
                    {
                        rebinding = act;
                        afterRebind = Build;
                        Build();
                    }, "btn-small", waiting ? "btn-primary" : "btn-ghost");
                    b.name = "bind-" + a.ToString().ToLowerInvariant();
                    b.style.minWidth = 110f;
                }
                UIK.Text(scroll.contentContainer, "ALWAYS", "caps", "mt-12");
                foreach (var (keys, what) in FixedControls)
                {
                    var r = UIK.Row(scroll.contentContainer, "table-row");
                    var k = UIK.Text(r, keys, "hotkey");
                    k.style.minWidth = 200f;
                    k.style.marginRight = 12f;
                    var w = UIK.Text(r, what, "small");
                    w.style.flexShrink = 1f;
                    w.style.whiteSpace = WhiteSpace.Normal;
                }
            }
            Build();
            var row = UIK.Row(p, "mt-8");
            UIK.Button(row, "Reset to defaults", () =>
            {
                Keybinds.ResetDefaults();
                g.Settings.Save();
                rebinding = null;
                Build();
            }, "btn-small", "btn-ghost");
        }

        // ------------------------------------------------------------------ per-frame and HUD extras

        ChallengeBanner challengeBanner;

        void InitScreens()
        {
            challengeBanner = new ChallengeBanner(g, hud);
        }

        void TickScreens(float dt)
        {
            challengeBanner?.Refresh(menu == null && UIK.Shown(hud));
            if (!rebinding.HasValue) return;
            if (modal == null)
            {
                rebinding = null;
                return;
            }
            if (!InputX.AnyKeyDown(out var k)) return;
            if (k != Key.Escape && Keybinds.Reserved(k))
            {
                root.Toast($"{Keybinds.KeyLabel(k)} is kept for the camera and menus. Pick another key.", ToastKind.Info, 2.5f);
                return;
            }
            if (k != Key.Escape)
            {
                Keybinds.Bind(rebinding.Value, k);
                g.Settings.Save();
            }
            rebinding = null;
            afterRebind?.Invoke();
        }

        // ------------------------------------------------------------------ help

        public void ShowHelp()
        {
            var p = OpenModal(680f);
            p.name = "help";
            ModalHead(p, "How to play", CloseModal, "head-mint");
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.maxHeight = 560f;
            p.Add(scroll);
            var c = scroll.contentContainer;
            IconKind[] icons = { IconKind.Line, IconKind.Smile, IconKind.City, IconKind.Coin, IconKind.Hall, IconKind.Pencil };
            Color[] discs = { Doodle.GumL, Doodle.MintL, Doodle.SkyL, Doodle.SunL, Doodle.GrapeL, Doodle.TangL };
            int n = 0;
            void Section(string head, string body)
            {
                var row = UIK.Row(c, "help-section");
                var disc = UIK.Div(row, "help-icon");
                disc.style.backgroundColor = discs[n % discs.Length];
                disc.Add(new IconElement(icons[n % icons.Length], 26f));
                n++;
                var col = UIK.Col(row);
                col.style.flexShrink = 1f;
                UIK.Text(col, head, "caps");
                var t = UIK.Text(col, body, "small");
                t.style.whiteSpace = WhiteSpace.Normal;
            }
            Section("THE BASICS",
                $"Draw lines with the Line tool ({Keybinds.Label(BindAction.Line)}): click to place stations about a kilometre apart, click an existing station to connect to it, " +
                "and right-click or press Enter to build. A line needs a depot (" + Keybinds.Label(BindAction.Depot) + ") at one of its stations before you can add trains " +
                "from the line's panel. People walk up to a kilometre to a station, ride, change lines where they meet and get off near where they're going.");
            Section("READING THE MAP",
                "Stations wear their mood on their face: chill green, busy yellow, squished orange (sweating) and meltdown red, when people are being left behind " +
                "(Settings → Game has colour-blind friendly colours). The little queue beside a station grows as it fills, and packed trains shout \"!!\". " +
                "Houses show where people live and towers where they work; clouds hide districts that haven't opened yet. On weekdays the rush hours are " +
                "07:00–09:30, when everyone heads into the centre, and 16:30–19:00, when they head back out.");
            Section("GROWING YOUR CITY",
                "Every city starts as a small town. The sticky note (top left) lists what it takes to grow. Once every goal is met the city grows at dawn: " +
                "a bigger map, new districts, more lines and new tools, each explained by a card. When you're ready, Skip to dawn fast-forwards to it.");
            Section("MONEY",
                "You earn fares, a subsidy per rider from City Hall and advertising, and you pay staff, maintenance and construction. Finances (" +
                Keybinds.Label(BindAction.Finance) + ") sets the fare. From City size, loans help you build big. If you stay in the red for three days, City Hall bails you out, once.");
            Section("CITY HALL",
                "From Market town the mayor sends requests. Accept them for grants and approval, or decline at a small cost. Approval sets your subsidy and building permits. " +
                "From City size there are also orders you can't refuse, levies and an election every two weeks. If approval is still below 25% a week after a final warning, you lose the metro.");
            Section("TIPS",
                $"Ctrl+Z undoes your last construction. Sketch future lines for free with the pencil ({Keybinds.Label(BindAction.Plan)}). " +
                $"{Keybinds.Label(BindAction.Overview)} zooms out to the whole city. Click a message in the manager's feed to jump to the problem. " +
                $"You can change any of these keys in Settings → Controls. Press {Keybinds.Label(BindAction.Help)} to open this help again.");
            var row = UIK.Row(p, "mt-12");
            UIK.Button(row, "Controls", () =>
            {
                settingsTab = 3;
                ShowSettings();
            }, "btn-ghost");
            UIK.Spacer(row);
            UIK.Button(row, "Got it", CloseModal, "btn-primary");
        }

        // ------------------------------------------------------------------ challenges

        public void ShowChallenges()
        {
            var p = OpenModal(700f);
            p.name = "challenges";
            ModalHead(p, "Challenges", CloseModal, "head-sky");
            var intro = UIK.Text(p, "Hand-made situations with one goal and a deadline. Earn up to three stars on each.", "small", "muted");
            intro.style.whiteSpace = WhiteSpace.Normal;
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.maxHeight = 560f;
            scroll.style.marginTop = 8f;
            p.Add(scroll);
            foreach (var c in Challenges.All)
            {
                var card = UIK.Col(scroll.contentContainer, "request-card", "challenge-card");
                var top = UIK.Row(card);
                var ic = UIK.Div(top, "challenge-icon");
                ic.Add(new IconElement(IconKind.Faster, 22f));
                UIK.Text(top, c.title, "challenge-title");
                UIK.Spacer(top);
                int best = Achievements.Profile.ChallengeStars(c.id);
                UIK.Stars(top, best, 22f);
                var blurb = UIK.Text(card, c.blurb, "small");
                blurb.style.whiteSpace = WhiteSpace.Normal;
                string time = c.goal == ChallengeGoal.EventShare ? "until the event is over" : $"{c.days:0} days";
                UIK.Text(card, $"Starts as a {ProgressionSystem.Tiers[c.startTier].name} · {Balance.DifficultyNames[Mathf.Clamp(c.difficulty, 0, 2)]} · {time}", "small", "muted", "mt-4");
                var goal = UIK.Text(card, "Goal: " + c.GoalText(), "small", "accent", "mt-4");
                goal.style.whiteSpace = WhiteSpace.Normal;
                UIK.Text(card, c.StarRule(), "tiny", "faint");
                var row = UIK.Row(card, "mt-4");
                UIK.Spacer(row);
                var def = c;
                var play = UIK.Button(row, best > 0 ? "Play again" : "Play", () =>
                {
                    CloseModal();
                    g.StartChallenge(def);
                }, "btn-small", "btn-primary", "btn-mint");
                play.name = "play-" + c.id;
            }
        }

        /// <summary>The end of a challenge: stars, a new best, try again or move on.</summary>
        public void ShowChallengeResult(ChallengeRun run, bool newBest)
        {
            var p = OpenModal(560f, null, false);
            p.name = "challenge-result";
            p.AddToClassList("tierup");
            var band = UIK.Div(p, "tierup-band");
            if (run.won) band.Add(new SunRays());
            else band.AddToClassList("tierup-band-grey");
            var kick = UIK.Row(band, "tierup-kicker-row");
            var kic = new IconElement(run.won ? IconKind.Trophy : IconKind.Clock, 22f);
            kic.style.marginRight = 7f;
            kick.Add(kic);
            UIK.Text(kick, run.won ? "CHALLENGE COMPLETE" : "CHALLENGE FAILED", "tierup-kicker");
            UIK.Text(band, run.def.title + (run.won ? "!" : ""), "tierup-title");
            UIK.Stars(band, run.stars, 46f).AddToClassList("result-stars");
            if (run.won)
            {
                var conf = new Confetti(70f, 5, 9);
                conf.AddToClassList("tierup-confetti-l");
                band.Add(conf);
            }
            p = UIK.Col(p, "tierup-body");
            string detail;
            if (run.won)
                detail = run.def.goal == ChallengeGoal.EventShare ? run.status : $"Done in {run.DaysUsed:0.0} days. {run.def.StarRule()}";
            else
                detail = g.State != null && g.State.gov.gameOver ? "City Hall took the metro away before you got there." : "Time ran out. " + run.status;
            var t = UIK.Text(p, detail, "tierup-blurb");
            t.style.whiteSpace = WhiteSpace.Normal;
            if (newBest && run.stars > 0)
            {
                var nb = UIK.Text(p, "New best!", "result-best");
                UIK.Display(nb);
            }
            var row = UIK.Row(p, "mt-12");
            row.style.flexWrap = Wrap.Wrap;
            var def = run.def;
            UIK.Button(row, "Try again", () =>
            {
                CloseModal();
                g.StartChallenge(def);
            }).style.marginRight = 8f;
            int next = Challenges.Next(def);
            if (run.won && next >= 0)
                UIK.Button(row, "Next challenge", () =>
                {
                    CloseModal();
                    g.StartChallenge(Challenges.All[next]);
                }).style.marginRight = 8f;
            UIK.Spacer(row);
            UIK.Button(row, "Keep playing", CloseModal).style.marginRight = 8f;
            UIK.Button(row, "Main menu", () =>
            {
                CloseModal();
                g.ExitToMenu();
            }, "btn-primary");
        }

        // ------------------------------------------------------------------ achievements

        public void ShowAchievements()
        {
            var p = OpenModal(660f);
            p.name = "achievements";
            ModalHead(p, "Achievements", CloseModal, "head-sun");
            var prof = Achievements.Profile;
            var sum = UIK.Text(p, $"{Achievements.UnlockedCount} of {Achievements.All.Length} unlocked · {Fmt.Count(prof.totalTrips)} trips carried · {prof.citiesFounded} cities founded · {prof.requestsDone} City Hall requests done", "small", "muted");
            sum.style.whiteSpace = WhiteSpace.Normal;
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.maxHeight = 520f;
            scroll.style.marginTop = 8f;
            p.Add(scroll);
            foreach (var a in Achievements.All)
            {
                bool got = prof.Has(a.id);
                var row = UIK.Row(scroll.contentContainer, "ach-row");
                if (!got) row.AddToClassList("ach-locked");
                var badge = UIK.Div(row, "ach-badge");
                badge.style.backgroundColor = got ? (Color)a.color : Doodle.Sand;
                badge.Add(new IconElement(got || !a.hidden ? a.icon : IconKind.Lock, 28f));
                var col = UIK.Col(row);
                col.style.flexGrow = 1f;
                col.style.flexShrink = 1f;
                bool secret = a.hidden && !got;
                UIK.Text(col, secret ? "Secret achievement" : a.title, "h3");
                var d = UIK.Text(col, secret ? "Keep playing to find out." : a.description, "small", "muted");
                d.style.whiteSpace = WhiteSpace.Normal;
                if (!got && a.progressMax > 0 && a.progress != null)
                {
                    float v = Mathf.Min(a.progress(prof), a.progressMax);
                    var (bar, fill) = UIK.Bar(col, a.color);
                    bar.style.maxWidth = 260f;
                    UIK.SetBar(fill, v / a.progressMax);
                    UIK.Text(col, $"{Fmt.Count(v)} / {Fmt.Count(a.progressMax)}", "tiny", "faint");
                }
                if (got)
                {
                    long t = prof.WhenUnlocked(a.id);
                    string when = t > 0 ? DateTimeOffset.FromUnixTimeSeconds(t).LocalDateTime.ToString("d MMM yyyy") : "";
                    UIK.Text(row, when, "tiny", "faint");
                }
            }
            var foot = UIK.Text(p, PlatformServices.Current.IsOnline ? "Unlocked achievements also appear on your Steam profile." : "Offline: achievements are kept on this computer and sent to Steam next time it's running.", "small", "muted", "mt-8");
            foot.style.whiteSpace = WhiteSpace.Normal;
        }

        public void AchievementToast(AchievementDef a)
        {
            root.Toast("Achievement unlocked: " + a.title + " — " + a.description, ToastKind.Achievement, 5f);
            Sfx.Reveal();
        }

        // ------------------------------------------------------------------ credits & licences

        public void ShowCredits()
        {
            var p = OpenModal(600f);
            p.name = "credits";
            ModalHead(p, "Credits", CloseModal, "head-gum");
            var logoTex = Resources.Load<Texture2D>("UI/MenuLogo");
            if (logoTex != null)
            {
                var logo = UIK.Div(p, "credits-logo");
                logo.style.backgroundImage = new StyleBackground(logoTex);
            }
            else UIK.Text(p, "ONE MORE STATION", "tierup-kicker");
            UIK.Text(p, $"Version {Application.version} · © {DateTime.Now.Year} {Application.companyName}", "small", "muted");
            void Line(string head, string body)
            {
                UIK.Text(p, head, "caps", "mt-12");
                var t = UIK.Text(p, body, "small");
                t.style.whiteSpace = WhiteSpace.Normal;
            }
            Line("MADE BY", $"{Application.companyName}: design, simulation, code, maps and music. Every graphic is drawn in code and the soundtrack is generated live while you play.");
            Line("BUILT WITH", "Unity 6 (" + Application.unityVersion + ").");
            Line("TYPEFACES", "Shantell Sans by Shantell Martin and Arrow Type (The Shantell Sans Project Authors), under the SIL Open Font License 1.1. " +
                              "Luckiest Guy by Brian J. Bonislawsky (Astigmatic), under the Apache License 2.0. " +
                              "Inter by Rasmus Andersson and the Inter Project Authors, under the SIL Open Font License 1.1.");
            Line("STEAM", "Steamworks.NET by Riley Labrecque (MIT License). Steam and the Steamworks SDK © Valve Corporation.");
            Line("THANK YOU", "To everyone who rides, plans and argues about metro maps.");
            var row = UIK.Row(p, "mt-12");
            UIK.Button(row, "Third-party licences", ShowLicences, "btn-ghost");
            UIK.Spacer(row);
            UIK.Button(row, "Close", CloseModal, "btn-primary");
        }

        public void ShowLicences()
        {
            var p = OpenModal(660f);
            ModalHead(p, "Third-party licences", CloseModal, "head-gum");
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.maxHeight = 560f;
            p.Add(scroll);
            string dir = Path.Combine(Application.streamingAssetsPath, "Licenses");
            try
            {
                var files = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.txt") : new string[0];
                Array.Sort(files);
                foreach (var f in files)
                {
                    UIK.Text(scroll.contentContainer, Path.GetFileNameWithoutExtension(f), "h3", "mt-12");
                    UIK.Text(scroll.contentContainer, File.ReadAllText(f), "licence-text");
                }
                if (files.Length == 0) UIK.Text(scroll.contentContainer, "Licence files are missing from this build.", "small", "muted");
            }
            catch (Exception e)
            {
                UIK.Text(scroll.contentContainer, "Couldn't read the licence files: " + e.Message, "small", "muted");
            }
            var row = UIK.Row(p, "mt-12");
            UIK.Spacer(row);
            UIK.Button(row, "Back", ShowCredits, "btn-primary");
        }
    }
}
