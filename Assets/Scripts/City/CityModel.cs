using System.Collections.Generic;
using UnityEngine;

namespace Metro
{
    public enum DistrictType { Central, OldTown, Residential, Suburb, NewTown, Industrial, University, BusinessPark, Airport, Stadium }

    /// <summary>Peak densities (per km²) of a fully grown district and how fast they fall off from its core.</summary>
    public struct DistrictProfile
    {
        public float res, jobs, att, resRadius, jobRadius;

        public DistrictProfile(float res, float jobs, float att, float resRadius, float jobRadius)
        {
            this.res = res;
            this.jobs = jobs;
            this.att = att;
            this.resRadius = resRadius;
            this.jobRadius = jobRadius;
        }

        public static DistrictProfile For(DistrictType t)
        {
            switch (t)
            {
                case DistrictType.Central: return new DistrictProfile(7000, 40000, 9000, 3.0f, 2.2f);
                case DistrictType.OldTown: return new DistrictProfile(16000, 12000, 8000, 2.4f, 2.0f);
                case DistrictType.Residential: return new DistrictProfile(13000, 3000, 1500, 2.6f, 2.0f);
                case DistrictType.Suburb: return new DistrictProfile(7000, 1400, 600, 3.0f, 2.2f);
                case DistrictType.NewTown: return new DistrictProfile(11000, 3500, 1500, 2.2f, 1.8f);
                case DistrictType.Industrial: return new DistrictProfile(1500, 10000, 300, 2.5f, 2.6f);
                case DistrictType.University: return new DistrictProfile(9000, 5000, 9000, 1.9f, 1.6f);
                case DistrictType.BusinessPark: return new DistrictProfile(2500, 20000, 1500, 2.0f, 1.9f);
                case DistrictType.Airport: return new DistrictProfile(0, 7000, 14000, 1.5f, 2.2f);
                case DistrictType.Stadium: return new DistrictProfile(6000, 2500, 7000, 2.0f, 1.6f);
            }
            return new DistrictProfile(5000, 1000, 500, 2.5f, 2f);
        }
    }

    public sealed class District
    {
        public int id;
        public string name;
        public DistrictType type;
        public Vector2 center;
        public List<Vector2> polygon = new List<Vector2>();
        public Vector2 labelPos;
        public bool revealed;
        public int revealOrder;
        public float revealedAtDay = -1f;
        public float maturity = 1f;          // 0..1: how much of its potential population has moved in
        public float revealAnim = 1f;        // 0..1 fog clearing animation (render only)

        // Aggregates, refreshed hourly.
        public float population, jobs, servedPopulation, servedJobs;
        public int stationCount;

        public float Coverage => population > 1f ? Mathf.Clamp01(servedPopulation / population) : 0f;

        public string TypeLabel
        {
            get
            {
                switch (type)
                {
                    case DistrictType.Central: return "City centre";
                    case DistrictType.OldTown: return "Historic quarter";
                    case DistrictType.Residential: return "Residential";
                    case DistrictType.Suburb: return "Suburb";
                    case DistrictType.NewTown: return "New town";
                    case DistrictType.Industrial: return "Industrial";
                    case DistrictType.University: return "University";
                    case DistrictType.BusinessPark: return "Business park";
                    case DistrictType.Airport: return "Airport";
                    case DistrictType.Stadium: return "Stadium & arenas";
                }
                return "District";
            }
        }
    }

    public sealed class BorderLine
    {
        public readonly List<Vector2> points = new List<Vector2>();
    }

    /// <summary>The city: geography, districts and a grid of cells holding residents, jobs and attractions.</summary>
    public sealed class CityModel
    {
        public const byte FlagWater = 1, FlagPark = 2;

        public int seed;
        public string name;
        public float width, height, cellSize;
        public int gw, gh;
        public Vector2 center;
        public readonly WaterSystem water = new WaterSystem();
        public readonly List<List<Vector2>> parks = new List<List<Vector2>>();
        public readonly List<District> districts = new List<District>();
        public readonly List<BorderLine> borders = new List<BorderLine>();

        public short[] cellDistrict;
        public byte[] cellFlags;
        public float[] resBase, jobBase, attBase;   // fully grown values
        public float[] res, jobs, att;               // current values
        public float[] cellAccess;                   // 0..1 metro access, written by the demand model
        public float cityGrowth = 1f;                // long-term densification multiplier

