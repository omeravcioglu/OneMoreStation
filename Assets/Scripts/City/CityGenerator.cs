using System;
using System.Collections.Generic;
using UnityEngine;

namespace Metro
{
    /// <summary>
    /// Procedurally builds a coastal city from a seed: coastline, rivers, lakes, parks,
    /// Voronoi districts with types, a population/jobs cell grid and the fog reveal order.
    /// </summary>
    public static class CityGenerator
    {
        public const int InitiallyRevealed = 2;
        public const int DistrictCount = 48;

        public static CityModel Generate(int seed, string cityName)
        {
            var rng = new Rng(seed);
            var city = new CityModel
            {
                seed = seed,
                name = cityName,
                width = Balance.MapWidth,
                height = Balance.MapHeight,
                cellSize = Balance.CellSize
            };
            city.gw = Mathf.CeilToInt(city.width / city.cellSize);
            city.gh = Mathf.CeilToInt(city.height / city.cellSize);
            float nx = rng.Range(0f, 900f), ny = rng.Range(0f, 900f);

            GenerateCoast(city, rng, nx);
            GenerateRivers(city, rng);
            city.water.FinalizeShapes();
            ChooseCenter(city, rng);
            GenerateLakes(city, rng);
            city.water.FinalizeShapes();
            GenerateParks(city, rng);
            GenerateDistricts(city, rng);
            ComputeCells(city, nx, ny);
            ComputeBorders(city);
            PlanReveals(city, rng);
            InitialPopulation(city);
            city.RefreshAggregates();
            return city;
        }

        // ------------------------------------------------------------------ water

        static void GenerateCoast(CityModel city, Rng rng, float nx)
        {
            var w = city.water;
            w.hasCoast = true;
            w.coastStep = 0.25f;
            w.coastX0 = -8f;
            int n = Mathf.CeilToInt((city.width + 16f) / w.coastStep) + 1;
            w.coastY = new float[n];
            float baseY = city.height * rng.Range(0.09f, 0.14f);
            float a1 = rng.Range(1.0f, 2.4f), f1 = rng.Range(0.5f, 1.1f), p1 = rng.Range(0f, 6.283f);
            float a2 = rng.Range(0.5f, 1.2f), f2 = rng.Range(1.6f, 3.0f), p2 = rng.Range(0f, 6.283f);
            float a3 = rng.Range(0.15f, 0.4f), f3 = rng.Range(5f, 9f), p3 = rng.Range(0f, 6.283f);
            float bayX = rng.Range(city.width * 0.2f, city.width * 0.8f);
            float bayW = rng.Range(2.5f, 5.5f);
            float bayDepth = rng.Range(-2.0f, 4.5f);
            for (int i = 0; i < n; i++)
            {
                float x = w.coastX0 + i * w.coastStep;
                float t = x / city.width;
                float y = baseY
                          + a1 * Mathf.Sin(6.283f * f1 * t + p1)
                          + a2 * Mathf.Sin(6.283f * f2 * t + p2)
                          + a3 * Mathf.Sin(6.283f * f3 * t + p3)
                          + bayDepth * Mathf.Exp(-Mathf.Pow((x - bayX) / bayW, 2f))
                          + (Mathf.PerlinNoise(x * 0.45f + nx, 3.7f) - 0.5f) * 0.9f;
                w.coastY[i] = Mathf.Clamp(y, 1.2f, city.height * 0.3f);
            }
        }

