using System;
using System.Collections.Generic;
using UnityEngine;

namespace Metro
{
    public enum MsgCategory { Operations, Riders, City, Finance, Safety }
    public enum MsgSeverity { Info, Good, Warning, Critical }

    public sealed class AdvisorMessage
    {
        public int id;
        public double time;
        public MsgCategory category;
        public MsgSeverity severity;
        public string text;
        public int stationId = -1, lineId = -1, districtId = -1;
        public bool read;

        public string CategoryLabel
        {
            get
            {
                switch (category)
                {
                    case MsgCategory.Operations: return "OPERATIONS";
                    case MsgCategory.Riders: return "RIDERS";
                    case MsgCategory.City: return "CITY HALL";
                    case MsgCategory.Finance: return "FINANCE";
                    case MsgCategory.Safety: return "SAFETY";
                }
                return "";
            }
        }

        public bool HasTarget => stationId >= 0 || lineId >= 0 || districtId >= 0;
    }

    /// <summary>
    /// The manager's feed: turns raw simulation data into short readable messages.
    /// Operations messages are accurate; rider complaints sometimes exaggerate,
    /// so the player is nudged to verify on the map before acting.
    /// </summary>
    public sealed class Advisor
    {
        readonly GameState gs;
        public readonly List<AdvisorMessage> messages = new List<AdvisorMessage>();
        readonly Dictionary<string, double> cooldowns = new Dictionary<string, double>();
        readonly Rng rng;
        int nextId;
        public int unread;
        public event Action<AdvisorMessage> Posted;

        static readonly string[] RumorLines =
        {
            "\"It's total chaos down here!\"",
            "\"I've been standing here for an hour!\"",
            "\"You can't even move on the platform!\"",
            "\"Worst commute of my life.\"",
            "\"Is anyone even running this place?\""
        };

        static readonly string[] WaitComplaints =
        {
            "Passengers are complaining about long waits on the {0}.",
            "Riders say the {0} never comes.",
            "Commuters on the {0} are fed up with the waiting."
        };

        public Advisor(GameState gs)
        {
            this.gs = gs;
            rng = new Rng(gs.options.seed * 31 + 7);
        }

        bool Ready(string key, float cooldownMinutes)
        {
            double now = gs.clock.totalMinutes;
            if (cooldowns.TryGetValue(key, out double until) && now < until) return false;
            cooldowns[key] = now + cooldownMinutes;
            return true;
        }

        public AdvisorMessage Post(MsgCategory cat, MsgSeverity sev, string text, int station = -1, int line = -1, int district = -1)
        {
            var m = new AdvisorMessage
            {
                id = nextId++,
                time = gs.clock.totalMinutes,
                category = cat,
                severity = sev,
                text = text,
                stationId = station,
                lineId = line,
                districtId = district
            };
            messages.Add(m);
            if (messages.Count > 80) messages.RemoveAt(0);
            unread++;
            Posted?.Invoke(m);
            return m;
        }

        public void Restore(AdvisorMessage m)
        {
            m.id = nextId++;
            messages.Add(m);
            if (!m.read) unread++;
        }

        public void MarkAllRead()
        {
            foreach (var m in messages) m.read = true;
            unread = 0;
        }

