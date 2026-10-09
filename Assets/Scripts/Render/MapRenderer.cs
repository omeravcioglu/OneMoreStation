using System.Collections.Generic;
using UnityEngine;

namespace Metro
{
    public enum DataOverlay { None, Population, Jobs, Coverage }

    /// <summary>
    /// The faint city layer under the network: land, built-up areas, parks, water,
    /// dashed district borders, data overlays and the hatched fog over unexplored districts.
    /// </summary>
    public sealed class MapRenderer
    {
        readonly Transform root;
        readonly Shader vecShader, ovlShader;
        GameState gs;

        MeshLayer land, parks, water, borders;
        GameObject builtQuad, dataQuad, fogQuad;
        Material builtMat, dataMat, fogMat;
        Texture2D builtTex, dataTex, fogTex, kindTex;
        Color32[] builtPx, dataPx, fogPx, kindPx;
        int tw, th;

        public DataOverlay overlay = DataOverlay.None;
        DataOverlay shownOverlay = DataOverlay.None;
        float dataTimer, builtTimer;
        bool fogDirty = true;

        public const int QLand = 3000, QBuilt = 3001, QParks = 3002, QWater = 3003, QBorders = 3004, QData = 3005, QFog = 3006, QFrame = 3007, QNight = 3008;

        GameObject nightQuad, frameQuad;
        Material nightMat, frameMat;
        Texture2D whiteTex;
        Rect frameRect;

        // Growth at dawn: the old town limit as a ghost, arrows pushing it out, sparkles on new districts.
        MeshLayer growFx;
        public const int QGrowFx = 3009;
        Rect settledFrame, ghostFrame;
        float growT = -1f, growSettledAt = -1f;
        int landmarkCount = -1;

        public MapRenderer(Transform parent)
        {
            root = new GameObject("Map").transform;
            root.SetParent(parent, false);
            vecShader = Resources.Load<Shader>("Shaders/MetroVector");
            ovlShader = Resources.Load<Shader>("Shaders/MetroOverlay");
        }