        static void GenerateRivers(CityModel city, Rng rng)
        {
            var w = city.water;
            float startX = city.width * rng.Range(0.3f, 0.7f);
            float mouthX = Mathf.Clamp(startX + rng.Range(-city.width * 0.22f, city.width * 0.22f), city.width * 0.18f, city.width * 0.82f);
            var main = BuildRiver(city, rng, new Vector2(startX, city.height + 3f), mouthX, 0.09f, 0.24f);
            main.name = "River";
            w.rivers.Add(main);

            if (rng.Chance(0.65f) && main.points.Count > 8)
            {
                bool west = rng.Chance(0.5f);
                var start = new Vector2(west ? -3f : city.width + 3f, city.height * rng.Range(0.5f, 0.85f));
                int joinIdx = Mathf.Clamp((int)(main.points.Count * rng.Range(0.3f, 0.55f)), 1, main.points.Count - 2);
                var trib = BuildTributary(rng, start, main.points[joinIdx], 0.05f, 0.11f);
                trib.name = "Brook";
                w.rivers.Add(trib);
            }
        }

        static River BuildRiver(CityModel city, Rng rng, Vector2 start, float mouthX, float hw0, float hw1)
        {
            var ctrl = new List<Vector2> { start };
            float y = start.y, x = start.x;
            float targetY = city.water.CoastY(mouthX) - 2f;
            for (int guard = 0; guard < 60; guard++)
            {
                y -= rng.Range(2.0f, 3.2f);
                float t = Mathf.Clamp01(Mathf.InverseLerp(start.y, targetY, y));
                float aim = Mathf.Lerp(start.x, mouthX, t);
                x = Mathf.Lerp(x, aim, 0.5f) + rng.Range(-2.6f, 2.6f) * (1f - 0.5f * t);
                x = Mathf.Clamp(x, 2f, city.width - 2f);
                ctrl.Add(new Vector2(x, y));
                if (y < city.water.CoastY(x) - 1.5f) break;
            }
            return Smooth(ctrl, rng, hw0, hw1);
        }

        static River BuildTributary(Rng rng, Vector2 start, Vector2 join, float hw0, float hw1)
        {
            var ctrl = new List<Vector2> { start };
            int steps = Mathf.Max(3, Mathf.RoundToInt(Vector2.Distance(start, join) / 2.6f));
            Vector2 dir = (join - start).normalized;
            Vector2 nrm = Geo.Perp(dir);
            for (int i = 1; i < steps; i++)
            {
                float t = i / (float)steps;
                ctrl.Add(Vector2.Lerp(start, join, t) + nrm * (rng.Range(-1.8f, 1.8f) * Mathf.Sin(t * Mathf.PI)));
            }
            ctrl.Add(join);
            return Smooth(ctrl, rng, hw0, hw1);
        }

        static River Smooth(List<Vector2> ctrl, Rng rng, float hw0, float hw1)
        {
            var river = new River();
            var pts = Geo.CatmullRom(ctrl, 10);
            river.points.AddRange(pts);
            float off = rng.Range(0f, 50f);
            for (int i = 0; i < pts.Count; i++)
            {
                float t = i / (float)Mathf.Max(1, pts.Count - 1);
                river.halfWidths.Add(Mathf.Lerp(hw0, hw1, t * t) * (0.85f + 0.3f * Mathf.PerlinNoise(off + t * 9f, 1.3f)));
            }
            return river;
        }

        static void ChooseCenter(CityModel city, Rng rng)
        {
            var w = city.water;
            Vector2 c = new Vector2(city.width * 0.5f, w.CoastY(city.width * 0.5f) + 9f);
            var main = w.rivers.Count > 0 ? w.rivers[0] : null;
            if (main != null)
            {
                float wantY = Mathf.Lerp(w.CoastY(city.width * 0.5f), city.height, rng.Range(0.27f, 0.36f));
                float bestScore = float.MaxValue;
                int bestI = -1;
                for (int i = 0; i < main.points.Count; i++)
                {
                    Vector2 p = main.points[i];
                    if (p.x < city.width * 0.22f || p.x > city.width * 0.78f) continue;
                    float s = Mathf.Abs(p.y - wantY) + Mathf.Abs(p.x - city.width * 0.5f) * 0.3f;
                    if (s < bestScore)
                    {
                        bestScore = s;
                        bestI = i;
                    }
                }
                if (bestI >= 0)
                {
                    Vector2 tan = (main.points[Mathf.Min(bestI + 1, main.points.Count - 1)] - main.points[Mathf.Max(bestI - 1, 0)]).normalized;
                    Vector2 side = Geo.Perp(tan) * (rng.Chance(0.5f) ? 1f : -1f);
                    c = main.points[bestI] + side * (main.halfWidths[bestI] + rng.Range(0.8f, 1.3f));
                }
            }
            for (int k = 0; k < 60 && w.IsWater(c, 0.4f); k++) c += new Vector2(rng.Range(-0.6f, 0.6f), 0.5f);
            city.center = c;
        }

