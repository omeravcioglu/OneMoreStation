using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Metro.UI
{
    /// <summary>Small helpers for building UI Toolkit trees from code.</summary>
    public static class UIK
    {
        /// <summary>Text classes set in the display face (Luckiest Guy) instead of the hand-lettered text face.</summary>
        static readonly HashSet<string> DisplayClasses = new HashSet<string>
        {
            "h1", "h2", "caps", "field-label", "clock", "city-name", "size-badge", "stat-value", "tierup-kicker",
            "tierup-title", "tierup-fact-v", "tile-v", "stepper-val", "line-chip-label", "feed-title", "feed-badge",
            "goal-count", "menu-letter", "menu-btn-label", "tutorial-step", "tutorial-title", "request-tag", "sticker-text"
        };

        public static VisualElement Div(VisualElement parent, params string[] classes)
        {
            var e = new VisualElement();
            foreach (var c in classes) e.AddToClassList(c);
            parent?.Add(e);
            return e;
        }

        public static Label Text(VisualElement parent, string text, params string[] classes)
        {
            var l = new Label(text);
            bool display = false;
            foreach (var c in classes)
            {
                l.AddToClassList(c);
                display |= DisplayClasses.Contains(c);
            }
            if (display) Display(l);
            parent?.Add(l);
            return l;
        }

        /// <summary>Sets an element's text in the chunky display face (headlines, numbers, stickers).</summary>
        public static void Display(VisualElement e)
        {
            e.style.unityFontDefinition = UIRoot.FontDisplay;
            e.style.unityFontStyleAndWeight = FontStyle.Normal;
        }

        /// <summary>The white "pressed" ring shown inside a round candy button while it is on.</summary>
        public static VisualElement Ring(VisualElement button)
        {
            var r = Div(button, "btn-ring");
            r.pickingMode = PickingMode.Ignore;
            return r;
        }

        /// <summary>A little keyboard-shortcut badge on the corner of a round button.</summary>
        public static Label KeyBadge(VisualElement button, string key)
        {
            var k = Text(button, key, "key-badge");
            k.pickingMode = PickingMode.Ignore;
            return k;
        }

        public static VisualElement Row(VisualElement parent, params string[] classes)
        {
            var e = Div(parent, classes);
            e.AddToClassList("row");
            return e;
        }

        public static VisualElement Col(VisualElement parent, params string[] classes)
        {
            var e = Div(parent, classes);
            e.AddToClassList("col");
            return e;
        }

        public static VisualElement Spacer(VisualElement parent) => Div(parent, "spacer");

        public static VisualElement Divider(VisualElement parent) => Div(parent, "divider");

        /// <summary>Clickable element styled as a button. Returns the element; label is its first child.</summary>
        public static VisualElement Button(VisualElement parent, string text, Action onClick, params string[] classes)
        {
            var b = Div(parent, "btn");
            foreach (var c in classes) b.AddToClassList(c);
            var l = Text(b, text, "btn-label");
            l.pickingMode = PickingMode.Ignore;
            // The big main action of a card or dialog wears the display face, like a candy label.
            if (b.ClassListContains("btn-primary") && !b.ClassListContains("btn-small"))
            {
                l.AddToClassList("btn-label-display");
                Display(l);
            }
            Action press = () =>
            {
                if (b.ClassListContains("btn-disabled")) return;
                Sfx.Click();
                onClick?.Invoke();
            };
            b.AddManipulator(new Clickable(press));
            // Kept so the keyboard (Enter in a dialog) can press the button too.
            b.userData = press;
            return b;
        }

        /// <summary>
        /// A candy button with its price on a gold coin chip ("Lengthen [$190k]"); a negative cost is a refund and
        /// wears a mint "+$38k" chip. The verb stays the button's first child, so it reads like any other button.
        /// </summary>
        public static VisualElement PriceButton(VisualElement parent, string verb, float cost, Action onClick, params string[] classes)
        {
            var b = Button(parent, verb, onClick, classes);
            b.AddToClassList("btn-priced");
            var chip = Text(b, (cost < 0f ? "+" : "") + Fmt.Money(Mathf.Abs(cost)), "price-chip");
            chip.pickingMode = PickingMode.Ignore;
            if (cost < 0f) chip.AddToClassList("price-chip-refund");
            Display(chip);
            return b;
        }

        /// <summary>Greys out a priced button the player can't afford right now (its chip turns red).</summary>
        public static void SetAffordable(VisualElement button, bool ok)
        {
            SetEnabled(button, ok);
            button.EnableInClassList("btn-poor", !ok);
        }

        /// <summary>
        /// "Hold to delete": a button that only fires after being held down for a moment, while a red fill grows
        /// across it. Letting go early cancels. Used instead of a confirm dialog for destructive inspector actions.
        /// </summary>
        public static VisualElement HoldButton(VisualElement parent, string text, Action onDone, params string[] classes)
        {
            var b = Div(parent, "btn", "btn-hold");
            foreach (var c in classes) b.AddToClassList(c);
            var l = Text(b, text, "btn-label");
            l.pickingMode = PickingMode.Ignore;
            b.AddManipulator(new HoldToConfirm(0.9f, onDone));
            return b;
        }

        public static void SetButtonText(VisualElement button, string text)
        {
            if (button.childCount > 0 && button[0] is Label l) l.text = text;
        }

        public static void SetEnabled(VisualElement button, bool on) => button.EnableInClassList("btn-disabled", !on);

        public static VisualElement IconButton(VisualElement parent, IconKind icon, Action onClick, string tooltip, string cls = "icon-btn", float size = 22f)
        {
            var b = Div(parent, cls);
            var ic = new IconElement(icon, size);
            ic.pickingMode = PickingMode.Ignore;
            b.Add(ic);
            b.AddManipulator(new Clickable(() =>
            {
                Sfx.Click();
                onClick?.Invoke();
            }));
            if (!string.IsNullOrEmpty(tooltip)) Tooltip(b, tooltip);
            return b;
        }

        public static IconElement IconOf(VisualElement button) => button.childCount > 0 ? button[0] as IconElement : null;

        public static void Tooltip(VisualElement e, string text)
        {
            e.RegisterCallback<PointerEnterEvent>(_ => UIRoot.I?.ShowTooltip(text, e));
            e.RegisterCallback<PointerLeaveEvent>(_ => UIRoot.I?.HideTooltip(e));
        }

        public static void Tooltip(VisualElement e, Func<string> text)
        {
            e.RegisterCallback<PointerEnterEvent>(_ => UIRoot.I?.ShowTooltip(text(), e));
            e.RegisterCallback<PointerLeaveEvent>(_ => UIRoot.I?.HideTooltip(e));
        }

        /// <summary>A tooltip with a title, labelled 0..1 bars (coloured by mood) and a footnote.</summary>
        public static void TooltipBars(VisualElement e, Func<(string title, (string label, float value)[] rows, string foot)> content)
        {
            e.RegisterCallback<PointerEnterEvent>(_ =>
            {
                var c = content();
                UIRoot.I?.ShowTooltipBars(c.title, c.rows, c.foot, e);
            });
            e.RegisterCallback<PointerLeaveEvent>(_ => UIRoot.I?.HideTooltip(e));
        }

        public static (VisualElement root, Label value) KeyValue(VisualElement parent, string key, string value = "")
        {
            var r = Div(parent, "kv");
            Text(r, key, "kv-k");
            var v = Text(r, value, "kv-v");
            return (r, v);
        }

        /// <summary>A number tile: a little tilted candy card. Tiles in one grid cycle through four colours.</summary>
        public static (VisualElement root, Label value, Label caption) Tile(VisualElement parent, string caption)
        {
            int n = parent != null ? parent.childCount % 4 : 0;
            var t = Div(parent, "tile", "tile-n" + n);
            var card = Div(t, "tile-card");
            var v = Text(card, "—", "tile-v");
            var c = Text(card, caption, "tile-k");
            return (t, v, c);
        }

        public static (VisualElement root, VisualElement fill) Bar(VisualElement parent, Color color)
        {
            var b = Div(parent, "bar");
            var f = Div(b, "bar-fill");
            f.style.backgroundColor = color;
            f.style.width = Length.Percent(0);
            return (b, f);
        }

        public static void SetBar(VisualElement fill, float v01) => fill.style.width = Length.Percent(Mathf.Clamp01(v01) * 100f);

        /// <summary>Segmented control. Returns a setter for the selected index.</summary>
        public static Action<int> Segmented(VisualElement parent, string[] items, int selected, Action<int> onPick)
        {
            var seg = Div(parent, "seg");
            var labels = new Label[items.Length];
            void Select(int i)
            {
                for (int k = 0; k < labels.Length; k++) labels[k].EnableInClassList("seg-item-on", k == i);
            }
            for (int i = 0; i < items.Length; i++)
            {
                int idx = i;
                var l = Text(seg, items[i], "seg-item");
                l.AddManipulator(new Clickable(() =>
                {
                    Sfx.Click();
                    Select(idx);
                    onPick?.Invoke(idx);
                }));
                labels[i] = l;
            }
            Select(selected);
            return Select;
        }

        /// <summary>Pill switch. Returns a setter for the state (without firing the callback).</summary>
        /// <summary>A candy switch whose knob smiles when it's on and looks unimpressed when it's off.</summary>
        public static Action<bool> Switch(VisualElement parent, bool on, Action<bool> onChange)
        {
            var s = Div(parent, "switch");
            var knob = Div(s, "switch-knob");
            knob.pickingMode = PickingMode.Ignore;
            var face = new IconElement(IconKind.Smile, 16f) { Color = Doodle.Paper };
            face.pickingMode = PickingMode.Ignore;
            knob.Add(face);
            bool state = on;
            void Look(bool v)
            {
                s.EnableInClassList("switch-on", v);
                face.Mood = v ? 1f : -0.1f;
            }
            Look(state);
            s.AddManipulator(new Clickable(() =>
            {
                Sfx.Click();
                state = !state;
                Look(state);
                onChange?.Invoke(state);
            }));
            return v =>
            {
                state = v;
                Look(v);
            };
        }

        /// <summary>A row of three stars, the earned ones in gold.</summary>
        public static VisualElement Stars(VisualElement parent, int earned, float size = 22f)
        {
            var row = Row(parent, "star-row");
            row.pickingMode = PickingMode.Ignore;
            for (int i = 0; i < 3; i++)
            {
                var st = new IconElement(IconKind.Star, size) { Color = i < earned ? Doodle.Coin : Doodle.Sand };
                if (i >= earned) st.style.opacity = 0.75f;
                st.style.rotate = new Rotate(i == 1 ? 0f : i == 0 ? -10f : 10f);
                row.Add(st);
            }
            return row;
        }

        public static VisualElement Swatch(VisualElement parent, Color32 c, float size = 14f)
        {
            var s = Div(parent, "insp-swatch");
            s.style.backgroundColor = (Color)c;
            s.style.width = size;
            s.style.height = size;
            s.style.borderTopLeftRadius = s.style.borderTopRightRadius = s.style.borderBottomLeftRadius = s.style.borderBottomRightRadius = size * 0.5f;
            return s;
        }

        public static Color Col32(Color32 c) => c;

        public static void Show(VisualElement e, bool on) => e.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
        public static bool Shown(VisualElement e) => e.resolvedStyle.display != DisplayStyle.None && e.style.display != DisplayStyle.None;

        public static void Bold(VisualElement e) => e.style.unityFontDefinition = UIRoot.FontSemiBold;
        public static void Heavy(VisualElement e) => e.style.unityFontDefinition = UIRoot.FontBold;
    }

    /// <summary>The press-and-hold behind <see cref="UIK.HoldButton"/>: fills the button in red, fires when full.</summary>
    public sealed class HoldToConfirm : PointerManipulator
    {
        readonly float seconds;
        readonly Action onDone;
        float progress;
        bool holding;
        int pointerId = -1;
        IVisualElementScheduledItem ticker;

        public HoldToConfirm(float seconds, Action onDone)
        {
            this.seconds = seconds;
            this.onDone = onDone;
            activators.Add(new ManipulatorActivationFilter { button = MouseButton.LeftMouse });
        }

        protected override void RegisterCallbacksOnTarget()
        {
            target.RegisterCallback<PointerDownEvent>(OnDown);
            target.RegisterCallback<PointerUpEvent>(OnUp);
            target.RegisterCallback<PointerCaptureOutEvent>(OnCaptureOut);
            target.generateVisualContent += Draw;
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            target.UnregisterCallback<PointerDownEvent>(OnDown);
            target.UnregisterCallback<PointerUpEvent>(OnUp);
            target.UnregisterCallback<PointerCaptureOutEvent>(OnCaptureOut);
            target.generateVisualContent -= Draw;
        }

        void OnDown(PointerDownEvent e)
        {
            if (!CanStartManipulation(e)) return;
            holding = true;
            pointerId = e.pointerId;
            target.CapturePointer(pointerId);
            if (ticker == null) ticker = target.schedule.Execute(Tick).Every(16);
            else ticker.Resume();
            e.StopPropagation();
        }

        void OnUp(PointerUpEvent e)
        {
            if (!holding) return;
            Release();
            e.StopPropagation();
        }

        void OnCaptureOut(PointerCaptureOutEvent e) => holding = false;

        void Release()
        {
            holding = false;
            if (pointerId >= 0 && target.HasPointerCapture(pointerId)) target.ReleasePointer(pointerId);
            pointerId = -1;
        }

        void Tick(TimerState ts)
        {
            float dt = Mathf.Min(0.1f, ts.deltaTime / 1000f);
            if (holding)
            {
                progress += dt / seconds;
                if (progress >= 1f)
                {
                    progress = 0f;
                    Release();
                    Sfx.Click();
                    onDone?.Invoke();
                }
            }
            else progress = Mathf.Max(0f, progress - dt * 4f);
            target.MarkDirtyRepaint();
            if (!holding && progress <= 0f) ticker.Pause();
        }

        void Draw(MeshGenerationContext mgc)
        {
            if (progress <= 0f) return;
            var s = target.resolvedStyle;
            var lb = target.layout;
            float x0 = s.borderLeftWidth, y0 = s.borderTopWidth;
            float w = (lb.width - s.borderLeftWidth - s.borderRightWidth) * Mathf.Clamp01(progress);
            float h = lb.height - s.borderTopWidth - s.borderBottomWidth;
            if (w < 1f || h < 1f) return;
            var p = mgc.painter2D;
            float r = Mathf.Min(8f, h * 0.5f, w * 0.5f);
            p.fillColor = Doodle.Tomato;
            p.BeginPath();
            p.MoveTo(new Vector2(x0 + r, y0));
            p.LineTo(new Vector2(x0 + w - r, y0));
            p.ArcTo(new Vector2(x0 + w, y0), new Vector2(x0 + w, y0 + r), r);
            p.LineTo(new Vector2(x0 + w, y0 + h - r));
            p.ArcTo(new Vector2(x0 + w, y0 + h), new Vector2(x0 + w - r, y0 + h), r);
            p.LineTo(new Vector2(x0 + r, y0 + h));
            p.ArcTo(new Vector2(x0, y0 + h), new Vector2(x0, y0 + h - r), r);
            p.LineTo(new Vector2(x0, y0 + r));
            p.ArcTo(new Vector2(x0, y0), new Vector2(x0 + r, y0), r);
            p.ClosePath();
            p.Fill();
            p.strokeColor = Doodle.Ink;
            p.lineWidth = 2.5f;
            p.BeginPath();
            p.MoveTo(new Vector2(x0 + w, y0 + 1f));
            p.LineTo(new Vector2(x0 + w, y0 + h - 1f));
            p.Stroke();
        }
    }
}
