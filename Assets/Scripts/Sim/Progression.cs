using System;
using System.Collections.Generic;
using UnityEngine;

namespace Metro
{
    /// <summary>Things the player can do that are introduced one city size at a time.</summary>
    public enum Feature
    {
        BuildLine, Depot, Demolish, InsertStation, Wagons, CityGrowth, CityWindow,
        Finance, Overlays, Stats, Upgrades, Surcharge, Loans, CityTargets, DepotExpansion, Council
    }

    public enum GoalKind { OpenLine, RidersDay, ConnectDistrict, Interchange, DailyProfit, DaySatisfaction, ServedResidents, Upgrade, LinesRunning, CouncilRequests, Approval }

    public sealed class GoalDef
    {
        public readonly GoalKind kind;
        public readonly float target;

        public GoalDef(GoalKind kind, float target)
        {
            this.kind = kind;
            this.target = target;
        }
    }

    public sealed class TierDef
    {
        public string name;
        public string blurb;
        public int areaDistricts;   // the map grows to fit this many districts (0 = the whole region)
        public int revealTotal;     // districts open right after reaching the size
        public int maxLines;
        public float grant;
        public Feature[] unlocks = new Feature[0];
        public GoalDef[] goals = new GoalDef[0];   // goals that lead to the NEXT size
    }

    public sealed class Goal
    {
        public GoalKind kind;
        public float target;
        public bool done;
        public float progress;
        public string title = "";
        public string status = "";
        public int districtId = -1;
        public int baseline;            // CouncilRequests: requests completed before this size
    }

    public sealed class TierReachedInfo
    {
        public int tier;
        public float grant;
        public Rect oldArea, newArea;
        public readonly List<District> newDistricts = new List<District>();
    }

    /// <summary>
    /// The learning curve. Every city starts as a small town on a small map with a handful of tools.
    /// Meeting the goals of a size grows the city overnight: the map frame pushes outward, new
    /// districts appear, the line licence grows and a few new mechanics unlock with an explanation.
    /// Targets are tuned with MetroHeadless.RunProgressionTest -calib (a simple bot reaches each
    /// size's rider target within about a day of good play; new players need a little longer).
    /// </summary>
    public sealed class ProgressionSystem
    {
        public static readonly TierDef[] Tiers =
        {
            new TierDef
            {
                name = "Small town", blurb = "One line, a few trains. Learn how people ride.",
                areaDistricts = 3, revealTotal = 2, maxLines = 1, grant = 0f,
                unlocks = new[] { Feature.BuildLine, Feature.Depot, Feature.Demolish },
                goals = new[] { new GoalDef(GoalKind.OpenLine, 4f), new GoalDef(GoalKind.RidersDay, 15_000f) }
            },
            new TierDef
            {
                name = "Growing town", blurb = "A second line, transfers, and a town that starts to spread.",
                areaDistricts = 7, revealTotal = 4, maxLines = 2, grant = 1_000_000f,
                unlocks = new[] { Feature.InsertStation, Feature.Wagons, Feature.CityGrowth, Feature.CityWindow },
                goals = new[] { new GoalDef(GoalKind.ConnectDistrict, 0.25f), new GoalDef(GoalKind.Interchange, 1f), new GoalDef(GoalKind.RidersDay, 40_000f) }
            },
            new TierDef
            {
                name = "Market town", blurb = "Money matters now: fares, ads, the city's data, and a council that wants things.",
                areaDistricts = 12, revealTotal = 7, maxLines = 3, grant = 1_500_000f,
                unlocks = new[] { Feature.Finance, Feature.Overlays, Feature.Stats, Feature.Council },
                goals = new[] { new GoalDef(GoalKind.DailyProfit, 120_000f), new GoalDef(GoalKind.CouncilRequests, 1f), new GoalDef(GoalKind.RidersDay, 75_000f) }
            },
            new TierDef
            {
                name = "City", blurb = "Busy stations need upgrades, and City Hall starts giving orders.",
                areaDistricts = 19, revealTotal = 11, maxLines = 5, grant = 2_500_000f,
                unlocks = new[] { Feature.Upgrades, Feature.Surcharge, Feature.Loans, Feature.CityTargets, Feature.DepotExpansion },
                goals = new[] { new GoalDef(GoalKind.Upgrade, 1f), new GoalDef(GoalKind.ServedResidents, 350_000f), new GoalDef(GoalKind.RidersDay, 140_000f) }
            },
            new TierDef
            {
                name = "Big city", blurb = "The old core gets crowded. Time to think in networks, not lines.",
                areaDistricts = 30, revealTotal = 17, maxLines = 8, grant = 4_000_000f,
                goals = new[] { new GoalDef(GoalKind.LinesRunning, 6f), new GoalDef(GoalKind.Approval, 60f), new GoalDef(GoalKind.RidersDay, 260_000f) }
            },
            new TierDef
            {
                name = "Metropolis", blurb = "The whole region is yours. Keep millions moving.",
                areaDistricts = 0, revealTotal = 24, maxLines = 99, grant = 6_000_000f
            }
        };