        static void GenerateLakes(CityModel city, Rng rng)
        {
            int count = rng.Range(0, 3);
            for (int attempts = 0; city.water.lakes.Count < count && attempts < 300; attempts++)
            {
                var c = new Vector2(rng.Range(4f, city.width - 4f), rng.Range(city.height * 0.3f, city.height - 4f));
                if ((c - city.center).magnitude < 8f) continue;
                if (city.water.DistanceToWater(c) < 3f) continue;
                city.water.AddLake(Blob(rng, c, rng.Range(0.6f, 1.5f), 0.28f, 40));
            }
        }

        static List<Vector2> Blob(Rng rng, Vector2 c, float r, float rough, int n)
        {
            float ox = rng.Range(0f, 100f), oy = rng.Range(0f, 100f);
            var pts = new List<Vector2>(n);
            for (int i = 0; i < n; i++)
            {
                float a = i / (float)n * Mathf.PI * 2f;
                float nz = Mathf.PerlinNoise(ox + Mathf.Cos(a) * 1.2f, oy + Mathf.Sin(a) * 1.2f);
                float rr = r * (1f + rough * (nz - 0.5f) * 2f);
                pts.Add(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rr);
            }
            return pts;
        }

        static void GenerateParks(CityModel city, Rng rng)
        {
            if (rng.Chance(0.75f))
            {
                for (int k = 0; k < 40; k++)
                {
                    Vector2 c = city.center + rng.InsideUnitCircle() * 2.4f;
                    if ((c - city.center).magnitude < 1.1f) continue;
                    if (city.water.DistanceToWater(c) < 1.0f) continue;
                    city.parks.Add(Blob(rng, c, rng.Range(0.45f, 0.75f), 0.3f, 28));
                    break;
                }
            }
            int count = rng.Range(7, 11);
            for (int attempts = 0; city.parks.Count < count && attempts < 500; attempts++)
            {
                Vector2 c = new Vector2(rng.Range(2f, city.width - 2f), rng.Range(2f, city.height - 2f));
                if (city.water.DistanceToWater(c) < 1.3f) continue;
                if ((c - city.center).magnitude < 2.8f) continue;
                bool overlap = false;
                foreach (var p in city.parks)
                {
                    if ((Geo.Centroid(p) - c).magnitude < 3.8f)
                    {
                        overlap = true;
                        break;
                    }
                }
                if (overlap) continue;
                city.parks.Add(Blob(rng, c, rng.Range(0.45f, 1.2f), 0.32f, 28));
            }
        }

        // ------------------------------------------------------------------ districts

        static float Spacing(float distToCenter) => 3.3f + 0.12f * distToCenter;

