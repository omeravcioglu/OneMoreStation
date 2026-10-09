using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Metro.UI
{
    /// <summary>
    /// 24-hour strip in the top bar: a candy sky pill from night to day and back, striped rush-hour
    /// bands, the demand curve and a little red train for the current time, so the player can see the rush coming.
    /// </summary>
    public sealed class DayBar : VisualElement
    {
        public float minute;
        public bool weekend;
        public Func<int, bool, float> profile;
        const float Top = 7f, BarH = 24f, Pad = 3f;
        static readonly Color Night = new Color32(60, 58, 120, 255);
        static readonly Color Dawn = new Color32(255, 184, 107, 255);
        static readonly Color Day = new Color32(255, 243, 176, 255);
        static readonly Color Dusk = new Color32(255, 159, 122, 255);
        static readonly Color RushLight = new Color32(255, 179, 211, 255);
        static readonly Color RushDark = new Color32(255, 126, 182, 255);
        float[] cache;
        bool cacheWeekend;
        readonly List<Vector2> stripe = new List<Vector2>(4);

        public DayBar()
        {
            style.height = 50f;
            style.minWidth = 220f;
            generateVisualContent += Draw;
            string[] hours = { "6am", "noon", "6pm" };
            for (int i = 0; i < hours.Length; i++)
            {
                var l = new Label(hours[i]);
                l.AddToClassList("daybar-label");
                l.style.position = Position.Absolute;
                l.style.top = Top + BarH + 1f;
                l.style.left = Length.Percent((i + 1) * 25f);
                l.style.translate = new Translate(Length.Percent(-50), 0);
                l.pickingMode = PickingMode.Ignore;
                Add(l);
            }
        }

        static Color Sky(float h)
        {
            if (h < 5f) return Night;
            if (h < 6.2f) return Color.Lerp(Night, Dawn, (h - 5f) / 1.2f);
            if (h < 7.5f) return Color.Lerp(Dawn, Day, (h - 6.2f) / 1.3f);
            if (h < 17.5f) return Day;
            if (h < 19.2f) return Color.Lerp(Day, Dusk, (h - 17.5f) / 1.7f);
            if (h < 21f) return Color.Lerp(Dusk, Night, (h - 19.2f) / 1.8f);
            return Night;
        }

        void Draw(MeshGenerationContext mgc)
        {
            var r = contentRect;
            float w = r.width;
            if (w <= 40f) return;
            var p = mgc.painter2D;
            float x0 = Pad, x1 = w - Pad, bw = x1 - x0, rad = BarH * 0.5f;
            float y0 = Top, y1 = Top + BarH, mid = Top + rad;
            float X(float hour) => x0 + bw * hour / 24f;

            // The sky, sliced and clipped to the pill's rounded ends.
            void Extent(float x, out float top, out float bot)
            {
                float dx = x < x0 + rad ? x0 + rad - x : x > x1 - rad ? x - (x1 - rad) : 0f;
                float hh = Mathf.Sqrt(Mathf.Max(0f, rad * rad - dx * dx));
                top = mid - hh;
                bot = mid + hh;
            }
            const int slices = 120;
            for (int i = 0; i < slices; i++)
            {
                float xa = x0 + bw * i / slices, xb = Mathf.Min(x1, x0 + bw * (i + 1) / slices + 0.5f);
                Extent(xa, out float ta, out float ba);
                Extent(xb, out float tb, out float bb);
                p.fillColor = Sky((i + 0.5f) * 24f / slices);
                p.BeginPath();
                p.MoveTo(new Vector2(xa, ta));
                p.LineTo(new Vector2(xb, tb));
                p.LineTo(new Vector2(xb, bb));
                p.LineTo(new Vector2(xa, ba));
                p.ClosePath();
                p.Fill();
            }
            if (!weekend)
            {
                RushBand(p, X(7f), X(9.5f), y0 + 1.5f, y1 - 1.5f);
                RushBand(p, X(16.5f), X(19f), y0 + 1.5f, y1 - 1.5f);
            }
            if (profile != null)
            {
                if (cache == null || cacheWeekend != weekend)
                {
                    cache = new float[97];
                    cacheWeekend = weekend;
                    float max = 1e-6f;
                    for (int i = 0; i <= 96; i++)
                    {
                        float v = 0f;
                        for (int k = 0; k < 15; k++) v += profile(Mathf.Min(1439, i * 15 + k), weekend);
                        cache[i] = v;
                        max = Mathf.Max(max, v);
                    }
                    for (int i = 0; i <= 96; i++) cache[i] /= max;
                }
                p.strokeColor = Doodle.WithAlpha(Doodle.Ink, 0.4f);
                p.lineWidth = 2f;
                p.lineJoin = LineJoin.Round;
                p.lineCap = LineCap.Round;
                p.BeginPath();
                for (int i = 0; i <= 96; i++)
                {
                    float x = x0 + rad * 0.6f + (bw - rad * 1.2f) * i / 96f;
                    var pt = new Vector2(x, y1 - 4f - cache[i] * (BarH - 8f));
                    if (i == 0) p.MoveTo(pt);
                    else p.LineTo(pt);
                }
                p.Stroke();
            }
            // Ink outline of the pill.
            p.strokeColor = Doodle.Ink;
            p.lineWidth = 2.5f;
            p.BeginPath();
            p.MoveTo(new Vector2(x0 + rad, y0));
            p.LineTo(new Vector2(x1 - rad, y0));
            p.Arc(new Vector2(x1 - rad, mid), rad, Angle.Degrees(270f), Angle.Degrees(450f));
            p.LineTo(new Vector2(x0 + rad, y1));
            p.Arc(new Vector2(x0 + rad, mid), rad, Angle.Degrees(90f), Angle.Degrees(270f));
            p.ClosePath();
            p.Stroke();

            // Now: a little red train riding along the day.
            float mx = Mathf.Clamp(x0 + bw * Mathf.Repeat(minute, 1440f) / 1440f, x0 + 13f, x1 - 13f);
            RoundRect(p, mx - 14f, mid - 8f, 28f, 16f, 8f);
            p.fillColor = Doodle.LineRed;
            p.Fill();
            p.strokeColor = Doodle.Ink;
            p.lineWidth = 2.4f;
            p.Stroke();
            for (int k = -1; k <= 1; k += 2)
            {
                RoundRect(p, mx + k * 6f - 3f, mid - 3.5f, 6f, 6f, 1.5f);
                p.fillColor = Doodle.Paper;
                p.Fill();
                p.lineWidth = 1.3f;
                p.Stroke();
            }
        }

        static void RoundRect(Painter2D p, float x, float y, float w, float h, float r)
        {
            r = Mathf.Min(r, Mathf.Min(w, h) * 0.5f);
            p.BeginPath();
            p.MoveTo(new Vector2(x + r, y));
            p.LineTo(new Vector2(x + w - r, y));
            p.ArcTo(new Vector2(x + w, y), new Vector2(x + w, y + r), r);
            p.LineTo(new Vector2(x + w, y + h - r));
            p.ArcTo(new Vector2(x + w, y + h), new Vector2(x + w - r, y + h), r);
            p.LineTo(new Vector2(x + r, y + h));
            p.ArcTo(new Vector2(x, y + h), new Vector2(x, y + h - r), r);
            p.LineTo(new Vector2(x, y + r));
            p.ArcTo(new Vector2(x, y), new Vector2(x + r, y), r);
            p.ClosePath();
        }

        /// <summary>A pink band with diagonal candy stripes.</summary>
        void RushBand(Painter2D p, float bx0, float bx1, float by0, float by1)
        {
            p.fillColor = RushLight;
            p.BeginPath();
            p.MoveTo(new Vector2(bx0, by0));
            p.LineTo(new Vector2(bx1, by0));
            p.LineTo(new Vector2(bx1, by1));
            p.LineTo(new Vector2(bx0, by1));
            p.ClosePath();
            p.Fill();
            float h = by1 - by0;
            p.fillColor = RushDark;
            for (float s = bx0 - h; s < bx1; s += 8f)
            {
                stripe.Clear();
                stripe.Add(new Vector2(s, by1));
                stripe.Add(new Vector2(s + h, by0));
                stripe.Add(new Vector2(s + h + 4f, by0));
                stripe.Add(new Vector2(s + 4f, by1));
                var poly = Clip.ToSlab(stripe, bx0, bx1);
                if (poly.Count < 3) continue;
                p.BeginPath();
                p.MoveTo(poly[0]);
                for (int i = 1; i < poly.Count; i++) p.LineTo(poly[i]);
                p.ClosePath();
                p.Fill();
            }
        }

        public void SetTime(float minuteOfDay, bool isWeekend)
        {
            if (Mathf.Abs(minuteOfDay - minute) < 0.5f && isWeekend == weekend) return;
            minute = minuteOfDay;
            weekend = isWeekend;
            MarkDirtyRepaint();
        }
    }

    /// <summary>Compact bar or line chart in candy colours with ink outlines. Optional second series drawn as a faint comparison line.</summary>
    public sealed class MiniChart : VisualElement
    {
        public float[] values = new float[0];
        public float[] compare;
        public Color color = Doodle.Sky;
        public Color compareColor = Doodle.WithAlpha(Doodle.Ink, 0.45f);
        public Color negativeColor = Doodle.Tomato;
        public bool bars = true;
        public int highlight = -1;
        public float forcedMax;
        /// <summary>When positive, a dashed red "full!" line at this value (e.g. 1 = 100 % load).</summary>
        public float fullLine = -1f;
        /// <summary>Puts a worried face over the tallest bar once it reaches 90 % of <see cref="fullLine"/>.</summary>
        public bool peakFace;
        /// <summary>For 24 hourly bars: pink bands behind the weekday rush hours.</summary>
        public bool rushBands;
        /// <summary>Bar index that is still filling up ("so far"): drawn striped, with a dashed "now" line.</summary>
        public int nowIndex = -1;
        readonly Label empty;
        Label fullLabel, nowLabel;

        public MiniChart(float height, string emptyText = "No data yet — check back after the first full day.")
        {
            style.height = height;
            generateVisualContent += Draw;
            empty = new Label(emptyText);
            empty.AddToClassList("small");
            empty.AddToClassList("faint");
            empty.style.position = Position.Absolute;
            empty.style.left = 0;
            empty.style.right = 0;
            empty.style.top = 0;
            empty.style.bottom = 0;
            empty.style.unityTextAlign = TextAnchor.MiddleCenter;
            empty.style.whiteSpace = WhiteSpace.Normal;
            empty.pickingMode = PickingMode.Ignore;
            Add(empty);
            RegisterCallback<GeometryChangedEvent>(_ => PlaceFullLabel());
        }

        void PlaceFullLabel()
        {
            if (fullLine <= 0f) return;
            if (fullLabel == null)
            {
                fullLabel = new Label("full!");
                fullLabel.AddToClassList("chart-full-label");
                fullLabel.style.position = Position.Absolute;
                fullLabel.style.right = 0f;
                fullLabel.pickingMode = PickingMode.Ignore;
                UIK.Display(fullLabel);
                Add(fullLabel);
            }
            float h = contentRect.height;
            if (h <= 2f) return;
            float max = forcedMax > 0f ? forcedMax : 1f;
            float y = h - fullLine / Mathf.Max(max, fullLine) * (h - 2f) - 1f;
            fullLabel.style.top = y - 17f;
        }

        public void SetData(float[] v, float[] cmp = null, int hl = -1)
        {
            values = v ?? new float[0];
            compare = cmp;
            highlight = hl;
            bool any = false;
            foreach (var x in values)
                if (Mathf.Abs(x) > 1e-4f) any = true;
            empty.visible = !any || (!bars && values.Length < 2);
            PlaceFullLabel();
            if (nowIndex >= 0 && values.Length > 0)
            {
                if (nowLabel == null)
                {
                    nowLabel = new Label("now");
                    nowLabel.AddToClassList("chart-now");
                    nowLabel.style.position = Position.Absolute;
                    nowLabel.style.top = -4f;
                    nowLabel.pickingMode = PickingMode.Ignore;
                    UIK.Display(nowLabel);
                    Add(nowLabel);
                }
                nowLabel.style.left = Length.Percent((nowIndex + 0.5f) / values.Length * 100f);
                nowLabel.style.translate = new Translate(Length.Percent(-50), 0);
            }
            if (nowLabel != null) nowLabel.visible = nowIndex >= 0;
            MarkDirtyRepaint();
        }

        void Draw(MeshGenerationContext mgc)
        {
            var r = contentRect;
            float w = r.width, h = r.height;
            int n = values.Length;
            if (w <= 2f || h <= 2f || n == 0) return;
            var p = mgc.painter2D;
            float max = forcedMax > 0f ? forcedMax : 1e-6f, min = 0f;
            foreach (var v in values)
            {
                max = Mathf.Max(max, v);
                min = Mathf.Min(min, v);
            }
            if (compare != null)
                foreach (var v in compare) max = Mathf.Max(max, v);
            if (fullLine > 0f) max = Mathf.Max(max, fullLine);
            float range = Mathf.Max(1e-6f, max - min);
            float Y(float v) => h - (v - min) / range * (h - 2f) - 1f;
            float zero = Y(0f);

            p.lineCap = LineCap.Round;
            p.lineJoin = LineJoin.Round;
            p.strokeColor = Doodle.WithAlpha(Doodle.Ink, 0.25f);
            p.lineWidth = 2f;
            p.BeginPath();
            p.MoveTo(new Vector2(1f, zero));
            p.LineTo(new Vector2(w - 1f, zero));
            p.Stroke();

            if (bars && rushBands && n == 24)
            {
                p.fillColor = Doodle.WithAlpha(Doodle.Gum, 0.22f);
                foreach (var (h0, h1) in new[] { (7f, 9.5f), (16.5f, 19f) })
                {
                    float xa = w * h0 / 24f, xb = w * h1 / 24f;
                    p.BeginPath();
                    p.MoveTo(new Vector2(xa, 0f));
                    p.LineTo(new Vector2(xb, 0f));
                    p.LineTo(new Vector2(xb, h));
                    p.LineTo(new Vector2(xa, h));
                    p.ClosePath();
                    p.Fill();
                }
            }
            if (bars)
            {
                float bw = w / n;
                bool outline = bw >= 6f;
                for (int i = 0; i < n; i++)
                {
                    float v = values[i];
                    float y = Y(v);
                    float x0 = i * bw + bw * 0.15f, x1 = (i + 1) * bw - bw * 0.15f;
                    float top = Mathf.Min(y, zero), bot = Mathf.Max(y, zero);
                    if (bot - top < 0.5f) continue;
                    Color c = v < 0f ? negativeColor : color;
                    if (highlight >= 0 && i != highlight) c.a *= 0.55f;
                    float rr = Mathf.Min(3f, (x1 - x0) * 0.35f, (bot - top) * 0.5f);
                    // Rounded at the end away from the zero line, like a candy stick.
                    p.BeginPath();
                    if (v >= 0f)
                    {
                        p.MoveTo(new Vector2(x0, bot));
                        p.LineTo(new Vector2(x0, top + rr));
                        p.ArcTo(new Vector2(x0, top), new Vector2(x0 + rr, top), rr);
                        p.LineTo(new Vector2(x1 - rr, top));
                        p.ArcTo(new Vector2(x1, top), new Vector2(x1, top + rr), rr);
                        p.LineTo(new Vector2(x1, bot));
                    }
                    else
                    {
                        p.MoveTo(new Vector2(x0, top));
                        p.LineTo(new Vector2(x0, bot - rr));
                        p.ArcTo(new Vector2(x0, bot), new Vector2(x0 + rr, bot), rr);
                        p.LineTo(new Vector2(x1 - rr, bot));
                        p.ArcTo(new Vector2(x1, bot), new Vector2(x1, bot - rr), rr);
                        p.LineTo(new Vector2(x1, top));
                    }
                    p.ClosePath();
                    p.fillColor = c;
                    p.Fill();
                    if (i == nowIndex) Stripes(p, x0, x1, top, bot);
                    if (i == nowIndex)
                    {
                        // Redo the outline path: the stripes started new paths.
                        p.BeginPath();
                        p.MoveTo(new Vector2(x0, bot));
                        p.LineTo(new Vector2(x0, top));
                        p.LineTo(new Vector2(x1, top));
                        p.LineTo(new Vector2(x1, bot));
                    }
                    if (outline)
                    {
                        p.strokeColor = highlight >= 0 && i != highlight ? Doodle.WithAlpha(Doodle.Ink, 0.5f) : Doodle.Ink;
                        p.lineWidth = 1.6f;
                        p.Stroke();
                    }
                }
                if (nowIndex >= 0 && nowIndex < n)
                {
                    float x = (nowIndex + 0.5f) * bw;
                    p.strokeColor = Doodle.Ink;
                    p.lineWidth = 2f;
                    for (float y = 10f; y < h; y += 7f)
                    {
                        p.BeginPath();
                        p.MoveTo(new Vector2(x, y));
                        p.LineTo(new Vector2(x, Mathf.Min(y + 3.5f, h)));
                        p.Stroke();
                    }
                }
                if (peakFace && fullLine > 0f)
                {
                    int top = 0;
                    for (int i = 1; i < n; i++)
                        if (values[i] > values[top]) top = i;
                    if (values[top] >= 0.9f * fullLine)
                    {
                        float cx = (top + 0.5f) * bw, cy = Mathf.Max(9f, Y(values[top]) - 9f);
                        DrawFace(p, new Vector2(cx, cy), 8f);
                    }
                }
                if (compare != null && compare.Length == n)
                {
                    p.strokeColor = compareColor;
                    p.lineWidth = 2f;
                    p.BeginPath();
                    for (int i = 0; i < n; i++)
                    {
                        var pt = new Vector2((i + 0.5f) * bw, Y(compare[i]));
                        if (i == 0) p.MoveTo(pt);
                        else p.LineTo(pt);
                    }
                    p.Stroke();
                }
            }
            else
            {
                float step = n > 1 ? (w - 4f) / (n - 1) : w;
                var fill = color;
                fill.a = 0.3f;
                p.fillColor = fill;
                p.BeginPath();
                p.MoveTo(new Vector2(2f, zero));
                for (int i = 0; i < n; i++) p.LineTo(new Vector2(2f + i * step, Y(values[i])));
                p.LineTo(new Vector2(2f + (n - 1) * step, zero));
                p.ClosePath();
                p.Fill();
                // Ink under-stroke, then the colour on top: a doodled line.
                for (int pass = 0; pass < 2; pass++)
                {
                    p.strokeColor = pass == 0 ? Doodle.Ink : color;
                    p.lineWidth = pass == 0 ? 4.6f : 2.4f;
                    p.BeginPath();
                    for (int i = 0; i < n; i++)
                    {
                        var pt = new Vector2(2f + i * step, Y(values[i]));
                        if (i == 0) p.MoveTo(pt);
                        else p.LineTo(pt);
                    }
                    p.Stroke();
                }
            }
            if (fullLine > 0f)
            {
                // Dashed red "full!" line.
                float y = Y(fullLine);
                p.strokeColor = Doodle.Tomato;
                p.lineWidth = 2f;
                for (float x = 1f; x < w - 1f; x += 9f)
                {
                    p.BeginPath();
                    p.MoveTo(new Vector2(x, y));
                    p.LineTo(new Vector2(Mathf.Min(x + 5f, w - 1f), y));
                    p.Stroke();
                }
            }
        }

        static readonly List<Vector2> stripe = new List<Vector2>(4);

        /// <summary>White diagonal candy stripes over a bar (a day or an hour that's still going).</summary>
        public static void Stripes(Painter2D p, float x0, float x1, float top, float bot)
        {
            float hgt = bot - top;
            if (hgt < 2f || x1 - x0 < 2f) return;
            p.fillColor = Doodle.WithAlpha(Color.white, 0.55f);
            for (float sx = x0 - hgt; sx < x1; sx += 7f)
            {
                stripe.Clear();
                stripe.Add(new Vector2(sx, bot));
                stripe.Add(new Vector2(sx + hgt, top));
                stripe.Add(new Vector2(sx + hgt + 3f, top));
                stripe.Add(new Vector2(sx + 3f, bot));
                var poly = Clip.ToSlab(stripe, x0, x1);
                if (poly.Count < 3) continue;
                p.BeginPath();
                p.MoveTo(poly[0]);
                for (int i = 1; i < poly.Count; i++) p.LineTo(poly[i]);
                p.ClosePath();
                p.Fill();
            }
        }

        /// <summary>A sweaty, cross little face (overcrowded!).</summary>
        static void DrawFace(Painter2D p, Vector2 c, float r)
        {
            p.fillColor = Doodle.MoodBad;
            p.BeginPath();
            p.Arc(c, r, Angle.Degrees(0f), Angle.Degrees(360f));
            p.ClosePath();
            p.Fill();
            p.strokeColor = Doodle.Ink;
            p.lineWidth = 1.8f;
            p.Stroke();
            p.fillColor = Doodle.Ink;
            foreach (float dx in new[] { -0.35f, 0.35f })
            {
                p.BeginPath();
                p.Arc(c + new Vector2(dx * r, -0.15f * r), 0.14f * r, Angle.Degrees(0f), Angle.Degrees(360f));
                p.ClosePath();
                p.Fill();
            }
            p.BeginPath();
            p.MoveTo(c + new Vector2(-0.4f * r, 0.5f * r));
            p.QuadraticCurveTo(c + new Vector2(0f, 0.15f * r), c + new Vector2(0.4f * r, 0.5f * r));
            p.lineWidth = 1.6f;
            p.Stroke();
            // A sweat drop.
            p.fillColor = Doodle.Sky;
            p.BeginPath();
            p.MoveTo(c + new Vector2(0.95f * r, -0.75f * r));
            p.QuadraticCurveTo(c + new Vector2(1.35f * r, -0.15f * r), c + new Vector2(0.95f * r, -0.1f * r));
            p.QuadraticCurveTo(c + new Vector2(0.6f * r, -0.15f * r), c + new Vector2(0.95f * r, -0.75f * r));
            p.ClosePath();
            p.Fill();
            p.lineWidth = 1.2f;
            p.Stroke();
        }
    }

    /// <summary>
    /// Bars for the last N days: past days solid, today striped ("so far"), days still to come as dashed
    /// placeholders. Day numbers sit under the bars; today's, the best and the worst value are written above.
    /// </summary>
    public sealed class DayBars : VisualElement
    {
        public Color color = Doodle.Mint, negativeColor = Doodle.Tomato;
        public Func<float, string> format = v => Fmt.Count(v);
        readonly int slots;
        readonly float height;
        float[] values = new float[0];
        int firstDay = 1;
        readonly Label[] dayLabels, valueLabels;
        const float Top = 20f, Bottom = 18f;

        public DayBars(int slots, float height)
        {
            this.slots = slots;
            this.height = height;
            style.height = height;
            generateVisualContent += Draw;
            dayLabels = new Label[slots];
            valueLabels = new Label[slots];
            for (int i = 0; i < slots; i++)
            {
                var d = new Label();
                d.AddToClassList("daybars-day");
                d.style.position = Position.Absolute;
                d.style.top = height - Bottom + 1f;
                d.style.left = Length.Percent((i + 0.5f) / slots * 100f);
                d.style.translate = new Translate(Length.Percent(-50), 0);
                d.pickingMode = PickingMode.Ignore;
                Add(d);
                dayLabels[i] = d;
                var v = new Label();
                v.AddToClassList("daybars-value");
                v.style.position = Position.Absolute;
                v.style.left = Length.Percent((i + 0.5f) / slots * 100f);
                v.style.translate = new Translate(Length.Percent(-50), 0);
                v.pickingMode = PickingMode.Ignore;
                UIK.Display(v);
                Add(v);
                valueLabels[i] = v;
            }
        }

        /// <summary>Values from the oldest shown day to today (last); firstDayNumber is the day number of values[0].</summary>
        public void SetData(float[] v, int firstDayNumber)
        {
            values = v ?? new float[0];
            if (values.Length > slots)
            {
                var cut = new float[slots];
                Array.Copy(values, values.Length - slots, cut, 0, slots);
                firstDayNumber += values.Length - slots;
                values = cut;
            }
            firstDay = firstDayNumber;
            Range(out float min, out float max);
            int best = -1, worst = -1;
            for (int i = 0; i < values.Length - 1; i++)
            {
                if (best < 0 || values[i] > values[best]) best = i;
                if (worst < 0 || values[i] < values[worst]) worst = i;
            }
            int today = values.Length - 1;
            // Label today first, then the most extreme past day, then the other one; never two side by side.
            var labelled = new List<int>();
            void Want(int i)
            {
                if (i < 0 || Mathf.Abs(values[i]) < 1e-3f) return;
                foreach (int j in labelled)
                    if (Mathf.Abs(j - i) < 2) return;
                labelled.Add(i);
            }
            Want(today);
            if (worst >= 0 && best >= 0 && Mathf.Abs(values[worst]) > Mathf.Abs(values[best])) { Want(worst); Want(best); }
            else { Want(best); Want(worst); }
            for (int i = 0; i < slots; i++)
            {
                dayLabels[i].text = i == today ? "today" : "D" + (firstDay + i);
                dayLabels[i].EnableInClassList("daybars-today", i == today);
                bool show = labelled.Contains(i);
                valueLabels[i].visible = show;
                if (!show) continue;
                valueLabels[i].text = format(values[i]);
                float y = Y(values[i], min, max);
                float zero = Y(0f, min, max);
                valueLabels[i].style.top = values[i] >= 0f ? y - 17f : y + 1f;
                valueLabels[i].EnableInClassList("bad", values[i] < 0f);
                if (values[i] < 0f && y + 18f > height - Bottom) valueLabels[i].style.top = zero - 17f;
            }
            MarkDirtyRepaint();
        }

        void Range(out float min, out float max)
        {
            min = 0f;
            max = 1e-6f;
            foreach (var v in values)
            {
                min = Mathf.Min(min, v);
                max = Mathf.Max(max, v);
            }
        }

        float Y(float v, float min, float max)
        {
            float top = Top, bot = height - Bottom;
            return bot - (v - min) / Mathf.Max(1e-6f, max - min) * (bot - top);
        }

        void Draw(MeshGenerationContext mgc)
        {
            var r = contentRect;
            float w = r.width;
            if (w <= 10f) return;
            var p = mgc.painter2D;
            Range(out float min, out float max);
            float zero = Y(0f, min, max);
            float bw = w / slots;
            p.lineJoin = LineJoin.Round;
            p.lineCap = LineCap.Round;
            p.strokeColor = Doodle.WithAlpha(Doodle.Ink, 0.25f);
            p.lineWidth = 2f;
            p.BeginPath();
            p.MoveTo(new Vector2(1f, zero));
            p.LineTo(new Vector2(w - 1f, zero));
            p.Stroke();
            int today = values.Length - 1;
            for (int i = 0; i < slots; i++)
            {
                float x0 = i * bw + bw * 0.16f, x1 = (i + 1) * bw - bw * 0.16f;
                if (i >= values.Length)
                {
                    // A day still to come: a dashed placeholder.
                    float ph = (height - Top - Bottom) * 0.28f;
                    p.strokeColor = Doodle.WithAlpha(Doodle.Ink, 0.3f);
                    p.lineWidth = 1.6f;
                    float yTop = zero - ph;
                    for (float y = yTop; y < zero; y += 6f)
                    {
                        p.BeginPath();
                        p.MoveTo(new Vector2(x0, y));
                        p.LineTo(new Vector2(x0, Mathf.Min(y + 3f, zero)));
                        p.Stroke();
                        p.BeginPath();
                        p.MoveTo(new Vector2(x1, y));
                        p.LineTo(new Vector2(x1, Mathf.Min(y + 3f, zero)));
                        p.Stroke();
                    }
                    p.BeginPath();
                    p.MoveTo(new Vector2(x0, yTop));
                    p.LineTo(new Vector2(x1, yTop));
                    p.Stroke();
                    continue;
                }
                float v = values[i];
                float yv = Y(v, min, max);
                float top = Mathf.Min(yv, zero), bot = Mathf.Max(yv, zero);
                if (bot - top < 1f) continue;
                p.BeginPath();
                p.MoveTo(new Vector2(x0, top));
                p.LineTo(new Vector2(x1, top));
                p.LineTo(new Vector2(x1, bot));
                p.LineTo(new Vector2(x0, bot));
                p.ClosePath();
                p.fillColor = v < 0f ? negativeColor : color;
                p.Fill();
                if (i == today) MiniChart.Stripes(p, x0, x1, top, bot);
                p.BeginPath();
                p.MoveTo(new Vector2(x0, top));
                p.LineTo(new Vector2(x1, top));
                p.LineTo(new Vector2(x1, bot));
                p.LineTo(new Vector2(x0, bot));
                p.ClosePath();
                p.strokeColor = Doodle.Ink;
                p.lineWidth = 1.6f;
                p.Stroke();
            }
        }
    }
}
