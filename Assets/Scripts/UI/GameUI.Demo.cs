using UnityEngine;
using UnityEngine.UIElements;

namespace Metro.UI
{
    public sealed partial class GameUI
    {
        /// <summary>
        /// "End of the demo": the growth card's sunny band and jumping mascot, a thank-you, what the town
        /// achieved, a peek at what the full game adds, and Wishlist / Main menu.
        /// </summary>
        public void ShowDemoEnd(bool timeUp)
        {
            var gs = g.State;
            CloseWindow();
            pendingModals.Clear();
            var p = OpenModal(720f, null, false);
            ModalLocked = true;
            p.AddToClassList("tierup");
            p.name = "demo-end";
            modal.AddToClassList("modal-backdrop-light");

            var band = UIK.Div(p, "tierup-band");
            band.Add(new SunRays());
            Kicker(band, IconKind.Star, "END OF THE DEMO");
            UIK.Text(band, "Thanks for playing!", "tierup-title");
            var lead = UIK.Text(band, timeUp
                ? $"That's all the time this demo has. I hope you liked running {gs.city.name}'s metro!"
                : $"{gs.city.name} is ready to become a {ProgressionSystem.Tiers[Demo.LastTier + 1].name}, and that's where the demo ends. I hope you liked it!", "tierup-blurb");
            lead.style.whiteSpace = WhiteSpace.Normal;
            var c1 = new Confetti(70f, 3, 9);
            c1.AddToClassList("tierup-confetti-l");
            band.Add(c1);
            var c2 = new Confetti(70f, 11, 9);
            c2.AddToClassList("tierup-confetti-r");
            band.Add(c2);

            var body = UIK.Col(p, "tierup-body");
            var facts = UIK.Div(body, "tierup-facts");
            int stations = 0, lines = 0;
            foreach (var s in gs.net.AliveStations()) stations++;
            foreach (var l in gs.net.AliveLines()) lines++;
            FactCard(facts, gs.progress.Current.name, $"your town · day {gs.clock.DayNumber}");
            FactCard(facts, Fmt.Count(gs.stats.recordRiders), "best day (trips)");
            FactCard(facts, $"{lines} · {stations}", "lines · stations");

            UIK.Text(body, "In the full game", "tierup-section", "mt-12");
            var grid = UIK.Row(body, "tierup-rules");
            void Teaser(IconKind icon, Color disc, string title, string text)
            {
                var r = UIK.Row(grid, "tierup-rule");
                var box = UIK.Div(r, "tierup-rule-icon");
                box.style.backgroundColor = disc;
                box.Add(new IconElement(icon, 24f));
                var col = UIK.Col(r);
                col.style.flexShrink = 1f;
                UIK.Text(col, title, "tierup-rule-title").style.whiteSpace = WhiteSpace.Normal;
                UIK.Text(col, text, "tierup-rule-body").style.whiteSpace = WhiteSpace.Normal;
            }
            Teaser(IconKind.City, Doodle.SkyL, "Six city sizes", "Keep growing all the way to a 48-district metropolis.");
            Teaser(IconKind.Hall, Doodle.GrapeL, "City Hall", "A mayor with an agenda, requests, elections and big events.");
            Teaser(IconKind.Coin, Doodle.SunL, "Money matters", "Fares, ads, loans and station upgrades.");
            Teaser(IconKind.Faster, Doodle.MintL, "Challenges", "Six puzzles with a goal, a clock and up to three stars.");

            var next = UIK.Text(body, "If you'd like to see where it goes, a wishlist helps a lot!", "tierup-next", "mt-12");
            next.style.whiteSpace = WhiteSpace.Normal;
            var foot = UIK.Row(body, "tierup-foot");
            UIK.Spacer(foot);
            UIK.Button(foot, "Main menu", () =>
            {
                ModalLocked = false;
                CloseModal();
                g.ExitToMenu();
            }).style.marginRight = 8f;
            var wish = UIK.Button(foot, "Wishlist on Steam", Metro.Platform.PlatformServices.OpenFullGameStorePage, "btn-primary", "btn-mint");
            wish.name = "demo-wishlist";

            var jump = new Mascot(124f, 130f);
            jump.AddToClassList("tierup-mascot");
            p.Add(jump);
            var burst = new Starburst(128f, "THANK\nYOU!");
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
    }
}
