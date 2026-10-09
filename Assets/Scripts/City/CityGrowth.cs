using System.Collections.Generic;
using UnityEngine;

namespace Metro
{
    /// <summary>
    /// The city grows on its own: districts emerge from the clouds on a schedule the player does not
    /// control (only inside the current map; the map itself grows with the player's progress),
    /// new districts fill up over a couple of days, and the whole city slowly densifies —
    /// faster near good metro service.
    /// </summary>
    public sealed class CityGrowth
    {
        readonly GameState gs;
        readonly Rng rng;
        public double nextRevealAt = double.MaxValue;
        public bool revealsPaused;

        public CityGrowth(GameState gs)
        {
            this.gs = gs;
            rng = new Rng(gs.options.seed * 17 + 3);
        }

        public void Init() => ScheduleNext(gs.clock.Day + 1);

        int RevealedCount
        {
            get
            {
                int n = 0;
                foreach (var d in gs.city.districts)
                    if (d.revealed) n++;
                return n;
            }
        }

        int IntervalDays()
        {
            int k = RevealedCount - CityGenerator.InitiallyRevealed;
            float days = (0.9f + 0.05f * k) / gs.GrowthPace;
            return Mathf.Clamp(Mathf.RoundToInt(days), 1, 3);
        }

        /// <summary>The next district the city will open: lowest reveal order inside the play area.</summary>
        public District NextInArea()
        {
            District best = null;
            var city = gs.city;
            foreach (var d in city.districts)
            {
                if (d.revealed || !city.InPlayArea(d.center)) continue;
                if (best == null || d.revealOrder < best.revealOrder) best = d;
            }
            return best;
        }

        public void ScheduleNext(int day)
        {
            nextRevealAt = day * (double)Balance.MinutesPerDay + rng.Range(9.5f, 15f) * 60f;
        }

        public void MinuteTick()
        {
            if (revealsPaused || gs.clock.totalMinutes < nextRevealAt) return;
            var d = NextInArea();
            if (d != null) Reveal(d, false);
            ScheduleNext(gs.clock.Day + (d != null ? IntervalDays() : 1));
        }

        /// <summary>Opens up to count districts inside the play area right now.</summary>
        public List<District> RevealInArea(int count, bool silent)
        {
            var list = new List<District>();
            for (int i = 0; i < count; i++)
            {
                var d = NextInArea();
                if (d == null) break;
                Reveal(d, silent);
                list.Add(d);
            }
            return list;
        }

        /// <summary>Reveals districts instantly without messages (demo city, tests).</summary>
        public void RevealSilently(int count)
        {
            RevealInArea(count, true);
            CityGenerator.InitialPopulation(gs.city);
            gs.city.RefreshAggregates();
            ScheduleNext(gs.clock.Day + 1);
            gs.MarkDemandDirty();
        }

        void Reveal(District d, bool silent)
        {
            var city = gs.city;
            d.revealed = true;
            d.revealedAtDay = (float)(gs.clock.totalMinutes / Balance.MinutesPerDay);
            d.maturity = silent ? 1f : Balance.InitialDistrictMaturity;
            d.revealAnim = silent ? 1f : 0f;
            float g = city.cityGrowth * d.maturity;
            for (int i = 0; i < city.CellCount; i++)
            {
                if (city.cellDistrict[i] != d.id || !city.IsCellActive(i)) continue;
                city.res[i] = city.resBase[i] * g;
                city.jobs[i] = city.jobBase[i] * g;
                city.att[i] = city.attBase[i] * g;
            }
            city.RefreshAggregates();
            gs.MarkDemandDirty();
            if (silent) return;
            gs.advisor.Post(MsgCategory.City, MsgSeverity.Info,
                $"The city is expanding: {d.name} ({d.TypeLabel.ToLowerInvariant()}) is opening up.", district: d.id);
            gs.RaiseDistrictRevealed(d);
        }

        /// <summary>Kept for older call sites: reveals the next district in the play area with an announcement.</summary>
        public District RevealNext()
        {
            var d = NextInArea();
            if (d != null) Reveal(d, false);
            ScheduleNext(gs.clock.Day + IntervalDays());
            return d;
        }

        public void HourTick()
        {
            var city = gs.city;
            float pace = gs.GrowthPace;
            foreach (var d in city.districts)
                if (d.revealed && d.maturity < 1f) d.maturity = Mathf.Min(1f, d.maturity + Balance.DistrictMaturityPerHour * pace);

            float g = city.cityGrowth;
            int n = city.CellCount;
            for (int i = 0; i < n; i++)
            {
                int di = city.cellDistrict[i];
                if (di < 0 || !city.IsCellActive(i)) continue;
                var d = city.districts[di];
                float m = d.maturity * g * (1f + Balance.TransitGrowthBonus * city.cellAccess[i]);
                city.res[i] += (city.resBase[i] * m - city.res[i]) * 0.06f;
                city.jobs[i] += (city.jobBase[i] * m - city.jobs[i]) * 0.06f;
                city.att[i] += (city.attBase[i] * m - city.att[i]) * 0.06f;
            }
            city.RefreshAggregates();
        }

        public void DayTick()
        {
            var city = gs.city;
            city.cityGrowth = Mathf.Min(Balance.MaxCityGrowth, city.cityGrowth + Balance.CityGrowthPerDay * gs.GrowthPace);
            if (gs.progress.Has(Feature.CityGrowth) && rng.Chance(0.35f)) RandomEvent();
        }

        void RandomEvent()
        {
            var city = gs.city;
            District pick = null;
            for (int tries = 0; tries < 12; tries++)
            {
                var d = city.districts[rng.Range(0, city.districts.Count)];
                if (d.revealed && city.InPlayArea(d.center))
                {
                    pick = d;
                    break;
                }
            }
            if (pick == null) return;
            bool jobs = rng.Chance(0.5f);
            for (int i = 0; i < city.CellCount; i++)
            {
                if (city.cellDistrict[i] != pick.id) continue;
                if (Vector2.Distance(city.CellCenter(i), pick.center) > 1.6f) continue;
                if (jobs) city.jobBase[i] *= 1.18f;
                else city.resBase[i] *= 1.15f;
            }
            string text = jobs
                ? $"A new office development opened in {pick.name}. Expect more commuters heading there."
                : $"A large housing project is going up in {pick.name}. More residents will need trains.";
            gs.advisor.Post(MsgCategory.City, MsgSeverity.Info, text, district: pick.id);
        }
    }
}