        static void GenerateDistricts(CityModel city, Rng rng)
        {
            var centers = new List<Vector2> { city.center };
            for (int k = 0; k < 120 && centers.Count < 2; k++)
            {
                float ang = rng.Range(0f, Mathf.PI * 2f);
                Vector2 c = city.center + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * rng.Range(2.6f, 3.4f);
                if (!city.InBounds(c, 1f) || city.water.IsWater(c, 0.35f)) continue;
                centers.Add(c);
            }
            if (centers.Count < 2) centers.Add(city.center + new Vector2(3f, 0.6f));

            for (int attempts = 0; centers.Count < 90 && attempts < 16000; attempts++)
            {
                Vector2 c = new Vector2(rng.Range(1.5f, city.width - 1.5f), rng.Range(1.0f, city.height - 1.5f));
                if (city.water.IsWater(c, 0.35f)) continue;
                float sp = Spacing((c - city.center).magnitude);
                bool ok = true;
                foreach (var o in centers)
                {
                    float need = 0.5f * (sp + Spacing((o - city.center).magnitude));
                    if ((o - c).sqrMagnitude < need * need)
                    {
                        ok = false;
                        break;
                    }
                }
                if (ok) centers.Add(c);
            }

            // Keep the districts closest to the centre: the region always has the same number of them,
            // and the far countryside belongs to the outermost ones.
            if (centers.Count > DistrictCount)
            {
                var rest = centers.GetRange(2, centers.Count - 2);
                rest.Sort((a, b) => (a - city.center).sqrMagnitude.CompareTo((b - city.center).sqrMagnitude));
                centers.RemoveRange(2, centers.Count - 2);
                centers.AddRange(rest.GetRange(0, DistrictCount - 2));
            }

            var ds = city.districts;
            for (int i = 0; i < centers.Count; i++)
                ds.Add(new District { id = i, center = centers[i], type = DistrictType.Residential });
            ds[0].type = DistrictType.Central;
            ds[1].type = DistrictType.OldTown;

            float Dist(District d) => Vector2.Distance(d.center, city.center);
            foreach (var d in ds)
            {
                if (d.id < 2) continue;
                float dc = Dist(d);
                d.type = dc < 8.5f ? DistrictType.Residential
                    : dc < 17f ? (rng.Chance(0.7f) ? DistrictType.Suburb : DistrictType.Residential)
                    : DistrictType.Suburb;
            }

            bool Plain(District d) => d.type == DistrictType.Residential || d.type == DistrictType.Suburb;

            // Port / industry near the river mouth.
            if (city.water.rivers.Count > 0)
            {
                var mouth = city.water.rivers[0].points[city.water.rivers[0].points.Count - 1];
                District best = null;
                float bd = float.MaxValue;
                foreach (var d in ds)
                {
                    if (!Plain(d) || Dist(d) < 4.5f) continue;
                    float dd = Vector2.Distance(d.center, mouth);
                    if (dd < bd)
                    {
                        bd = dd;
                        best = d;
                    }
                }
                if (best != null) best.type = DistrictType.Industrial;
            }
            AssignRandom(ds, rng, d => Plain(d) && Dist(d) > 4f && Dist(d) < 11f, DistrictType.University);
            AssignRandom(ds, rng, d => Plain(d) && Dist(d) > 7f && Dist(d) < 16f, DistrictType.BusinessPark);
            AssignRandom(ds, rng, d => Plain(d) && Dist(d) > 5f && Dist(d) < 13f, DistrictType.Stadium);
            AssignRandom(ds, rng, d => Plain(d) && Dist(d) > 9f && Dist(d) < 22f, DistrictType.Industrial, 0.6f);
            AssignRandom(ds, rng, d => Plain(d) && Dist(d) > 15f, DistrictType.NewTown);
            AssignRandom(ds, rng, d => Plain(d) && Dist(d) > 15f, DistrictType.NewTown, 0.5f);

            // Airport: one of the farthest districts.
            District far = null;
            float fd = 0f;
            foreach (var d in ds)
            {
                if (!Plain(d)) continue;
                float score = Dist(d) + rng.Range(0f, 4f);
                if (score > fd)
                {
                    fd = score;
                    far = d;
                }
            }
            if (far != null && Dist(far) > 14f) far.type = DistrictType.Airport;

            var used = new HashSet<string>();
            foreach (var d in ds) d.name = NameBank.DistrictName(rng, used, d, city);

            // Voronoi polygons clipped to the map.
            var rect = new List<Vector2> { new Vector2(0, 0), new Vector2(city.width, 0), new Vector2(city.width, city.height), new Vector2(0, city.height) };
            foreach (var d in ds)
            {
                var poly = new List<Vector2>(rect);
                foreach (var o in ds)
                {
                    if (o == d) continue;
                    poly = Geo.ClipHalfPlane(poly, (d.center + o.center) * 0.5f, o.center - d.center);
                    if (poly.Count < 3) break;
                }
                d.polygon = poly;
            }
        }

