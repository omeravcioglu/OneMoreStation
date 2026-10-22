using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Metro.UI
{
    /// <summary>The floating guided-start card.</summary>
    /// <summary>
    /// The guided-start card: a step track, the mascot peeking over the top, the step's title, its text with
    /// the key words highlighted, a mint check box that ticks off progress, a wavy "Skip the guide" link and
    /// the step's button. Whatever the step points at gets a scribbled highlighter ring and a "here!" sticker.
    /// </summary>
    public sealed class TutorialView
    {
        readonly GameRoot g;
        public readonly VisualElement root;
        readonly Label step, title, body, check, helper;
        readonly VisualElement nextBtn, checkRow;
        readonly StepTrack track;
        readonly Mascot mascot;
        readonly VisualElement ring, here;
        VisualElement highlighted;

        public TutorialView(GameRoot g, VisualElement layer)
        {
            this.g = g;
            root = UIK.Div(layer, "panel", "tutorial");
            mascot = new Mascot(110f, 116f);
            mascot.AddToClassList("tutorial-mascot");
            root.Add(mascot);
            var top = UIK.Row(root, "tutorial-top");
            track = new StepTrack();
            top.Add(track);
            step = UIK.Text(top, "", "tutorial-step");
            title = UIK.Text(root, "", "tutorial-title");
            body = UIK.Text(root, "", "tutorial-body");
            body.style.whiteSpace = WhiteSpace.Normal;
            body.enableRichText = true;
            checkRow = UIK.Row(root, "tutorial-check-row");
            checkRow.Add(new IconElement(IconKind.Check, 24f) { Color = Doodle.Mint });
            check = UIK.Text(checkRow, "", "tutorial-check");
            check.style.whiteSpace = WhiteSpace.Normal;
            var row = UIK.Row(root, "tutorial-foot");
            var skip = UIK.Col(row, "wavy-link");
            UIK.Text(skip, "Skip the guide", "wavy-link-text").pickingMode = PickingMode.Ignore;
            var sq = new Squiggle(Doodle.Gum, 2.2f);
            sq.AddToClassList("wavy-link-line");
            skip.Add(sq);
            skip.AddManipulator(new Clickable(() =>
            {
                Sfx.Click();
                g.Tutorial.Skip();
            }));
            UIK.Spacer(row);
            helper = UIK.Text(row, "", "tutorial-helper");
            nextBtn = UIK.Button(row, "Next", () => g.Tutorial.Next(), "btn-primary", "btn-mint");
            UIK.Show(root, false);

            // The scribbled ring and its sticker live on the overlay, over whatever they point at.
            ring = new ScribbleRing();
            ring.AddToClassList("tutorial-ring");
            ring.pickingMode = PickingMode.Ignore;
            layer.Add(ring);
            here = UIK.Text(layer, "here!", "tutorial-here");
            here.pickingMode = PickingMode.Ignore;
            UIK.Display((Label)here);
            UIK.Show(ring, false);
            UIK.Show(here, false);
        }

        /// <summary>"*Line tool*" in a step's text becomes a highlighter-marked keyword.</summary>
        static string Mark(string text)
        {
            var sb = new System.Text.StringBuilder(text.Length + 32);
            bool open = false;
            foreach (char c in text)
            {
                if (c == '*')
                {
                    sb.Append(open ? "</b></mark>" : "<mark=#FFE45CB0><b>");
                    open = !open;
                }
                else sb.Append(c);
            }
            if (open) sb.Append("</b></mark>");
            return sb.ToString();
        }

        public void Show(int index, int total, string t, string b, bool manualNext, string nextLabel)
        {
            UIK.Show(root, true);
            track.Set(index, total);
            step.text = $"Guided start · step {index + 1} of {total}";
            title.text = t;
            body.text = Mark(b);
            UIK.Show(nextBtn, manualNext);
            UIK.SetButtonText(nextBtn, nextLabel ?? "Next");
            helper.text = manualNext ? "" : "do it on the map to carry on";
            UIK.Show(helper, !manualNext);
            SetCheck("");
        }

        public void SetCheck(string text)
        {
            if (check.text != (text ?? "")) check.text = text ?? "";
            UIK.Show(checkRow, !string.IsNullOrEmpty(text));
        }

        public void Hide()
        {
            UIK.Show(root, false);
            Highlight(null);
        }

        public void Highlight(string elementName)
        {
            if (highlighted != null)
            {
                highlighted.RemoveFromClassList("tut-highlight");
                highlighted.style.borderTopColor = highlighted.style.borderBottomColor = highlighted.style.borderLeftColor = highlighted.style.borderRightColor = StyleKeyword.Null;
            }
            highlighted = null;
            UIK.Show(ring, false);
            UIK.Show(here, false);
            if (string.IsNullOrEmpty(elementName)) return;
            highlighted = UIRoot.I.Root.Q(elementName);
            highlighted?.AddToClassList("tut-highlight");
        }

        public void Tick(float dt)
        {
            float t = Time.unscaledTime;
            if (UIK.Shown(root)) mascot.style.translate = new Translate(0f, -Mathf.Abs(Mathf.Sin(t * 2.4f)) * 5f);
            bool show = highlighted != null && highlighted.panel != null && UIK.Shown(highlighted) && highlighted.worldBound.width > 1f;
            UIK.Show(ring, show);
            UIK.Show(here, show);
            if (!show) return;
            float a = 0.5f + 0.5f * Mathf.Sin(t * 5f);
            var c = Doodle.WithAlpha(Doodle.Gum, 0.35f + 0.65f * a);
            highlighted.style.borderTopColor = highlighted.style.borderBottomColor = highlighted.style.borderLeftColor = highlighted.style.borderRightColor = c;
            // Ring and sticker follow the element (it may scroll or move).
            var wb = highlighted.worldBound;
            var lb = ((VisualElement)ring.parent).worldBound;
            float pad = 12f + 3f * a;
            ring.style.left = wb.xMin - lb.xMin - pad;
            ring.style.top = wb.yMin - lb.yMin - pad;
            ring.style.width = wb.width + pad * 2f;
            ring.style.height = wb.height + pad * 2f;
            bool right = wb.xMax + 90f < lb.xMax;
            here.style.left = right ? wb.xMax - lb.xMin + pad + 4f : wb.xMin - lb.xMin - pad - 64f;
            here.style.top = wb.yMin - lb.yMin - 16f;
            here.style.rotate = new Rotate(-8f + Mathf.Sin(t * 3f) * 4f);
        }
    }

    /// <summary>The guided start's progress: done steps as pink dots, the current one big, the rest pencilled in.</summary>
    sealed class StepTrack : VisualElement
    {
        int index, total = 8;

        public StepTrack()
        {
            pickingMode = PickingMode.Ignore;
            style.height = 26f;
            style.flexShrink = 0f;
            generateVisualContent += Draw;
        }

        public void Set(int i, int n)
        {
            index = i;
            total = Mathf.Max(1, n);
            style.width = 14f * total + 14f * (total - 1) + 12f;
            MarkDirtyRepaint();
        }

        void Draw(MeshGenerationContext mgc)
        {
            var p = mgc.painter2D;
            float y = contentRect.height * 0.5f, x = 7f;
            var pencil = (Color)MapPalette.Pencil;
            for (int i = 0; i < total; i++)
            {
                float cx = x + i * 28f + (i > index ? 6f : 0f);
                if (i > 0)
                {
                    float px = x + (i - 1) * 28f + (i - 1 > index ? 6f : 0f);
                    p.BeginPath();
                    p.MoveTo(new Vector2(px + 8f, y));
                    p.LineTo(new Vector2(cx - 8f, y));
                    p.strokeColor = i <= index ? Doodle.Ink : pencil;
                    p.lineWidth = i <= index ? 4f : 2.5f;
                    p.Stroke();
                }
                p.BeginPath();
                if (i < index)
                {
                    p.Arc(new Vector2(cx, y), 6f, Angle.Degrees(0f), Angle.Degrees(360f));
                    p.fillColor = Doodle.Gum;
                    p.Fill();
                    p.strokeColor = Doodle.Ink;
                    p.lineWidth = 2.5f;
                    p.Stroke();
                }
                else if (i == index)
                {
                    p.Arc(new Vector2(cx + 3f, y), 10f, Angle.Degrees(0f), Angle.Degrees(360f));
                    p.fillColor = Doodle.Paper;
                    p.Fill();
                    p.strokeColor = Doodle.Ink;
                    p.lineWidth = 4f;
                    p.Stroke();
                    p.BeginPath();
                    p.Arc(new Vector2(cx + 3f, y), 4.5f, Angle.Degrees(0f), Angle.Degrees(360f));
                    p.fillColor = Doodle.Sun;
                    p.Fill();
                }
                else
                {
                    p.Arc(new Vector2(cx, y), 4.5f, Angle.Degrees(0f), Angle.Degrees(360f));
                    p.strokeColor = pencil;
                    p.lineWidth = 2.5f;
                    p.Stroke();
                }
            }
        }
    }

    /// <summary>A loose, doubled highlighter scribble around a rectangle.</summary>
    sealed class ScribbleRing : VisualElement
    {
        public ScribbleRing()
        {
            style.position = Position.Absolute;
            generateVisualContent += Draw;
        }

        void Draw(MeshGenerationContext mgc)
        {
            var r = contentRect;
            if (r.width < 8f || r.height < 8f) return;
            var p = mgc.painter2D;
            p.lineCap = LineCap.Round;
            p.lineJoin = LineJoin.Round;
            Vector2 c = r.center;
            float rx = r.width * 0.5f, ry = r.height * 0.5f;
            for (int pass = 0; pass < 2; pass++)
            {
                p.BeginPath();
                for (int i = 0; i <= 64; i++)
                {
                    float t = i / 64f * Mathf.PI * 2.15f - 0.4f;
                    float wob = 1f + 0.05f * Mathf.Sin(t * 3f + pass * 1.7f) + (pass == 0 ? 0f : 0.04f);
                    var q = c + new Vector2(Mathf.Cos(t) * rx * wob, Mathf.Sin(t) * ry * wob);
                    if (i == 0) p.MoveTo(q);
                    else p.LineTo(q);
                }
                p.strokeColor = pass == 0 ? Doodle.Ink : (Color)MapPalette.Highlighter;
                p.lineWidth = pass == 0 ? 7f : 4.5f;
                p.Stroke();
            }
        }
    }
}

