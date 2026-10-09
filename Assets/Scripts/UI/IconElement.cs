using UnityEngine;
using UnityEngine.UIElements;

namespace Metro.UI
{
    public enum IconKind
    {
        Pause, Play, Fast, Faster, Select, Line, Station, Depot, Demolish, Population, Jobs, Coverage,
        Overview, Finance, Stats, City, Menu, Close, Plus, Minus, Train, People, Smile, Warning, Info,
        Check, Chevron, Insert, Feed, Dice, Wagon, Lock, Star, Hall, Pin, Trophy, Ballot, Calendar, Cloud, Volume,
        Coin, Clock, Gear, Platform, Exit, Elevator, Concourse, Sunrise, Moon, Pencil, Floppy, Folder, Door
    }

    /// <summary>
    /// "Doodle" vector icons drawn with Painter2D so the game needs no image assets: chunky ink outlines
    /// over candy fills, on a 24×24 design grid. <see cref="Color"/> is the icon's main fill (or the ink of
    /// plain glyphs such as play, close and plus); left unset, each icon uses its own colour.
    /// </summary>
    public sealed class IconElement : VisualElement
    {
        IconKind kind;
        Color? color;
        float mood = 1f;
        readonly float size;

        public IconElement(IconKind kind, float size = 22f)
        {
            this.kind = kind;
            this.size = size;
            style.width = size;
            style.height = size;
            style.flexShrink = 0f;
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        public IconKind Kind
        {
            get => kind;
            set
            {
                if (kind == value) return;
                kind = value;
                MarkDirtyRepaint();
            }
        }

        /// <summary>Main fill colour (glyph colour for plain glyphs).</summary>
        public Color Color
        {
            get => color ?? DefaultColor(kind);
            set
            {
                if (color.HasValue && color.Value == value) return;
                color = value;
                MarkDirtyRepaint();
            }
        }

        /// <summary>Back to the icon's own colour.</summary>
        public void ResetColor()
        {
            if (!color.HasValue) return;
            color = null;
            MarkDirtyRepaint();
        }

        /// <summary>Smile only: -1 cross … 0 meh … +1 grin.</summary>
        public float Mood
        {
            get => mood;
            set
            {
                value = Mathf.Clamp(value, -1f, 1f);
                if (Mathf.Abs(value - mood) < 0.02f) return;
                mood = value;
                MarkDirtyRepaint();
            }
        }

        static Color DefaultColor(IconKind k)
        {
            switch (k)
            {
                case IconKind.Select: return Doodle.Sky;
                case IconKind.Line: return Doodle.LineRed;
                case IconKind.Depot: return Doodle.Tang;
                case IconKind.Demolish: return Doodle.Sun;
                case IconKind.Population: return Doodle.Grape;
                case IconKind.Jobs: return Doodle.Brown;
                case IconKind.Overview: return Doodle.Ink2;
                case IconKind.Finance: return Doodle.Pig;
                case IconKind.Stats: return Doodle.Gum;
                case IconKind.City: return Doodle.Grape;
                case IconKind.Menu: return Doodle.Bun;
                case IconKind.Train: return Doodle.LineRed;
                case IconKind.People: return Doodle.Gum;
                case IconKind.Smile: return Doodle.Sun;
                case IconKind.Warning: return Doodle.Sun;
                case IconKind.Info: return Doodle.Sky;
                case IconKind.Check: return Doodle.Mint;
                case IconKind.Wagon: return Doodle.Sky;
                case IconKind.Lock: return Doodle.Sun;
                case IconKind.Star: return Doodle.Sun;
                case IconKind.Hall: return Doodle.Grape;
                case IconKind.Pin: return Doodle.Tomato;
                case IconKind.Trophy: return Doodle.Coin;
                case IconKind.Ballot: return Doodle.Sky;
                case IconKind.Calendar: return Doodle.Tomato;
                case IconKind.Volume: return Doodle.Sun;
                case IconKind.Coin: return Doodle.Coin;
                case IconKind.Clock: return Doodle.Tomato;
                case IconKind.Gear: return Doodle.Sky;
                case IconKind.Platform: return Doodle.Sky;
                case IconKind.Exit: return Doodle.Mint;
                case IconKind.Elevator: return Doodle.Sky;
                case IconKind.Concourse: return Doodle.Grape;
                case IconKind.Sunrise: return Doodle.Sun;
                case IconKind.Moon: return Doodle.SunL;
                case IconKind.Pencil: return Doodle.Sun;
                case IconKind.Floppy: return Doodle.Sky;
                case IconKind.Folder: return Doodle.Sun;
                case IconKind.Door: return Doodle.Tang;
                case IconKind.Station:
                case IconKind.Insert:
                case IconKind.Feed:
                case IconKind.Dice:
                case IconKind.Cloud:
                    return Doodle.Paper;
                default: return Doodle.Ink; // plain glyphs: pause, play, close, plus, chevron, footprints...
            }
        }

        void Draw(MeshGenerationContext mgc)
        {
            var p = mgc.painter2D;
            float u = size / 24f;
            Color main = Color;
            Color ink = Doodle.Ink;
            float vb = 1f;                          // grid scale for icons drawn on a 28/36/40 grid
            float rot = 0f, rcx = 12f, rcy = 12f;   // optional rotation (degrees) around (rcx, rcy)
            p.lineCap = LineCap.Round;
            p.lineJoin = LineJoin.Round;

            Vector2 P(float x, float y)
            {
                if (rot != 0f)
                {
                    float a = rot * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
                    float dx = x - rcx, dy = y - rcy;
                    x = rcx + dx * c - dy * s;
                    y = rcy + dx * s + dy * c;
                }
                return new Vector2(x * vb * u, y * vb * u);
            }

            float R(float r) => r * vb * u;

            void Fill(Color c)
            {
                p.fillColor = c;
                p.Fill();
            }

            void Stroke(Color c, float w)
            {
                p.strokeColor = c;
                p.lineWidth = Mathf.Max(0.75f, w * vb * u);
                p.Stroke();
            }

            void FillStroke(Color c, float w = 2f)
            {
                Fill(c);
                Stroke(ink, w);
            }

            void Circle(float cx, float cy, float r)
            {
                p.BeginPath();
                p.Arc(P(cx, cy), R(r), Angle.Degrees(0f), Angle.Degrees(360f));
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

            void Box(float x, float y, float w, float h, float r)
            {
                r = Mathf.Min(r, Mathf.Min(w, h) * 0.5f);
                p.BeginPath();
                if (r < 0.05f)
                {
                    p.MoveTo(P(x, y));
                    p.LineTo(P(x + w, y));
                    p.LineTo(P(x + w, y + h));
                    p.LineTo(P(x, y + h));
                    p.ClosePath();
                    return;
                }
                p.MoveTo(P(x + r, y));
                p.LineTo(P(x + w - r, y));
                p.ArcTo(P(x + w, y), P(x + w, y + r), R(r));
                p.LineTo(P(x + w, y + h - r));
                p.ArcTo(P(x + w, y + h), P(x + w - r, y + h), R(r));
                p.LineTo(P(x + r, y + h));
                p.ArcTo(P(x, y + h), P(x, y + h - r), R(r));
                p.LineTo(P(x, y + r));
                p.ArcTo(P(x, y), P(x + r, y), R(r));
                p.ClosePath();
            }

            void Poly(bool close, params float[] xy)
            {
                p.BeginPath();
                p.MoveTo(P(xy[0], xy[1]));
                for (int i = 2; i + 1 < xy.Length; i += 2) p.LineTo(P(xy[i], xy[i + 1]));
                if (close) p.ClosePath();
            }

            void Line(Color c, float w, params float[] xy)
            {
                Poly(false, xy);
                Stroke(c, w);
            }

            void Dot(float cx, float cy, float r, Color c)
            {
                Circle(cx, cy, r);
                Fill(c);
            }

            switch (kind)
            {
                // ---------------------------------------------------------- plain glyphs (speed, steppers, close)
                case IconKind.Pause:
                    Box(5, 4, 5, 16, 2.2f);
                    Fill(main);
                    Box(14, 4, 5, 16, 2.2f);
                    Fill(main);
                    break;
                case IconKind.Play:
                    p.BeginPath();
                    p.MoveTo(P(7, 4.5f));
                    p.BezierCurveTo(P(7, 3.6f), P(8, 3.1f), P(8.7f, 3.6f));
                    p.LineTo(P(19.6f, 11));
                    p.BezierCurveTo(P(20.3f, 11.5f), P(20.3f, 12.5f), P(19.6f, 13));
                    p.LineTo(P(8.7f, 20.4f));
                    p.BezierCurveTo(P(8, 20.9f), P(7, 20.4f), P(7, 19.5f));
                    p.ClosePath();
                    Fill(main);
                    break;
                case IconKind.Fast:
                    for (int i = 0; i < 2; i++)
                    {
                        float o = i * 9.5f;
                        p.BeginPath();
                        p.MoveTo(P(2.5f + o, 6));
                        p.BezierCurveTo(P(2.5f + o, 5.2f), P(3.4f + o, 4.8f), P(4 + o, 5.3f));
                        p.LineTo(P(11 + o, 11.2f));
                        p.BezierCurveTo(P(11.5f + o, 11.6f), P(11.5f + o, 12.4f), P(11 + o, 12.8f));
                        p.LineTo(P(4 + o, 18.7f));
                        p.BezierCurveTo(P(3.4f + o, 19.2f), P(2.5f + o, 18.8f), P(2.5f + o, 18));
                        p.ClosePath();
                        Fill(main);
                    }
                    break;
                case IconKind.Faster:
                    for (int i = 0; i < 3; i++)
                    {
                        float o = i * 7.5f;
                        Poly(true, 1 + o, 7, 7.5f + o, 12, 1 + o, 17);
                        Fill(main);
                        Stroke(main, 1.6f);
                    }
                    break;
                case IconKind.Close:
                    Line(main, 3.2f, 6.5f, 6.5f, 17.5f, 17.5f);
                    Line(main, 3.2f, 17.5f, 6.5f, 6.5f, 17.5f);
                    break;
                case IconKind.Plus:
                    Line(main, 3.4f, 12, 5, 12, 19);
                    Line(main, 3.4f, 5, 12, 19, 12);
                    break;
                case IconKind.Minus:
                    Line(main, 3.4f, 5, 12, 19, 12);
                    break;
                case IconKind.Chevron:
                    Line(main, 3f, 7, 9.5f, 12, 14.5f, 17, 9.5f);
                    break;
                case IconKind.Check:
                    Poly(false, 4.5f, 12.5f, 9.5f, 17.5f, 19.5f, 6.5f);
                    Stroke(ink, 5.4f);
                    Stroke(main, 2.6f);
                    break;

                // ---------------------------------------------------------- tools
                case IconKind.Select: // magnifying glass
                    Line(ink, 4f, 15, 15, 20.5f, 20.5f);
                    Circle(10, 10, 6.5f);
                    FillStroke(Doodle.Paper, 2.6f);
                    p.BeginPath();
                    p.MoveTo(P(7, 8.5f));
                    p.BezierCurveTo(P(7.5f, 7), P(8.6f, 6.2f), P(10, 6.1f));
                    Stroke(main, 1.8f);
                    break;
                case IconKind.Line: // a wiggly line between two stations
                    p.BeginPath();
                    p.MoveTo(P(3.5f, 17));
                    p.BezierCurveTo(P(7, 9), P(11, 19), P(14, 11));
                    p.BezierCurveTo(P(17, 3), P(19, 6), P(20.5f, 7));
                    Stroke(ink, 2.6f);
                    Circle(3.6f, 17, 2.8f);
                    FillStroke(main, 1.6f);
                    Circle(20.4f, 7, 2.8f);
                    FillStroke(Doodle.LineYellow, 1.6f);
                    break;
                case IconKind.Insert:
                    Line(ink, 2.6f, 1.5f, 14, 5, 14);
                    Line(ink, 2.6f, 17, 14, 22.5f, 14);
                    Circle(11, 14, 6);
                    FillStroke(main, 2.6f);
                    Line(ink, 2.6f, 18, 2.5f, 18, 9);
                    Line(ink, 2.6f, 14.8f, 5.8f, 21.2f, 5.8f);
                    break;
                case IconKind.Station:
                    Circle(12, 12, 6.5f);
                    FillStroke(main, 2.4f);
                    break;
                case IconKind.Depot: // a little shed with a big roof
                    Box(5, 10, 14, 10, 0);
                    FillStroke(Doodle.Paper, 2.2f);
                    Poly(false, 2.5f, 11.5f, 12, 4, 21.5f, 11.5f);
                    Stroke(main, 4f);
                    Stroke(ink, 1.6f);
                    Box(8.5f, 13.5f, 7, 6.5f, 0);
                    FillStroke(Doodle.Sun, 1.8f);
                    break;
                case IconKind.Demolish: // a cartoon bomb
                    p.BeginPath();
                    p.MoveTo(P(14.5f, 8.5f));
                    p.BezierCurveTo(P(15.5f, 6), P(17.5f, 5), P(19, 5.5f));
                    Stroke(ink, 2f);
                    Poly(true, 20, 2, 20.8f, 3.8f, 22.6f, 4.2f, 21.2f, 5.5f, 21.5f, 7.3f, 20, 6.4f, 18.5f, 7.3f, 18.8f, 5.5f, 17.4f, 4.2f, 19.2f, 3.8f);
                    FillStroke(main, 1.1f);
                    Circle(10.5f, 14, 7);
                    Fill(ink);
                    rot = 35f;
                    rcx = 13.75f;
                    rcy = 8f;
                    Box(11.5f, 6.2f, 4.5f, 3.6f, 1f);
                    FillStroke(Doodle.Ink2, 1.4f);
                    rot = 0f;
                    p.BeginPath();
                    p.MoveTo(P(7, 11.5f));
                    p.BezierCurveTo(P(7.6f, 10.2f), P(8.6f, 9.4f), P(9.8f, 9.1f));
                    Stroke(Doodle.Paper, 1.8f);
                    break;

                // ---------------------------------------------------------- overlays
                case IconKind.Population: // two neighbours
                    p.BeginPath();
                    p.MoveTo(P(2, 21));
                    p.BezierCurveTo(P(2, 16), P(4.8f, 14), P(8, 14));
                    p.BezierCurveTo(P(11.2f, 14), P(14, 16), P(14, 21));
                    p.ClosePath();
                    FillStroke(main, 1.8f);
                    p.BeginPath();
                    p.MoveTo(P(11, 21));
                    p.BezierCurveTo(P(11, 16.5f), P(13.5f, 15), P(16.2f, 15));
                    p.BezierCurveTo(P(19, 15), P(22, 16.5f), P(22, 21));
                    p.ClosePath();
                    FillStroke(Doodle.Mint, 1.8f);
                    Circle(8, 9, 4);
                    FillStroke(Doodle.Skin, 1.8f);
                    Circle(16.4f, 10, 3.4f);
                    FillStroke(Doodle.Skin, 1.8f);
                    break;
                case IconKind.Jobs: // briefcase
                    p.BeginPath();
                    p.MoveTo(P(9, 8));
                    p.LineTo(P(9, 6));
                    p.BezierCurveTo(P(9, 5), P(9.8f, 4.4f), P(10.8f, 4.4f));
                    p.LineTo(P(13.2f, 4.4f));
                    p.BezierCurveTo(P(14.2f, 4.4f), P(15, 5), P(15, 6));
                    p.LineTo(P(15, 8));
                    Stroke(ink, 2.2f);
                    Box(3, 8, 18, 12, 3);
                    FillStroke(main, 2.2f);
                    Line(ink, 1.8f, 3, 13, 21, 13);
                    Box(10.3f, 11.5f, 3.4f, 3, 0.8f);
                    FillStroke(Doodle.Sun, 1.4f);
                    break;
                case IconKind.Coverage: // footprints: how far people walk
                    rot = -10f;
                    rcx = 8f;
                    rcy = 15.5f;
                    Ellipse(8, 15.5f, 2.8f, 4.2f);
                    Fill(main);
                    rot = 10f;
                    rcx = 16f;
                    rcy = 9.5f;
                    Ellipse(16, 9.5f, 2.8f, 4.2f);
                    Fill(main);
                    rot = 0f;
                    Dot(6.3f, 9.6f, 1.1f, main);
                    Dot(8.3f, 9, 1.1f, main);
                    Dot(10.1f, 9.8f, 1, main);
                    Dot(14.4f, 3.7f, 1.1f, main);
                    Dot(16.4f, 3.2f, 1.1f, main);
                    Dot(18.2f, 4, 1, main);
                    break;
                case IconKind.Overview: // binoculars
                    Box(4.5f, 4, 5, 7, 1.5f);
                    FillStroke(main, 2f);
                    Box(14.5f, 4, 5, 7, 1.5f);
                    FillStroke(main, 2f);
                    Box(9, 11, 6, 3, 0);
                    FillStroke(main, 2f);
                    Circle(7, 15.5f, 5);
                    FillStroke(Doodle.Paper, 2f);
                    Circle(17, 15.5f, 5);
                    FillStroke(Doodle.Paper, 2f);
                    Dot(7, 15.5f, 2, Doodle.Sky);
                    Dot(17, 15.5f, 2, Doodle.Sky);
                    break;

                // ---------------------------------------------------------- windows
                case IconKind.Finance: // piggy bank
                    Line(ink, 2.2f, 6, 17, 6, 20.5f);
                    Line(ink, 2.2f, 9, 18, 9, 20.5f);
                    Line(ink, 2.2f, 14, 18, 14, 20.5f);
                    Line(ink, 2.2f, 17, 17, 17, 20.5f);
                    Ellipse(11.5f, 13, 8.5f, 6.3f);
                    FillStroke(main, 1.8f);
                    Poly(true, 6.5f, 8, 7.2f, 4.6f, 10, 7);
                    Fill(main);
                    Poly(false, 6.5f, 8, 7.2f, 4.6f, 10, 7);
                    Stroke(ink, 1.8f);
                    Ellipse(20.2f, 13, 2.2f, 2.8f);
                    FillStroke(main, 1.8f);
                    Box(9, 7.6f, 5, 1.6f, 0.8f);
                    Fill(ink);
                    Dot(8, 11.5f, 1.1f, ink);
                    Dot(20, 12.2f, 0.6f, ink);
                    Dot(20, 13.8f, 0.6f, ink);
                    break;
                case IconKind.Stats: // candy bar chart
                    Box(3, 12, 5, 8, 1.2f);
                    FillStroke(Doodle.Sky, 2f);
                    Box(9.5f, 7, 5, 13, 1.2f);
                    FillStroke(Doodle.Sun, 2f);
                    Box(16, 3.5f, 5, 16.5f, 1.2f);
                    FillStroke(main, 2f);
                    Line(ink, 2.2f, 1.5f, 20.8f, 22.5f, 20.8f);
                    break;
                case IconKind.City: // a little skyline
                    Box(2.5f, 9, 6.5f, 11.5f, 1f);
                    FillStroke(Doodle.Sky, 1.8f);
                    Box(15, 7, 6.5f, 13.5f, 1f);
                    FillStroke(Doodle.Tang, 1.8f);
                    Box(8.5f, 3, 7, 17.5f, 1f);
                    FillStroke(main, 1.8f);
                    for (int row = 0; row < 4; row++)
                    {
                        Box(10.1f, 5.4f + row * 3.1f, 1.6f, 1.8f, 0.4f);
                        Fill(Doodle.SunL);
                        Box(12.4f, 5.4f + row * 3.1f, 1.6f, 1.8f, 0.4f);
                        Fill(Doodle.SunL);
                    }
                    for (int row = 0; row < 2; row++)
                    {
                        Box(4.1f, 11.5f + row * 3.1f, 1.5f, 1.8f, 0.4f);
                        Fill(Doodle.Paper);
                        Box(6.2f, 11.5f + row * 3.1f, 1.5f, 1.8f, 0.4f);
                        Fill(Doodle.Paper);
                    }
                    for (int row = 0; row < 3; row++)
                    {
                        Box(16.6f, 9.4f + row * 3.1f, 1.5f, 1.8f, 0.4f);
                        Fill(Doodle.SunL);
                        Box(18.8f, 9.4f + row * 3.1f, 1.5f, 1.8f, 0.4f);
                        Fill(Doodle.SunL);
                    }
                    Line(ink, 2.2f, 1.5f, 20.5f, 22.5f, 20.5f);
                    break;
                case IconKind.Hall: // City Hall: pediment, columns, steps
                    Poly(true, 2.5f, 9.5f, 12, 3, 21.5f, 9.5f);
                    FillStroke(main, 2f);
                    Box(3.5f, 9.5f, 17, 2.6f, 0.6f);
                    FillStroke(Doodle.Paper, 1.8f);
                    for (int i = 0; i < 4; i++)
                    {
                        Box(4.9f + i * 3.9f, 12.1f, 2.4f, 6.4f, 0);
                        FillStroke(Doodle.Paper, 1.5f);
                    }
                    Box(2.5f, 18.5f, 19, 3, 0.8f);
                    FillStroke(Doodle.Paper, 1.8f);
                    break;
                case IconKind.Menu: // a burger, obviously
                    vb = 24f / 28f;
                    p.BeginPath();
                    p.MoveTo(P(4, 12));
                    p.BezierCurveTo(P(4, 5.5f), P(24, 5.5f), P(24, 12));
                    p.ClosePath();
                    FillStroke(main, 2.2f);
                    p.BeginPath();
                    p.MoveTo(P(3.5f, 15));
                    p.BezierCurveTo(P(6, 13.4f), P(8, 16.6f), P(10.5f, 15));
                    p.BezierCurveTo(P(13, 13.4f), P(15, 16.6f), P(17.5f, 15));
                    p.BezierCurveTo(P(20, 13.4f), P(22, 16.6f), P(24.5f, 15));
                    Stroke(Doodle.Lettuce, 2.8f);
                    Box(4, 16.6f, 20, 4.2f, 2.1f);
                    FillStroke(Doodle.Patty, 2f);
                    p.BeginPath();
                    p.MoveTo(P(4.5f, 22));
                    p.LineTo(P(23.5f, 22));
                    p.BezierCurveTo(P(23.5f, 25.5f), P(4.5f, 25.5f), P(4.5f, 22));
                    p.ClosePath();
                    FillStroke(main, 2.2f);
                    Ellipse(10, 8.6f, 1.1f, 0.7f);
                    Fill(Doodle.Cream);
                    Ellipse(14, 7.6f, 1.1f, 0.7f);
                    Fill(Doodle.Cream);
                    Ellipse(18, 8.6f, 1.1f, 0.7f);
                    Fill(Doodle.Cream);
                    vb = 1f;
                    break;

                // ---------------------------------------------------------- things and people
                case IconKind.Train: // front view
                    Line(ink, 2f, 8.5f, 18.5f, 6.5f, 21.5f);
                    Line(ink, 2f, 15.5f, 18.5f, 17.5f, 21.5f);
                    Box(5, 2.5f, 14, 16.5f, 4f);
                    FillStroke(main, 2f);
                    Box(7.5f, 5, 9, 5.5f, 1.5f);
                    FillStroke(Doodle.Paper, 1.6f);
                    Circle(9, 14.5f, 1.5f);
                    FillStroke(Doodle.Sun, 1.2f);
                    Circle(15, 14.5f, 1.5f);
                    FillStroke(Doodle.Sun, 1.2f);
                    break;
                case IconKind.Wagon:
                    Box(2.5f, 6, 19, 11, 3);
                    FillStroke(main, 2f);
                    for (int i = 0; i < 3; i++)
                    {
                        Box(5 + i * 5.1f, 8.4f, 3.8f, 3.6f, 0.9f);
                        FillStroke(Doodle.Paper, 1.2f);
                    }
                    Dot(7, 18.5f, 2f, ink);
                    Dot(17, 18.5f, 2f, ink);
                    break;
                case IconKind.People: // three riders
                    vb = 24f / 36f;
                    p.BeginPath();
                    p.MoveTo(P(2.5f, 33));
                    p.BezierCurveTo(P(2.5f, 26), P(6.5f, 23), P(10.5f, 23));
                    p.BezierCurveTo(P(14.5f, 23), P(17.5f, 26), P(17.5f, 33));
                    p.ClosePath();
                    FillStroke(Doodle.Sky, 2.2f);
                    p.BeginPath();
                    p.MoveTo(P(18.5f, 33));
                    p.BezierCurveTo(P(18.5f, 26), P(22.5f, 23), P(26.5f, 23));
                    p.BezierCurveTo(P(30.5f, 23), P(33.5f, 26), P(33.5f, 33));
                    p.ClosePath();
                    FillStroke(Doodle.Grape, 2.2f);
                    Circle(10.5f, 16, 5.4f);
                    FillStroke(Doodle.Skin, 2.2f);
                    Circle(26.5f, 16, 5.4f);
                    FillStroke(Doodle.Skin, 2.2f);
                    p.BeginPath();
                    p.MoveTo(P(10, 34));
                    p.BezierCurveTo(P(10, 25), P(14, 21), P(18, 21));
                    p.BezierCurveTo(P(22, 21), P(26, 25), P(26, 34));
                    p.ClosePath();
                    FillStroke(main, 2.2f);
                    Circle(18, 13, 6.2f);
                    FillStroke(Doodle.Skin, 2.2f);
                    Dot(16, 12.5f, 1.1f, ink);
                    Dot(20, 12.5f, 1.1f, ink);
                    p.BeginPath();
                    p.MoveTo(P(16, 15.5f));
                    p.BezierCurveTo(P(17.2f, 16.6f), P(18.8f, 16.6f), P(20, 15.5f));
                    Stroke(ink, 1.4f);
                    vb = 1f;
                    break;
                case IconKind.Smile: // a face whose mouth follows Mood
                {
                    vb = 24f / 40f;
                    Circle(20, 20, 16.5f);
                    FillStroke(main, 2.8f);
                    if (mood > 0.2f)
                    {
                        Ellipse(10, 24, 3, 2);
                        Fill(Doodle.WithAlpha(Doodle.Gum, 0.7f));
                        Ellipse(30, 24, 3, 2);
                        Fill(Doodle.WithAlpha(Doodle.Gum, 0.7f));
                    }
                    Dot(14, 16.5f, 2.4f, ink);
                    Dot(26, 16.5f, 2.4f, ink);
                    float ey = 25f - mood * 1.5f, cy = 25f + mood * 4.5f, inset = (1f - Mathf.Abs(mood)) * 1.5f;
                    p.BeginPath();
                    p.MoveTo(P(12.5f + inset, ey));
                    p.BezierCurveTo(P(15.5f + inset, cy), P(24.5f - inset, cy), P(27.5f - inset, ey));
                    Stroke(ink, 2.8f);
                    vb = 1f;
                    break;
                }
                case IconKind.Warning:
                    Poly(true, 12, 3, 22, 20.5f, 2, 20.5f);
                    FillStroke(main, 2.2f);
                    Line(ink, 2.6f, 12, 9, 12, 14);
                    Dot(12, 17.3f, 1.4f, ink);
                    break;
                case IconKind.Info:
                    Circle(12, 12, 9);
                    FillStroke(main, 2.2f);
                    Line(ink, 2.6f, 12, 11, 12, 16.5f);
                    Dot(12, 7.6f, 1.4f, ink);
                    break;
                case IconKind.Feed: // speech bubble
                    Box(3, 4, 18, 13, 4);
                    FillStroke(main, 2f);
                    Poly(true, 8f, 15.5f, 13.3f, 15.5f, 6.4f, 21.5f);
                    Fill(main);
                    Line(ink, 2f, 7.6f, 17, 6.4f, 21.5f, 11.6f, 17);
                    Line(ink, 2f, 7, 9, 17, 9);
                    Line(ink, 2f, 7, 12.5f, 13.5f, 12.5f);
                    break;
                case IconKind.Dice:
                    rot = -8f;
                    Box(4, 4, 16, 16, 4);
                    FillStroke(main, 2.2f);
                    Dot(8.6f, 8.6f, 1.6f, ink);
                    Dot(15.4f, 8.6f, 1.6f, ink);
                    Dot(12, 12, 1.6f, ink);
                    Dot(8.6f, 15.4f, 1.6f, ink);
                    Dot(15.4f, 15.4f, 1.6f, ink);
                    rot = 0f;
                    break;
                case IconKind.Lock: // padlock
                    p.BeginPath();
                    p.MoveTo(P(8, 11));
                    p.LineTo(P(8, 8));
                    p.Arc(P(12, 8), R(4f), Angle.Degrees(180f), Angle.Degrees(360f));
                    p.LineTo(P(16, 11));
                    Stroke(ink, 2.6f);
                    Box(4.5f, 10.5f, 15, 11, 2.6f);
                    FillStroke(main, 2.2f);
                    Dot(12, 15, 1.6f, ink);
                    Line(ink, 1.8f, 12, 15, 12, 18);
                    break;
                case IconKind.Star:
                    Poly(true, 12, 2.5f, 14.6f, 8.9f, 21.5f, 9.4f, 16.2f, 13.8f, 17.9f, 20.6f, 12, 16.9f, 6.1f, 20.6f, 7.8f, 13.8f, 2.5f, 9.4f, 9.4f, 8.9f);
                    FillStroke(main, 2.2f);
                    break;
                case IconKind.Pin: // map pin
                    p.BeginPath();
                    p.Arc(P(12, 9.5f), R(6.4f), Angle.Degrees(150f), Angle.Degrees(390f));
                    p.LineTo(P(12, 21.5f));
                    p.ClosePath();
                    FillStroke(main, 2f);
                    Circle(12, 9.5f, 2.4f);
                    FillStroke(Doodle.Paper, 1.4f);
                    break;
                case IconKind.Trophy:
                    Line(ink, 2f, 7, 5.5f, 3.8f, 5.5f, 4.2f, 9, 7.6f, 11.5f);
                    Line(ink, 2f, 17, 5.5f, 20.2f, 5.5f, 19.8f, 9, 16.4f, 11.5f);
                    Line(ink, 2.2f, 12, 14, 12, 17.5f);
                    p.BeginPath();
                    p.MoveTo(P(7, 4));
                    p.LineTo(P(17, 4));
                    p.LineTo(P(17, 9));
                    p.Arc(P(12, 9), R(5f), Angle.Degrees(0f), Angle.Degrees(180f));
                    p.ClosePath();
                    FillStroke(main, 2f);
                    Box(7.5f, 17.5f, 9, 3.2f, 1f);
                    FillStroke(Doodle.CoinDark, 1.8f);
                    Line(Doodle.Paper, 1.4f, 9.3f, 6.2f, 9.3f, 9);
                    break;
                case IconKind.Ballot:
                    Box(8, 3.5f, 8, 9.5f, 1f);
                    FillStroke(Doodle.Paper, 1.8f);
                    Line(Doodle.Tomato, 1.6f, 9.8f, 7.6f, 11.4f, 9.2f, 14.2f, 6);
                    Box(4, 11, 16, 9.5f, 2);
                    FillStroke(main, 2f);
                    Line(ink, 2f, 7, 14.5f, 17, 14.5f);
                    break;
                case IconKind.Calendar:
                    Box(4, 5.5f, 16, 15, 2.5f);
                    FillStroke(Doodle.Paper, 2f);
                    p.BeginPath();
                    p.MoveTo(P(4, 10.5f));
                    p.LineTo(P(4, 8));
                    p.ArcTo(P(4, 5.5f), P(6.5f, 5.5f), R(2.5f));
                    p.LineTo(P(17.5f, 5.5f));
                    p.ArcTo(P(20, 5.5f), P(20, 8), R(2.5f));
                    p.LineTo(P(20, 10.5f));
                    p.ClosePath();
                    FillStroke(main, 2f);
                    Line(ink, 2.2f, 8.5f, 3.2f, 8.5f, 7.4f);
                    Line(ink, 2.2f, 15.5f, 3.2f, 15.5f, 7.4f);
                    Dot(9, 14, 1.15f, ink);
                    Dot(12, 14, 1.15f, ink);
                    Dot(15, 14, 1.15f, ink);
                    Dot(9, 17.2f, 1.15f, ink);
                    Dot(12, 17.2f, 1.15f, ink);
                    break;
                case IconKind.Cloud:
                    // Stroke every puff thick, then fill them all on top: only the outer outline survives.
                    Circle(8.5f, 14, 4.2f);
                    Stroke(ink, 4f);
                    Circle(13.5f, 10.5f, 5.5f);
                    Stroke(ink, 4f);
                    Circle(17.6f, 14.2f, 3.7f);
                    Stroke(ink, 4f);
                    Box(5, 13, 15.5f, 6.2f, 3.1f);
                    Stroke(ink, 4f);
                    Circle(8.5f, 14, 4.2f);
                    Fill(main);
                    Circle(13.5f, 10.5f, 5.5f);
                    Fill(main);
                    Circle(17.6f, 14.2f, 3.7f);
                    Fill(main);
                    Box(5, 13, 15.5f, 6.2f, 3.1f);
                    Fill(main);
                    break;
                case IconKind.Volume:
                    Poly(true, 3.5f, 9.5f, 7.5f, 9.5f, 12.5f, 5, 12.5f, 19, 7.5f, 14.5f, 3.5f, 14.5f);
                    FillStroke(main, 2f);
                    p.BeginPath();
                    p.Arc(P(13, 12), R(4f), Angle.Degrees(-45f), Angle.Degrees(45f));
                    Stroke(ink, 2.2f);
                    p.BeginPath();
                    p.Arc(P(13, 12), R(7.5f), Angle.Degrees(-45f), Angle.Degrees(45f));
                    Stroke(ink, 2.2f);
                    break;
                case IconKind.Coin: // a fat gold coin
                    vb = 24f / 36f;
                    Ellipse(18, 21, 14, 12.5f);
                    FillStroke(Doodle.CoinDark, 2.6f);
                    Ellipse(18, 16.5f, 14, 12.5f);
                    FillStroke(main, 2.6f);
                    Ellipse(18, 16.5f, 9.5f, 8.2f);
                    Stroke(Doodle.CoinDark, 2f);
                    p.BeginPath();
                    p.MoveTo(P(21, 13));
                    p.BezierCurveTo(P(20, 11.8f), P(18.8f, 11.5f), P(17.6f, 11.5f));
                    p.BezierCurveTo(P(15.8f, 11.5f), P(14.8f, 12.5f), P(14.8f, 13.7f));
                    p.BezierCurveTo(P(14.8f, 15.1f), P(16.2f, 15.5f), P(18, 16));
                    p.BezierCurveTo(P(19.9f, 16.5f), P(21.3f, 17.1f), P(21.3f, 18.7f));
                    p.BezierCurveTo(P(21.3f, 20.1f), P(20, 21.1f), P(18, 21.1f));
                    p.BezierCurveTo(P(16.6f, 21.1f), P(15.4f, 20.6f), P(14.6f, 19.7f));
                    Stroke(ink, 2f);
                    Line(ink, 2f, 18, 9.9f, 18, 11.5f);
                    Line(ink, 2f, 18, 21.1f, 18, 22.7f);
                    vb = 1f;
                    break;
                case IconKind.Clock: // alarm clock
                    vb = 24f / 28f;
                    Line(ink, 2.2f, 8, 24, 6, 27);
                    Line(ink, 2.2f, 20, 24, 22, 27);
                    Circle(6.5f, 6.5f, 3.6f);
                    FillStroke(main, 2f);
                    Circle(21.5f, 6.5f, 3.6f);
                    FillStroke(main, 2f);
                    Circle(14, 15.5f, 9.5f);
                    FillStroke(Doodle.Paper, 2.4f);
                    Line(ink, 2.2f, 14, 15.5f, 14, 10);
                    Line(ink, 2.2f, 14, 15.5f, 18, 17.5f);
                    vb = 1f;
                    break;
                case IconKind.Gear: // settings cog: eight chunky teeth
                    p.BeginPath();
                    for (int t = 0; t < 8; t++)
                    {
                        float b = t * 45f;
                        float[] angles = { b - 12f, b - 8f, b + 8f, b + 12f };
                        float[] radii = { 7.4f, 10f, 10f, 7.4f };
                        for (int k = 0; k < 4; k++)
                        {
                            float a = angles[k] * Mathf.Deg2Rad;
                            var pt = P(12f + Mathf.Cos(a) * radii[k], 12f + Mathf.Sin(a) * radii[k]);
                            if (t == 0 && k == 0) p.MoveTo(pt);
                            else p.LineTo(pt);
                        }
                    }
                    p.ClosePath();
                    FillStroke(main, 2f);
                    Circle(12, 12, 3.2f);
                    FillStroke(Doodle.Paper, 1.8f);
                    break;
                case IconKind.Platform: // a train waiting at a long platform
                    Box(4, 5, 16, 9, 3f);
                    FillStroke(main, 2f);
                    Box(6.5f, 7.2f, 4.5f, 3.4f, 0.9f);
                    FillStroke(Doodle.Paper, 1.2f);
                    Box(13, 7.2f, 4.5f, 3.4f, 0.9f);
                    FillStroke(Doodle.Paper, 1.2f);
                    Box(1.5f, 15, 21, 5.5f, 1.4f);
                    FillStroke(Doodle.Sand, 2f);
                    Line(Doodle.Sun, 1.6f, 3.2f, 16.6f, 20.8f, 16.6f);
                    break;
                case IconKind.Exit: // a door with an arrow out
                    Box(4, 3, 12, 18, 2f);
                    FillStroke(Doodle.Paper, 2f);
                    Box(6.3f, 5.3f, 7.4f, 15.7f, 1f);
                    FillStroke(main, 1.6f);
                    Dot(11.6f, 13.2f, 0.9f, ink);
                    Line(ink, 2.4f, 13.5f, 12, 21.5f, 12);
                    Line(ink, 2.4f, 18.5f, 9, 21.5f, 12, 18.5f, 15);
                    break;
                case IconKind.Elevator: // a lift with up and down arrows
                    Box(5, 2.5f, 14, 19, 3f);
                    FillStroke(main, 2f);
                    Poly(true, 12, 5.8f, 15.6f, 10.2f, 8.4f, 10.2f);
                    FillStroke(Doodle.Paper, 1.4f);
                    Poly(true, 12, 18.2f, 15.6f, 13.8f, 8.4f, 13.8f);
                    FillStroke(Doodle.Paper, 1.4f);
                    break;
                case IconKind.Concourse: // swapping between lines
                    Circle(12, 12, 9.5f);
                    FillStroke(main, 2f);
                    Line(ink, 2.4f, 6.5f, 9, 17, 9);
                    Line(ink, 2.4f, 14.2f, 6.2f, 17, 9, 14.2f, 11.8f);
                    Line(ink, 2.4f, 17.5f, 15, 7, 15);
                    Line(ink, 2.4f, 9.8f, 12.2f, 7, 15, 9.8f, 17.8f);
                    break;
                case IconKind.Sunrise: // the sun peeking over the horizon
                    for (int k = 0; k < 5; k++)
                    {
                        float a = (-150f + k * 30f) * Mathf.Deg2Rad;
                        Line(ink, 2f, 12 + Mathf.Cos(a) * 8.6f, 16.5f + Mathf.Sin(a) * 8.6f, 12 + Mathf.Cos(a) * 11f, 16.5f + Mathf.Sin(a) * 11f);
                    }
                    p.BeginPath();
                    p.Arc(P(12, 16.5f), R(6f), Angle.Degrees(180f), Angle.Degrees(360f));
                    p.ClosePath();
                    FillStroke(main, 2f);
                    Line(ink, 2.4f, 2.5f, 16.8f, 21.5f, 16.8f);
                    Line(ink, 2f, 6, 20.5f, 18, 20.5f);
                    break;
                case IconKind.Moon: // a crescent moon
                    p.BeginPath();
                    p.MoveTo(P(14, 3.5f));
                    p.BezierCurveTo(P(6, 4.5f), P(3.5f, 13), P(9.5f, 18.5f));
                    p.BezierCurveTo(P(14, 22.5f), P(19.5f, 19), P(20.5f, 15));
                    p.BezierCurveTo(P(15, 17.5f), P(9.5f, 14), P(10.5f, 8.5f));
                    p.BezierCurveTo(P(11, 6), P(12.5f, 4.5f), P(14, 3.5f));
                    p.ClosePath();
                    FillStroke(main, 2f);
                    Dot(18.5f, 5.5f, 1f, ink);
                    Dot(20.5f, 9f, 0.7f, ink);
                    break;
                case IconKind.Pencil: // the planning pencil
                    Line(Doodle.Ink3, 2f, 2.5f, 21.5f, 5.5f, 21.5f);
                    Line(Doodle.Ink3, 2f, 8, 21.5f, 11, 21.5f);
                    Line(Doodle.Ink3, 2f, 13.5f, 21.5f, 16.5f, 21.5f);
                    Line(Doodle.Ink3, 2f, 19, 21.5f, 21.5f, 21.5f);
                    rot = -40f;
                    rcx = 12f;
                    rcy = 11f;
                    Box(4, 8.5f, 12.5f, 5, 0);
                    FillStroke(main, 1.8f);
                    Poly(true, 16.5f, 8.5f, 21, 11, 16.5f, 13.5f);
                    FillStroke(new Color32(242, 199, 154, 255), 1.8f);
                    Poly(true, 19.6f, 10.2f, 21, 11, 19.6f, 11.8f);
                    Fill(ink);
                    Box(1.4f, 8.5f, 2.8f, 5, 0);
                    FillStroke(Doodle.Gum, 1.6f);
                    rot = 0f;
                    break;
                case IconKind.Floppy: // save
                    Box(4, 4, 16, 16, 2.4f);
                    FillStroke(main, 2f);
                    Box(8, 4, 8, 5.5f, 0.8f);
                    FillStroke(Doodle.Ink2, 1.5f);
                    Box(7, 12.5f, 10, 7.5f, 1f);
                    FillStroke(Doodle.Paper, 1.5f);
                    Line(Doodle.Ink3, 1.2f, 9, 15, 15, 15);
                    Line(Doodle.Ink3, 1.2f, 9, 17.5f, 13.5f, 17.5f);
                    break;
                case IconKind.Folder: // load
                    Poly(true, 3, 6.5f, 9.5f, 6.5f, 11.5f, 8.5f, 21, 8.5f, 21, 19.5f, 3, 19.5f);
                    FillStroke(Doodle.Coin, 2f);
                    Box(3, 11, 18, 8.5f, 1.5f);
                    FillStroke(main, 2f);
                    break;
                case IconKind.Door: // leave
                    Box(5.5f, 3, 13, 18, 1.5f);
                    FillStroke(main, 2f);
                    Box(8, 5.5f, 8, 6, 1f);
                    FillStroke(Doodle.Paper, 1.4f);
                    Dot(15.5f, 13.5f, 1.2f, ink);
                    Line(ink, 2.2f, 3, 21, 21, 21);
                    break;
            }
        }
    }
}
