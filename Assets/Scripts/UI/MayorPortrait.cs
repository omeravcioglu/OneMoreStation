using UnityEngine;
using UnityEngine.UIElements;

namespace Metro.UI
{
    /// <summary>A small drawn portrait of a mayor. The face is seeded per mayor; the mouth follows their mood towards the metro.</summary>
    public sealed class MayorPortrait : VisualElement
    {
        readonly float size;
        int face = 1;
        Agenda agenda;
        float mood;     // -1 cross ... +1 delighted

        static readonly Color32[] Skin =
        {
            new Color32(255, 220, 189, 255), new Color32(241, 194, 150, 255), new Color32(224, 172, 130, 255),
            new Color32(198, 134, 94, 255), new Color32(141, 85, 54, 255), new Color32(102, 64, 42, 255)
        };

        static readonly Color32[] Hair =
        {
            new Color32(44, 34, 30, 255), new Color32(92, 60, 36, 255), new Color32(170, 120, 60, 255),
            new Color32(214, 188, 120, 255), new Color32(190, 190, 196, 255), new Color32(150, 60, 40, 255)
        };

        public static Color AgendaColor(Agenda a)
        {
            switch (a)
            {
                case Agenda.Growth: return Doodle.Sky;
                case Agenda.Thrift: return Doodle.Coin;
                case Agenda.Green: return Doodle.Mint;
                default: return Doodle.Gum;
            }
        }

        public MayorPortrait(float size)
        {
            this.size = size;
            style.width = size;
            style.height = size;
            style.flexShrink = 0f;
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        public void Set(Mayor m, float moodValue)
        {
            if (m == null) return;
            float q = Mathf.Round(Mathf.Clamp(moodValue, -1f, 1f) * 4f) / 4f;
            if (m.face == face && m.agenda == agenda && Mathf.Approximately(q, mood)) return;
            face = m.face;
            agenda = m.agenda;
            mood = q;
            MarkDirtyRepaint();
        }

        void Draw(MeshGenerationContext mgc)
        {
            var p = mgc.painter2D;
            float u = size / 48f;
            Vector2 P(float x, float y) => new Vector2(x * u, y * u);
            var rng = new Rng(face);
            Color bg = AgendaColor(agenda);
            Color skin = Skin[rng.Range(0, Skin.Length)];
            Color hair = Hair[rng.Range(0, Hair.Length)];
            int hairStyle = rng.Range(0, 4);
            bool glasses = rng.Chance(0.4f);

            void Circle(float cx, float cy, float r, Color c)
            {
                p.fillColor = c;
                p.BeginPath();
                p.Arc(P(cx, cy), r * u, Angle.Degrees(0f), Angle.Degrees(360f));
                p.ClosePath();
                p.Fill();
            }

            // Background disc.
            Circle(24, 24, 24, Color.Lerp(bg, Color.white, 0.6f));
            // Shoulders / suit.
            p.fillColor = Color.Lerp(bg, Color.black, 0.25f);
            p.BeginPath();
            p.MoveTo(P(7, 48));
            p.BezierCurveTo(P(8, 38), P(14, 35), P(24, 35));
            p.BezierCurveTo(P(34, 35), P(40, 38), P(41, 48));
            p.ClosePath();
            p.Fill();
            // Collar.
            p.fillColor = Color.white;
            p.BeginPath();
            p.MoveTo(P(19, 35.5f));
            p.LineTo(P(24, 41));
            p.LineTo(P(29, 35.5f));
            p.ClosePath();
            p.Fill();
            // Neck and head.
            Circle(24, 33, 4.2f, Color.Lerp(skin, Color.black, 0.08f));
            if (hairStyle == 1) Circle(24, 22, 12.5f, hair);   // long hair behind the face
            Circle(24, 22, 10.5f, skin);
            // Hair on top.
            p.fillColor = hair;
            switch (hairStyle)
            {
                case 0:
                case 1:
                    p.BeginPath();
                    p.Arc(P(24, 21), 11f * u, Angle.Degrees(180f), Angle.Degrees(360f));
                    p.LineTo(P(33, 18));
                    p.BezierCurveTo(P(28, 16), P(20, 16), P(15, 18));
                    p.ClosePath();
                    p.Fill();
                    break;
                case 2:
                    Circle(14.5f, 20, 3f, hair);
                    Circle(33.5f, 20, 3f, hair);
                    break;
                default:
                    Circle(24, 9.5f, 4.5f, hair);
                    p.BeginPath();
                    p.Arc(P(24, 21), 10.8f * u, Angle.Degrees(190f), Angle.Degrees(350f));
                    p.ClosePath();
                    p.Fill();
                    break;
            }
            // Eyes.
            var ink = new Color32(43, 30, 63, 255);
            Circle(20, 23, 1.3f, ink);
            Circle(28, 23, 1.3f, ink);
            if (glasses)
            {
                p.strokeColor = ink;
                p.lineWidth = 1.2f * u;
                p.BeginPath();
                p.Arc(P(20, 23), 3.4f * u, Angle.Degrees(0f), Angle.Degrees(360f));
                p.Stroke();
                p.BeginPath();
                p.Arc(P(28, 23), 3.4f * u, Angle.Degrees(0f), Angle.Degrees(360f));
                p.Stroke();
                p.BeginPath();
                p.MoveTo(P(23.4f, 23));
                p.LineTo(P(24.6f, 23));
                p.Stroke();
            }
            // Mouth: smile, flat or frown.
            p.strokeColor = ink;
            p.lineWidth = 1.5f * u;
            p.lineCap = LineCap.Round;
            float curve = mood * 3f;
            p.BeginPath();
            p.MoveTo(P(20.5f, 28 - curve * 0.3f));
            p.QuadraticCurveTo(P(24, 28 + curve), P(27.5f, 28 - curve * 0.3f));
            p.Stroke();
            // Doodle ink rim around the whole portrait.
            p.strokeColor = ink;
            p.lineWidth = Mathf.Max(1.5f, 2.4f * u);
            p.BeginPath();
            p.Arc(P(24, 24), 22.8f * u, Angle.Degrees(0f), Angle.Degrees(360f));
            p.ClosePath();
            p.Stroke();
        }
    }
}