        readonly GameState gs;
        public int tier;
        public readonly List<Goal> goals = new List<Goal>();
        public bool holdTierUps;
        float bestRidersThisTier;
        bool announcedReady;
        /// <summary>True while a new size is opening its districts (the growth card announces them).</summary>
        public bool Growing { get; private set; }
        public event Action<TierReachedInfo> TierReached;

        /// <summary>Demo only: the demo is over. True when the play-time cap ended it, false when the town finished it.</summary>
        public event Action<bool> DemoFinished;
        /// <summary>Demo only: set by the game once the play-time cap is reached; the demo ends that evening.</summary>
        public bool demoTimeUp;
        public bool DemoOver { get; private set; }
        /// <summary>Demo only: the town has reached the demo's last size, so it won't grow any further.</summary>
        public bool AtDemoEnd => Demo.On && tier >= Demo.LastTier;

        public ProgressionSystem(GameState gs)
        {
            this.gs = gs;
        }

        public int MaxTier => Tiers.Length - 1;
        public bool IsMax => tier >= MaxTier;
        public TierDef Current => Tiers[tier];
        public TierDef Next => IsMax ? null : Tiers[tier + 1];
        public int MaxLines => Current.maxLines;
        public int GoalsDone
        {
            get
            {
                int n = 0;
                foreach (var g in goals)
                    if (g.done) n++;
                return n;
            }
        }

        public bool Has(Feature f) => UnlockTier(f) <= tier;

        public static int UnlockTier(Feature f)
        {
            for (int t = 0; t < Tiers.Length; t++)
                if (Array.IndexOf(Tiers[t].unlocks, f) >= 0) return t;
            return 0;
        }

        public static string LockedMessage(Feature f) => $"{FeatureTitle(f)} unlocks when your city becomes a {Tiers[UnlockTier(f)].name}.";

        // ------------------------------------------------------------------ setup

        /// <summary>Puts a fresh state at a starting size (0 for normal games; the max for demos and tests).</summary>
        public void Setup(int startTier)
        {
            tier = Mathf.Clamp(startTier, 0, MaxTier);
            gs.city.playArea = AreaFor(tier);
            var growth = gs.growth;
            int open = 0;
            foreach (var d in gs.city.districts)
                if (d.revealed) open++;
            if (open < Current.revealTotal) growth.RevealInArea(Current.revealTotal - open, true);
            CityGenerator.InitialPopulation(gs.city);
            gs.city.RefreshAggregates();
            growth.revealsPaused = !Has(Feature.CityGrowth);
            growth.ScheduleNext(gs.clock.Day + 1);
            gs.gov.SyncWithProgress(false);
            MakeGoals(null);
            gs.MarkDemandDirty();
        }