        public void Bind(GameState state)
        {
            Clear();
            gs = state;
            var city = gs.city;
            land = new MeshLayer("Land", root, vecShader, QLand);
            parks = new MeshLayer("Parks", root, vecShader, QParks);
            water = new MeshLayer("Water", root, vecShader, QWater);
            borders = new MeshLayer("Landmarks", root, vecShader, QBorders);
            growFx = new MeshLayer("GrowFx", root, vecShader, QGrowFx);
            BuildStatic();

            tw = city.gw + 2;
            th = city.gh + 2;
            builtTex = MakeTexture("BuiltUp");
            dataTex = MakeTexture("Data");
            fogTex = MakeTexture("Fog");
            // Linear (not sRGB), so the style codes come through the sampler unchanged.
            kindTex = new Texture2D(tw, th, TextureFormat.RGBA32, false, true)
            {
                name = "TownStyle",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            kindPx = new Color32[tw * th];
            builtPx = new Color32[tw * th];
            dataPx = new Color32[tw * th];
            fogPx = new Color32[tw * th];

            // The storybook town: little houses, towers and sheds drawn by the overlay shader from density and style.
            builtMat = new Material(ovlShader) { renderQueue = QBuilt };
            builtMat.SetTexture("_MainTex", builtTex);
            builtMat.SetTexture("_KindTex", kindTex);
            builtMat.SetFloat("_Mode", 3f);
            builtMat.SetFloat("_Lot", 0.3f);
            dataMat = new Material(ovlShader) { renderQueue = QData };
            dataMat.SetTexture("_MainTex", dataTex);
            dataMat.SetFloat("_Mode", 0f);
            fogMat = new Material(ovlShader) { renderQueue = QFog };
            fogMat.SetTexture("_MainTex", fogTex);
            fogMat.SetFloat("_Mode", 1f);
            fogMat.SetColor("_Color", MapPalette.WithAlpha(MapPalette.Fog, 0.94f));
            fogMat.SetColor("_HatchColor", MapPalette.WithAlpha(MapPalette.FogHatch, 0.9f));
            fogMat.SetColor("_EdgeColor", MapPalette.Ink);
            fogMat.SetFloat("_HatchSpacing", 14f);
            fogMat.SetFloat("_Cell", city.cellSize);
            fogMat.SetFloat("_Puff", 1.3f);

            var mapRect = new Rect(0f, 0f, city.width, city.height);
            builtQuad = MakeQuad("BuiltUpQuad", builtMat, mapRect);
            dataQuad = MakeQuad("DataQuad", dataMat, mapRect);
            fogQuad = MakeQuad("FogQuad", fogMat, new Rect(-80f, -80f, city.width + 160f, city.height + 160f));
            dataQuad.SetActive(false);

            whiteTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            whiteTex.SetPixel(0, 0, Color.white);
            whiteTex.Apply(false);
            nightMat = new Material(ovlShader) { renderQueue = QNight };
            nightMat.SetTexture("_MainTex", whiteTex);
            nightMat.SetFloat("_Mode", 0f);
            nightMat.SetColor("_Color", new Color(0.12f, 0.15f, 0.28f, 0f));
            nightQuad = MakeQuad("NightQuad", nightMat, new Rect(-80f, -80f, city.width + 160f, city.height + 160f));

            frameMat = new Material(ovlShader) { renderQueue = QFrame };
            frameMat.SetTexture("_MainTex", whiteTex);
            frameMat.SetFloat("_Mode", 2f);
            // Past the town limit: a pale haze with diagonal hatching, behind a dashed ink frame.
            frameMat.SetColor("_Color", MapPalette.WithAlpha(MapPalette.Uncharted, 0.66f));
            frameMat.SetColor("_HatchColor", MapPalette.WithAlpha(MapPalette.UnchartedHatch, 0.6f));
            frameMat.SetFloat("_HatchSpacing", 18f);
            frameMat.SetColor("_EdgeColor", MapPalette.CityLimits);
            frameMat.SetFloat("_Soft", 0.35f);
            frameQuad = MakeQuad("FrameQuad", frameMat, new Rect(-80f, -80f, city.width + 160f, city.height + 160f));
            SetFrame(city.playArea);
            settledFrame = city.playArea;
            growT = -1f;
            landmarkCount = -1;

            RefreshBuiltUp();
            fogDirty = true;
            RefreshFog(0f);
        }

        public void Clear()
        {
            land?.Destroy();
            parks?.Destroy();
            water?.Destroy();
            borders?.Destroy();
            growFx?.Destroy();
            land = parks = water = borders = growFx = null;
            foreach (var go in new[] { builtQuad, dataQuad, fogQuad, nightQuad, frameQuad })
            {
                if (go == null) continue;
                Object.Destroy(go.GetComponent<MeshFilter>().sharedMesh);
                Object.Destroy(go);
            }
            builtQuad = dataQuad = fogQuad = nightQuad = frameQuad = null;
            foreach (var m in new[] { builtMat, dataMat, fogMat, nightMat, frameMat })
                if (m != null) Object.Destroy(m);
            foreach (var t in new[] { builtTex, dataTex, fogTex, kindTex, whiteTex })
                if (t != null) Object.Destroy(t);
            builtMat = dataMat = fogMat = nightMat = frameMat = null;
            builtTex = dataTex = fogTex = kindTex = whiteTex = null;
            gs = null;
        }

        Texture2D MakeTexture(string name)
        {
            var t = new Texture2D(tw, th, TextureFormat.RGBA32, false, false)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            return t;
        }

        GameObject MakeQuad(string name, Material m, Rect world)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            float cs = gs.city.cellSize;
            float texW = tw * cs, texH = th * cs;
            Vector2 Uv(float x, float y) => new Vector2((x + cs) / texW, (y + cs) / texH);
            var mesh = new Mesh { name = name };
            mesh.SetVertices(new List<Vector3>
            {
                new Vector3(world.xMin, world.yMin, 0f), new Vector3(world.xMax, world.yMin, 0f),
                new Vector3(world.xMax, world.yMax, 0f), new Vector3(world.xMin, world.yMax, 0f)
            });
            mesh.SetUVs(0, new List<Vector2>
            {
                Uv(world.xMin, world.yMin), Uv(world.xMax, world.yMin), Uv(world.xMax, world.yMax), Uv(world.xMin, world.yMax)
            });
            mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            mesh.bounds = MeshLayer.WorldBounds(gs.city);
            mf.sharedMesh = mesh;
            mr.sharedMaterial = m;
            return go;
        }