        static void AssignRandom(List<District> ds, Rng rng, Func<District, bool> pred, DistrictType type, float chance = 1f)
        {
            if (!rng.Chance(chance)) return;
            var cands = new List<District>();
            foreach (var d in ds)
                if (pred(d)) cands.Add(d);
            if (cands.Count > 0) rng.Pick(cands).type = type;
        }

        // ------------------------------------------------------------------ cells

        static void ComputeCells(CityModel city, float nx, float ny)
        {
            int n = city.CellCount;
            city.cellDistrict = new short[n];
            city.cellFlags = new byte[n];
            city.resBase = new float[n];
            city.jobBase = new float[n];
            city.attBase = new float[n];
            city.res = new float[n];
            city.jobs = new float[n];
            city.att = new float[n];
            city.cellAccess = new float[n];
            float area = city.cellSize * city.cellSize;
            var parkBounds = new List<Rect>();
            foreach (var p in city.parks) parkBounds.Add(Geo.Bounds(p));

            var labelSum = new Vector2[city.districts.Count];
            var labelCount = new int[city.districts.Count];

            for (int y = 0; y < city.gh; y++)
            {
                for (int x = 0; x < city.gw; x++)
                {
                    int i = city.CellIndex(x, y);
                    Vector2 p = city.CellCenter(x, y);
                    int di = city.DistrictAt(p);
                    city.cellDistrict[i] = (short)di;
                    bool water = city.water.IsWater(p);
                    bool park = false;
                    if (!water)
                    {
                        for (int k = 0; k < city.parks.Count; k++)
                        {
                            if (parkBounds[k].Contains(p) && Geo.PointInPolygon(p, city.parks[k]))
                            {
                                park = true;
                                break;
                            }
                        }
                    }
                    city.cellFlags[i] = (byte)((water ? CityModel.FlagWater : 0) | (park ? CityModel.FlagPark : 0));
                    if (water) continue;

                    labelSum[di] += p;
                    labelCount[di]++;

                    var d = city.districts[di];
                    var prof = DistrictProfile.For(d.type);
                    float dd = Vector2.Distance(p, d.center);
                    float fr = Mathf.Exp(-Mathf.Pow(dd / prof.resRadius, 1.6f)) * 0.88f + 0.12f;
                    float fj = Mathf.Exp(-Mathf.Pow(dd / prof.jobRadius, 1.6f)) * 0.9f + 0.1f;
                    float dC = Vector2.Distance(p, city.center);
                    float cityFactor = Mathf.Lerp(1f, 0.6f, Mathf.Clamp01((dC - 6f) / 26f));
                    float n1 = 0.6f + 0.8f * Mathf.PerlinNoise(nx + p.x * 0.33f, ny + p.y * 0.33f);
                    float n2 = 0.6f + 0.8f * Mathf.PerlinNoise(ny + p.x * 0.41f + 17f, nx + p.y * 0.41f + 5f);
                    float r = prof.res * fr * n1 * cityFactor * area;
                    float j = prof.jobs * fj * n2 * area;
                    float a = prof.att * fj * n2 * area;
                    if (park)
                    {
                        r *= 0.08f;
                        j *= 0.15f;
                        a = a * 0.5f + 300f * area;
                    }
                    city.resBase[i] = r;
                    city.jobBase[i] = j;
                    city.attBase[i] = a;
                }
            }

            foreach (var d in city.districts)
            {
                Vector2 lp = labelCount[d.id] > 0 ? labelSum[d.id] / labelCount[d.id] : d.center;
                d.labelPos = city.water.IsWater(lp, 0.2f) ? d.center : lp;
            }
        }

