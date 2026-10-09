using System;
using System.Collections.Generic;
using UnityEngine;

namespace Metro
{
    [Serializable]
    public sealed class NewGameOptions
    {
        public int seed = 1234;
        public string cityName = "Port Avalon";
        public int difficulty = 1;
        public bool tutorial;
        public bool demo;
        /// <summary>City size to start at: 0 (small town) for normal games, the last size for demos/tests.</summary>
        public int startTier;
        /// <summary>The city's sizes were earned by growing it from a small town (counts for achievements).</summary>
        public bool earnedSizes;
    }

    /// <summary>
    /// The whole simulation: city, network and every system that runs on it.
    /// Pure C# so it can be stepped headlessly in tests and in the editor.
    /// </summary>
    public sealed class GameState
    {
        public readonly NewGameOptions options;
        public readonly CityModel city;
        public readonly MetroNetwork net;
        public readonly GameClock clock = new GameClock();
        public readonly RoutingSystem routing;
        public readonly DemandModel demand;
        public readonly PassengerSystem pax;
        public readonly TrainSystem trains;
        public readonly Economy econ;
        public readonly SatisfactionModel satisfaction;
        public readonly StatsTracker stats;
        public readonly Advisor advisor;
        public readonly GovernmentSystem gov;
        public readonly CityGrowth growth;
        public readonly ConstructionService build;
        public readonly ProgressionSystem progress;
        /// <summary>Set when this game is a challenge scenario (Challenges.Create).</summary>
        public ChallengeRun challenge;
        /// <summary>Lines sketched with the pencil Plan tool (free, saved with the city).</summary>
        public readonly List<PencilPlan> plans = new List<PencilPlan>();

        public bool growthEnabled = true;
        public bool advisorEnabled = true;
        /// <summary>City Hall: requests, approval, elections, bailouts. Off for the menu's demo city.</summary>
        public bool governmentEnabled = true;

        bool routingDirty = true, demandDirty = true;

        public event Action NetworkChanged;
        public event Action<int> DayEnded;
        public event Action<District> DistrictRevealed;
        public event Action<MetroLine> LineCreated;

        public float CostMultiplier => Balance.CostMultiplier(options.difficulty);
        public float GrowthPace => Balance.GrowthPace(options.difficulty);
        public float FineMultiplier => Balance.FineMultiplier(options.difficulty);

        public GameState(NewGameOptions opts, CityModel prebuiltCity = null)
        {
            options = opts;
            if (opts.startTier == 0) opts.earnedSizes = true;
            city = prebuiltCity ?? CityGenerator.Generate(opts.seed, opts.cityName);
            net = new MetroNetwork(city);
            clock.totalMinutes = Balance.StartMinute;
            routing = new RoutingSystem(this);
            demand = new DemandModel(this);
            pax = new PassengerSystem(this);
            trains = new TrainSystem(this);
            econ = new Economy(this);
            satisfaction = new SatisfactionModel(this);
            stats = new StatsTracker(this);
            advisor = new Advisor(this);
            gov = new GovernmentSystem(this);
            growth = new CityGrowth(this);
            build = new ConstructionService(this);
            progress = new ProgressionSystem(this);
            econ.money = Balance.StartMoney(opts.difficulty);
            econ.today.day = clock.Day;
            growth.Init();
            progress.Setup(opts.startTier);
        }

        public bool Has(Feature f) => progress.Has(f);

        public void MarkNetworkChanged()
        {
            routingDirty = true;
            demandDirty = true;
            NetworkChanged?.Invoke();
        }

        public void MarkServiceChanged()
        {
            routingDirty = true;
            demandDirty = true;
        }

        public void MarkDemandDirty() => demandDirty = true;

        public void RaiseDistrictRevealed(District d) => DistrictRevealed?.Invoke(d);
        public void RaiseLineCreated(MetroLine l) => LineCreated?.Invoke(l);

        /// <summary>Recomputes routing and demand if anything changed since the last minute.</summary>
        public void EnsureFresh()
        {
            if (routingDirty)
            {
                routing.Recompute();
                routingDirty = false;
                demandDirty = true;
            }
            if (demandDirty)
            {
                demand.Recompute();
                demandDirty = false;
            }
        }

        /// <summary>Advances the simulation by dt game minutes, ticking minute-based systems on every boundary.</summary>
        public void Advance(float dtMinutes)
        {
            int guard = 0;
            while (dtMinutes > 1e-6f && guard++ < 20000)
            {
                double nextMinute = Math.Floor(clock.totalMinutes + 1e-7) + 1.0;
                float toNext = (float)(nextMinute - clock.totalMinutes);
                float step = Mathf.Min(Mathf.Min(dtMinutes, 0.25f), Mathf.Max(toNext, 1e-5f));
                trains.Tick(step);
                clock.totalMinutes += step;
                dtMinutes -= step;
                if (clock.totalMinutes >= nextMinute - 1e-6)
                {
                    clock.totalMinutes = nextMinute;
                    MinuteTick();
                }
            }
        }

        void MinuteTick()
        {
            EnsureFresh();
            int minute = Mathf.RoundToInt(clock.MinuteOfDay) % Balance.MinutesPerDay;
            pax.MinuteTick();
            econ.MinuteTick();
            stats.MinuteTick();
            satisfaction.MinuteTick();
            if (growthEnabled) growth.MinuteTick();
            progress.MinuteTick();
            gov.MinuteTick();
            challenge?.MinuteTick();
            if (advisorEnabled && minute % 10 == 0) advisor.Check();
            if (minute % 60 == 0) HourTick();
            if (minute == 0) DayTick();
        }

        void HourTick()
        {
            if (growthEnabled) growth.HourTick();
            demandDirty = true;
            stats.HourTick();
        }

        void DayTick()
        {
            int ended = clock.Day - 1;
            stats.EndDay(ended);
            econ.EndDay();
            satisfaction.EndDay();
            progress.DayTick();
            gov.DayTick();
            challenge?.DayTick();
            if (growthEnabled) growth.DayTick();
            if (advisorEnabled) advisor.DaySummary(ended);
            DayEnded?.Invoke(ended);
        }
    }
}
