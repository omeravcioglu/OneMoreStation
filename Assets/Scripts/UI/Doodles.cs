using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Metro.UI
{
    /// <summary>
    /// The "Doodle Metro" palette: ink outlines, cream paper and candy fills. Metro.uss defines the
    /// same values as USS variables (--ink, --sun, ...); drawn widgets and code-coloured elements use these.
    /// </summary>
    public static class Doodle
    {
        public static readonly Color Ink = new Color32(43, 30, 63, 255);
        public static readonly Color Ink2 = new Color32(91, 78, 112, 255);
        public static readonly Color Ink3 = new Color32(138, 127, 158, 255);
        public static readonly Color Paper = Color.white;
        public static readonly Color Cream = new Color32(255, 246, 227, 255);
        public static readonly Color Sand = new Color32(243, 228, 192, 255);
        public static readonly Color Sun = new Color32(255, 210, 63, 255);
        public static readonly Color SunL = new Color32(255, 233, 138, 255);
        public static readonly Color Gum = new Color32(255, 126, 182, 255);
        public static readonly Color GumL = new Color32(255, 209, 229, 255);
        public static readonly Color Mint = new Color32(95, 224, 176, 255);
        public static readonly Color MintL = new Color32(207, 247, 230, 255);
        public static readonly Color Sky = new Color32(79, 195, 247, 255);
        public static readonly Color SkyL = new Color32(210, 238, 255, 255);
        public static readonly Color Grape = new Color32(161, 120, 255, 255);
        public static readonly Color GrapeL = new Color32(230, 219, 255, 255);
        public static readonly Color Tang = new Color32(255, 154, 61, 255);
        public static readonly Color TangL = new Color32(255, 224, 194, 255);
        public static readonly Color Tomato = new Color32(255, 90, 78, 255);
        public static readonly Color TomatoL = new Color32(255, 214, 210, 255);
        public static readonly Color Coin = new Color32(255, 194, 61, 255);
        public static readonly Color CoinDark = new Color32(232, 154, 16, 255);
        /// <summary>Text colours that stay readable on cream.</summary>
        public static readonly Color Good = new Color32(23, 102, 58, 255);
        public static readonly Color Warn = new Color32(163, 87, 10, 255);
        public static readonly Color Bad = new Color32(180, 36, 36, 255);

        // Faces: happy, fine, meh, cross.
        public static readonly Color MoodGood = new Color32(126, 224, 168, 255);
        public static readonly Color MoodOk = new Color32(255, 216, 77, 255);
        public static readonly Color MoodMeh = new Color32(255, 159, 69, 255);
        public static readonly Color MoodBad = new Color32(255, 90, 78, 255);

        // Icon extras.
        public static readonly Color Skin = new Color32(255, 217, 184, 255);
        public static readonly Color Brown = new Color32(184, 122, 75, 255);
        public static readonly Color Pig = new Color32(255, 156, 198, 255);
        public static readonly Color Bun = new Color32(244, 166, 78, 255);
        public static readonly Color Lettuce = new Color32(76, 184, 74, 255);
        public static readonly Color Patty = new Color32(139, 90, 60, 255);
        public static readonly Color LineRed = new Color32(255, 81, 70, 255);
        public static readonly Color LineYellow = new Color32(255, 201, 41, 255);

        public static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, a);

        /// <summary>Green / yellow / red for a 0..1 "how good is it" value (bars, faces).</summary>
        public static Color Score(float v01) => v01 >= 0.7f ? Mint : v01 >= 0.45f ? Sun : Tomato;

        /// <summary>Crowding as a face: +1 grinning when empty, 0 meh around 70 %, -1 cross when full.</summary>
        public static float MoodFromCrowding(float crowding) => Mathf.Lerp(1f, -1f, Mathf.InverseLerp(0.3f, 1f, crowding));

        /// <summary>Crowding in words, matching the 40 / 70 / 90 / 100 % bands of the crowding colours.</summary>
        public static string CrowdWord(float crowding) =>
            crowding < 0.4f ? "quiet" : crowding < 0.7f ? "busy" : crowding < 0.9f ? "packed" : crowding < 1f ? "squished!" : "dangerous!";

        /// <summary>A colour per kind of district (district cards, the City window's district strip, the map).</summary>
        public static Color DistrictColor(DistrictType t)
        {
            switch (t)
            {
                case DistrictType.Central: return Sun;
                case DistrictType.OldTown: return Tang;
                case DistrictType.Residential: return Mint;
                case DistrictType.Suburb: return new Color32(184, 233, 134, 255);
                case DistrictType.NewTown: return Sky;
                case DistrictType.Industrial: return new Color32(183, 176, 196, 255);
                case DistrictType.University: return Grape;
                case DistrictType.BusinessPark: return new Color32(122, 176, 255, 255);
                case DistrictType.Airport: return SkyL;
                case DistrictType.Stadium: return Gum;
            }
            return Sand;
        }

        public static IconKind DistrictIcon(DistrictType t)
        {
            switch (t)
            {
                case DistrictType.Central: return IconKind.City;
                case DistrictType.OldTown: return IconKind.Hall;
                case DistrictType.Residential:
                case DistrictType.Suburb:
                case DistrictType.NewTown: return IconKind.Population;
                case DistrictType.Industrial:
                case DistrictType.BusinessPark: return IconKind.Jobs;
                case DistrictType.University: return IconKind.Star;
                case DistrictType.Airport: return IconKind.Cloud;
                case DistrictType.Stadium: return IconKind.Trophy;
            }
            return IconKind.City;
        }
    }

    /// <summary>
    /// A meter in four zones (quiet / busy / packed / dangerous at 40, 70, 90 and 100 %) with a pointer
    /// at the current value: how full a platform is, at a glance.
    /// </summary>
    public sealed class ZoneMeter : VisualElement
    {
        float value;
        static readonly float[] Stops = { 0f, 0.4f, 0.7f, 0.9f, 1f, 1.2f };
        static readonly Color[] Zones = { Doodle.MintL, Doodle.SunL, Doodle.TangL, Doodle.TomatoL, Doodle.Tomato };

        public ZoneMeter()
        {
            style.height = 26f;
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        public float Value
        {
            get => value;
            set
            {
                value = Mathf.Max(0f, value);
                if (Mathf.Abs(value - this.value) < 0.002f) return;
                this.value = value;
                MarkDirtyRepaint();
            }
        }

        void Draw(MeshGenerationContext mgc)
        {
            var r = contentRect;
            if (r.width < 20f) return;
            var p = mgc.painter2D;
            float x0 = 2f, x1 = r.width - 2f, top = 8f, bot = r.height - 3f;
            float X(float v) => Mathf.Lerp(x0, x1, Mathf.Clamp01(v / Stops[Stops.Length - 1]));
            for (int i = 0; i < Zones.Length; i++)
            {
                p.fillColor = Zones[i];
                p.BeginPath();
                p.MoveTo(new Vector2(X(Stops[i]), top));
                p.LineTo(new Vector2(X(Stops[i + 1]), top));
                p.LineTo(new Vector2(X(Stops[i + 1]), bot));
                p.LineTo(new Vector2(X(Stops[i]), bot));
                p.ClosePath();
                p.Fill();
            }
            p.strokeColor = Doodle.WithAlpha(Doodle.Ink, 0.35f);
            p.lineWidth = 1.5f;
            for (int i = 1; i < Stops.Length - 1; i++)
            {
                p.BeginPath();
                p.MoveTo(new Vector2(X(Stops[i]), top + 1f));
                p.LineTo(new Vector2(X(Stops[i]), bot - 1f));
                p.Stroke();
            }
            p.strokeColor = Doodle.Ink;
            p.lineWidth = 2.5f;
            p.lineJoin = LineJoin.Round;
            p.BeginPath();
            p.MoveTo(new Vector2(x0, top));
            p.LineTo(new Vector2(x1, top));
            p.LineTo(new Vector2(x1, bot));
            p.LineTo(new Vector2(x0, bot));
            p.ClosePath();
            p.Stroke();
            // The pointer: a little ink arrow riding on top of the meter.
            float px = X(value);
            p.fillColor = Doodle.Ink;
            p.BeginPath();
            p.MoveTo(new Vector2(px - 6f, 0f));
            p.LineTo(new Vector2(px + 6f, 0f));
            p.LineTo(new Vector2(px, top + 3f));
            p.ClosePath();
            p.Fill();
            p.lineWidth = 3f;
            p.BeginPath();
            p.MoveTo(new Vector2(px, top));
            p.LineTo(new Vector2(px, bot));
            p.Stroke();
        }
    }

    /// <summary>A spiky sticker (the "Rush hour!" burst on the clock): a 16-point star with an ink outline.</summary>
    public sealed class Starburst : VisualElement
    {
        Color fill = Doodle.Gum;
        public readonly Label label;

        public Starburst(float size, string text)
        {
            style.width = size;
            style.height = size;
            style.alignItems = Align.Center;
            style.justifyContent = Justify.Center;
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
            label = UIK.Text(this, text, "starburst-text");
            label.pickingMode = PickingMode.Ignore;
            UIK.Display(label);
        }

        public Color Fill
        {
            get => fill;
            set
            {
                if (fill == value) return;
                fill = value;
                MarkDirtyRepaint();
            }
        }

        void Draw(MeshGenerationContext mgc)
        {
            var r = contentRect;
            float s = Mathf.Min(r.width, r.height);
            if (s < 4f) return;
            var p = mgc.painter2D;
            Vector2 c = r.center;
            float lw = Mathf.Max(1.5f, s * 0.04f);
            float ro = s * 0.5f - lw * 0.5f, ri = ro * 0.74f;
            p.BeginPath();
            for (int i = 0; i < 32; i++)
            {
                float a = (i * 11.25f - 90f) * Mathf.Deg2Rad;
                float rad = i % 2 == 0 ? ro : ri;
                var pt = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rad;
                if (i == 0) p.MoveTo(pt);
                else p.LineTo(pt);
            }
            p.ClosePath();
            p.fillColor = fill;
            p.Fill();
            p.strokeColor = Doodle.Ink;
            p.lineWidth = lw;
            p.lineJoin = LineJoin.Round;
            p.Stroke();
        }
    }

    /// <summary>A hand-drawn wavy rule, used between groups of tools.</summary>
    public sealed class Squiggle : VisualElement
    {
        Color color = Doodle.WithAlpha(Doodle.Ink, 0.45f);
        float width = 2f;

        public Squiggle()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        /// <summary>A coloured squiggle, e.g. the wavy underline of a link.</summary>
        public Squiggle(Color color, float width = 2f) : this()
        {
            this.color = color;
            this.width = width;
        }

        void Draw(MeshGenerationContext mgc)
        {
            var r = contentRect;
            if (r.width < 6f || r.height < 2f) return;
            var p = mgc.painter2D;
            p.strokeColor = color;
            p.lineWidth = width;
            p.lineCap = LineCap.Round;
            p.lineJoin = LineJoin.Round;
            float y = r.height * 0.5f, amp = Mathf.Min(3.5f, r.height * 0.4f);
            float x0 = 1.5f, x1 = r.width - 1.5f;
            int waves = Mathf.Max(2, Mathf.RoundToInt((x1 - x0) / 10f));
            float step = (x1 - x0) / waves;
            p.BeginPath();
            p.MoveTo(new Vector2(x0, y));
            for (int i = 0; i < waves; i++)
            {
                float x = x0 + i * step;
                p.QuadraticCurveTo(new Vector2(x + step * 0.25f, y - amp * 2f), new Vector2(x + step * 0.5f, y));
                p.QuadraticCurveTo(new Vector2(x + step * 0.75f, y + amp * 2f), new Vector2(x + step, y));
            }
            p.Stroke();
        }
    }

    /// <summary>A red thumbtack holding the sticky note (goals panel) to the screen.</summary>
    public sealed class Pushpin : VisualElement
    {
        public Pushpin(float size = 30f)
        {
            style.width = size;
            style.height = size;
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        void Draw(MeshGenerationContext mgc)
        {
            float u = contentRect.width / 30f;
            if (u <= 0f) return;
            var p = mgc.painter2D;
            void Circle(float cx, float cy, float r)
            {
                p.BeginPath();
                p.Arc(new Vector2(cx * u, cy * u), r * u, Angle.Degrees(0f), Angle.Degrees(360f));
                p.ClosePath();
            }
            // Soft shadow on the paper.
            p.fillColor = Doodle.WithAlpha(Doodle.Ink, 0.22f);
            p.BeginPath();
            const float k = 0.5523f;
            float cx0 = 15f * u, cy0 = 25f * u, rx = 6f * u, ry = 2f * u;
            p.MoveTo(new Vector2(cx0 + rx, cy0));
            p.BezierCurveTo(new Vector2(cx0 + rx, cy0 + ry * k), new Vector2(cx0 + rx * k, cy0 + ry), new Vector2(cx0, cy0 + ry));
            p.BezierCurveTo(new Vector2(cx0 - rx * k, cy0 + ry), new Vector2(cx0 - rx, cy0 + ry * k), new Vector2(cx0 - rx, cy0));
            p.BezierCurveTo(new Vector2(cx0 - rx, cy0 - ry * k), new Vector2(cx0 - rx * k, cy0 - ry), new Vector2(cx0, cy0 - ry));
            p.BezierCurveTo(new Vector2(cx0 + rx * k, cy0 - ry), new Vector2(cx0 + rx, cy0 - ry * k), new Vector2(cx0 + rx, cy0));
            p.ClosePath();
            p.Fill();
            Circle(15f, 13f, 10f);
            p.fillColor = Doodle.Tomato;
            p.Fill();
            p.strokeColor = Doodle.Ink;
            p.lineWidth = Mathf.Max(1.5f, 2.6f * u);
            p.Stroke();
            Circle(11.5f, 9.5f, 3f);
            p.fillColor = Doodle.WithAlpha(Color.white, 0.7f);
            p.Fill();
        }
    }

    /// <summary>
    /// The paper slip behind a cursor receipt: rounded top, a torn zig-zag bottom edge, an ink outline and a
    /// hard ink shadow. It fills its parent, behind the receipt's text.
    /// </summary>
    public sealed class ReceiptPaper : VisualElement
    {
        Color fill = Doodle.Paper;
        const float Shadow = 4f, Teeth = 8f, Radius = 14f, Stroke = 2.5f;

        public ReceiptPaper()
        {
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute;
            style.left = 0f;
            style.top = 0f;
            style.right = 0f;
            style.bottom = 0f;
            generateVisualContent += Draw;
        }

        public Color Fill
        {
            get => fill;
            set
            {
                if (fill == value) return;
                fill = value;
                MarkDirtyRepaint();
            }
        }

        void Draw(MeshGenerationContext mgc)
        {
            var r = contentRect;
            if (r.width < 30f || r.height < 30f) return;
            var p = mgc.painter2D;
            p.lineJoin = LineJoin.Round;
            Shape(p, r, Shadow);
            p.fillColor = Doodle.Ink;
            p.Fill();
            Shape(p, r, 0f);
            p.fillColor = fill;
            p.Fill();
            p.strokeColor = Doodle.Ink;
            p.lineWidth = Stroke;
            p.Stroke();
        }

        static void Shape(Painter2D p, Rect r, float dy)
        {
            float h = Stroke * 0.5f;
            float x0 = h, x1 = r.width - h, y0 = h + dy, y1 = r.height - Shadow - h + dy;
            int n = Mathf.Max(6, Mathf.RoundToInt((x1 - x0) / 15f));
            float step = (x1 - x0) / n;
            p.BeginPath();
            p.MoveTo(new Vector2(x0 + Radius, y0));
            p.LineTo(new Vector2(x1 - Radius, y0));
            p.ArcTo(new Vector2(x1, y0), new Vector2(x1, y0 + Radius), Radius);
            p.LineTo(new Vector2(x1, y1 - Teeth));
            for (int i = n - 1; i >= 0; i--)
            {
                p.LineTo(new Vector2(x0 + (i + 0.5f) * step, y1));
                p.LineTo(new Vector2(x0 + i * step, y1 - Teeth));
            }
            p.LineTo(new Vector2(x0, y0 + Radius));
            p.ArcTo(new Vector2(x0, y0), new Vector2(x0 + Radius, y0), Radius);
            p.ClosePath();
        }
    }

    /// <summary>
    /// The mascot: a sunny little train with a conductor's cap, big eyes, rosy cheeks and a waving hand
    /// (drawn on a 136×140 grid). <see cref="Wave"/> swings the arm, in degrees.
    /// </summary>
    public sealed class Mascot : VisualElement
    {
        float wave;

        public Mascot(float width = 150f, float height = 160f)
        {
            style.width = width;
            style.height = height;
            style.flexShrink = 0f;
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        public float Wave
        {
            get => wave;
            set
            {
                if (Mathf.Abs(value - wave) < 0.05f) return;
                wave = value;
                MarkDirtyRepaint();
            }
        }

        void Draw(MeshGenerationContext mgc)
        {
            var r = contentRect;
            float u = Mathf.Min(r.width / 136f, r.height / 140f);
            if (u <= 0.05f) return;
            float ox = (r.width - 136f * u) * 0.5f, oy = (r.height - 140f * u) * 0.5f;
            var p = mgc.painter2D;
            p.lineJoin = LineJoin.Round;
            p.lineCap = LineCap.Round;
            Vector2 P(float x, float y) => new Vector2(ox + x * u, oy + y * u);
            float wa = wave * Mathf.Deg2Rad, wc = Mathf.Cos(wa), ws = Mathf.Sin(wa);
            Vector2 Arm(float x, float y)
            {
                float dx = x - 108f, dy = y - 70f;
                return P(108f + dx * wc - dy * ws, 70f + dx * ws + dy * wc);
            }
            void Circle(Vector2 c, float rad)
            {
                p.BeginPath();
                p.Arc(c, rad * u, Angle.Degrees(0f), Angle.Degrees(360f));
                p.ClosePath();
            }
            void FillStroke(Color fill, float lw)
            {
                p.fillColor = fill;
                p.Fill();
                if (lw <= 0f) return;
                p.strokeColor = Doodle.Ink;
                p.lineWidth = lw * u;
                p.Stroke();
            }
            void Stroke(Color c, float lw)
            {
                p.strokeColor = c;
                p.lineWidth = lw * u;
                p.Stroke();
            }
            void Line(Color c, float lw, float x0, float y0, float x1, float y1)
            {
                p.BeginPath();
                p.MoveTo(P(x0, y0));
                p.LineTo(P(x1, y1));
                Stroke(c, lw);
            }
            void RoundRect(float x, float y, float w, float h, float rad)
            {
                p.BeginPath();
                p.MoveTo(P(x + rad, y));
                p.LineTo(P(x + w - rad, y));
                p.ArcTo(P(x + w, y), P(x + w, y + rad), rad * u);
                p.LineTo(P(x + w, y + h - rad));
                p.ArcTo(P(x + w, y + h), P(x + w - rad, y + h), rad * u);
                p.LineTo(P(x + rad, y + h));
                p.ArcTo(P(x, y + h), P(x, y + h - rad), rad * u);
                p.LineTo(P(x, y + rad));
                p.ArcTo(P(x, y), P(x + rad, y), rad * u);
                p.ClosePath();
            }
            void Ellipse(float cx, float cy, float rx, float ry)
            {
                const float k = 0.5523f;
                p.BeginPath();
                p.MoveTo(P(cx + rx, cy));
                p.BezierCurveTo(P(cx + rx, cy + ry * k), P(cx + rx * k, cy + ry), P(cx, cy + ry));
                p.BezierCurveTo(P(cx - rx * k, cy + ry), P(cx - rx, cy + ry * k), P(cx - rx, cy));
                p.BezierCurveTo(P(cx - rx, cy - ry * k), P(cx - rx * k, cy - ry), P(cx, cy - ry));
                p.BezierCurveTo(P(cx + rx * k, cy - ry), P(cx + rx, cy - ry * k), P(cx + rx, cy));
                p.ClosePath();
            }

            // The waving arm, behind the body.
            p.BeginPath();
            p.MoveTo(Arm(108f, 70f));
            p.BezierCurveTo(Arm(118f, 62f), Arm(122f, 52f), Arm(124f, 42f));
            Stroke(Doodle.Ink, 5f);
            Circle(Arm(125f, 36f), 9f);
            FillStroke(Doodle.Paper, 4f);
            // Body, the pink stripe and the window.
            RoundRect(12f, 24f, 96f, 96f, 30f);
            FillStroke(Doodle.Sun, 4.5f);
            Line(Doodle.Gum, 7f, 26f, 42f, 94f, 42f);
            RoundRect(26f, 50f, 68f, 38f, 14f);
            FillStroke(Doodle.SkyL, 4f);
            // Big eyes with a glint.
            foreach (float ex in new[] { 47f, 73f })
            {
                Circle(P(ex, 68f), 9f);
                FillStroke(Doodle.Paper, 3f);
                Circle(P(ex + 2f, 69f), 4.6f);
                FillStroke(Doodle.Ink, 0f);
                Circle(P(ex + 3.5f, 67f), 1.5f);
                FillStroke(Doodle.Paper, 0f);
            }
            // Rosy cheeks and a smile.
            Ellipse(33f, 98f, 6f, 3.5f);
            FillStroke(Doodle.WithAlpha(Doodle.Gum, 0.75f), 0f);
            Ellipse(87f, 98f, 6f, 3.5f);
            FillStroke(Doodle.WithAlpha(Doodle.Gum, 0.75f), 0f);
            p.BeginPath();
            p.MoveTo(P(50f, 95f));
            p.BezierCurveTo(P(55f, 103f), P(65f, 103f), P(70f, 95f));
            Stroke(Doodle.Ink, 4f);
            // Headlights and the wheels' bar.
            Circle(P(30f, 108f), 5f);
            FillStroke(Doodle.Paper, 3f);
            Circle(P(90f, 108f), 5f);
            FillStroke(Doodle.Paper, 3f);
            Line(Doodle.Ink, 6f, 34f, 128f, 86f, 128f);
            // The conductor's cap with its badge.
            p.BeginPath();
            p.MoveTo(P(34f, 28f));
            p.BezierCurveTo(P(36f, 10f), P(84f, 10f), P(86f, 28f));
            p.ClosePath();
            FillStroke(new Color32(57, 73, 184, 255), 4f);
            Line(Doodle.Ink, 5f, 28f, 29f, 92f, 29f);
            Circle(P(60f, 19f), 4.5f);
            FillStroke(Doodle.Sun, 2f);
        }
    }

    /// <summary>
    /// The wooden "PAUSED" sign hanging from a nail over the pause menu (a 400×160 doodle). It sways
    /// gently on its own.
    /// </summary>
    public sealed class PausedSign : VisualElement
    {
        public PausedSign()
        {
            style.width = 400f;
            style.height = 160f;
            pickingMode = PickingMode.Ignore;
            style.transformOrigin = new TransformOrigin(Length.Percent(50f), Length.Percent(4f));
            generateVisualContent += Draw;
            var text = UIK.Text(this, "PAUSED", "pause-sign-text");
            text.pickingMode = PickingMode.Ignore;
            UIK.Display(text);
            schedule.Execute(() => style.rotate = new Rotate(Mathf.Sin(Time.unscaledTime * 2.1f) * 2.5f)).Every(16);
        }

        void Draw(MeshGenerationContext mgc)
        {
            var r = contentRect;
            float u = Mathf.Min(r.width / 400f, r.height / 160f);
            if (u <= 0.05f) return;
            var p = mgc.painter2D;
            Vector2 P(float x, float y) => new Vector2(x * u, y * u);
            p.lineCap = LineCap.Round;
            p.lineJoin = LineJoin.Round;
            // Strings from the nail to the board's screws.
            p.strokeColor = Doodle.Ink;
            p.lineWidth = 3.5f * u;
            p.BeginPath();
            p.MoveTo(P(200f, 12f));
            p.LineTo(P(96f, 70f));
            p.MoveTo(P(200f, 12f));
            p.LineTo(P(304f, 70f));
            p.Stroke();
            void Circle(float cx, float cy, float rad, Color fill, float lw)
            {
                p.BeginPath();
                p.Arc(P(cx, cy), rad * u, Angle.Degrees(0f), Angle.Degrees(360f));
                p.ClosePath();
                p.fillColor = fill;
                p.Fill();
                p.strokeColor = Doodle.Ink;
                p.lineWidth = lw * u;
                p.Stroke();
            }
            Circle(200f, 10f, 8f, new Color32(180, 191, 211, 255), 3f);
            // The board, its planks and screws.
            const float x = 56f, y = 62f, w = 288f, h = 86f, rad = 14f;
            p.BeginPath();
            p.MoveTo(P(x + rad, y));
            p.LineTo(P(x + w - rad, y));
            p.ArcTo(P(x + w, y), P(x + w, y + rad), rad * u);
            p.LineTo(P(x + w, y + h - rad));
            p.ArcTo(P(x + w, y + h), P(x + w - rad, y + h), rad * u);
            p.LineTo(P(x + rad, y + h));
            p.ArcTo(P(x, y + h), P(x, y + h - rad), rad * u);
            p.LineTo(P(x, y + rad));
            p.ArcTo(P(x, y), P(x + rad, y), rad * u);
            p.ClosePath();
            p.fillColor = new Color32(233, 196, 138, 255);
            p.Fill();
            p.strokeColor = Doodle.Ink;
            p.lineWidth = 4f * u;
            p.Stroke();
            p.strokeColor = new Color32(201, 149, 90, 255);
            p.lineWidth = 3f * u;
            p.BeginPath();
            p.MoveTo(P(60f, 92f));
            p.LineTo(P(340f, 92f));
            p.MoveTo(P(60f, 120f));
            p.LineTo(P(340f, 120f));
            p.Stroke();
            Circle(96f, 76f, 5f, new Color32(139, 90, 60, 255), 2f);
            Circle(304f, 76f, 5f, new Color32(139, 90, 60, 255), 2f);
        }
    }

    /// <summary>The little pointed tail under a speech bubble (white with an ink edge), drawn over the bubble's border.</summary>
    public sealed class BubbleTail : VisualElement
    {
        public BubbleTail()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        void Draw(MeshGenerationContext mgc)
        {
            var r = contentRect;
            if (r.width < 4f || r.height < 4f) return;
            var p = mgc.painter2D;
            float w = r.width, h = r.height;
            p.BeginPath();
            p.MoveTo(new Vector2(1f, 0f));
            p.LineTo(new Vector2(w - 1f, 0f));
            p.LineTo(new Vector2(w * 0.3f, h - 1f));
            p.ClosePath();
            p.fillColor = Doodle.Paper;
            p.Fill();
            p.strokeColor = Doodle.Ink;
            p.lineWidth = 3f;
            p.lineCap = LineCap.Round;
            p.lineJoin = LineJoin.Round;
            p.BeginPath();
            p.MoveTo(new Vector2(1.5f, 2.5f));
            p.LineTo(new Vector2(w * 0.3f, h - 1.5f));
            p.LineTo(new Vector2(w - 1.5f, 2.5f));
            p.Stroke();
        }
    }

    /// <summary>Shared Painter2D helpers for the dialog doodles (drawn on a fixed design grid scaled by u).</summary>
    static class Pen
    {
        public static void Circle(Painter2D p, Vector2 c, float r)
        {
            p.BeginPath();
            p.Arc(c, r, Angle.Degrees(0f), Angle.Degrees(360f));
            p.ClosePath();
        }

        public static void Fill(Painter2D p, Color c)
        {
            p.fillColor = c;
            p.Fill();
        }

        public static void Stroke(Painter2D p, Color c, float w)
        {
            p.strokeColor = c;
            p.lineWidth = w;
            p.Stroke();
        }
    }

    /// <summary>Sunrise rays fanning up from below the bottom edge (the growth card's dawn band). Fills its parent.</summary>
    public sealed class SunRays : VisualElement
    {
        static readonly Color Back = new Color32(255, 184, 116, 255), Ray = new Color32(255, 154, 61, 255);

        public SunRays()
        {
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute;
            style.left = 0f;
            style.top = 0f;
            style.right = 0f;
            style.bottom = 0f;
            generateVisualContent += Draw;
        }

        void Draw(MeshGenerationContext mgc)
        {
            var r = contentRect;
            if (r.width < 4f || r.height < 4f) return;
            var p = mgc.painter2D;
            p.BeginPath();
            p.MoveTo(Vector2.zero);
            p.LineTo(new Vector2(r.width, 0f));
            p.LineTo(new Vector2(r.width, r.height));
            p.LineTo(new Vector2(0f, r.height));
            p.ClosePath();
            Pen.Fill(p, Back);
            Vector2 c = new Vector2(r.width * 0.5f, r.height * 1.3f);
            float far = r.width + r.height * 2f;
            for (float a = 180f; a < 360f; a += 14f)
            {
                float a0 = a * Mathf.Deg2Rad, a1 = (a + 7f) * Mathf.Deg2Rad;
                p.BeginPath();
                p.MoveTo(c);
                p.LineTo(c + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * far);
                p.LineTo(c + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * far);
                p.ClosePath();
                Pen.Fill(p, Ray);
            }
        }
    }

    /// <summary>The demolish dialog's doodle: a worried cartoon bomb whose fuse sparkles.</summary>
    public sealed class BombDoodle : VisualElement
    {
        float spark;

        public BombDoodle()
        {
            style.width = 76f;
            style.height = 76f;
            style.flexShrink = 0f;
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
            schedule.Execute(() =>
            {
                spark = Mathf.Floor(Time.unscaledTime * 4f) % 2f;
                MarkDirtyRepaint();
            }).Every(120);
        }

        void Draw(MeshGenerationContext mgc)
        {
            float u = Mathf.Min(contentRect.width, contentRect.height) / 76f;
            if (u <= 0.05f) return;
            var p = mgc.painter2D;
            Vector2 P(float x, float y) => new Vector2(x * u, y * u);
            p.lineCap = LineCap.Round;
            p.lineJoin = LineJoin.Round;
            // The fuse and its spark.
            p.BeginPath();
            p.MoveTo(P(44f, 26f));
            p.BezierCurveTo(P(48f, 16f), P(56f, 12f), P(62f, 14f));
            Pen.Stroke(p, Doodle.Ink, 4f * u);
            float k = spark > 0.5f ? 1.15f : 0.85f, rot = spark > 0.5f ? 20f : 0f;
            Vector2 sc = new Vector2(66f, 13f);
            Vector2 S(float x, float y)
            {
                Vector2 d = (new Vector2(x, y) - sc) * k;
                float a = rot * Mathf.Deg2Rad;
                return P(sc.x + d.x * Mathf.Cos(a) - d.y * Mathf.Sin(a), sc.y + d.x * Mathf.Sin(a) + d.y * Mathf.Cos(a));
            }
            float[] star = { 66, 4, 68.5f, 10, 75, 11, 70, 15.5f, 71.5f, 22, 66, 18.5f, 60.5f, 22, 62, 15.5f, 57, 11, 63.5f, 10 };
            p.BeginPath();
            for (int i = 0; i < star.Length; i += 2)
            {
                if (i == 0) p.MoveTo(S(star[i], star[i + 1]));
                else p.LineTo(S(star[i], star[i + 1]));
            }
            p.ClosePath();
            Pen.Fill(p, Doodle.Sun);
            Pen.Stroke(p, Doodle.Ink, 2f * u);
            // The body and its cap.
            Pen.Circle(p, P(32f, 46f), 26f * u);
            Pen.Fill(p, Doodle.Ink);
            Vector2 cc = new Vector2(43f, 23.5f);
            float ca = 35f * Mathf.Deg2Rad;
            Vector2 R(float x, float y)
            {
                Vector2 d = new Vector2(x, y) - cc;
                return P(cc.x + d.x * Mathf.Cos(ca) - d.y * Mathf.Sin(ca), cc.y + d.x * Mathf.Sin(ca) + d.y * Mathf.Cos(ca));
            }
            p.BeginPath();
            p.MoveTo(R(36f, 18f));
            p.LineTo(R(50f, 18f));
            p.LineTo(R(50f, 29f));
            p.LineTo(R(36f, 29f));
            p.ClosePath();
            Pen.Fill(p, Doodle.Ink2);
            Pen.Stroke(p, Doodle.Ink, 2.5f * u);
            // Shine, worried eyes and a wobbly mouth.
            p.BeginPath();
            p.MoveTo(P(18f, 36f));
            p.BezierCurveTo(P(20f, 30f), P(25f, 26f), P(30f, 25f));
            Pen.Stroke(p, Doodle.WithAlpha(Color.white, 0.7f), 4f * u);
            Pen.Circle(p, P(24f, 48f), 3f * u);
            Pen.Fill(p, Color.white);
            Pen.Circle(p, P(40f, 48f), 3f * u);
            Pen.Fill(p, Color.white);
            p.BeginPath();
            p.MoveTo(P(25f, 58f));
            p.BezierCurveTo(P(29f, 55f), P(35f, 55f), P(39f, 58f));
            Pen.Stroke(p, Color.white, 3f * u);
        }
    }

    /// <summary>The leave/quit dialog's doodle: an open door with an arrow heading out.</summary>
    public sealed class DoorDoodle : VisualElement
    {
        public DoorDoodle()
        {
            style.width = 58f;
            style.height = 64f;
            style.flexShrink = 0f;
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        void Draw(MeshGenerationContext mgc)
        {
            float u = Mathf.Min(contentRect.width / 58f, contentRect.height / 64f);
            if (u <= 0.05f) return;
            var p = mgc.painter2D;
            Vector2 P(float x, float y) => new Vector2(x * u, y * u);
            p.lineCap = LineCap.Round;
            p.lineJoin = LineJoin.Round;
            p.BeginPath();
            p.MoveTo(P(12f, 4f));
            p.LineTo(P(40f, 4f));
            p.ArcTo(P(44f, 4f), P(44f, 8f), 4f * u);
            p.LineTo(P(44f, 56f));
            p.ArcTo(P(44f, 60f), P(40f, 60f), 4f * u);
            p.LineTo(P(12f, 60f));
            p.ArcTo(P(8f, 60f), P(8f, 56f), 4f * u);
            p.LineTo(P(8f, 8f));
            p.ArcTo(P(8f, 4f), P(12f, 4f), 4f * u);
            p.ClosePath();
            Pen.Fill(p, Color.white);
            Pen.Stroke(p, Doodle.Ink, 3.5f * u);
            p.BeginPath();
            p.MoveTo(P(12f, 8f));
            p.LineTo(P(36f, 12f));
            p.LineTo(P(36f, 58f));
            p.LineTo(P(12f, 58f));
            p.ClosePath();
            Pen.Fill(p, Doodle.Tang);
            Pen.Stroke(p, Doodle.Ink, 3f * u);
            Pen.Circle(p, P(31f, 34f), 2.5f * u);
            Pen.Fill(p, Doodle.Ink);
            p.BeginPath();
            p.MoveTo(P(44f, 32f));
            p.LineTo(P(56f, 32f));
            p.MoveTo(P(50f, 26f));
            p.LineTo(P(56f, 32f));
            p.LineTo(P(50f, 38f));
            Pen.Stroke(p, Doodle.Ink, 3.5f * u);
        }
    }

    /// <summary>
    /// The snapshot inside a save polaroid: a little doodled city (sand, water, a few candy lines with
    /// stations), the same for the same save every time. Empty slots show a dashed "+".
    /// </summary>
    public sealed class PolaroidPhoto : VisualElement
    {
        readonly int seed;
        readonly bool filled;
        static readonly Color[] LineColors =
        {
            new Color32(255, 81, 70, 255), new Color32(61, 134, 245, 255), new Color32(53, 184, 100, 255),
            new Color32(255, 143, 31, 255), new Color32(154, 91, 240, 255), new Color32(255, 201, 41, 255)
        };

        public PolaroidPhoto(int seed, bool filled)
        {
            this.seed = seed;
            this.filled = filled;
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        void Draw(MeshGenerationContext mgc)
        {
            var r = contentRect;
            float w = r.width, h = r.height;
            if (w < 20f || h < 20f) return;
            var p = mgc.painter2D;
            p.lineCap = LineCap.Round;
            p.lineJoin = LineJoin.Round;
            if (!filled)
            {
                p.BeginPath();
                p.MoveTo(new Vector2(w * 0.5f - 14f, h * 0.5f));
                p.LineTo(new Vector2(w * 0.5f + 14f, h * 0.5f));
                p.MoveTo(new Vector2(w * 0.5f, h * 0.5f - 14f));
                p.LineTo(new Vector2(w * 0.5f, h * 0.5f + 14f));
                Pen.Stroke(p, Doodle.WithAlpha(Doodle.Ink, 0.3f), 4f);
                return;
            }
            var rng = new System.Random(seed);
            float F() => (float)rng.NextDouble();
            // Water: a river or a bit of sea.
            p.BeginPath();
            if (F() < 0.5f)
            {
                float x0 = w * (0.55f + 0.3f * F());
                p.MoveTo(new Vector2(x0, 0f));
                p.BezierCurveTo(new Vector2(x0 - 30f, h * 0.3f), new Vector2(x0 + 25f, h * 0.6f), new Vector2(x0 - 10f, h));
                Pen.Stroke(p, new Color32(169, 222, 136, 255), 16f);
                p.BeginPath();
                p.MoveTo(new Vector2(x0, 0f));
                p.BezierCurveTo(new Vector2(x0 - 30f, h * 0.3f), new Vector2(x0 + 25f, h * 0.6f), new Vector2(x0 - 10f, h));
                Pen.Stroke(p, new Color32(109, 203, 245, 255), 8f);
            }
            else
            {
                float y0 = h * (0.72f + 0.12f * F());
                p.MoveTo(new Vector2(0f, h));
                p.LineTo(new Vector2(0f, y0));
                p.BezierCurveTo(new Vector2(w * 0.3f, y0 - 14f), new Vector2(w * 0.6f, y0 + 12f), new Vector2(w, y0 - 6f));
                p.LineTo(new Vector2(w, h));
                p.ClosePath();
                Pen.Fill(p, new Color32(109, 203, 245, 255));
            }
            // Two to four lines through the middle, on ink casings.
            int lines = 2 + rng.Next(3);
            Vector2 hub = new Vector2(w * (0.4f + 0.2f * F()), h * (0.4f + 0.2f * F()));
            var paths = new List<Vector2[]>();
            for (int i = 0; i < lines; i++)
            {
                float a = (i / (float)lines + F() * 0.2f) * Mathf.PI;
                Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Vector2 a0 = hub - dir * (w * 0.45f), a1 = hub + dir * (w * 0.45f);
                Vector2 mid = hub + new Vector2(F() - 0.5f, F() - 0.5f) * 12f;
                paths.Add(new[] { a0, mid, a1 });
            }
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < paths.Count; i++)
                {
                    var pts = paths[i];
                    p.BeginPath();
                    p.MoveTo(pts[0]);
                    p.LineTo(pts[1]);
                    p.LineTo(pts[2]);
                    if (pass == 0) Pen.Stroke(p, Doodle.Ink, 6f);
                    else Pen.Stroke(p, LineColors[((i + seed) & 0x7FFFFFFF) % LineColors.Length], 3.4f);
                }
            }
            foreach (var pts in paths)
            {
                for (int k = 0; k <= 4; k++)
                {
                    float t = k / 4f;
                    Vector2 q = t < 0.5f ? Vector2.Lerp(pts[0], pts[1], t * 2f) : Vector2.Lerp(pts[1], pts[2], (t - 0.5f) * 2f);
                    if (q.x < 4f || q.y < 4f || q.x > w - 4f || q.y > h - 4f) continue;
                    Pen.Circle(p, q, 3.4f);
                    Pen.Fill(p, Color.white);
                    Pen.Stroke(p, Doodle.Ink, 1.8f);
                }
            }
        }
    }

    /// <summary>A hand-drawn curly arrow (a 70×46 doodle) pointing up and to the left, or right when flipped.</summary>
    public sealed class CurlyArrow : VisualElement
    {
        readonly bool flip;

        public CurlyArrow(float width = 70f, float height = 46f, bool flip = false)
        {
            this.flip = flip;
            style.width = width;
            style.height = height;
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        void Draw(MeshGenerationContext mgc)
        {
            var r = contentRect;
            if (r.width < 8f || r.height < 8f) return;
            float sx = r.width / 70f, sy = r.height / 46f;
            Vector2 P(float x, float y) => new Vector2((flip ? 70f - x : x) * sx, y * sy);
            var p = mgc.painter2D;
            p.strokeColor = Doodle.Ink;
            p.lineWidth = Mathf.Max(2f, 3f * Mathf.Min(sx, sy));
            p.lineCap = LineCap.Round;
            p.lineJoin = LineJoin.Round;
            p.BeginPath();
            p.MoveTo(P(66f, 36f));
            p.BezierCurveTo(P(48f, 44f), P(22f, 40f), P(8f, 14f));
            p.MoveTo(P(8f, 26f));
            p.LineTo(P(8f, 14f));
            p.LineTo(P(19f, 18f));
            p.Stroke();
        }
    }

    /// <summary>A little burst of confetti (good news, a city growing): candy bits with ink outlines.</summary>
    public sealed class Confetti : VisualElement
    {
        static readonly Color[] Bits = { Doodle.Gum, Doodle.Sun, Doodle.Mint, Doodle.Sky, Doodle.Grape, Doodle.Tang, Doodle.Tomato };
        readonly int seed, count;

        public Confetti(float size, int seed = 7, int count = 7)
        {
            this.seed = seed;
            this.count = count;
            style.width = size;
            style.height = size;
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        void Draw(MeshGenerationContext mgc)
        {
            var r = contentRect;
            if (r.width < 8f || r.height < 8f) return;
            var p = mgc.painter2D;
            var rng = new System.Random(seed);
            float s = Mathf.Min(r.width, r.height);
            p.strokeColor = Doodle.Ink;
            p.lineWidth = Mathf.Clamp(s * 0.045f, 1.2f, 2.5f);
            p.lineJoin = LineJoin.Round;
            for (int i = 0; i < count; i++)
            {
                float a = i / (float)count * Mathf.PI * 2f + (float)rng.NextDouble() * 0.6f;
                float d = 0.2f + 0.26f * (float)rng.NextDouble();
                var c = new Vector2(r.width * (0.5f + Mathf.Cos(a) * d), r.height * (0.5f + Mathf.Sin(a) * d));
                p.fillColor = Bits[(i + seed) % Bits.Length];
                p.BeginPath();
                if (i % 3 == 0)
                {
                    float rad = s * 0.065f;
                    p.MoveTo(c + new Vector2(rad, 0f));
                    p.Arc(c, rad, Angle.Degrees(0f), Angle.Degrees(360f));
                }
                else
                {
                    float rot = (float)rng.NextDouble() * Mathf.PI;
                    var ux = new Vector2(Mathf.Cos(rot), Mathf.Sin(rot)) * (s * 0.085f);
                    var uy = new Vector2(-Mathf.Sin(rot), Mathf.Cos(rot)) * (s * 0.04f);
                    p.MoveTo(c - ux - uy);
                    p.LineTo(c + ux - uy);
                    p.LineTo(c + ux + uy);
                    p.LineTo(c - ux + uy);
                }
                p.ClosePath();
                p.Fill();
                p.Stroke();
            }
        }
    }

    /// <summary>A dashed circle: the "almost there" placeholder for a line that's still being drawn.</summary>
    public sealed class DashedDisc : VisualElement
    {
        Color fill = Doodle.Paper;

        public DashedDisc(float size)
        {
            style.width = size;
            style.height = size;
            style.flexShrink = 0f;
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        public Color Fill
        {
            get => fill;
            set
            {
                if (fill == value) return;
                fill = value;
                MarkDirtyRepaint();
            }
        }

        void Draw(MeshGenerationContext mgc)
        {
            var r = contentRect;
            float s = Mathf.Min(r.width, r.height);
            if (s < 8f) return;
            var p = mgc.painter2D;
            var c = r.center;
            float rad = s * 0.5f - 2f;
            p.fillColor = Doodle.WithAlpha(fill, 0.5f);
            p.BeginPath();
            p.Arc(c, rad, Angle.Degrees(0f), Angle.Degrees(360f));
            p.ClosePath();
            p.Fill();
            p.strokeColor = Doodle.Ink;
            p.lineWidth = 2.6f;
            p.lineCap = LineCap.Round;
            const int dashes = 12;
            float step = 360f / dashes;
            for (int i = 0; i < dashes; i++)
            {
                p.BeginPath();
                p.Arc(c, rad, Angle.Degrees(i * step), Angle.Degrees(i * step + step * 0.55f));
                p.Stroke();
            }
        }
    }

    /// <summary>The dotted leader between an item and its price on a receipt.</summary>
    public sealed class DottedLeader : VisualElement
    {
        static readonly Color Dots = new Color32(201, 185, 218, 255);

        public DottedLeader()
        {
            pickingMode = PickingMode.Ignore;
            AddToClassList("hint-leader");
            generateVisualContent += Draw;
        }

        void Draw(MeshGenerationContext mgc)
        {
            var r = contentRect;
            if (r.width < 5f || r.height < 3f) return;
            var p = mgc.painter2D;
            p.fillColor = Dots;
            float y = r.height - 1.6f;
            p.BeginPath();
            for (float x = 1.6f; x <= r.width - 1.6f; x += 6f)
            {
                p.MoveTo(new Vector2(x + 1.5f, y));
                p.Arc(new Vector2(x, y), 1.5f, Angle.Degrees(0f), Angle.Degrees(360f));
                p.ClosePath();
            }
            p.Fill();
        }
    }

    /// <summary>Clips a convex polygon to a vertical slab (used for the striped rush-hour bands).</summary>
    static class Clip
    {
        public static List<Vector2> ToSlab(List<Vector2> poly, float xMin, float xMax)
        {
            return Side(Side(poly, xMin, true), xMax, false);
        }

        static List<Vector2> Side(List<Vector2> poly, float x, bool keepRight)
        {
            var o = new List<Vector2>(poly.Count + 2);
            for (int i = 0; i < poly.Count; i++)
            {
                Vector2 a = poly[i], b = poly[(i + 1) % poly.Count];
                bool ina = keepRight ? a.x >= x : a.x <= x;
                bool inb = keepRight ? b.x >= x : b.x <= x;
                if (ina) o.Add(a);
                if (ina != inb)
                {
                    float t = (x - a.x) / (b.x - a.x);
                    o.Add(new Vector2(x, a.y + (b.y - a.y) * t));
                }
            }
            return o;
        }
    }
}