        public void Check()
        {
            var net = gs.net;
            var clock = gs.clock;
            float minute = clock.MinuteOfDay;
            string period = clock.PeriodAdjective;

            // Station crowding.
            Station worst = null;
            foreach (var s in net.AliveStations())
            {
                if (s.lines.Count == 0) continue;
                float c = s.Crowding;
                if (worst == null || c > worst.Crowding) worst = s;
                if (c >= 1f)
                {
                    if (Ready("crowd:" + s.id, 150f))
                        Post(MsgCategory.Operations, MsgSeverity.Critical,
                            $"{period} crowding at {s.name} platform is critical. Staff are holding people at the gates.", station: s.id);
                }
                else if (c >= 0.8f)
                {
                    if (Ready("busy:" + s.id, 200f))
                        Post(MsgCategory.Operations, MsgSeverity.Warning,
                            $"{s.name} is getting crowded ({Fmt.Pct(c)} of platform capacity).", station: s.id);
                }
                else if (c >= 0.5f && rng.Chance(0.04f))
                {
                    // Riders exaggerate: the station is busy, not broken. The map tells the truth.
                    if (Ready("rumor:" + s.id, 360f))
                        Post(MsgCategory.Riders, MsgSeverity.Warning,
                            $"A rider at {s.name}: {rng.Pick(RumorLines)}", station: s.id);
                }
            }

            // Lines.
            foreach (var l in net.AliveLines())
            {
                if (l.trains.Count == 0)
                {
                    if (!net.LineHasDepot(l))
                    {
                        if (Ready("nodepot:" + l.id, 240f))
                            Post(MsgCategory.Operations, MsgSeverity.Warning,
                                $"The {l.name} has no depot. Build one at any of its stations so trains can run.", line: l.id);
                    }
                    else if (Ready("notrains:" + l.id, 180f))
                        Post(MsgCategory.Operations, MsgSeverity.Warning, $"The {l.name} is built but no trains are running on it.", line: l.id);
                    continue;
                }
                float load = l.MaxSegLoad;
                if (load > 0.95f && Ready("packed:" + l.id, 200f))
                    Post(MsgCategory.Operations, MsgSeverity.Warning,
                        $"Trains on the {l.name} are packed to {Fmt.Pct(load)}. More trains or longer trains would help.", line: l.id);

                float wait = l.avgWaitRecent;
                if (wait > 7f && Ready("wait:" + l.id, 240f))
                    Post(MsgCategory.Riders, MsgSeverity.Warning, string.Format(rng.Pick(WaitComplaints), l.name), line: l.id);
                else if (wait > 4.5f && rng.Chance(0.03f) && Ready("waitrumor:" + l.id, 480f))
                    Post(MsgCategory.Riders, MsgSeverity.Info, $"Riders say they waited \"forever\" for the {l.name}.", line: l.id);

                if (l.deniedToday > 2000f && l.deniedToday > l.boardingsToday * 0.15f && Ready("denied:" + l.id, 300f))
                    Post(MsgCategory.Operations, MsgSeverity.Warning,
                        $"{Fmt.Count(l.deniedToday)} people were left behind on full {l.name} trains today.", line: l.id);
            }

            // Districts asking for service.
            foreach (var d in gs.city.districts)
            {
                if (!d.revealed || d.population < 25000f) continue;
                if (gs.clock.Day - d.revealedAtDay < 0.25f && d.revealOrder >= CityGenerator.InitiallyRevealed) continue;
                if (d.Coverage < 0.12f && Ready("access:" + d.id, 900f))
                    Post(MsgCategory.City, MsgSeverity.Info,
                        $"The {d.name} district ({Fmt.Count(d.population)} residents) is asking for metro access.", district: d.id);
            }

            // Money.
            var e = gs.econ;
            if (e.money < 0)
            {
                if (Ready("red", 360f))
                    Post(MsgCategory.Finance, MsgSeverity.Critical, "We're in the red. Construction is frozen until cash recovers — consider a loan or cutting costs.");
            }
            else if (e.money < e.DailyRunningTotal * 0.5f && net.LineCount > 0 && Ready("lowcash", 480f))
                Post(MsgCategory.Finance, MsgSeverity.Warning, "Cash is running low: less than half a day of running costs left.");

            if (e.baseFare > 3.6f && gs.satisfaction.price < 0.45f && Ready("fare", 720f))
                Post(MsgCategory.Riders, MsgSeverity.Warning, $"Riders say tickets are too expensive at {Fmt.Fare(e.baseFare)}.");

            if (gs.satisfaction.score < 0.45f && net.LineCount > 0 && Ready("sat", 600f))
                Post(MsgCategory.Riders, MsgSeverity.Warning, $"Rider satisfaction is sliding ({gs.satisfaction.Percent}%). Check waits, crowding and prices.");

            // Rush hour heads-up.
            if (!clock.IsWeekend && net.LineCount > 0)
            {
                if (minute >= 390f && minute < 400f && Ready("rushAM", 600f))
                    Post(MsgCategory.Operations, MsgSeverity.Info, "Morning rush starts soon. Commuters will head into the centre.");
                if (minute >= 960f && minute < 970f && Ready("rushPM", 600f))
                    Post(MsgCategory.Operations, MsgSeverity.Info, "Evening rush is coming: flows reverse as people head home.");
            }
        }

        public void DaySummary(int day)
        {
            var y = gs.econ.Yesterday;
            var ds = gs.stats.LastDay;
            if (ds == null || y == null || gs.net.LineCount == 0) return;
            var sev = y.Net >= 0 ? MsgSeverity.Info : MsgSeverity.Warning;
            Post(MsgCategory.Finance, sev,
                $"Day {day + 1} closed: {Fmt.Count(ds.riders)} trips, {Fmt.Money(y.Income)} in, {Fmt.Money(y.Expenses)} out ({Fmt.MoneySigned(y.Net)}).");
        }
    }
}
