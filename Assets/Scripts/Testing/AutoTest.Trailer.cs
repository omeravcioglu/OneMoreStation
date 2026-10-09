using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Metro
{
    // Trailer footage: "-autotest trailer -nosteam -shotdir <dir> -screen-width 1920 -screen-height 1080
    // -screen-fullscreen 0". The game clock runs at a fixed 30 fps (Time.captureFramerate), and every
    // frame of a scene is written to <dir>/frames/f_00001.jpg..., so the footage is smooth however long
    // a frame takes. autotest_log.txt marks each scene: "scene <name> start <frame>" / "... end <frame>".
    public sealed partial class AutoTest
    {
        const int Fps = 30;
        bool recording;
        int frameNo;
        string frameDir;

        IEnumerator TrailerTest(GameRoot g)
        {
            game = g;
            frameDir = Path.Combine(outDir, "frames");
            Directory.CreateDirectory(frameDir);
            Time.captureFramerate = Fps;
            g.Settings.autosave = false;     // no "Autosaved" toasts in the footage (not saved to disk)
            InputX.simulate = true;
            ParkMouse();
            StartCoroutine(Recorder());
            yield return Frames(3);
            Note($"trailer: {Screen.width}x{Screen.height}, game step per frame {GameRoot.FrameDt:0.0000} s (real {Time.unscaledDeltaTime:0.000} s)");

            yield return NewTown(g);
            yield return FirstRush(g);
            yield return DawnGrowth(g);
            yield return CityHallAndMatchday(g);
            yield return BigCity(g);
            yield return MenuScene(g);

            recording = false;
            Time.captureFramerate = 0;
            InputX.simulate = false;
            Note($"trailer: {frameNo} frames, {frameNo / (float)Fps:0.0} s of footage");
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
                frameNo++;
                File.WriteAllBytes(Path.Combine(frameDir, $"f_{frameNo:00000}.jpg"), tex.EncodeToJPG(92));
                Destroy(tex);
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

        static void ParkMouse() => InputX.SimMouse(new Vector2(-300f, -300f));

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

        static void AdvanceToAbs(GameState gs, double minute)
        {
            int guard = 0;
            while (gs.clock.totalMinutes < minute && guard++ < 100000) gs.Advance(Mathf.Min(10f, (float)(minute - gs.clock.totalMinutes) + 0.01f));
        }

        // ------------------------------------------------------------------ a. new_town

        IEnumerator NewTown(GameRoot g)
        {
            g.StartNewGame(new NewGameOptions { seed = 2024, cityName = "Port Avalon", difficulty = 1 });
            var gs = g.State;
            gs.progress.holdTierUps = true;
            var cam = g.CamCtl;
            var area = gs.city.playArea;
            float fit = cam.SizeToFit(area, 1.02f);
            cam.FocusOn(area.center + new Vector2(area.width * 0.1f, -area.height * 0.06f), fit * 1.3f, true);
            yield return Frames(2);
            SceneStart("new_town");
            yield return CamTo(area.center, fit, 60);

            // Old Town -> Central -> the district beyond, a stop about every 0.4 s.
            var central = gs.city.districts[0];
            var oldTown = gs.city.districts[1];
            District third = null;
            foreach (var d in gs.city.districts)
                if (d.revealed && d.id > 1 && (third == null || Vector2.Distance(d.center, oldTown.center) > Vector2.Distance(third.center, oldTown.center))) third = d;
            Vector2 a = oldTown.center, b = central.center, c = third != null ? third.center : b + (b - a);
            var route = new List<Vector2>();
            for (int i = 0; i <= 2; i++) route.Add(Land(gs, Vector2.Lerp(a, b, i / 2f)));
            for (int i = 1; i <= 2; i++) route.Add(Land(gs, Vector2.Lerp(b, c, i / 2f)));
            yield return Press(UnityEngine.InputSystem.Key.L);
            Vector2 mouse = ScreenOf(route[0]) + new Vector2(-160f, 90f);
            foreach (var p in route)
            {
                Vector2 target = ScreenOf(p);
                for (int f = 1; f <= 8; f++)
                {
                    InputX.SimMouse(Vector2.Lerp(mouse, target, Ease(f / 8f)));
                    yield return null;
                }
                mouse = target;
                InputX.SimMouse(mouse, true);
                yield return Frames(2);
                InputX.SimMouse(mouse);
                yield return Frames(2);
            }
            InputX.SimMouse(mouse, false, true);
            yield return Frames(2);
            InputX.SimMouse(mouse);
            yield return Frames(3);
            ParkMouse();
            MetroLine line = null;
            foreach (var l in gs.net.AliveLines()) line = l;
            if (line != null)
            {
                gs.build.BuildDepot(line.stops[line.stops.Count / 2]);
                gs.build.AddTrain(line);
                gs.build.AddTrain(line);
            }
            // Building selects the new line; keep the map clear for the trains rolling out.
            g.ClearSelection();
            g.SetSpeed(1);
            yield return Frames(80);
            SceneEnd("new_town");
            Note($"new_town: lines {gs.net.LineCount}, stations {gs.net.StationCount}, trains {gs.trains.AliveCount}");
        }

        // ------------------------------------------------------------------ b. first_rush

        IEnumerator FirstRush(GameRoot g)
        {
            var gs = g.State;
            g.SetPaused(true);
            g.ClearSelection();
            AdvanceTo(gs, 0, 7 * 60 + 50);
            Station busiest = null;
            Vector2 mid = gs.city.districts[0].center;
            int n = 0;
            Vector2 sum = Vector2.zero;
            foreach (var s in gs.net.AliveStations())
            {
                sum += s.pos;
                n++;
                if (busiest == null || s.waiting > busiest.waiting) busiest = s;
            }
            if (n > 0) mid = sum / n;
            g.CamCtl.FocusOn(mid, 4.4f, true);
            g.SetSpeed(1);
            yield return Frames(2);
            SceneStart("first_rush");
            yield return CamTo(busiest != null ? Vector2.Lerp(mid, busiest.pos, 0.8f) : mid, 2.3f, 195);
            SceneEnd("first_rush");
            Note($"first_rush: {gs.clock.TimeString}, waiting {Fmt.Count(gs.stats.totalWaiting)}, busiest {busiest?.name} ({Fmt.Count(busiest != null ? busiest.waiting : 0f)})");
        }

        // ------------------------------------------------------------------ c. dawn_growth

        IEnumerator DawnGrowth(GameRoot g)
        {
            var gs = g.State;
            g.SetPaused(true);
            AdvanceTo(gs, 1, 4 * 60 + 57);
            var cam = g.CamCtl;
            var area = gs.city.playArea;
            cam.FocusOn(area.center, cam.SizeToFit(area, 1.02f), true);
            TierReachedInfo info = null;
            Action<TierReachedInfo> grabbed = i => info = i;
            gs.progress.TierReached += grabbed;
            g.SetSpeed(0);
            yield return Frames(2);
            SceneStart("dawn_growth");
            yield return Frames(40);
            // Grow now; the card is held back so the frame can be seen pushing outward first.
            recording = false;
            gs.progress.TierUp();
            yield return Frames(1);
            if (g.UI.ModalOpen) g.UI.CloseModal();
            recording = true;
            yield return Frames(105);
            if (info != null) g.UI.ShowTierUp(info, () => { });
            yield return Frames(70);
            if (g.UI.ModalOpen) g.UI.CloseModal();
            yield return Frames(8);
            SceneEnd("dawn_growth");
            gs.progress.TierReached -= grabbed;
            Note($"dawn_growth: now a {gs.progress.Current.name}, map {gs.city.playArea.width:0}x{gs.city.playArea.height:0} km");
        }

        // ------------------------------------------------------------------ d. city_hall + matchday

        IEnumerator CityHallAndMatchday(GameRoot g)
        {
            g.StartNewGame(new NewGameOptions { seed = 2024, cityName = "Port Avalon", difficulty = 1, startTier = 2 });
            var gs = g.State;
            var gov = gs.gov;
            gs.progress.holdTierUps = true;
            AutoPlanner.BuildNetwork(gs, 3, true, 0.45f, 3);
            gs.trains.RefreshAllPoses();
            g.SetPaused(true);
            AdvanceTo(gs, 0, 9 * 60);
            var near = gov.Propose(RequestKind.StationNear);
            var lm = near != null ? gov.LandmarkById(near.landmarkId) : null;
            var cam = g.CamCtl;
            var area = gs.city.playArea;
            cam.FocusOn(area.center, cam.SizeToFit(area, 1.02f), true);
            yield return Frames(2);
            SceneStart("city_hall");
            yield return Frames(24);
            if (lm != null) yield return CamTo(lm.pos, 2.8f, 80);
            yield return Frames(45);
            SceneEnd("city_hall");
            Note($"city_hall: {near?.title}, landmark {lm?.name}");
            if (near != null) gov.Decline(near);

            var ev = gov.Propose(RequestKind.Event);
            if (ev == null)
            {
                Note("matchday: no event could be proposed");
                yield break;
            }
            gov.Accept(ev);
            var venue = gov.LandmarkById(ev.landmarkId);
            if (venue != null && gov.ServedStationNear(venue.pos) == null) ConnectVenue(gs, venue.pos);
            AdvanceToAbs(gs, ev.eventEnd + 6);
            var st = venue != null ? gov.ServedStationNear(venue.pos) : null;
            Vector2 focus = st != null ? st.pos : venue != null ? venue.pos : area.center;
            cam.FocusOn(focus, 3.4f, true);
            g.SetSpeed(0);
            yield return Frames(2);
            SceneStart("matchday");
            yield return CamTo(focus, 1.7f, 165);
            SceneEnd("matchday");
            Note($"matchday: {ev.title}, venue station {st?.name ?? "none"}, waiting there {Fmt.Count(st != null ? st.waiting : 0f)}, fans carried {Fmt.Count(ev.served)} of {Fmt.Count(ev.wanted)}");
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

        // ------------------------------------------------------------------ e. rush_closeup, f. zoom_out, g. night

        IEnumerator BigCity(GameRoot g)
        {
            var big = new GameState(new NewGameOptions { seed = 31415, cityName = "Kestrel Bay", difficulty = 1, startTier = ProgressionSystem.Tiers.Length - 1 });
            big.growth.RevealSilently(26);
            AutoPlanner.BuildNetwork(big, 9, true, 0.45f, 5);
            big.gov.SyncWithProgress(false);
            AdvanceTo(big, 1, 8 * 60 + 15);
            g.EnterGame(big);
            g.UI.SetHudVisible(true);
            var cam = g.CamCtl;
            Vector2 c = big.city.center;
            Vector2 from = c + new Vector2(-1.7f, 0.5f), to = c + new Vector2(1.5f, -0.3f);
            cam.FocusOn(from, 3.0f, true);
            g.SetSpeed(0);
            yield return Frames(2);
            SceneStart("rush_closeup");
            const int pan = 240;
            for (int i = 0; i < pan; i++)
            {
                float t = Ease(i / (float)(pan - 1));
                cam.FocusOn(Vector2.Lerp(from, to, t), Mathf.Lerp(3.0f, 2.6f, t), true);
                if (i == pan / 2) g.UI.SetHudVisible(false);
                yield return null;
            }
            SceneEnd("rush_closeup");

            SceneStart("zoom_out");
            var area = big.city.playArea;
            yield return CamTo(area.center, cam.SizeToFit(area, 1.02f), 190);
            yield return Frames(20);
            SceneEnd("zoom_out");

            // Night falls: the clock is wound by hand from 22:15 to 00:45 over 6 s.
            g.SetPaused(true);
            AdvanceTo(big, 1, 22 * 60 + 15);
            cam.FocusOn(c, cam.SizeToFit(area, 1.02f) * 0.62f, true);
            yield return Frames(2);
            SceneStart("night");
            const int nightFrames = 180;
            for (int i = 0; i < nightFrames; i++)
            {
                big.Advance(150f / nightFrames);
                cam.FocusOn(c, cam.Size * 0.9988f, true);
                yield return null;
            }
            SceneEnd("night");
            g.UI.SetHudVisible(true);
            Note($"big city: {big.net.StationCount} stations, {big.net.LineCount} lines, {big.trains.AliveCount} trains, clock {big.clock.TimeString}");
        }

        // ------------------------------------------------------------------ h. menu

        IEnumerator MenuScene(GameRoot g)
        {
            g.SetPaused(false);
            g.ExitToMenu();
            yield return Frames(24);
            SceneStart("menu");
            yield return Frames(130);
            SceneEnd("menu");
        }
    }
}