        void BuildStatic()
        {
            var city = gs.city;
            var bounds = MeshLayer.WorldBounds(city);

            land.b.Clear();
            land.b.AddRect(new Rect(0f, 0f, city.width, city.height), MapPalette.Land);
            land.Commit(bounds);

            // Parks: candy green with a darker edge, dotted with round trees.
            parks.b.Clear();
            var parkEdge = StrokeWidth.Pixels(1.25f);
            var rng = new System.Random(city.seed * 31 + 7);
            foreach (var p in city.parks)
            {
                parks.b.AddFill(p, Geo.Triangulate(p), MapPalette.Park);
                parks.b.AddPolyline(p, parkEdge, MapPalette.ParkEdge, 0f, true, false);
                var box = Geo.Bounds(p);
                const float step = 0.42f;
                for (float y = box.yMin + step * 0.5f; y < box.yMax; y += step)
                {
                    for (float x = box.xMin + step * 0.5f; x < box.xMax; x += step)
                    {
                        if (rng.NextDouble() < 0.45) continue;
                        var q = new Vector2(x + (float)(rng.NextDouble() - 0.5) * step * 0.8f, y + (float)(rng.NextDouble() - 0.5) * step * 0.8f);
                        if (!Geo.PointInPolygon(q, p) || Geo.DistToPolyline(q, p, out _, out _) < 0.14f) continue;
                        Tree(parks.b, q, 0.08f + (float)rng.NextDouble() * 0.05f, rng);
                    }
                }
            }
            parks.Commit(bounds);

            water.b.Clear();
            var w = city.water;
            var shore = StrokeWidth.Pixels(1.3f);
            var world = new StrokeWidth(1f, 0f, 100000f);
            // Rivers run between grassy green banks.
            foreach (var r in w.rivers)
            {
                var bank = new List<float>(r.halfWidths.Count);
                foreach (var hw in r.halfWidths) bank.Add(hw * 1.9f + 0.06f);
                water.b.AddPolyline(r.points, world, MapPalette.RiverBank, 0f, false, true, 1f, bank);
            }
            // The sea: a sandy beach, then the water with a crisp shoreline.
            var coast = new List<Vector2>(w.coastY.Length);
            for (int i = 0; i < w.coastY.Length; i++) coast.Add(new Vector2(w.coastX0 + i * w.coastStep, w.coastY[i]));
            if (w.hasCoast)
            {
                water.b.AddPolyline(coast, new StrokeWidth(0.32f, 3f, 100000f), MapPalette.Beach, 0f, false, false);
                water.b.AddStripBelow(coast, -120f, MapPalette.Water);
                water.b.AddPolyline(coast, shore, MapPalette.Shore, 0f, false, false);
            }
            foreach (var r in w.rivers)
            {
                var edge = new List<float>(r.halfWidths.Count);
                foreach (var hw in r.halfWidths) edge.Add(hw + 0.035f);
                water.b.AddPolyline(r.points, world, MapPalette.Shore, 0f, false, true, 1f, edge);
            }
            foreach (var l in w.lakes)
            {
                water.b.AddFill(l, Geo.Triangulate(l), MapPalette.Water);
                water.b.AddPolyline(l, shore, MapPalette.Shore, 0f, true, false);
            }
            foreach (var r in w.rivers)
            {
                water.b.AddPolyline(r.points, world, MapPalette.Water, 0f, false, true, 1f, r.halfWidths);
                // A dashed glint down the middle.
                water.b.AddDashed(r.points, StrokeWidth.Pixels(1.1f), MapPalette.WithAlpha(MapPalette.WaterHighlight, 0.9f), 0.32f, 0.26f);
            }
            if (w.hasCoast) Waves(water.b, w, city, rng);
            water.Commit(bounds);

            // No district borders: the city reads as one place; districts are named, not fenced.
            // (The borders layer now holds the landmarks, built as districts open.)
        }

        static void Tree(VectorMeshBuilder b, Vector2 at, float r, System.Random rng)
        {
            Color32 crown = rng.NextDouble() < 0.5 ? MapPalette.TreeDark : MapPalette.TreeLight;
            b.AddDisc(at + new Vector2(r * 0.25f, -r * 0.3f), StrokeWidth.World(r, 0.6f), 1f, MapPalette.WithAlpha(MapPalette.ParkEdge, 0.55f));
            b.AddDisc(at, StrokeWidth.World(r + r * 0.18f, 0.9f), 1f, Balance.Hex("#4E8A36"));
            b.AddDisc(at, StrokeWidth.World(r, 0.6f), 1f, crown);
        }

        static readonly List<Vector2> wave = new List<Vector2>();

        /// <summary>Little wave squiggles scattered over the sea.</summary>
        static void Waves(VectorMeshBuilder b, WaterSystem w, CityModel city, System.Random rng)
        {
            var c = MapPalette.WithAlpha(MapPalette.WaterHighlight, 0.95f);
            var stroke = StrokeWidth.Pixels(1.2f);
            for (float x = -6f; x < city.width + 6f; x += 2.6f)
            {
                float top = w.CoastY(x);
                for (float y = top - 1.2f; y > -24f; y -= 2.2f)
                {
                    float px = x + (float)(rng.NextDouble() - 0.5) * 1.6f;
                    float py = y - (float)rng.NextDouble() * 1.2f;
                    if (py > w.CoastY(px) - 0.6f || py > w.CoastY(px + 0.6f) - 0.6f) continue;
                    wave.Clear();
                    for (int i = 0; i <= 8; i++)
                    {
                        float t = i / 8f;
                        wave.Add(new Vector2(px + t * 0.62f, py + Mathf.Sin(t * Mathf.PI * 2f) * 0.07f));
                    }
                    b.AddPolyline(wave, stroke, c, 0f, false, true);
                }
            }
        }

        // ------------------------------------------------------------------ textures

        int TexIndex(int cx, int cy) => (cy + 1) * tw + (cx + 1);

