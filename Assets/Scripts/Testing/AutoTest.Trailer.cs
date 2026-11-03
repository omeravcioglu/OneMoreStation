using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.UIElements;

namespace Metro
{
    // Trailer footage: "-autotest trailer -nosteam -shotdir <dir> -screen-width 1920 -screen-height 1080
    // -screen-fullscreen 0". The game clock steps exactly 1/30 s per frame (Time.captureFramerate, which
    // GameRoot.FrameDt and UIClock follow), and the capture is paced to real time with JPG encoding on
    // worker threads, so UI transitions match the footage too. Recorded frames go to
    // <dir>/frames/f_00001.jpg...; autotest_log.txt has "scene <name> start|end <frame>" and
    // "event <name> <frame>" lines that the trailer edit lines up with the music.
    public sealed partial class AutoTest
    {
        const int Fps = 30;
        bool recording;
        int frameNo;
        string frameDir;
        int pendingWrites, writeErrors;

        IEnumerator TrailerTest(GameRoot g)
        {
            game = g;
            frameDir = Path.Combine(outDir, "frames");
            Directory.CreateDirectory(frameDir);
            Time.captureFramerate = Fps;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = Fps;
            g.Settings.autosave = false;     // no "Autosaved" toasts in the footage (not written to disk)
            // No "Achievement unlocked" toasts either: mark them all as earned in the (test) profile.
            foreach (var a in Metro.Platform.Achievements.All)
                if (!Metro.Platform.Achievements.Profile.Has(a.id)) Metro.Platform.Achievements.Profile.Add(a.id, Metro.Platform.PlayerProfile.Now);
            InputX.simulate = true;
            ParkMouse();
            StartCoroutine(Recorder());
            yield return Frames(3);
            Note($"trailer: {Screen.width}x{Screen.height}, game step per frame {GameRoot.FrameDt:0.0000} s");

            yield return Town(g);
            yield return Market(g);
            yield return Metropolis(g);
            yield return MenuAndChallenge(g);

            recording = false;
            while (Volatile.Read(ref pendingWrites) > 0) yield return null;
            Time.captureFramerate = 0;
            InputX.simulate = false;
            Note($"trailer: {frameNo} frames, {frameNo / (float)Fps:0.0} s of footage, {writeErrors} write errors");
        }

        // ------------------------------------------------------------------ recording and camera