        static void ComputeBorders(CityModel city)
        {
            var ds = city.districts;
            for (int i = 0; i < ds.Count; i++)
            {
                var poly = ds[i].polygon;
                int n = poly.Count;
                for (int k = 0; k < n; k++)
                {
                    Vector2 a = poly[k], b = poly[(k + 1) % n];
                    if ((b - a).sqrMagnitude < 1e-6f) continue;
                    Vector2 mid = (a + b) * 0.5f;
                    float di = Vector2.Distance(mid, ds[i].center);
                    int nb = -1;
                    for (int j = 0; j < ds.Count; j++)
                    {
                        if (j == i) continue;
                        if (Mathf.Abs(Vector2.Distance(mid, ds[j].center) - di) < 2e-3f)
                        {
                            nb = j;
                            break;
                        }
                    }
                    if (nb < 0 || nb < i) continue;
                    AddLandRuns(city, a, b);
                }
            }
        }

        static void AddLandRuns(CityModel city, Vector2 a, Vector2 b)
        {
            float len = Vector2.Distance(a, b);
            int steps = Mathf.Max(1, Mathf.CeilToInt(len / 0.1f));
            BorderLine cur = null;
            for (int s = 0; s <= steps; s++)
            {
                Vector2 p = Vector2.Lerp(a, b, s / (float)steps);
                bool land = !city.water.IsWater(p, 0.08f);
                if (land)
                {
                    if (cur == null)
                    {
                        cur = new BorderLine();
                        city.borders.Add(cur);
                    }
                    cur.points.Add(p);
                }
                else cur = null;
            }
            city.borders.RemoveAll(bl => bl.points.Count < 2);
        }

        // ------------------------------------------------------------------ reveal order

        static void PlanReveals(CityModel city, Rng rng)
        {
            var ds = city.districts;
            var rest = new List<District>();
            var keys = new Dictionary<District, float>();
            foreach (var d in ds)
            {
                if (d.id < 2) continue;
                float key = Vector2.Distance(d.center, city.center) + rng.Range(-2.5f, 2.5f);
                if (d.type == DistrictType.Airport) key += 10f;
                keys[d] = key;
                rest.Add(d);
            }
            rest.Sort((a, b) => keys[a].CompareTo(keys[b]));
            var order = new List<District> { ds[0], ds[1] };
            order.AddRange(rest);
            for (int i = 0; i < order.Count; i++)
            {
                order[i].revealOrder = i;
                order[i].revealed = i < InitiallyRevealed;
                order[i].revealedAtDay = order[i].revealed ? 0f : -1f;
                order[i].maturity = order[i].revealed ? (i < 2 ? 1f : 0.85f) : Balance.InitialDistrictMaturity;
                order[i].revealAnim = order[i].revealed ? 1f : 0f;
            }
        }

        /// <summary>Fills current population for open districts inside the play area so a game starts with a living city.</summary>
        public static void InitialPopulation(CityModel city)
        {
            int n = city.CellCount;
            for (int i = 0; i < n; i++)
            {
                int di = city.cellDistrict[i];
                if (di < 0 || !city.IsCellActive(i))
                {
                    city.res[i] = city.jobs[i] = city.att[i] = 0f;
                    continue;
                }
                float m = city.districts[di].maturity * city.cityGrowth;
                city.res[i] = city.resBase[i] * m;
                city.jobs[i] = city.jobBase[i] * m;
                city.att[i] = city.attBase[i] * m;
            }
        }

        public static District DistrictByOrder(CityModel city, int order)
        {
            foreach (var d in city.districts)
                if (d.revealOrder == order) return d;
            return null;
        }
    }
}