namespace Metro
{
    using Metro.UI;

    /// <summary>
    /// The guided start for a brand-new small town: the map → a first line → a depot → trains →
    /// the clock → the first rush hour → how the town grows. Each step waits for the player to
    /// actually do the thing. Later mechanics are introduced by the city-growth cards instead,
    /// one size at a time.
    /// </summary>
    public sealed class TutorialController
    {
        sealed class Step
        {
            public string title, body, highlight, nextLabel;
            public Func<bool> done;
            public Func<string> progress;
            public Action enter;
            public bool manual;
        }

        readonly GameRoot g;
        readonly List<Step> steps = new List<Step>();
        int index = -1;
        public bool Active { get; private set; }
        Vector2 camStart;
        float camSizeStart;
        double rushEnd;

        public TutorialController(GameRoot g)
        {
            this.g = g;
        }

        GameState S => g.State;

        public void Begin()
        {
            Active = true;
            BuildSteps();
            S.advisorEnabled = false;
            S.progress.holdTierUps = true;
            index = -1;
            Next();
        }

        void BuildSteps()
        {
            steps.Clear();
            string town = S.city.name;
            steps.Add(new Step
            {
                title = $"Welcome to {town}",
                body = $"{town} is a small town with big plans, and you run its brand-new metro.\n\nYou start small: a small map, one line and a few trains. As people start riding, the town grows. The map opens up, new districts appear and new tools unlock, one at a time.",
                manual = true,
                nextLabel = "Let's go",
                enter = () => g.SetPaused(true)
            });
            steps.Add(new Step
            {
                title = "Read the map",
                body = "The *little houses* are where people live, the *towers* are where they work, and the more there are, the busier it gets. *Clouds* hide districts that haven't opened yet, and past the *dashed frame* (the town limit) the land is hazy and out of reach for now.\n\nPan with right-drag or WASD, zoom with the mouse wheel.",
                manual = true,
                enter = () =>
                {
                    camStart = g.CamCtl.Position;
                    camSizeStart = g.CamCtl.Size;
                },
                progress = () => (g.CamCtl.Position - camStart).magnitude > 1f || Mathf.Abs(g.CamCtl.Size - camSizeStart) > 0.8f ? "Nicely done." : "Try moving the camera around."
            });
            steps.Add(new Step
            {
                title = "Draw your first line",
                body = $"Pick the *Line tool* ({Keybinds.Label(BindAction.Line)}) and click on the map to place stations one after another, about a kilometre apart. Run it through the *busy middle of town*, where the houses and towers crowd together.\n\nRight-click or press Enter to build it.",
                highlight = "tool-line",
                done = () =>
                {
                    foreach (var l in S.net.AliveLines())
                        if (l.stops.Count >= 4) return true;
                    return false;
                },
                progress = () => g.Tools.Active == ToolKind.Line ? $"{g.Tools.Line.PlannedCount} stations planned (build at least 4)." : "Select the Line tool on the left."
            });
            steps.Add(new Step
            {
                title = "Give it a depot",
                body = $"Trains need somewhere to live. Choose the *Depot tool* ({Keybinds.Label(BindAction.Depot)}) and click any station on your line.",
                highlight = "tool-depot",
                done = () =>
                {
                    foreach (var _ in S.net.AliveDepots()) return true;
                    return false;
                }
            });
            steps.Add(new Step
            {
                title = "Run some trains",
                body = "Click your line (or its coloured chip at the bottom left) and add at least *two trains* with the + button. More trains mean shorter waits, but each one costs money every day.",
                highlight = "add-train",
                done = () =>
                {
                    foreach (var l in S.net.AliveLines())
                        if (l.trains.Count >= 2) return true;
                    return false;
                }
            });
            steps.Add(new Step
            {
                title = "Start the clock",
                body = $"Press {Keybinds.Label(BindAction.Pause)} (or the play button at the top) to start time. Watch the stations: their *faces* show how crowded the platform is, from chill green to busy yellow, squished orange and meltdown red.",
                enter = () => S.advisorEnabled = true,
                done = () => !g.Paused && S.clock.MinuteOfDay > Balance.StartMinute + 20
            });
            steps.Add(new Step
            {
                title = "Your first morning rush",
                body = "Between 07:00 and 09:30 everybody heads to work. Platforms fill up, queues grow beside the stations and packed trains start shouting *!!*.\n\nIf stations turn red, *add trains*. The Manager's feed on the right tells you what's going wrong; click a message to jump there. Riders sometimes exaggerate, so check the map.",
                enter = () =>
                {
                    double day = S.clock.Day * (double)Balance.MinutesPerDay;
                    if (S.clock.MinuteOfDay < 6 * 60 + 40) S.clock.totalMinutes = day + 6 * 60 + 40;
                    if (S.clock.MinuteOfDay > 9 * 60) day += Balance.MinutesPerDay;
                    rushEnd = day + 9 * 60 + 30;
                    g.SetSpeed(1);
                },
                done = () => S.clock.totalMinutes >= rushEnd,
                progress = () => $"The rush ends at 09:30 · it's {S.clock.TimeString}"
            });
            steps.Add(new Step
            {
                title = $"Grow {town}",
                body = $"The *sticky note* at the top left shows what it takes for {town} to grow into a {ProgressionSystem.Tiers[1].name}: a bigger map, a second line and new tools. Meet every goal and the town grows overnight, ready for the next morning.\n\nFrom here on you're in charge. Every time the town grows, a card explains what's new." +
                    (Demo.On ? $"\n\n*This demo* covers the first two sizes. It ends when {town} is ready to become a {ProgressionSystem.Tiers[Demo.LastTier + 1].name}, in about 15–20 minutes. Speed up time with {Keybinds.Label(BindAction.Speed2)} and {Keybinds.Label(BindAction.Speed3)}." : ""),
                highlight = "goals",
                manual = true,
                nextLabel = "Got it"
            });
        }

        public void OnFinanceOpened() { }

        public void Next()
        {
            index++;
            if (index >= steps.Count)
            {
                End(true);
                return;
            }
            var st = steps[index];
            st.enter?.Invoke();
            g.UI.tutorial.Show(index, steps.Count, st.title, st.body, st.manual, st.nextLabel);
            g.UI.tutorial.Highlight(st.highlight);
            Sfx.Chime();
        }

        public void Skip()
        {
            g.UI.Confirm("Skip the guide?", "You'll keep this town and can play on freely. The goals panel shows how it grows.", () => End(false), "Skip");
        }

        void End(bool completed)
        {
            if (completed && Active) GameEvents.RaiseGuidedStartFinished();
            Active = false;
            index = -1;
            g.UI.tutorial.Hide();
            if (S != null)
            {
                S.advisorEnabled = true;
                S.progress.holdTierUps = false;
                S.progress.Evaluate();
            }
        }

        public void Tick(float dt)
        {
            if (!Active || index < 0 || index >= steps.Count || S == null) return;
            var st = steps[index];
            g.UI.tutorial.SetCheck(st.progress?.Invoke());
            if (!st.manual && st.done != null && st.done())
            {
                Sfx.Coin();
                Next();
            }
        }
    }
}
