using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

namespace Metro
{
    /// <summary>
    /// "-autotest demo -demo": the demo build's flow. Checks the demo menu, the guided start, that the town
    /// stops at the demo's last size and shows "End of the demo" at dawn, and the play-time cap ending.
    /// </summary>
    public sealed partial class AutoTest
    {
        IEnumerator DemoTest(GameRoot g)
        {
            game = g;
            Note($"demo mode {Demo.On}");
            g.StartMenu();
            yield return Wait(3f);
            var ui = Metro.UI.UIRoot.I.Root;
            Note($"menu: play {(ui.Q("demo-play") != null)}, wishlist {(ui.Q("demo-menu-wishlist") != null)}, challenges shown {FindButton("Challenges") != null}");
            yield return Shot("d01_demo_menu");

            // The guided demo town.
            g.StartDemoGame(true);
            yield return Wait(1.5f);
            Note($"guided: city {g.State.city.name}, tutorial {g.Tutorial.Active}, difficulty {g.State.options.difficulty}, tier {g.State.progress.Current.name}");
            yield return Shot("d02_guided_start");

            // A town that plays through: grow once, meet the last goals, and the demo ends at dawn.
            g.StartDemoGame(false);
            yield return Wait(1f);
            var gs = g.State;
            var line = AutoPlanner.BuildStarterLine(gs);
            if (line != null)
            {
                gs.build.BuildDepot(line.stops[line.stops.Count / 2]);
                gs.build.AddTrain(line);
                gs.build.AddTrain(line);
            }
            gs.progress.TierUp();
            yield return Wait(1.5f);
            Note($"grew to {gs.progress.Current.name}, card {g.UI.ModalOpen}");
            yield return UiClick(FindButton("Keep building"), "Keep building");
            yield return Wait(1f);
            foreach (var goal in gs.progress.goals) goal.done = true;
            gs.progress.Evaluate();
            Note($"ready {gs.progress.Ready}, at demo end {gs.progress.AtDemoEnd}");
            yield return Wait(0.5f);
            yield return Shot("d03_demo_finale_goals");
            AdvanceTo(gs, gs.clock.Day + 1, Balance.NightEnd + 2);
            yield return Wait(2f);
            Note($"after dawn: tier {gs.progress.Current.name} (must stay {ProgressionSystem.Tiers[Demo.LastTier].name}), demo over {gs.progress.DemoOver}, end card {ui.Q("demo-end") != null}");
            yield return Shot("d04_demo_end");

            // The play-time cap: ends in the evening, even in a small town.
            var back = FindButton("Main menu");
            yield return UiClick(back, "Main menu");
            yield return Wait(2f);
            Demo.PlayCapSeconds = 2f;
            g.StartDemoGame(false);
            yield return Wait(1f);
            gs = g.State;
            g.SetPaused(false);
            float until = Time.realtimeSinceStartup + 10f;
            while (!gs.progress.demoTimeUp && Time.realtimeSinceStartup < until) yield return null;
            Note($"cap reached {gs.progress.demoTimeUp} at {gs.clock.TimeString}, over {gs.progress.DemoOver}");
            AdvanceTo(gs, gs.clock.Day, Demo.EveningMinute + 2);
            yield return Wait(2f);
            Note($"after evening: demo over {gs.progress.DemoOver}, end card {ui.Q("demo-end") != null}, tier {gs.progress.Current.name}");
            yield return Shot("d05_time_up");
        }
    }
}
