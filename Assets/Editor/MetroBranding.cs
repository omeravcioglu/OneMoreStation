using System;
using System.Collections.Generic;
using System.IO;
using Metro.Platform;
using UiIcon = Metro.UI.IconKind;
using UnityEditor;
using UnityEngine;

namespace Metro.EditorTools
{
    /// <summary>
    /// Generates the game's branding from code: the application icon (set in Player Settings) and the
    /// Steam achievement icons (64×64, unlocked and greyed "locked" versions, plus 256×256 previews).
    /// Run: -executeMethod Metro.EditorTools.MetroBranding.GenerateAll
    /// </summary>
    public static class MetroBranding
    {
        const string IconPath = "Assets/Branding/AppIcon.png";
        static string SteamDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Steam", "achievements"));

        [MenuItem("Metro/Generate Icons (app + achievements)")]
        public static void GenerateAllMenu() => GenerateAll();

        public static void GenerateAll()
        {
            int code = 0;
            try
            {
                GenerateAppIcon();
                GenerateAchievementIcons();
                Debug.Log("[Metro] Branding generated: " + IconPath + " and " + SteamDir);
            }
            catch (Exception e)
            {
                Debug.LogError("[Metro] Branding failed: " + e);
                code = 1;
            }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        // ------------------------------------------------------------------ app icon

        /// <summary>The doodle-style icon made for the Steam client (Steam/store/make_icon.py); the exe uses it too.</summary>
        static string StoreIcon => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Steam", "store", "client_icon_512.png"));

        public static void GenerateAppIcon()
        {
            if (File.Exists(StoreIcon))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(IconPath)));
                File.Copy(StoreIcon, IconPath, true);
                ApplyAppIcon();
                return;
            }
            DrawAppIcon();
            ApplyAppIcon();
        }

        static void ApplyAppIcon()
        {
            AssetDatabase.ImportAsset(IconPath, ImportAssetOptions.ForceUpdate);
            var imp = (TextureImporter)AssetImporter.GetAtPath(IconPath);
            imp.textureType = TextureImporterType.Default;
            imp.mipmapEnabled = false;
            imp.alphaIsTransparency = true;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.SaveAndReimport();
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            PlayerSettings.SetIcons(UnityEditor.Build.NamedBuildTarget.Unknown, new[] { tex }, UnityEditor.IconKind.Any);
            AssetDatabase.SaveAssets();
        }

        /// <summary>The original icon, drawn from code (used when the Steam store icon isn't there).</summary>
        static void DrawAppIcon()
        {
            const int size = 1024;
            var c = new Canvas(size, size / 24f * 1f);
            float u = size / 24f;
            var ink = new Color32(30, 30, 48, 255);
            // Rounded square background with a soft vertical gradient.
            c.Fill((x, y) =>
            {
                float d = RoundRectSdf(x, y, size * 0.5f, size * 0.5f, size * 0.46f, size * 0.46f, size * 0.2f);
                float t = y / size;
                var col = Color.Lerp(new Color32(40, 42, 64, 255), new Color32(24, 24, 40, 255), t);
                return (col, Cov(d, 1.2f));
            });
            // Three metro lines meeting at two interchanges, octilinear like the in-game map.
            var red = new Color32(226, 64, 60, 255);
            var blue = new Color32(47, 111, 237, 255);
            var green = new Color32(36, 170, 100, 255);
            float lw = 2.1f * u;
            c.Stroke(new[] { P(18, 3.6f, u), P(18, 20.4f, u) }, lw, green);
            c.Stroke(new[] { P(6, 3.6f, u), P(6, 10, u), P(12, 16, u), P(12, 20.4f, u) }, lw, blue);
            c.Stroke(new[] { P(3.6f, 16, u), P(12, 16, u), P(18, 10, u), P(20.4f, 10, u) }, lw, red);
            // Regular stations: small white dots with an ink rim.
            foreach (var (x, y) in new[] { (6f, 7f), (7.4f, 16f), (18f, 17f), (15f, 13f) })
            {
                c.Disc(x * u, y * u, 1.35f * u, ink);
                c.Disc(x * u, y * u, 0.85f * u, Color.white);
            }
            // Interchanges: bigger white rings.
            foreach (var (x, y) in new[] { (12f, 16f), (18f, 10f) })
            {
                c.Disc(x * u, y * u, 2.1f * u, ink);
                c.Disc(x * u, y * u, 1.5f * u, Color.white);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(IconPath)));
            File.WriteAllBytes(IconPath, c.Encode());
        }

        static Vector2 P(float x, float y, float u) => new Vector2(x * u, y * u);

        // ------------------------------------------------------------------ achievement icons

        public static void GenerateAchievementIcons()
        {
            Directory.CreateDirectory(SteamDir);
            foreach (var a in Achievements.All)
            {
                foreach (int size in new[] { 64, 256 })
                {
                    string suffix = size == 64 ? "" : "_256";
                    File.WriteAllBytes(Path.Combine(SteamDir, a.id + suffix + ".png"), Badge(a, size, false).Encode());
                    File.WriteAllBytes(Path.Combine(SteamDir, a.id + "_locked" + suffix + ".png"), Badge(a, size, true).Encode());
                }
            }
        }

        static Canvas Badge(AchievementDef a, int size, bool locked)
        {
            var c = new Canvas(size, size / 24f);
            float u = size / 24f;
            Color main = locked ? new Color32(150, 150, 158, 255) : (Color)a.color;
            Color dark = Color.Lerp(main, Color.black, 0.35f);
            Color bg = new Color32(26, 26, 40, 255);
            c.Fill((x, y) => (bg, 1f));
            // Medal: outer ring, inner disc with a subtle gradient.
            c.Disc(12 * u, 12 * u, 11.2f * u, dark);
            c.Fill((x, y) =>
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(12 * u, 12 * u)) - 9.8f * u;
                float t = y / size;
                return (Color.Lerp(Color.Lerp(main, Color.white, 0.18f), main, t), Cov(d, 1f));
            });
            // Locked: the same glyph in grey (hidden achievements show a padlock instead).
            Glyph(c, locked && a.hidden ? UiIcon.Lock : a.icon, u, locked ? new Color(0.9f, 0.9f, 0.92f, 0.85f) : Color.white);
            return c;
        }

        /// <summary>The achievement glyphs, traced from the in-game IconElement shapes (24-unit grid, centred and shrunk).</summary>
        static void Glyph(Canvas c, UiIcon k, float u0, Color col)
        {
            // Map the 24 grid into the medal's centre (about 60% of the badge).
            float s = 0.62f, off = 12f * (1f - s);
            float u = u0 * s;
            Vector2 Q(float x, float y) => new Vector2((x * s + off) * u0, (y * s + off) * u0);
            float w = 2.2f * u;
            switch (k)
            {
                case UiIcon.Line:
                    c.Stroke(new[] { Q(4, 18), Q(10, 18), Q(16, 6), Q(20, 6) }, w, col);
                    c.Disc(Q(4, 18).x, Q(4, 18).y, 2.6f * u, col);
                    c.Disc(Q(20, 6).x, Q(20, 6).y, 2.6f * u, col);
                    break;
                case UiIcon.Check:
                    c.Stroke(new[] { Q(5, 12.5f), Q(10, 17), Q(19, 7) }, 2.8f * u, col);
                    break;
                case UiIcon.People:
                    c.Disc(Q(12, 7.5f).x, Q(12, 7.5f).y, 3.6f * u, col);
                    c.Polygon(new[] { Q(5, 20), Q(6.5f, 14), Q(12, 12), Q(17.5f, 14), Q(19, 20) }, col);
                    break;
                case UiIcon.Star:
                    c.Polygon(new[] { Q(12, 2.5f), Q(14.6f, 8.9f), Q(21.5f, 9.4f), Q(16.2f, 13.8f), Q(17.9f, 20.6f), Q(12, 16.9f), Q(6.1f, 20.6f), Q(7.8f, 13.8f), Q(2.5f, 9.4f), Q(9.4f, 8.9f) }, col);
                    break;
                case UiIcon.Trophy:
                    c.Polygon(new[] { Q(7, 4), Q(17, 4), Q(17, 9), Q(15.5f, 12.5f), Q(12, 14), Q(8.5f, 12.5f), Q(7, 9) }, col);
                    c.Stroke(new[] { Q(7, 5.5f), Q(3.8f, 5.5f), Q(4.2f, 9), Q(7.6f, 11.5f) }, 1.8f * u, col);
                    c.Stroke(new[] { Q(17, 5.5f), Q(20.2f, 5.5f), Q(19.8f, 9), Q(16.4f, 11.5f) }, 1.8f * u, col);
                    c.Stroke(new[] { Q(12, 14), Q(12, 17.5f) }, 2.2f * u, col);
                    c.Polygon(new[] { Q(7.5f, 17.5f), Q(16.5f, 17.5f), Q(16.5f, 20.5f), Q(7.5f, 20.5f) }, col);
                    break;
                case UiIcon.Calendar:
                    c.StrokeClosed(new[] { Q(4, 6), Q(20, 6), Q(20, 20.5f), Q(4, 20.5f) }, w, col);
                    c.Stroke(new[] { Q(4, 10.5f), Q(20, 10.5f) }, w, col);
                    c.Stroke(new[] { Q(8.5f, 3.5f), Q(8.5f, 7.5f) }, w, col);
                    c.Stroke(new[] { Q(15.5f, 3.5f), Q(15.5f, 7.5f) }, w, col);
                    foreach (float x in new[] { 9f, 12f, 15f }) c.Disc(Q(x, 14.5f).x, Q(x, 14.5f).y, 1.3f * u, col);
                    break;
                case UiIcon.Insert:
                    c.Stroke(new[] { Q(3, 12), Q(21, 12) }, w, col);
                    c.Disc(Q(12, 12).x, Q(12, 12).y, 4.6f * u, col);
                    break;
                case UiIcon.Coverage:
                    c.Ring(Q(12, 12).x, Q(12, 12).y, 8f * u, w, col);
                    c.Disc(Q(12, 12).x, Q(12, 12).y, 3f * u, col);
                    break;
                case UiIcon.Station:
                    c.Ring(Q(12, 12).x, Q(12, 12).y, 6.5f * u, 2.6f * u, col);
                    break;
                case UiIcon.Train:
                    c.StrokeClosed(new[] { Q(5, 3), Q(19, 3), Q(19, 18), Q(5, 18) }, w, col);
                    c.Stroke(new[] { Q(5, 10), Q(19, 10) }, w, col);
                    c.Disc(Q(9, 14).x, Q(9, 14).y, 1.3f * u, col);
                    c.Disc(Q(15, 14).x, Q(15, 14).y, 1.3f * u, col);
                    c.Stroke(new[] { Q(8, 18), Q(6, 21) }, w, col);
                    c.Stroke(new[] { Q(16, 18), Q(18, 21) }, w, col);
                    break;
                case UiIcon.Smile:
                    c.Ring(Q(12, 12).x, Q(12, 12).y, 8.5f * u, w, col);
                    c.Disc(Q(9, 10).x, Q(9, 10).y, 1.3f * u, col);
                    c.Disc(Q(15, 10).x, Q(15, 10).y, 1.3f * u, col);
                    c.Stroke(new[] { Q(8.4f, 14.5f), Q(10, 16.2f), Q(12, 16.8f), Q(14, 16.2f), Q(15.6f, 14.5f) }, w, col);
                    break;
                case UiIcon.Finance:
                    c.Ring(Q(12, 12).x, Q(12, 12).y, 8.5f * u, w, col);
                    c.Stroke(new[] { Q(15, 8.5f), Q(10.5f, 8.5f), Q(9.2f, 10), Q(9.8f, 11.6f), Q(14.2f, 12.4f), Q(14.8f, 14), Q(13.5f, 15.5f), Q(9, 15.5f) }, 1.9f * u, col);
                    c.Stroke(new[] { Q(12, 6), Q(12, 8.5f) }, 1.9f * u, col);
                    c.Stroke(new[] { Q(12, 15.5f), Q(12, 18) }, 1.9f * u, col);
                    break;
                case UiIcon.Hall:
                    c.StrokeClosed(new[] { Q(3, 9), Q(12, 3.5f), Q(21, 9) }, w, col);
                    foreach (float x in new[] { 6.5f, 10.5f, 13.5f, 17.5f }) c.Stroke(new[] { Q(x, 11), Q(x, 17.5f) }, w, col);
                    c.Stroke(new[] { Q(3, 20.5f), Q(21, 20.5f) }, w, col);
                    break;
                case UiIcon.Ballot:
                    c.Stroke(new[] { Q(8, 11), Q(8, 4), Q(16, 4), Q(16, 11) }, w, col);
                    c.StrokeClosed(new[] { Q(4, 11), Q(20, 11), Q(20, 20.5f), Q(4, 20.5f) }, w, col);
                    c.Stroke(new[] { Q(7, 14.5f), Q(17, 14.5f) }, w, col);
                    break;
                case UiIcon.Warning:
                    c.StrokeClosed(new[] { Q(12, 4), Q(21, 19.5f), Q(3, 19.5f) }, w, col);
                    c.Stroke(new[] { Q(12, 10), Q(12, 14) }, w, col);
                    c.Disc(Q(12, 17).x, Q(12, 17).y, 1.2f * u, col);
                    break;
                case UiIcon.Lock:
                    c.Polygon(new[] { Q(5, 10.5f), Q(19, 10.5f), Q(19, 20.5f), Q(5, 20.5f) }, col);
                    c.Stroke(new[] { Q(8, 10.5f), Q(8, 8), Q(9.2f, 5.2f), Q(12, 4), Q(14.8f, 5.2f), Q(16, 8), Q(16, 10.5f) }, 2.4f * u, col);
                    break;
                default:
                    c.Polygon(new[] { Q(12, 2.5f), Q(14.6f, 8.9f), Q(21.5f, 9.4f), Q(16.2f, 13.8f), Q(17.9f, 20.6f), Q(12, 16.9f), Q(6.1f, 20.6f), Q(7.8f, 13.8f), Q(2.5f, 9.4f), Q(9.4f, 8.9f) }, col);
                    break;
            }
        }

        // ------------------------------------------------------------------ tiny anti-aliased rasteriser

        static float Cov(float sdf, float aa) => Mathf.Clamp01(0.5f - sdf / aa);

        static float RoundRectSdf(float x, float y, float cx, float cy, float hw, float hh, float r)
        {
            float qx = Mathf.Abs(x - cx) - hw + r, qy = Mathf.Abs(y - cy) - hh + r;
            float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
            return outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
        }

        sealed class Canvas
        {
            readonly int w, h;
            readonly Color[] px;

            public Canvas(int w, float unused)
            {
                this.w = w;
                h = w;
                px = new Color[w * h];
            }

            /// <summary>Blends a colour per pixel; y grows downwards like the 24-grid icons.</summary>
            public void Fill(Func<float, float, (Color c, float a)> f)
            {
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        var (c, a) = f(x + 0.5f, y + 0.5f);
                        if (a <= 0f) continue;
                        Blend(x, y, c, a * c.a);
                    }
            }

            void Blend(int x, int y, Color c, float a)
            {
                int i = (h - 1 - y) * w + x;   // PNG rows bottom-up in Unity textures
                var d = px[i];
                float outA = a + d.a * (1f - a);
                if (outA <= 0f) return;
                px[i] = new Color((c.r * a + d.r * d.a * (1f - a)) / outA, (c.g * a + d.g * d.a * (1f - a)) / outA, (c.b * a + d.b * d.a * (1f - a)) / outA, outA);
            }

            public void Disc(float cx, float cy, float r, Color c) =>
                Area(cx - r - 2, cy - r - 2, cx + r + 2, cy + r + 2, (x, y) => Vector2.Distance(new Vector2(x, y), new Vector2(cx, cy)) - r, c);

            public void Ring(float cx, float cy, float r, float width, Color c) =>
                Area(cx - r - width, cy - r - width, cx + r + width, cy + r + width, (x, y) => Mathf.Abs(Vector2.Distance(new Vector2(x, y), new Vector2(cx, cy)) - r) - width * 0.5f, c);

            public void Stroke(Vector2[] pts, float width, Color c)
            {
                // Segments of one stroke share a coverage mask so their round joins don't double-blend.
                if (strokeMask != null) Array.Clear(strokeMask, 0, strokeMask.Length);
                for (int i = 0; i < pts.Length - 1; i++) Segment(pts[i], pts[i + 1], width, c);
            }

            public void StrokeClosed(Vector2[] pts, float width, Color c)
            {
                var list = new List<Vector2>(pts) { pts[0] };
                Stroke(list.ToArray(), width, c);
            }

            void Segment(Vector2 a, Vector2 b, float width, Color c)
            {
                float r = width * 0.5f;
                Area(Mathf.Min(a.x, b.x) - r - 2, Mathf.Min(a.y, b.y) - r - 2, Mathf.Max(a.x, b.x) + r + 2, Mathf.Max(a.y, b.y) + r + 2, (x, y) =>
                {
                    var p = new Vector2(x, y);
                    var ab = b - a;
                    float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
                    return Vector2.Distance(p, a + ab * t) - r;
                }, c, true);
            }

            public void Polygon(Vector2[] pts, Color c)
            {
                float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
                foreach (var p in pts)
                {
                    minX = Mathf.Min(minX, p.x);
                    minY = Mathf.Min(minY, p.y);
                    maxX = Mathf.Max(maxX, p.x);
                    maxY = Mathf.Max(maxY, p.y);
                }
                Area(minX - 2, minY - 2, maxX + 2, maxY + 2, (x, y) =>
                {
                    var p = new Vector2(x, y);
                    float d = float.MaxValue;
                    bool inside = false;
                    for (int i = 0, j = pts.Length - 1; i < pts.Length; j = i++)
                    {
                        Vector2 a = pts[j], b = pts[i];
                        var ab = b - a;
                        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
                        d = Mathf.Min(d, Vector2.Distance(p, a + ab * t));
                        if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
                    }
                    return inside ? -d : d;
                }, c);
            }

            // Max-coverage accumulation for strokes keeps overlapping segments from darkening the joins.
            float[] strokeMask;

            void Area(float x0, float y0, float x1, float y1, Func<float, float, float> sdf, Color c, bool maxBlend = false)
            {
                int ix0 = Mathf.Max(0, Mathf.FloorToInt(x0)), iy0 = Mathf.Max(0, Mathf.FloorToInt(y0));
                int ix1 = Mathf.Min(w - 1, Mathf.CeilToInt(x1)), iy1 = Mathf.Min(h - 1, Mathf.CeilToInt(y1));
                for (int y = iy0; y <= iy1; y++)
                    for (int x = ix0; x <= ix1; x++)
                    {
                        float a = Cov(sdf(x + 0.5f, y + 0.5f), 1f);
                        if (a <= 0f) continue;
                        if (maxBlend)
                        {
                            strokeMask ??= new float[w * h];
                            int i = y * w + x;
                            if (a <= strokeMask[i]) continue;
                            float extra = (a - strokeMask[i]) / Mathf.Max(1e-4f, 1f - strokeMask[i]);
                            strokeMask[i] = a;
                            Blend(x, y, c, extra * c.a);
                        }
                        else Blend(x, y, c, a * c.a);
                    }
            }

            public byte[] Encode()
            {
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                tex.SetPixels(px);
                tex.Apply();
                var png = tex.EncodeToPNG();
                UnityEngine.Object.DestroyImmediate(tex);
                return png;
            }
        }
    }
}