        IEnumerator Recorder()
        {
            var eof = new WaitForEndOfFrame();
            while (true)
            {
                yield return eof;
                if (!recording) continue;
                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                byte[] raw = tex.GetRawTextureData();
                GraphicsFormat fmt = tex.graphicsFormat;
                uint w = (uint)tex.width, h = (uint)tex.height;
                Destroy(tex);
                string path = Path.Combine(frameDir, $"f_{++frameNo:00000}.jpg");
                // At most a dozen raw frames (8 MB each) wait for the encoder.
                while (Volatile.Read(ref pendingWrites) > 12) Thread.Sleep(1);
                Interlocked.Increment(ref pendingWrites);
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        File.WriteAllBytes(path, ImageConversion.EncodeArrayToJPG(raw, fmt, w, h, 0, 90));
                    }
                    catch (Exception)
                    {
                        Interlocked.Increment(ref writeErrors);
                    }
                    finally
                    {
                        Interlocked.Decrement(ref pendingWrites);
                    }
                });
            }
        }

        void SceneStart(string name)
        {
            Note($"scene {name} start {frameNo + 1}");
            recording = true;
        }

        void SceneEnd(string name)
        {
            recording = false;
            Note($"scene {name} end {frameNo}");
        }

        /// <summary>Marks the frame being drawn now (the next one recorded).</summary>
        void Mark(string name) => Note($"event {name} {frameNo + 1}");

        static void ParkMouse() => InputX.SimMouse(new Vector2(-300f, -300f));

        /// <summary>Waits, paused and not recording, until toasts from setup (welcome, new districts) have gone.</summary>
        IEnumerator Settle(int frames = 210)
        {
            bool was = game.Paused;
            game.SetPaused(true);
            yield return Frames(frames);
            game.SetPaused(was);
        }

        static float Ease(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

        /// <summary>An eased camera move; the zoom runs in log space so it feels even at every scale.</summary>
        IEnumerator CamTo(Vector2 toPos, float toSize, int frames)
        {
            var cam = game.CamCtl;
            Vector2 p0 = cam.Position;
            float s0 = cam.Size;
            for (int i = 1; i <= frames; i++)
            {
                float t = Ease(i / (float)frames);
                cam.FocusOn(Vector2.Lerp(p0, toPos, t), Mathf.Exp(Mathf.Lerp(Mathf.Log(s0), Mathf.Log(toSize), t)), true);
                yield return null;
            }
        }

        /// <summary>A slow constant drift (no easing), for shots that are already moving when the cut lands.</summary>
        IEnumerator Drift(Vector2 from, Vector2 to, float s0, float s1, int frames)
        {
            var cam = game.CamCtl;
            for (int i = 0; i < frames; i++)
            {
                float t = i / (float)Mathf.Max(1, frames - 1);
                cam.FocusOn(Vector2.Lerp(from, to, t), Mathf.Lerp(s0, s1, t), true);
                yield return null;
            }
        }

        static void AdvanceToAbs(GameState gs, double minute)
        {
            int guard = 0;
            while (gs.clock.totalMinutes < minute && guard++ < 100000) gs.Advance(Mathf.Min(10f, (float)(minute - gs.clock.totalMinutes) + 0.01f));
        }

        /// <summary>Glides the pointer to each point and clicks it, like a player placing stops.</summary>
        IEnumerator ClickPath(List<Vector2> worldPoints, int glideFrames, Vector2 startScreen)
        {
            Vector2 mouse = startScreen;
            Vector2? prev = null;
            foreach (var p in worldPoints)
            {
                Vector2 target = ScreenOf(p);
                // Start the glide 0.5 km out from the stop just placed: closer than Balance.MinStationSpacing,
                // the receipt under the cursor only says "too close to the previous stop".
                float s0 = prev.HasValue ? Mathf.Clamp(0.5f / Mathf.Max(0.01f, Vector2.Distance(prev.Value, p)), 0.33f, 0.85f) : 0f;
                for (int f = 1; f <= glideFrames; f++)
                {
                    InputX.SimMouse(Vector2.Lerp(mouse, target, s0 + (1f - s0) * Ease(f / (float)glideFrames)));
                    yield return null;
                }
                prev = p;
                mouse = target;
                InputX.SimMouse(mouse, true);
                yield return Frames(2);
                InputX.SimMouse(mouse);
                yield return Frames(2);
            }
        }

        /// <summary>
        /// Builds the planned line with Enter. The pointer moves away first, so the last frames before the build
        /// show the clean plan rather than a "too close to the previous stop" receipt under the cursor.
        /// </summary>
        IEnumerator FinishLine(string mark)
        {
            // Over the goals panel, the map tools ignore the pointer: no preview segment, no receipt.
            InputX.SimMouse(new Vector2(230f, Screen.height - 250f));
            yield return Frames(2);
            InputX.SimKey(UnityEngine.InputSystem.Key.Enter, true);
            yield return null;
            Mark(mark);
            yield return null;
            InputX.SimKey(UnityEngine.InputSystem.Key.Enter, false);
            yield return Frames(2);
        }

        static Rect Bounds(List<Vector2> pts)
        {
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            foreach (var p in pts)
            {
                x0 = Mathf.Min(x0, p.x);
                y0 = Mathf.Min(y0, p.y);
                x1 = Mathf.Max(x1, p.x);
                y1 = Mathf.Max(y1, p.y);
            }
            return Rect.MinMaxRect(x0, y0, x1, y1);
        }

        float FrameFor(Rect r, float pad = 1.35f) => game.CamCtl.SizeToFitArea(r, pad) + 0.4f;

        // ------------------------------------------------------------------ the small town

        IEnumerator Town(GameRoot g)
        {
            g.StartNewGame(new NewGameOptions { seed = 2024, cityName = "Port Avalon", difficulty = 1 });
            var gs = g.State;
            gs.progress.holdTierUps = true;
            var cam = g.CamCtl;
            yield return Settle();
            var area = gs.city.playArea;
            float fit = cam.SizeToFit(area, 1.02f);
            var central = gs.city.districts[0];
            var oldTown = gs.city.districts[1];

            // town_intro: close on the old town at first light, pulling back to show the whole little town.
            cam.FocusOn(Vector2.Lerp(oldTown.center, central.center, 0.35f), fit * 0.42f, true);
            yield return Frames(2);
            SceneStart("town_intro");
            yield return CamTo(area.center, fit, 165);
            yield return Frames(15);
            SceneEnd("town_intro");

            // draw_line: the first line, stop by stop with the cost receipts, then built and running.
            District third = null;
            foreach (var d in gs.city.districts)
                if (d.revealed && d.id > 1 && (third == null || Vector2.Distance(d.center, oldTown.center) > Vector2.Distance(third.center, oldTown.center))) third = d;
            Vector2 a = oldTown.center, b = central.center, c = third != null ? third.center : b + (b - a);
            var route = new List<Vector2>();
            for (int i = 0; i <= 2; i++) route.Add(Land(gs, Vector2.Lerp(a, b, i / 2f)));
            for (int i = 1; i <= 2; i++) route.Add(Land(gs, Vector2.Lerp(b, c, i / 2f)));
            var rb = Bounds(route);
            cam.FocusOn(rb.center, FrameFor(rb), true);
            yield return Frames(2);
            SceneStart("draw_line");
            yield return Press(UnityEngine.InputSystem.Key.L);
            yield return ClickPath(route, 10, ScreenOf(route[0]) + new Vector2(-180f, 110f));
            yield return FinishLine("line_built");
            MetroLine line = null;
            foreach (var l in gs.net.AliveLines()) line = l;
            if (line != null)
            {
                gs.build.BuildDepot(line.stops[line.stops.Count / 2]);
                gs.build.AddTrain(line);
                gs.build.AddTrain(line);
            }
            g.ClearSelection();
            g.SetSpeed(1);
            yield return Drift(rb.center, rb.center + new Vector2(0.4f, 0.15f), cam.Size, cam.Size * 0.93f, 140);
            SceneEnd("draw_line");
            Note($"draw_line: lines {gs.net.LineCount}, stations {gs.net.StationCount}, trains {gs.trains.AliveCount}");
            if (line == null) yield break;

            // rush: the morning rush at 2x; platforms fill and the faces start to sweat.
            g.SetPaused(true);
            AdvanceTo(gs, 0, 7 * 60 + 35);
            Station busiest = null;
            foreach (int sid in line.stops)
            {
                var s = gs.net.GetStation(sid);
                if (s != null && (busiest == null || s.waiting > busiest.waiting)) busiest = s;
            }
            Vector2 mid = rb.center;
            cam.FocusOn(mid, FrameFor(rb) * 0.95f, true);
            g.SetSpeed(1);
            yield return Frames(2);
            SceneStart("rush");
            yield return CamTo(busiest != null ? Vector2.Lerp(mid, busiest.pos, 0.85f) : mid, 1.9f, 195);
            SceneEnd("rush");

            // add_trains: the line's panel, two more trains on the + button.
            g.Select(SelKind.Line, line.id);
            yield return Frames(3);
            SceneStart("add_trains");
            yield return Frames(12);
            Mark("train_added_1");
            yield return UiClick(Metro.UI.UIRoot.I.Root.Q("add-train"), "add train");
            yield return Frames(10);
            Mark("train_added_2");
            yield return UiClick(Metro.UI.UIRoot.I.Root.Q("add-train"), "add train");
            yield return Frames(50);
            SceneEnd("add_trains");
            g.ClearSelection();
            Note($"add_trains: trains on the line {line.trains.Count}, waiting at {busiest?.name} {Fmt.Count(busiest != null ? busiest.waiting : 0f)}");

            // dawn: the night lifts, the town grows (the frame pushes outward), then the growth card.
            g.SetPaused(true);
            AdvanceTo(gs, 1, 4 * 60 + 46);
            cam.FocusOn(area.center, fit, true);
            TierReachedInfo info = null;
            Action<TierReachedInfo> grabbed = i => info = i;
            gs.progress.TierReached += grabbed;
            g.SetSpeed(0);
            yield return Frames(2);
            SceneStart("dawn");
            yield return Frames(66);
            recording = false;
            gs.progress.TierUp();
            yield return Frames(1);
            if (g.UI.ModalOpen) g.UI.CloseModal();
            recording = true;
            Mark("grow");
            yield return Frames(125);
            Mark("card");
            if (info != null) g.UI.ShowTierUp(info, () => { });
            yield return Frames(80);
            if (g.UI.ModalOpen) g.UI.CloseModal();
            yield return Frames(8);
            SceneEnd("dawn");
            gs.progress.TierReached -= grabbed;
            Note($"dawn: now a {gs.progress.Current.name}, map {gs.city.playArea.width:0}x{gs.city.playArea.height:0} km");

            // second_line: a new line from a fresh district through Central, the first interchange.
            area = gs.city.playArea;
            var hub = gs.net.NearestStation(central.center, 3f);
            District fresh = null;
            foreach (var d in gs.city.districts)
            {
                if (!d.revealed || !gs.city.InPlayArea(d.center, 0.8f) || d.Coverage > 0.05f) continue;
                if (fresh == null || d.population > fresh.population) fresh = d;
            }
            if (hub != null && fresh != null)
            {
                Vector2 f0 = Land(gs, fresh.center);
                Vector2 dir = (hub.pos - f0).normalized;
                var route2 = new List<Vector2> { f0, Land(gs, Vector2.Lerp(f0, hub.pos, 0.5f)), hub.pos, Land(gs, hub.pos + dir * 1.6f + Geo.Perp(dir) * 0.5f) };
                var rb2 = Bounds(route2);
                cam.FocusOn(rb2.center, FrameFor(rb2), true);
                g.SetSpeed(1);
                yield return Frames(2);
                SceneStart("second_line");
                yield return Press(UnityEngine.InputSystem.Key.L);
                yield return ClickPath(route2, 8, ScreenOf(route2[0]) + new Vector2(150f, -90f));
                yield return FinishLine("line2_built");
                MetroLine line2 = null;
                foreach (var l in gs.net.AliveLines()) line2 = l;
                if (line2 != null && line2 != line)
                {
                    if (!gs.net.LineHasDepot(line2)) gs.build.BuildDepot(line2.stops[line2.stops.Count / 2]);
                    gs.build.AddTrain(line2);
                    gs.build.AddTrain(line2);
                }
                g.ClearSelection();
                yield return Drift(rb2.center, rb2.center + new Vector2(-0.3f, 0.2f), cam.Size, cam.Size * 0.94f, 95);
                SceneEnd("second_line");
                Note($"second_line: lines {gs.net.LineCount}, interchange at {hub.name}: {hub.lines.Count} lines");

                // pencil: sketching the next idea out past the town limit, for free.
                Vector2 q0 = hub.pos;
                Vector2 q1 = Vector2.Lerp(hub.pos, new Vector2(area.xMax + 1.8f, area.center.y - 0.4f), 0.5f);
                Vector2 q2 = new Vector2(area.xMax + 1.8f, area.center.y - 1.3f);
                var pr = Bounds(new List<Vector2> { q0, q1, q2 });
                cam.FocusOn(pr.center, FrameFor(pr, 1.5f), true);
                yield return Frames(2);
                SceneStart("pencil");
                yield return Press(UnityEngine.InputSystem.Key.K);
                yield return ClickPath(new List<Vector2> { q0, q1, q2 }, 12, ScreenOf(q0) + new Vector2(-120f, -60f));
                InputX.SimMouse(new Vector2(230f, Screen.height - 250f));
                Mark("plan_done");
                yield return Press(UnityEngine.InputSystem.Key.Enter);
                yield return Frames(70);
                SceneEnd("pencil");
                yield return Press(UnityEngine.InputSystem.Key.V);
                Note($"pencil: plans {gs.plans.Count}" + (gs.plans.Count > 0 ? $", '{gs.plans[0].name}'" : ""));
            }
        }

        // ------------------------------------------------------------------ a market town with City Hall

        IEnumerator Market(GameRoot g)
        {
            g.StartNewGame(new NewGameOptions { seed = 2024, cityName = "Port Avalon", difficulty = 1, startTier = 2 });
            var gs = g.State;
            var gov = gs.gov;
            gs.progress.holdTierUps = true;
            AutoPlanner.BuildNetwork(gs, 3, true, 0.45f, 3);
            gs.trains.RefreshAllPoses();
            g.SetPaused(true);
            AdvanceTo(gs, 0, 10 * 60 + 30);
            yield return Settle();
            var cam = g.CamCtl;
            var area = gs.city.playArea;
            Vector2 c = gs.city.districts[0].center;

            // market_pan: a busy little network at 1x.
            cam.FocusOn(c + new Vector2(-2.2f, 1.0f), 3.4f, true);
            g.SetSpeed(0);
            yield return Frames(2);
            SceneStart("market_pan");
            yield return Drift(c + new Vector2(-2.2f, 1.0f), c + new Vector2(1.8f, -0.6f), 3.4f, 3.1f, 150);
            SceneEnd("market_pan");

            // hall_letter: a letter from the mayor arrives; the camera finds its landmark.
            cam.FocusOn(area.center, cam.SizeToFit(area, 1.0f) * 0.8f, true);
            yield return Frames(2);
            SceneStart("hall_letter");
            yield return Frames(24);
            Mark("letter");
            var near = gov.Propose(RequestKind.StationNear);
            var lm = near != null ? gov.LandmarkById(near.landmarkId) : null;
            yield return Frames(24);
            if (lm != null) yield return CamTo(lm.pos, 2.5f, 72);
            yield return Frames(30);
            SceneEnd("hall_letter");
            Note($"hall_letter: {near?.title}");
            if (near != null) gov.Decline(near);
            Metro.UI.UIRoot.I.ClearTransient();

            // matchday: the final whistle, and every fan wants the same train home.
            var ev = gov.Propose(RequestKind.Event);
            if (ev != null)
            {
                gov.Accept(ev);
                var venue = gov.LandmarkById(ev.landmarkId);
                if (venue != null && gov.ServedStationNear(venue.pos) == null) ConnectVenue(gs, venue.pos);
                g.SetPaused(true);
                AdvanceToAbs(gs, ev.eventEnd + 3);
                var st = venue != null ? gov.ServedStationNear(venue.pos) : null;
                Vector2 focus = st != null ? st.pos : venue != null ? venue.pos : area.center;
                cam.FocusOn(focus, 3.2f, true);
                g.SetSpeed(0);
                yield return Frames(2);
                SceneStart("matchday");
                yield return CamTo(focus, 1.55f, 175);
                SceneEnd("matchday");
                Note($"matchday: {ev.title}, {st?.name ?? "no station"}, waiting {Fmt.Count(st != null ? st.waiting : 0f)}");
            }

            // council: election night at the Monday session.
            gov.AnnounceElection();
            gov.Endorse(1);
            int sessionDay = (gs.clock.Day / 7 + 1) * 7;
            g.SetPaused(true);
            AdvanceTo(gs, sessionDay, Balance.CouncilSessionMinute - 4);
            cam.FocusOn(c, 4.0f, true);
            yield return Settle(240);
            g.SetSpeed(0);
            yield return Frames(2);
            SceneStart("council");
            for (int i = 0; i < 200 && !g.UI.ModalOpen; i++) yield return null;
            Mark("session");
            yield return Frames(95);
            SceneEnd("council");
            if (g.UI.ModalOpen) g.UI.CloseModal();
        }

        /// <summary>Gives the event venue a station with trains (built for free: this is staging).</summary>
        static void ConnectVenue(GameState gs, Vector2 venue)
        {
            var b = gs.build;
            bool wasFree = b.freeBuild;
            b.freeBuild = true;
            MetroLine best = null;
            float bd = float.MaxValue;
            foreach (var l in gs.net.AliveLines())
            {
                if (l.loop) continue;
                float d = Mathf.Min(Vector2.Distance(gs.net.stations[l.stops[0]].pos, venue), Vector2.Distance(gs.net.stations[l.stops[l.stops.Count - 1]].pos, venue));
                if (d < bd)
                {
                    bd = d;
                    best = l;
                }
            }
            if (best != null) AutoPlanner.ExtendToward(gs, best, venue, out _);
            if (gs.gov.ServedStationNear(venue) == null && gs.net.LineCount < gs.progress.MaxLines)
            {
                var nl = AutoPlanner.BuildAlong(gs, new List<Vector2> { venue, gs.city.districts[0].center });
                if (nl != null) best = nl;
            }
            if (best != null)
            {
                if (!gs.net.LineHasDepot(best)) b.BuildDepot(best.stops[best.stops.Count / 2]);
                for (int i = 0; i < 4; i++) b.AddTrain(best);
            }
            b.freeBuild = wasFree;
        }

        // ------------------------------------------------------------------ the metropolis

        IEnumerator Metropolis(GameRoot g)
        {
            var big = new GameState(new NewGameOptions { seed = 31415, cityName = "Kestrel Bay", difficulty = 1, startTier = ProgressionSystem.Tiers.Length - 1 });
            big.growth.RevealSilently(26);
            AutoPlanner.BuildNetwork(big, 9, true, 0.45f, 5);
            big.gov.SyncWithProgress(false);
            AdvanceTo(big, 1, 8 * 60 + 5);
            g.EnterGame(big);
            yield return Frames(2);
            Metro.UI.UIRoot.I.ClearTransient();
            var cam = g.CamCtl;
            var area = big.city.playArea;
            Vector2 c = big.city.center;

            // The busiest interchanges, spread out over the city.
            var hubs = new List<Station>(big.net.AliveStations());
            hubs.Sort((x, y) => (y.lines.Count * 1000f + y.waiting).CompareTo(x.lines.Count * 1000f + x.waiting));
            var picks = new List<Station>();
            foreach (var s in hubs)
            {
                bool apart = true;
                foreach (var p in picks)
                    if (Vector2.Distance(p.pos, s.pos) < 4.5f) apart = false;
                if (apart) picks.Add(s);
                if (picks.Count == 4) break;
            }
            while (picks.Count < 4) picks.Add(hubs[picks.Count]);
            Note($"metropolis hubs: {string.Join(", ", picks.ConvertAll(s => s.name + " (" + s.lines.Count + ")"))}");

            g.UI.SetHudVisible(false);
            g.SetSpeed(0);
            // hero: the morning rush in the big city, gumball trains everywhere, no interface.
            Vector2 h0 = picks[0].pos;
            cam.FocusOn(h0 + new Vector2(-0.9f, 0.3f), 2.6f, true);
            yield return Frames(2);
            SceneStart("hero");
            yield return Drift(h0 + new Vector2(-0.9f, 0.3f), h0 + new Vector2(0.7f, -0.2f), 2.6f, 2.15f, 160);
            SceneEnd("hero");

            for (int k = 1; k <= 3; k++)
            {
                float ang = (40f + 120f * (k - 1)) * Mathf.Deg2Rad;
                Vector2 p = h0 + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * 2.4f;
                Vector2 dir = k % 2 == 0 ? new Vector2(1f, 0.25f) : new Vector2(-0.8f, 0.4f);
                if (k == 2) g.UI.SetHudVisible(true);
                cam.FocusOn(p - dir * 0.7f, 2.3f, true);
                yield return Frames(2);
                SceneStart("close_" + k);
                yield return Drift(p - dir * 0.7f, p + dir * 0.7f, 2.3f, 2.05f, 100);
                SceneEnd("close_" + k);
                if (k == 2) g.UI.SetHudVisible(false);
            }

            // wide_pan: a calm look across the whole network.
            float wide = cam.SizeToFit(area, 1.02f) * 0.42f;
            cam.FocusOn(c + new Vector2(-3f, 1.2f), wide, true);
            yield return Frames(2);
            SceneStart("wide_pan");
            yield return Drift(c + new Vector2(-3f, 1.2f), c + new Vector2(3f, -0.8f), wide, wide * 0.95f, 150);
            SceneEnd("wide_pan");

            // zoom_out: from one platform to the whole region in one move.
            cam.FocusOn(h0, 1.7f, true);
            yield return Frames(2);
            SceneStart("zoom_out");
            yield return CamTo(area.center, cam.SizeToFit(area, 1.02f), 200);
            yield return Frames(25);
            SceneEnd("zoom_out");

            // night: the clock wound from 22:15 to 00:45 over 5 s, the city goes to sleep.
            g.SetPaused(true);
            AdvanceTo(big, 1, 22 * 60 + 15);
            cam.FocusOn(c, cam.SizeToFit(area, 1.02f) * 0.6f, true);
            yield return Frames(2);
            SceneStart("night");
            const int nightFrames = 160;
            for (int i = 0; i < nightFrames; i++)
            {
                big.Advance(150f / nightFrames);
                cam.FocusOn(c, cam.Size * 0.9988f, true);
                yield return null;
            }
            SceneEnd("night");
            g.UI.SetHudVisible(true);
            Note($"metropolis: {big.net.StationCount} stations, {big.net.LineCount} lines, {big.trains.AliveCount} trains");
        }

        // ------------------------------------------------------------------ menu, challenges, three stars

        IEnumerator MenuAndChallenge(GameRoot g)
        {
            g.SetPaused(false);
            g.ExitToMenu();
            yield return Frames(30);
            SceneStart("menu");
            yield return Frames(110);
            Mark("challenges");
            g.UI.ShowChallenges();
            yield return Frames(70);
            SceneEnd("menu");
            if (g.UI.ModalOpen) g.UI.CloseModal();

            // Boomtown finished in three days: the result card and its three stars.
            g.StartChallenge(Challenges.Get("boomtown"));
            yield return Frames(3);
            var gs = g.State;
            g.SetPaused(true);
            AdvanceTo(gs, 3, 4 * 60 + 50);
            for (int i = 0; i < 4; i++) gs.progress.TierUp();
            for (int i = 0; i < 40 && !Visible("challenge-result"); i++)
            {
                if (g.UI.ModalOpen) g.UI.CloseModal();
                yield return null;
            }
            SceneStart("stars");
            Mark("stars");
            yield return Frames(95);
            SceneEnd("stars");
            Note($"stars: {gs.challenge?.stars} stars, {gs.challenge?.DaysUsed:0.0} days");
        }
    }
}
