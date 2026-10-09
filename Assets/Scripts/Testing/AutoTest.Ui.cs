using System.Collections;
using Metro.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace Metro
{
    // Visual checks of the interface (tooltips, receipts, pencil plans, feed, toasts): "-autotest ui".
    // This part of AutoTest belongs to the UI session; the scenario switch is in AutoTest.cs.
    public sealed partial class AutoTest
    {
        IEnumerator UiTest(GameRoot g)
        {
            game = g;
            g.StartNewGame(new NewGameOptions { seed = 2024, cityName = "Port Avalon", difficulty = 1, startTier = 3 });
            yield return Wait(1.2f);
            var gs = g.State;
            gs.progress.holdTierUps = true;
            AutoPlanner.BuildNetwork(gs, 3, true, 0.45f, 3);
            gs.trains.RefreshAllPoses();
            AdvanceTo(gs, 0, 8 * 60);
            yield return Wait(1f);
            InputX.simulate = true;

            // 1. Rich tooltips: "why so grumpy?" bars on the mood sticker, a tool with its key chip.
            yield return UiHover(UIRoot.I.Root.Q(className: "stat-sun"), "mood sticker");
            yield return Wait(0.4f);
            yield return Shot("u01_mood_tooltip");
            yield return UiHover(UIRoot.I.Root.Q("tool-plan"), "plan tool button");
            yield return Wait(0.4f);
            yield return Shot("u02_tool_tooltip");
            UIRoot.I.ClearTransient();

            // 2. Build receipts while drawing a line.
            District open = null;
            float bestGap = 0f;
            foreach (var d in gs.city.districts)
            {
                if (!d.revealed || !gs.city.InPlayArea(d.center, 0.5f)) continue;
                var near = gs.net.NearestStation(d.center, 99f);
                float gap = near != null ? Vector2.Distance(near.pos, d.center) : 99f;
                if (gap > bestGap)
                {
                    bestGap = gap;
                    open = d;
                }
            }
            Vector2 p0 = Land(gs, open.center);
            var station = gs.net.NearestStation(p0, 99f);
            Vector2 p1 = Land(gs, Vector2.Lerp(p0, station.pos, 0.45f));
            g.FocusOn((p0 + station.pos) * 0.5f, Vector2.Distance(p0, station.pos) * 0.6f + 1.5f);
            yield return Wait(1f);
            yield return Press(UnityEngine.InputSystem.Key.L);
            yield return Hover(p0);
            yield return Wait(0.3f);
            yield return Shot("u03_receipt_start");
            yield return Click(p0);
            yield return Hover(p1);
            yield return Wait(0.3f);
            yield return Shot("u04_receipt_new_station");
            yield return Hover(p0 + (p1 - p0).normalized * 0.2f);
            yield return Wait(0.3f);
            yield return Shot("u05_receipt_cant_build");
            yield return Hover(station.pos);
            yield return Wait(0.3f);
            yield return Shot("u06_receipt_connect");
            Note($"line plan stops: {g.Tools.Line.PlannedCount}");
            yield return Press(UnityEngine.InputSystem.Key.Escape);
            yield return Press(UnityEngine.InputSystem.Key.Escape);

            // 3. A pencil plan wandering out past the town limit.
            var area = gs.city.playArea;
            Vector2 q0 = station.pos;
            Vector2 q1 = Vector2.Lerp(station.pos, new Vector2(area.xMax + 3f, area.center.y), 0.5f);
            Vector2 q2 = new Vector2(area.xMax + 3f, area.center.y + 2f);
            g.FocusOn((q0 + q2) * 0.5f, Vector2.Distance(q0, q2) * 0.6f + 2f);
            yield return Wait(1f);
            yield return Press(UnityEngine.InputSystem.Key.K);
            Note($"tool after K: {g.Tools.Active}");
            yield return Click(q0);
            yield return Click(q1);
            yield return Hover(q2);
            yield return Wait(0.3f);
            yield return Shot("u07_pencil_drawing");
            yield return Click(q2);
            yield return Click(q2 + new Vector2(0.01f, 0f), true);
            yield return Wait(0.3f);
            Note($"pencil plans: {gs.plans.Count}" + (gs.plans.Count > 0 ? $", first '{gs.plans[0].name}' with {gs.plans[0].points.Count} points" : ""));
            yield return Hover(Vector2.Lerp(q0, q1, 0.5f));
            yield return Wait(0.3f);
            yield return Shot("u08_pencil_rub_out");
            yield return Press(UnityEngine.InputSystem.Key.V);

            // 4. Map hovers are ink bubbles.
            g.FocusOn(station.pos, 3f);
            yield return Wait(0.8f);
            yield return Hover(station.pos);
            yield return Wait(0.3f);
            yield return Shot("u09_station_bubble");

            // 5. Toasts: a new district's star sticker, a small save pill, a padlock.
            UIRoot.I.Toast("New district: Thistledown", ToastKind.Info, 8f);
            UIRoot.I.Toast("Game saved", ToastKind.Good, 8f);
            UIRoot.I.Toast(ProgressionSystem.LockedMessage(Feature.Council), ToastKind.Info, 8f);
            yield return Wait(0.5f);
            yield return Shot("u10_toasts");

            // 6. The feed tucked away as a pill.
            var feed = UIRoot.I.Root.Q(className: "feed");
            VisualElement chevron = null;
            feed?.Query(className: "icon-btn").ForEach(e => chevron = e);
            yield return UiClick(chevron, "feed collapse");
            yield return Wait(0.4f);
            yield return Shot("u11_feed_pill");
            yield return UiClick(chevron, "feed expand");

            // 7. Dialogs.
            void Close() => g.UI.CloseModal();
            g.UI.ShowNewGame();
            yield return Wait(0.6f);
            yield return Shot("u12_new_city");
            Close();
            g.UI.ShowSaveLoad(false);
            yield return Wait(0.6f);
            yield return Shot("u13_load_polaroids");
            Close();
            g.UI.Confirm($"Demolish {station.name}?", "Lines through it will reconnect around it. Waiting passengers will leave.", null, "Demolish", gs.build.StationCost * Balance.DemolishRefund);
            yield return Wait(0.6f);
            yield return Shot("u14_confirm_demolish");
            Close();
            g.UI.ShowHelp();
            yield return Wait(0.6f);
            yield return Shot("u15_help");
            Close();
            g.UI.ShowChallenges();
            yield return Wait(0.6f);
            yield return Shot("u16_challenges");
            Close();
            g.UI.ShowSettings();
            yield return Wait(0.6f);
            yield return Shot("u17_settings");
            Close();
            g.UI.ShowCredits();
            yield return Wait(0.6f);
            yield return Shot("u18_credits");
            Close();
            InputX.simulate = false;
            Note("done");
        }

        /// <summary>Moves UI Toolkit's pointer over an element, so its hover effects and tooltip show.</summary>
        IEnumerator UiHover(VisualElement e, string what)
        {
            if (e == null || e.panel == null)
            {
                Note("UI element not found: " + what);
                yield break;
            }
            Vector2 pos = e.worldBound.center;
            var move = new Event { type = EventType.MouseMove, mousePosition = pos };
            using (var pe = PointerMoveEvent.GetPooled(move)) e.panel.visualTree.SendEvent(pe);
            yield return Frames(3);
            // If the synthetic move didn't produce an enter event, send one straight to the element.
            using (var enter = PointerEnterEvent.GetPooled(move))
            {
                enter.target = e;
                e.SendEvent(enter);
            }
            yield return Frames(2);
        }
    }
}