        /// <summary>
        /// The town texture: alpha is how built-up each cell is (0..1); the style texture says what gets drawn
        /// there: houses (0), downtown towers (40) or sheds (80).
        /// </summary>
        public void RefreshBuiltUp()
        {
            var city = gs.city;
            float area = city.cellSize * city.cellSize;
            Color32 baseC = MapPalette.BuiltUp;
            for (int i = 0; i < builtPx.Length; i++)
            {
                builtPx[i] = new Color32(baseC.r, baseC.g, baseC.b, 0);
                kindPx[i] = new Color32(0, 0, 0, 255);
            }
            for (int y = 0; y < city.gh; y++)
            {
                for (int x = 0; x < city.gw; x++)
                {
                    int i = city.CellIndex(x, y);
                    if (city.IsCellWater(i)) continue;
                    bool rev = city.IsCellActive(i);
                    float r = rev ? city.res[i] : city.resBase[i] * 0.7f;
                    float j = rev ? city.jobs[i] : city.jobBase[i] * 0.7f;
                    float v = Mathf.Clamp01((r + 0.6f * j) / (area * 16000f));
                    int ti = TexIndex(x, y);
                    builtPx[ti] = new Color32(baseC.r, baseC.g, baseC.b, (byte)(v * 255f));
                    byte style = 0;
                    int di = city.cellDistrict[i];
                    if (di >= 0)
                    {
                        var t = city.districts[di].type;
                        if ((t == DistrictType.Central || t == DistrictType.BusinessPark) && v > 0.3f) style = 40;
                        else if ((t == DistrictType.Industrial || t == DistrictType.Airport) && j > r * 0.8f) style = 80;
                    }
                    kindPx[ti] = new Color32(style, 0, 0, 255);
                }
            }
            builtTex.SetPixels32(builtPx);
            builtTex.Apply(false);
            kindTex.SetPixels32(kindPx);
            kindTex.Apply(false);
        }

        void RefreshData()
        {
            var city = gs.city;
            float area = city.cellSize * city.cellSize;
            for (int i = 0; i < dataPx.Length; i++) dataPx[i] = new Color32(0, 0, 0, 0);
            Color32 lo, hi;
            for (int y = 0; y < city.gh; y++)
            {
                for (int x = 0; x < city.gw; x++)
                {
                    int i = city.CellIndex(x, y);
                    if (!city.IsCellActive(i)) continue;
                    float v;
                    Color32 c;
                    switch (overlay)
                    {
                        case DataOverlay.Population:
                            v = Mathf.Clamp01(city.res[i] / (area * 16000f));
                            lo = Balance.Hex("#F6C177");
                            hi = Balance.Hex("#D9472B");
                            c = MapPalette.Lerp(lo, hi, v);
                            c.a = (byte)(Mathf.Sqrt(v) * 200f);
                            break;
                        case DataOverlay.Jobs:
                            v = Mathf.Clamp01(city.jobs[i] / (area * 30000f));
                            lo = Balance.Hex("#B9A3F0");
                            hi = Balance.Hex("#4B2FA6");
                            c = MapPalette.Lerp(lo, hi, v);
                            c.a = (byte)(Mathf.Sqrt(v) * 200f);
                            break;
                        default:
                            v = Mathf.Clamp01(city.res[i] / (area * 12000f));
                            if (v < 0.02f) continue;
                            float acc = city.cellAccess[i];
                            c = acc > 0f ? MapPalette.Lerp(MapPalette.CrowdYellow, MapPalette.CrowdGreen, acc) : MapPalette.CrowdRed;
                            c.a = (byte)((0.25f + 0.6f * Mathf.Sqrt(v)) * 200f);
                            break;
                    }
                    dataPx[TexIndex(x, y)] = c;
                }
            }
            dataTex.SetPixels32(dataPx);
            dataTex.Apply(false);
        }