        /// <summary>The playable rectangle for a size: grows to fit the next districts, never shrinks.</summary>
        public Rect AreaFor(int t)
        {
            var city = gs.city;
            var full = new Rect(0f, 0f, city.width, city.height);
            Rect area = default;
            for (int k = 0; k <= t; k++)
            {
                var def = Tiers[k];
                Rect r;
                if (def.areaDistricts <= 0 || def.areaDistricts >= city.districts.Count) r = full;
                else
                {
                    var ordered = new List<District>(city.districts);
                    ordered.Sort((a, b) => a.revealOrder.CompareTo(b.revealOrder));
                    float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
                    for (int i = 0; i < def.areaDistricts && i < ordered.Count; i++)
                    {
                        var c = ordered[i].center;
                        minX = Mathf.Min(minX, c.x);
                        minY = Mathf.Min(minY, c.y);
                        maxX = Mathf.Max(maxX, c.x);
                        maxY = Mathf.Max(maxY, c.y);
                    }
                    float margin = 2.8f + 0.4f * k;
                    r = Rect.MinMaxRect(minX - margin, minY - margin, maxX + margin, maxY + margin);
                    // Keep a pleasant landscape shape and a minimum size.
                    float minW = 12f + 6f * k, minH = 8.5f + 4f * k;
                    if (r.width < minW) r = Grow(r, (minW - r.width) * 0.5f, 0f);
                    if (r.height < minH) r = Grow(r, 0f, (minH - r.height) * 0.5f);
                    if (r.width < r.height * 1.35f) r = Grow(r, (r.height * 1.35f - r.width) * 0.5f, 0f);
                    if (r.height < r.width / 1.9f) r = Grow(r, 0f, (r.width / 1.9f - r.height) * 0.5f);
                    r = FitInside(r, full);
                }
                area = k == 0 ? r : Union(area, r);
            }
            return area;
        }

        static Rect Grow(Rect r, float dx, float dy) => Rect.MinMaxRect(r.xMin - dx, r.yMin - dy, r.xMax + dx, r.yMax + dy);

