using UnityEngine;
using UnityEngine.UIElements;

namespace Metro.UI
{
    /// <summary>The goal of the challenge being played: what to do, how far along, how long is left.</summary>
    public sealed class ChallengeBanner
    {
        readonly GameRoot g;
        public readonly VisualElement root;
        readonly Label title, goal, status, time;
        readonly VisualElement fill, starSlot;
        int shownStars = -1;
        ChallengeRun shown;

        public ChallengeBanner(GameRoot g, VisualElement parent)
        {
            this.g = g;
            root = UIK.Div(parent, "panel", "challenge-banner");
            root.name = "challenge-banner";
            root.pickingMode = PickingMode.Ignore;
            root.style.position = Position.Absolute;
            root.style.top = 76f;
            root.style.left = Length.Percent(50);
            root.style.translate = new Translate(Length.Percent(-50), 0);
            root.style.width = 440f;
            root.style.paddingLeft = root.style.paddingRight = 14f;
            root.style.paddingTop = root.style.paddingBottom = 8f;
            var head = UIK.Row(root);
            var icon = new IconElement(IconKind.Faster, 20f);
            icon.style.marginRight = 6f;
            head.Add(icon);
            title = UIK.Text(head, "", "caps");
            UIK.Spacer(head);
            time = UIK.Text(head, "", "tiny");
            goal = UIK.Text(root, "", "goal-title");
            goal.style.whiteSpace = WhiteSpace.Normal;
            var (_, f) = UIK.Bar(root, Doodle.Sky);
            fill = f;
            var foot = UIK.Row(root);
            status = UIK.Text(foot, "", "goal-meta");
            status.style.whiteSpace = WhiteSpace.Normal;
            status.style.flexShrink = 1f;
            UIK.Spacer(foot);
            starSlot = UIK.Row(foot);
            UIK.Show(root, false);
        }

        public void Refresh(bool hudShown)
        {
            var run = g.State?.challenge;
            bool show = hudShown && run != null && g.Mode == AppMode.Playing;
            UIK.Show(root, show);
            if (!show) return;
            if (run != shown)
            {
                shown = run;
                title.text = "CHALLENGE · " + run.def.title.ToUpperInvariant();
                goal.text = run.def.GoalText();
            }
            UIK.SetBar(fill, run.progress);
            string s = run.finished ? (run.won ? "Done!" : "Not this time.") : run.status;
            if (status.text != s) status.text = s;
            int stars = run.finished && run.won ? run.stars : -1;
            if (stars != shownStars)
            {
                shownStars = stars;
                starSlot.Clear();
                if (stars >= 0) UIK.Stars(starSlot, stars, 18f);
            }
            string t = run.finished ? "" : run.TimeLeft;
            if (time.text != t) time.text = t;
        }

        public static string Stars(int n) => new string('★', Mathf.Clamp(n, 0, 3)) + new string('☆', 3 - Mathf.Clamp(n, 0, 3));
    }
}