        /// <summary>The part of the region the player can see and build in. It grows as the city grows.</summary>
        public Rect playArea = new Rect(0f, 0f, 1e6f, 1e6f);

        public float revealedPopulation, revealedJobs, servedPopulation;

        public int CellCount => gw * gh;
        public int CellIndex(int x, int y) => y * gw + x;
        public Vector2 CellCenter(int x, int y) => new Vector2((x + 0.5f) * cellSize, (y + 0.5f) * cellSize);
        public Vector2 CellCenter(int i) => CellCenter(i % gw, i / gw);
        public Rect Bounds => new Rect(0f, 0f, width, height);

        public bool CellCoords(Vector2 p, out int x, out int y)
        {
            x = Mathf.FloorToInt(p.x / cellSize);
            y = Mathf.FloorToInt(p.y / cellSize);
            return x >= 0 && y >= 0 && x < gw && y < gh;
        }

        public bool InBounds(Vector2 p, float margin = 0f) =>
            p.x >= margin && p.y >= margin && p.x <= width - margin && p.y <= height - margin;

        public int DistrictAt(Vector2 p)
        {
            int best = -1;
            float bd = float.MaxValue;
            for (int i = 0; i < districts.Count; i++)
            {
                float d = (districts[i].center - p).sqrMagnitude;
                if (d < bd)
                {
                    bd = d;
                    best = i;
                }
            }
            return best;
        }

        public District DistrictObjAt(Vector2 p)
        {
            int i = DistrictAt(p);
            return i >= 0 ? districts[i] : null;
        }

        public bool InPlayArea(Vector2 p, float margin = 0f) =>
            p.x >= playArea.xMin + margin && p.y >= playArea.yMin + margin && p.x <= playArea.xMax - margin && p.y <= playArea.yMax - margin;

        public bool IsRevealed(Vector2 p)
        {
            if (!InBounds(p) || !InPlayArea(p)) return false;
            int d = DistrictAt(p);
            return d >= 0 && districts[d].revealed;
        }

        public bool IsCellRevealed(int i)
        {
            int d = cellDistrict[i];
            return d >= 0 && districts[d].revealed;
        }

        public bool CellInArea(int i) => InPlayArea(CellCenter(i));

        /// <summary>A cell people can live in right now: land, in an open district, inside the play area.</summary>
        public bool IsCellActive(int i) => !IsCellWater(i) && IsCellRevealed(i) && CellInArea(i);

        public bool IsCellWater(int i) => (cellFlags[i] & FlagWater) != 0;

        /// <summary>Recomputes district totals from the cell grid.</summary>
        public void RefreshAggregates()
        {
            foreach (var d in districts)
            {
                d.population = 0f;
                d.jobs = 0f;
                d.servedPopulation = 0f;
                d.servedJobs = 0f;
            }
            float rp = 0f, rj = 0f, sp = 0f;
            int n = CellCount;
            for (int i = 0; i < n; i++)
            {
                int di = cellDistrict[i];
                if (di < 0) continue;
                var d = districts[di];
                float r = res[i], j = jobs[i];
                if (r <= 0f && j <= 0f) continue;
                float acc = cellAccess != null ? cellAccess[i] : 0f;
                d.population += r;
                d.jobs += j;
                d.servedPopulation += r * acc;
                d.servedJobs += j * acc;
                if (d.revealed)
                {
                    rp += r;
                    rj += j;
                    sp += r * acc;
                }
            }
            revealedPopulation = rp;
            revealedJobs = rj;
            servedPopulation = sp;
        }

        public Rect RevealedBounds()
        {
            bool any = false;
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < CellCount; i++)
            {
                if (!IsCellActive(i)) continue;
                Vector2 c = CellCenter(i);
                any = true;
                minX = Mathf.Min(minX, c.x);
                minY = Mathf.Min(minY, c.y);
                maxX = Mathf.Max(maxX, c.x);
                maxY = Mathf.Max(maxY, c.y);
            }
            if (!any) return new Rect(center.x - 5f, center.y - 5f, 10f, 10f);
            return Rect.MinMaxRect(minX - cellSize, minY - cellSize, maxX + cellSize, maxY + cellSize);
        }

        public float CoverageRatio => revealedPopulation > 1f ? Mathf.Clamp01(servedPopulation / revealedPopulation) : 0f;
    }
}