        static Rect Union(Rect a, Rect b) =>
            Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin), Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));

        static Rect FitInside(Rect r, Rect bounds)
        {
            float w = Mathf.Min(r.width, bounds.width), h = Mathf.Min(r.height, bounds.height);
            float x = Mathf.Clamp(r.x, bounds.xMin, bounds.xMax - w);
            float y = Mathf.Clamp(r.y, bounds.yMin, bounds.yMax - h);
            return new Rect(x, y, w, h);
        }

        // ------------------------------------------------------------------ goals

        void MakeGoals(List<District> newDistricts)
        {
            goals.Clear();
            bestRidersThisTier = 0f;
            announcedReady = false;
            if (IsMax) return;
            foreach (var def in Current.goals)
            {
                var g = new Goal { kind = def.kind, target = def.target, baseline = gs.gov.completed };
                if (def.kind == GoalKind.ConnectDistrict)
                {
                    District pick = null;
                    if (newDistricts != null)
                        foreach (var d in newDistricts)
                            if (pick == null || d.revealOrder < pick.revealOrder) pick = d;
                    if (pick == null)
                    {
                        foreach (var d in gs.city.districts)
                            if (d.revealed && d.id > 1 && (pick == null || d.revealOrder > pick.revealOrder)) pick = d;
                    }
                    g.districtId = pick != null ? pick.id : -1;
                }
                g.title = GoalTitle(g);
                goals.Add(g);
            }
            Evaluate();
        }

        string GoalTitle(Goal g)
        {
            switch (g.kind)
            {
                case GoalKind.OpenLine: return $"Open a line with {g.target:0}+ stations and 2+ trains";
                case GoalKind.RidersDay: return $"Carry {Fmt.Count(g.target)} trips in one day";
                case GoalKind.ConnectDistrict:
                    return g.districtId >= 0 ? $"Connect {gs.city.districts[g.districtId].name}" : "Connect a new district";
                case GoalKind.Interchange: return "Build an interchange between two lines";
                case GoalKind.DailyProfit: return $"End a day with {Fmt.Money(g.target)}+ operating profit";
                case GoalKind.DaySatisfaction: return $"End a day with {Fmt.Pct(g.target)}+ rider satisfaction";
                case GoalKind.ServedResidents: return $"Put {Fmt.Count(g.target)} residents near a station";
                case GoalKind.Upgrade: return "Upgrade a station";
                case GoalKind.LinesRunning: return $"Run {g.target:0} lines with trains";
                case GoalKind.CouncilRequests: return g.target <= 1f ? "Complete a City Hall request" : $"Complete {g.target:0} City Hall requests";
                case GoalKind.Approval: return $"Reach {g.target:0}% City Hall approval";
            }
            return "";
        }

        /// <summary>All goals met: the city grows at the next dawn.</summary>
        public bool Ready => !IsMax && goals.Count > 0 && GoalsDone == goals.Count;

        public void MinuteTick()
        {
            bestRidersThisTier = Mathf.Max(bestRidersThisTier, gs.stats.ridersToday);
            int minute = Mathf.RoundToInt(gs.clock.MinuteOfDay) % Balance.MinutesPerDay;
            if (minute % 5 == 0) Evaluate();
            if (Demo.On && !DemoOver && CheckDemoEnd(minute)) return;
            // Growth happens overnight: at most one size per day, and never in the middle of a rush.
            if (minute == Balance.NightEnd && Ready && !holdTierUps && !AtDemoEnd) TierUp();
        }

        /// <summary>
        /// Demo: the town that is ready to leave the demo's last size ends the demo at dawn, instead of
        /// growing. After the play-time cap, the demo ends once the evening rush is over.
        /// </summary>
        bool CheckDemoEnd(int minute)
        {
            bool finished = minute == Balance.NightEnd && Ready && AtDemoEnd && !holdTierUps;
            bool timeUp = demoTimeUp && (minute >= Demo.EveningMinute || minute < Balance.NightEnd);
            if (!finished && !timeUp) return false;
            DemoOver = true;
            DemoFinished?.Invoke(!finished);
            return true;
        }

        /// <summary>Day-end goals are judged on the day that just closed.</summary>
        public void DayTick()
        {
            var y = gs.econ.Yesterday;
            foreach (var g in goals)
            {
                if (g.done) continue;
                if (g.kind == GoalKind.DailyProfit && y != null && y.OperatingNet >= g.target) g.done = true;
                if (g.kind == GoalKind.DaySatisfaction && gs.satisfaction.yesterday >= g.target && gs.net.LineCount > 0) g.done = true;
            }
            Evaluate();
        }

        public void Evaluate()
        {
            var net = gs.net;
            foreach (var g in goals)
            {
                switch (g.kind)
                {
                    case GoalKind.OpenLine:
                    {
                        float best = 0f;
                        int bs = 0, bt = 0;
                        foreach (var l in net.AliveLines())
                        {
                            float p = Mathf.Min(1f, l.stops.Count / g.target) * 0.5f + Mathf.Min(1f, l.trains.Count / 2f) * 0.5f;
                            if (p > best)
                            {
                                best = p;
                                bs = l.stops.Count;
                                bt = l.trains.Count;
                            }
                        }
                        g.progress = best;
                        if (!g.done && best >= 0.999f) g.done = true;
                        g.status = $"{Mathf.Min(bs, (int)g.target)}/{g.target:0} stations · {Mathf.Min(bt, 2)}/2 trains";
                        break;
                    }
                    case GoalKind.RidersDay:
                    {
                        float best = Mathf.Max(bestRidersThisTier, gs.stats.ridersToday);
                        g.progress = Mathf.Clamp01(best / g.target);
                        if (!g.done && best >= g.target) g.done = true;
                        g.status = $"{Fmt.Count(gs.stats.ridersToday)} today · best {Fmt.Count(best)} of {Fmt.Count(g.target)}";
                        break;
                    }
                    case GoalKind.ConnectDistrict:
                    {
                        var d = g.districtId >= 0 ? gs.city.districts[g.districtId] : null;
                        float c = d != null ? d.Coverage : 0f;
                        g.progress = Mathf.Clamp01(c / g.target);
                        if (!g.done && c >= g.target) g.done = true;
                        g.status = d != null ? $"{Fmt.Pct(c)} of {d.name} served (need {Fmt.Pct(g.target)})" : "";
                        break;
                    }
                    case GoalKind.Interchange:
                    {
                        bool any = false;
                        foreach (var s in net.AliveStations())
                            if (s.lines.Count >= 2) any = true;
                        g.progress = any ? 1f : 0f;
                        if (any) g.done = true;
                        g.status = any ? "Lines meet" : "Draw a line through a station of another line";
                        break;
                    }
                    case GoalKind.DailyProfit:
                    {
                        float today = gs.econ.today.OperatingNet;
                        g.progress = Mathf.Clamp01(today / g.target);
                        g.status = $"so far today {Fmt.MoneySigned(today)}";
                        break;
                    }
                    case GoalKind.DaySatisfaction:
                    {
                        g.progress = Mathf.Clamp01(gs.satisfaction.score / g.target);
                        g.status = $"now {gs.satisfaction.Percent}% · judged at midnight";
                        break;
                    }
                    case GoalKind.ServedResidents:
                    {
                        float served = gs.city.servedPopulation;
                        g.progress = Mathf.Clamp01(served / g.target);
                        if (!g.done && served >= g.target) g.done = true;
                        g.status = $"{Fmt.Count(served)} within a short walk ({Fmt.Pct(gs.city.CoverageRatio)} of the city)";
                        break;
                    }
                    case GoalKind.Upgrade:
                    {
                        bool any = false;
                        foreach (var s in net.AliveStations())
                            if (s.UpgradeLevels > 0) any = true;
                        g.progress = any ? 1f : 0f;
                        if (any) g.done = true;
                        g.status = any ? "Done" : "Open a station and buy an upgrade";
                        break;
                    }
                    case GoalKind.CouncilRequests:
                    {
                        int n = gs.gov.completed - g.baseline;
                        g.progress = Mathf.Clamp01(n / g.target);
                        if (!g.done && n >= g.target) g.done = true;
                        g.status = gs.gov.open.Count > 0 ? $"{n}/{g.target:0} done · {gs.gov.open.Count} open in City Hall (M)" : $"{n}/{g.target:0} done · the next request is on its way";
                        break;
                    }
                    case GoalKind.Approval:
                    {
                        g.progress = Mathf.Clamp01(gs.gov.approval / g.target);
                        if (!g.done && gs.gov.approval >= g.target) g.done = true;
                        g.status = $"approval {gs.gov.approval:0}% · {gs.gov.Standing.ToLowerInvariant()}";
                        break;
                    }
                    case GoalKind.LinesRunning:
                    {
                        int n = 0;
                        foreach (var l in net.AliveLines())
                            if (l.trains.Count > 0) n++;
                        g.progress = Mathf.Clamp01(n / g.target);
                        if (!g.done && n >= g.target) g.done = true;
                        g.status = $"{n}/{g.target:0} lines running";
                        break;
                    }
                }
                if (g.done) g.progress = 1f;
            }
            if (Ready && !announcedReady)
            {
                announcedReady = true;
                gs.advisor.Post(MsgCategory.City, MsgSeverity.Good, AtDemoEnd
                    ? $"Every goal is met! {gs.city.name} is ready to become a {Next.name}. That's the end of the demo, at dawn."
                    : $"Every goal is met! {gs.city.name} will grow into a {Next.name} at dawn.");
            }
        }

        // ------------------------------------------------------------------ growing

        public void TierUp()
        {
            if (IsMax) return;
            var info = new TierReachedInfo { oldArea = gs.city.playArea };
            tier++;
            info.tier = tier;
            gs.city.playArea = AreaFor(tier);
            info.newArea = gs.city.playArea;
            int open = 0;
            foreach (var d in gs.city.districts)
                if (d.revealed) open++;
            Growing = true;
            try
            {
                if (open < Current.revealTotal) info.newDistricts.AddRange(gs.growth.RevealInArea(Current.revealTotal - open, false));
            }
            finally
            {
                Growing = false;
            }
            float scale = gs.options.difficulty == 0 ? 1.2f : gs.options.difficulty == 1 ? 1f : 0.8f;
            info.grant = Current.grant * scale;
            if (info.grant > 0f) gs.econ.Earn(info.grant, LedgerCat.Grants);
            gs.growth.revealsPaused = !Has(Feature.CityGrowth);
            gs.growth.ScheduleNext(gs.clock.Day + 1);
            gs.gov.SyncWithProgress(true);
            gs.city.RefreshAggregates();
            gs.MarkDemandDirty();
            MakeGoals(info.newDistricts);
            gs.advisor.Post(MsgCategory.City, MsgSeverity.Good,
                $"{gs.city.name} is now a {Current.name}! The map grew to {info.newArea.width:0} × {info.newArea.height:0} km.");
            TierReached?.Invoke(info);
        }

        /// <summary>For save games: restore a size without any of the tier-up side effects.</summary>
        public void Restore(int savedTier, bool[] doneFlags, int districtForConnect)
        {
            tier = Mathf.Clamp(savedTier, 0, MaxTier);
            MakeGoals(null);
            for (int i = 0; i < goals.Count && doneFlags != null && i < doneFlags.Length; i++) goals[i].done = doneFlags[i];
            foreach (var g in goals)
                if (g.kind == GoalKind.ConnectDistrict && districtForConnect >= 0 && districtForConnect < gs.city.districts.Count)
                {
                    g.districtId = districtForConnect;
                    g.title = GoalTitle(g);
                }
            Evaluate();
        }

        // ------------------------------------------------------------------ text for the UI

        public static string FeatureTitle(Feature f)
        {
            switch (f)
            {
                case Feature.BuildLine: return "Line building";
                case Feature.Depot: return "Depots";
                case Feature.Demolish: return "Demolish";
                case Feature.InsertStation: return "Adding stations to lines";
                case Feature.Wagons: return "Longer trains";
                case Feature.CityGrowth: return "New districts";
                case Feature.CityWindow: return "City overview";
                case Feature.Finance: return "Finances";
                case Feature.Overlays: return "Data maps";
                case Feature.Stats: return "Statistics";
                case Feature.Upgrades: return "Station upgrades";
                case Feature.Surcharge: return "Rush-hour pricing";
                case Feature.Loans: return "Loans";
                case Feature.CityTargets: return "Mandates & elections";
                case Feature.Council: return "City Hall";
                case Feature.DepotExpansion: return "Bigger depots";
            }
            return f.ToString();
        }

        public static string FeatureBody(Feature f)
        {
            switch (f)
            {
                case Feature.BuildLine: return "Press L, click to place stations, right-click to build.";
                case Feature.Depot: return "Press B and click a station. Every train needs depot space.";
                case Feature.Demolish: return "Press X to remove a station or depot.";
                case Feature.InsertStation: return "Press I and click any track to drop a new stop on it.";
                case Feature.Wagons: return "Add wagons to every train on a line from the line's panel.";
                case Feature.CityGrowth: return "New districts now appear every day or two, wherever the city decides.";
                case Feature.CityWindow: return "Press G to see every district and how well it's served.";
                case Feature.Finance: return "Press F to set the ticket price and sell advertising.";
                case Feature.Overlays: return "P, J and C show where people live, where they work, and who has no station nearby.";
                case Feature.Stats: return "Press T for trips per hour, the busiest stations and what riders think.";
                case Feature.Upgrades: return "Longer platforms, more exits, elevators and transfer concourses, from a station's panel.";
                case Feature.Surcharge: return "In Finances: charge extra at rush hour to spread the crowds.";
                case Feature.Loans: return "In Finances: borrow against your network when you need to build big.";
                case Feature.CityTargets: return "City Hall can now order things you can't refuse, charge levies and fine you. Every two weeks the city elects a mayor.";
                case Feature.Council: return "The council sends requests: accept them for grants and approval. Approval sets your subsidy and permit costs. Press M.";
                case Feature.DepotExpansion: return "Expand a depot from its panel to store more trains.";
            }
            return "";
        }

        public static string FeatureHotkey(Feature f)
        {
            switch (f)
            {
                case Feature.BuildLine: return Keybinds.Label(BindAction.Line);
                case Feature.Depot: return Keybinds.Label(BindAction.Depot);
                case Feature.Demolish: return Keybinds.Label(BindAction.Demolish);
                case Feature.InsertStation: return Keybinds.Label(BindAction.Insert);
                case Feature.CityWindow: return Keybinds.Label(BindAction.City);
                case Feature.Finance: return Keybinds.Label(BindAction.Finance);
                case Feature.Overlays: return Keybinds.Label(BindAction.Population) + " " + Keybinds.Label(BindAction.Jobs) + " " + Keybinds.Label(BindAction.Coverage);
                case Feature.Stats: return Keybinds.Label(BindAction.Stats);
                case Feature.Council: return Keybinds.Label(BindAction.CityHall);
            }
            return null;
        }
    }
}