        void RefreshFog(float dt)
        {
            var city = gs.city;
            bool animating = false;
            foreach (var d in city.districts)
            {
                if (d.revealed && d.revealAnim < 1f)
                {
                    d.revealAnim = Mathf.Min(1f, d.revealAnim + dt / 2.5f);
                    animating = true;
                }
            }
            if (!animating && !fogDirty) return;
            fogDirty = false;
            var fogC = new Color32(255, 255, 255, 255);
            for (int i = 0; i < fogPx.Length; i++) fogPx[i] = fogC;
            for (int y = 0; y < city.gh; y++)
            {
                for (int x = 0; x < city.gw; x++)
                {
                    int i = city.CellIndex(x, y);
                    int di = city.cellDistrict[i];
                    if (di < 0) continue;
                    var d = city.districts[di];
                    float a = d.revealed ? 1f - Geo.SmoothStep(0f, 1f, d.revealAnim) : 1f;
                    fogPx[TexIndex(x, y)] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            fogTex.SetPixels32(fogPx);
            fogTex.Apply(false);
        }

        public void MarkFogDirty() => fogDirty = true;

        /// <summary>Moves the city-limits frame (animated by the caller as the map grows).</summary>
        public void SetFrame(Rect r)
        {
            // The limit moving outwards from where it last settled: the town is growing.
            if (gs != null && growT < 0f && r.width > settledFrame.width + 0.05f && frameRect.width > 0f)
            {
                ghostFrame = settledFrame;
                growT = 0f;
                growSettledAt = -1f;
            }
            frameRect = r;
            if (frameMat != null) frameMat.SetVector("_Rect", new Vector4(r.xMin, r.yMin, r.xMax, r.yMax));
        }

        public Rect FrameRect => frameRect;

        /// <summary>0 by day, 1 in the dead of night (service closed 01:00-05:00).</summary>
        static float NightFactor(float minute)
        {
            float h = minute / 60f;
            if (h >= 22.5f) return Geo.SmoothStep(22.5f, 24.5f, h);
            if (h < 0.5f) return Geo.SmoothStep(-1.5f, 0.5f, h);
            if (h < 4.5f) return 1f;
            return 1f - Geo.SmoothStep(4.5f, 6f, h);
        }

        public void Tick(float dt)
        {
            if (gs == null) return;
            int open = 0;
            foreach (var d in gs.city.districts)
                if (d.revealed) open++;
            if (open != landmarkCount)
            {
                landmarkCount = open;
                BuildLandmarks();
            }
            TickGrowth(dt);
            if (nightMat != null)
            {
                Color night = MapPalette.Night;
                night.a = 0.28f * NightFactor(gs.clock.MinuteOfDay);
                nightMat.SetColor("_Color", night);
            }
            RefreshFog(dt);
            builtTimer -= dt;
            if (builtTimer <= 0f)
            {
                builtTimer = 4f;
                RefreshBuiltUp();
            }
            if (overlay != shownOverlay)
            {
                shownOverlay = overlay;
                dataQuad.SetActive(overlay != DataOverlay.None);
                dataTimer = 0f;
            }
            if (overlay != DataOverlay.None)
            {
                dataTimer -= dt;
                if (dataTimer <= 0f)
                {
                    dataTimer = 1.5f;
                    RefreshData();
                }
            }
        }

        // ------------------------------------------------------------------ growth at dawn

        static readonly List<Vector2> fxPts = new List<Vector2>();

        void TickGrowth(float dt)
        {
            bool sparkling = false;
            foreach (var d in gs.city.districts)
                if (d.revealed && d.revealAnim < 1f) sparkling = true;
            if (growT < 0f && !sparkling)
            {
                if (growFx.b.VertexCount > 0)
                {
                    growFx.b.Clear();
                    growFx.Commit(MeshLayer.WorldBounds(gs.city));
                }
                return;
            }
            var cam = Camera.main;
            float pw = cam != null ? cam.orthographicSize * 2f / Mathf.Max(1, Screen.height) : 0.02f;
            float refPx = pw * StrokeWidth.PxScale;
            growFx.b.Clear();
            if (growT >= 0f)
            {
                growT += dt;
                bool settled = Mathf.Abs(frameRect.width - gs.city.playArea.width) < 0.02f && Mathf.Abs(frameRect.height - gs.city.playArea.height) < 0.02f;
                if (settled && growSettledAt < 0f) growSettledAt = growT;
                // Hold a moment once the limit has settled, then fade out.
                float fade = growSettledAt < 0f ? 1f : Mathf.Clamp01(1f - (growT - growSettledAt - 1.5f));
                if (growSettledAt >= 0f && growT - growSettledAt > 2.5f)
                {
                    growT = -1f;
                    settledFrame = frameRect;
                }
                if (growT >= 0f)
                {
                    // The old limit, dashed and fading.
                    var r = ghostFrame;
                    fxPts.Clear();
                    fxPts.Add(new Vector2(r.xMin, r.yMin));
                    fxPts.Add(new Vector2(r.xMax, r.yMin));
                    fxPts.Add(new Vector2(r.xMax, r.yMax));
                    fxPts.Add(new Vector2(r.xMin, r.yMax));
                    fxPts.Add(new Vector2(r.xMin, r.yMin));
                    growFx.b.AddDashed(fxPts, StrokeWidth.Pixels(2.2f), MapPalette.WithAlpha(MapPalette.Ink, 0.45f * fade), 14f * refPx, 10f * refPx);
                    // Orange arrows pushing each side out.
                    float pulse = 1f + 0.15f * Mathf.Sin(growT * 6f);
                    Color32 orange = MapPalette.WithAlpha(MapPalette.DepotRoof, fade);
                    Color32 ink = MapPalette.WithAlpha(MapPalette.Ink, fade);
                    for (int side = 0; side < 4; side++)
                    {
                        for (int k = 1; k <= 3; k++)
                        {
                            float t = k / 4f;
                            Vector2 at, dir;
                            switch (side)
                            {
                                case 0: at = new Vector2(Mathf.Lerp(r.xMin, r.xMax, t), r.yMax); dir = Vector2.up; break;
                                case 1: at = new Vector2(Mathf.Lerp(r.xMin, r.xMax, t), r.yMin); dir = Vector2.down; break;
                                case 2: at = new Vector2(r.xMax, Mathf.Lerp(r.yMin, r.yMax, t)); dir = Vector2.right; break;
                                default: at = new Vector2(r.xMin, Mathf.Lerp(r.yMin, r.yMax, t)); dir = Vector2.left; break;
                            }
                            Arrow(growFx.b, at + dir * (10f * refPx), dir, 26f * refPx * pulse, refPx, orange, ink);
                        }
                    }
                }
            }
            // Twinkling sparkles over districts that are opening.
            foreach (var d in gs.city.districts)
            {
                if (!d.revealed || d.revealAnim >= 1f) continue;
                float a = Mathf.Sin(d.revealAnim * Mathf.PI);
                for (int k = 0; k < 5; k++)
                {
                    float ang = k * 1.2566f + d.id;
                    Vector2 at = d.labelPos + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * (0.6f + 0.35f * Mathf.Sin(Time.time * 2f + k)) ;
                    float size = (10f + 6f * Mathf.Sin(Time.time * 5f + k * 1.7f)) * refPx * a;
                    Sparkle(growFx.b, at, size, refPx, MapPalette.WithAlpha(MapPalette.Highlighter, a), MapPalette.WithAlpha(MapPalette.Ink, a));
                }
            }
            growFx.Commit(MeshLayer.WorldBounds(gs.city));
        }

        static void Arrow(VectorMeshBuilder b, Vector2 tail, Vector2 dir, float len, float refPx, Color32 fill, Color32 ink)
        {
            Vector2 n = new Vector2(-dir.y, dir.x);
            Vector2 tip = tail + dir * len;
            fxPts.Clear();
            fxPts.Add(tail);
            fxPts.Add(tip - dir * (8f * refPx));
            b.AddPolyline(fxPts, StrokeWidth.Pixels(5f), ink, 0f, false, true);
            b.AddPolyline(fxPts, StrokeWidth.Pixels(3.2f), fill, 0f, false, true);
            fxPts.Clear();
            fxPts.Add(tip);
            fxPts.Add(tip - dir * (11f * refPx) + n * (8f * refPx));
            fxPts.Add(tip - dir * (11f * refPx) - n * (8f * refPx));
            b.AddFill(fxPts, TriIdx, fill);
            fxPts.Add(tip);
            b.AddPolyline(fxPts, StrokeWidth.Pixels(1.5f), ink, 0f, false, true);
        }

        static readonly int[] TriIdx = { 0, 1, 2 };

        /// <summary>A four-point sparkle (a star of two thin diamonds).</summary>
        static void Sparkle(VectorMeshBuilder b, Vector2 c, float r, float refPx, Color32 fill, Color32 ink)
        {
            if (r <= 0f) return;
            fxPts.Clear();
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f;
                float rr = i % 2 == 0 ? r : r * 0.32f;
                fxPts.Add(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rr);
            }
            var tris = new int[] { 0, 1, 7, 1, 2, 3, 3, 4, 5, 5, 6, 7, 1, 3, 5, 1, 5, 7 };
            b.AddFill(fxPts, tris, fill);
            fxPts.Add(fxPts[0]);
            b.AddPolyline(fxPts, StrokeWidth.Pixels(1.2f), ink, 0f, false, false);
        }

        // ------------------------------------------------------------------ landmarks

        /// <summary>
        /// A storybook landmark in each open district, by its type: a clock tower downtown, a castle in the
        /// old town, a domed hall at the university, a stadium, a factory, a plane at the airport, glass
        /// towers in the business park, a ferris wheel in new towns and windmills in the suburbs.
        /// </summary>
        void BuildLandmarks()
        {
            var city = gs.city;
            borders.b.Clear();
            foreach (var d in city.districts)
            {
                if (!d.revealed) continue;
                Vector2 at = d.labelPos + new Vector2(0f, -0.95f);
                if (city.water.IsWater(at, 0.4f) || !city.InBounds(at)) continue;
                Landmark(borders.b, d.type, at, 0.55f);
            }
            borders.Commit(MeshLayer.WorldBounds(city));
        }

        static readonly Color32 LInk = Balance.Hex("#5B4E70");
        static readonly List<Vector2> lm = new List<Vector2>();
        static readonly StrokeWidth LStroke = new StrokeWidth(0.012f, 0.6f, 1.6f);

        static void Poly(VectorMeshBuilder b, Vector2 at, float s, Color32 fill, params float[] xy)
        {
            lm.Clear();
            for (int i = 0; i < xy.Length; i += 2) lm.Add(at + new Vector2(xy[i], xy[i + 1]) * s);
            var tris = Geo.Triangulate(lm);
            b.AddFill(lm, tris, fill);
            lm.Add(lm[0]);
            b.AddPolyline(lm, LStroke, LInk, 0f, false, false);
        }

        static void Disc(VectorMeshBuilder b, Vector2 at, float s, Vector2 c, float r, Color32 fill)
        {
            lm.Clear();
            for (int i = 0; i < 20; i++)
            {
                float a = i / 20f * Mathf.PI * 2f;
                lm.Add(at + (c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r) * s);
            }
            b.AddFill(lm, Geo.Triangulate(lm), fill);
            lm.Add(lm[0]);
            b.AddPolyline(lm, LStroke, LInk, 0f, false, false);
        }

        static void Stroke(VectorMeshBuilder b, Vector2 at, float s, params float[] xy)
        {
            lm.Clear();
            for (int i = 0; i < xy.Length; i += 2) lm.Add(at + new Vector2(xy[i], xy[i + 1]) * s);
            b.AddPolyline(lm, LStroke, LInk, 0f, false, true);
        }

        static void Landmark(VectorMeshBuilder b, DistrictType t, Vector2 at, float s)
        {
            Color32 wall = Balance.Hex("#FFF9EC"), roof = Balance.Hex("#F4876A"), glass = Balance.Hex("#CFE7FF"),
                gold = Balance.Hex("#FFD23F"), green = Balance.Hex("#A9DE88"), lav = Balance.Hex("#E6DBFF"), grey = Balance.Hex("#E4E2EA");
            switch (t)
            {
                case DistrictType.Central: // clock tower
                    Poly(b, at, s, wall, -0.25f, 0f, 0.25f, 0f, 0.25f, 1.1f, -0.25f, 1.1f);
                    Poly(b, at, s, roof, -0.34f, 1.1f, 0.34f, 1.1f, 0f, 1.6f);
                    Disc(b, at, s, new Vector2(0f, 0.8f), 0.17f, Balance.Hex("#FFFFFF"));
                    Stroke(b, at, s, 0f, 0.8f, 0f, 0.92f);
                    Stroke(b, at, s, 0f, 0.8f, 0.08f, 0.8f);
                    Poly(b, at, s, glass, -0.08f, 0.15f, 0.08f, 0.15f, 0.08f, 0.45f, -0.08f, 0.45f);
                    break;
                case DistrictType.OldTown: // castle
                    Poly(b, at, s, wall, -0.6f, 0f, 0.6f, 0f, 0.6f, 0.55f, 0.45f, 0.55f, 0.45f, 0.65f, 0.3f, 0.65f, 0.3f, 0.55f, 0.15f, 0.55f, 0.15f, 0.65f, -0.15f, 0.65f, -0.15f, 0.55f, -0.3f, 0.55f, -0.3f, 0.65f, -0.45f, 0.65f, -0.45f, 0.55f, -0.6f, 0.55f);
                    Poly(b, at, s, wall, -0.75f, 0f, -0.45f, 0f, -0.45f, 0.95f, -0.75f, 0.95f);
                    Poly(b, at, s, roof, -0.82f, 0.95f, -0.38f, 0.95f, -0.6f, 1.3f);
                    Poly(b, at, s, wall, 0.45f, 0f, 0.75f, 0f, 0.75f, 0.95f, 0.45f, 0.95f);
                    Poly(b, at, s, roof, 0.38f, 0.95f, 0.82f, 0.95f, 0.6f, 1.3f);
                    Poly(b, at, s, Balance.Hex("#A8693F"), -0.14f, 0f, 0.14f, 0f, 0.14f, 0.3f, 0f, 0.4f, -0.14f, 0.3f);
                    break;
                case DistrictType.University: // domed hall with a flag
                    Poly(b, at, s, wall, -0.6f, 0f, 0.6f, 0f, 0.6f, 0.5f, -0.6f, 0.5f);
                    Poly(b, at, s, lav, -0.7f, 0.5f, 0.7f, 0.5f, 0.7f, 0.6f, -0.7f, 0.6f);
                    lm.Clear();
                    for (int i = 0; i <= 12; i++)
                    {
                        float a = i / 12f * Mathf.PI;
                        lm.Add(new Vector2(Mathf.Cos(a) * 0.38f, 0.6f + Mathf.Sin(a) * 0.38f));
                    }
                    var dome = new List<float>();
                    foreach (var q in lm) { dome.Add(q.x); dome.Add(q.y); }
                    Poly(b, at, s, Balance.Hex("#BE93E0"), dome.ToArray());
                    Stroke(b, at, s, 0f, 0.98f, 0f, 1.35f);
                    Poly(b, at, s, Balance.Hex("#FF62AE"), 0f, 1.35f, 0.25f, 1.27f, 0f, 1.19f);
                    for (int i = -2; i <= 2; i++) Stroke(b, at, s, i * 0.22f, 0.05f, i * 0.22f, 0.45f);
                    break;
                case DistrictType.Stadium:
                    lm.Clear();
                    {
                        var o = new List<float>();
                        for (int i = 0; i < 24; i++)
                        {
                            float a = i / 24f * Mathf.PI * 2f;
                            o.Add(Mathf.Cos(a) * 0.8f);
                            o.Add(0.4f + Mathf.Sin(a) * 0.45f);
                        }
                        Poly(b, at, s, Balance.Hex("#FF7EB6"), o.ToArray());
                        var f = new List<float>();
                        for (int i = 0; i < 24; i++)
                        {
                            float a = i / 24f * Mathf.PI * 2f;
                            f.Add(Mathf.Cos(a) * 0.55f);
                            f.Add(0.4f + Mathf.Sin(a) * 0.27f);
                        }
                        Poly(b, at, s, green, f.ToArray());
                        Stroke(b, at, s, 0f, 0.13f, 0f, 0.67f);
                    }
                    break;
                case DistrictType.Industrial: // factory with a smoking chimney
                    Poly(b, at, s, grey, -0.6f, 0f, 0.6f, 0f, 0.6f, 0.45f, 0.4f, 0.7f, 0.4f, 0.45f, 0.2f, 0.7f, 0.2f, 0.45f, 0f, 0.7f, 0f, 0.45f, -0.6f, 0.45f);
                    Poly(b, at, s, Balance.Hex("#C9C4D6"), -0.5f, 0.45f, -0.32f, 0.45f, -0.32f, 1.15f, -0.5f, 1.15f);
                    Disc(b, at, s, new Vector2(-0.35f, 1.35f), 0.14f, Balance.Hex("#FFFFFF"));
                    Disc(b, at, s, new Vector2(-0.18f, 1.55f), 0.1f, Balance.Hex("#FFFFFF"));
                    break;
                case DistrictType.Airport: // a little plane over a runway
                    Poly(b, at, s, grey, -0.9f, 0f, 0.9f, 0f, 0.9f, 0.18f, -0.9f, 0.18f);
                    Poly(b, at, s, Balance.Hex("#FFFFFF"), -0.55f, 0.65f, 0.6f, 0.75f, 0.68f, 0.85f, -0.55f, 0.82f);
                    Poly(b, at, s, Balance.Hex("#3D86F5"), -0.1f, 0.78f, 0.15f, 0.79f, -0.15f, 1.15f, -0.32f, 1.13f);
                    Poly(b, at, s, Balance.Hex("#3D86F5"), -0.1f, 0.7f, 0.15f, 0.71f, -0.2f, 0.42f, -0.35f, 0.44f);
                    Poly(b, at, s, Balance.Hex("#FF5146"), -0.55f, 0.75f, -0.42f, 0.78f, -0.55f, 1.0f, -0.65f, 0.98f);
                    break;
                case DistrictType.BusinessPark: // glass towers
                    Poly(b, at, s, glass, -0.45f, 0f, -0.05f, 0f, -0.05f, 1.35f, -0.45f, 1.35f);
                    Poly(b, at, s, Balance.Hex("#D7E9FB"), 0.05f, 0f, 0.45f, 0f, 0.45f, 0.95f, 0.05f, 0.95f);
                    for (int i = 1; i < 6; i++) Stroke(b, at, s, -0.4f, i * 0.22f, -0.1f, i * 0.22f);
                    for (int i = 1; i < 4; i++) Stroke(b, at, s, 0.1f, i * 0.22f, 0.4f, i * 0.22f);
                    break;
                case DistrictType.NewTown: // ferris wheel
                    {
                        Stroke(b, at, s, -0.35f, 0f, 0f, 0.7f, 0.35f, 0f);
                        var w = new List<float>();
                        for (int i = 0; i < 20; i++)
                        {
                            float a = i / 20f * Mathf.PI * 2f;
                            w.Add(Mathf.Cos(a) * 0.6f);
                            w.Add(0.75f + Mathf.Sin(a) * 0.6f);
                        }
                        Poly(b, at, s, new Color32(255, 255, 255, 140), w.ToArray());
                        for (int i = 0; i < 6; i++)
                        {
                            float a = i / 6f * Mathf.PI * 2f;
                            Stroke(b, at, s, 0f, 0.75f, Mathf.Cos(a) * 0.6f, 0.75f + Mathf.Sin(a) * 0.6f);
                            Disc(b, at, s, new Vector2(Mathf.Cos(a) * 0.6f, 0.75f + Mathf.Sin(a) * 0.6f), 0.1f, i % 2 == 0 ? gold : Balance.Hex("#FF7EB6"));
                        }
                    }
                    break;
                case DistrictType.Suburb: // windmill
                    Poly(b, at, s, wall, -0.22f, 0f, 0.22f, 0f, 0.14f, 0.8f, -0.14f, 0.8f);
                    Poly(b, at, s, roof, -0.2f, 0.8f, 0.2f, 0.8f, 0f, 1.0f);
                    for (int i = 0; i < 4; i++)
                    {
                        float a = i * Mathf.PI / 2f + 0.4f;
                        Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a)), n = new Vector2(-d.y, d.x);
                        Vector2 c = new Vector2(0f, 0.85f);
                        Vector2 p1 = c + d * 0.1f, p2 = c + d * 0.6f;
                        Poly(b, at, s, Balance.Hex("#FFF1C4"), p1.x, p1.y, p2.x, p2.y, (p2 + n * 0.14f).x, (p2 + n * 0.14f).y, (p1 + n * 0.1f).x, (p1 + n * 0.1f).y);
                    }
                    Disc(b, at, s, new Vector2(0f, 0.85f), 0.06f, gold);
                    break;
                default: // residential: a big round tree by a fountain
                    Poly(b, at, s, Balance.Hex("#9B6B43"), -0.06f, 0f, 0.06f, 0f, 0.06f, 0.5f, -0.06f, 0.5f);
                    Disc(b, at, s, new Vector2(0f, 0.75f), 0.38f, Balance.Hex("#7FCF5C"));
                    Disc(b, at, s, new Vector2(-0.18f, 0.88f), 0.12f, Balance.Hex("#9FDB7C"));
                    Disc(b, at, s, new Vector2(0.55f, 0.12f), 0.16f, Balance.Hex("#6DCBF5"));
                    break;
            }
        }

        public void SetFogVisible(bool v)
        {
            if (fogQuad != null) fogQuad.SetActive(v);
        }
    }
}
