using System;
using System.Collections.Generic;
using UnityEngine;

namespace Metro
{
    public enum ChallengeGoal { ReachSize, DaySatisfaction, RidersDay, ProfitDay, ServedResidents, EventShare }

    /// <summary>A hand-made scenario: a fixed city, a starting situation, one goal and a deadline.</summary>
    public sealed class ChallengeDef
    {
        public string id, title, blurb, cityName;
        public int seed, startTier, difficulty = 1;
        public float money = -1f;          // starting cash override
        public float fare = -1f;           // starting base fare override
        public int prebuiltLines;          // starting network built for free
        public float trainsPerKm = 0.4f;
        public int wagons = 3;
        public int mayorAgenda = -1;       // force the mayor's agenda
        public RequestKind[] blockedRequests; // City Hall never asks for these (they'd fight the goal)
        public ChallengeGoal goal;
        public float target;
        public float days;                 // time limit, in game days from the start
        public float[] starDays;           // finish within these many days for 3 and 2 stars
        public float[] starShares;         // EventShare: shares for 1, 2 and 3 stars

        public ChallengeDef Copy() => (ChallengeDef)MemberwiseClone();

        public string GoalText()
        {
            switch (goal)
            {
                case ChallengeGoal.ReachSize: return $"Grow into a {ProgressionSystem.Tiers[(int)target].name}";
                case ChallengeGoal.DaySatisfaction: return $"End a day with {Fmt.Pct(target)} rider satisfaction";
                case ChallengeGoal.RidersDay: return $"Carry {Fmt.Count(target)} trips in one day";
                case ChallengeGoal.ProfitDay: return $"End a day with {Fmt.Money(target)} operating profit";
                case ChallengeGoal.ServedResidents: return $"Put {Fmt.Count(target)} residents near a station";
                default: return $"Carry at least {Fmt.Pct(starShares[0])} of the fans to the big event and home";
            }
        }

        public string StarRule()
        {
            if (goal == ChallengeGoal.EventShare)
                return $"★ {Fmt.Pct(starShares[0])} · ★★ {Fmt.Pct(starShares[1])} · ★★★ {Fmt.Pct(starShares[2])} of the fans";
            return $"★★★ within {starDays[0]:0.#} days · ★★ within {starDays[1]:0.#} · ★ within {days:0.#}";
        }
    }

    /// <summary>A challenge being played: progress, deadline, result.</summary>
    public sealed class ChallengeRun
    {
        public readonly ChallengeDef def;
        readonly GameState gs;
        public double start, end, finishedAt;
        public bool finished, won;
        public int stars;
        public float progress;
        public string status = "";
        public int eventRequestId = -1;

        public ChallengeRun(GameState gs, ChallengeDef def)
        {
            this.gs = gs;
            this.def = def;
            start = gs.clock.totalMinutes;
            end = start + def.days * Balance.MinutesPerDay;
            // Size goals are met the moment the city grows (at dawn), not at the next check.
            if (def.goal == ChallengeGoal.ReachSize)
                gs.progress.TierReached += _ =>
                {
                    if (!finished) Evaluate(false);
                };
        }

        public double DaysUsed => ((finished ? finishedAt : gs.clock.totalMinutes) - start) / Balance.MinutesPerDay;

        public string TimeLeft
        {
            get
            {
                double left = end - gs.clock.totalMinutes;
                if (left <= 0) return "time's up";
                if (left < Balance.MinutesPerDay) return $"{Mathf.CeilToInt((float)(left / 60.0))} h left";
                return (left / Balance.MinutesPerDay).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " days left";
            }
        }

        public void MinuteTick()
        {
            if (finished) return;
            int minute = Mathf.RoundToInt(gs.clock.MinuteOfDay) % Balance.MinutesPerDay;
            if (minute % 5 == 0) Evaluate(false);
            if (gs.gov.gameOver) Finish(false);
            else if (gs.clock.totalMinutes > end) Finish(false);
        }

        public void DayTick()
        {
            if (!finished) Evaluate(true);
        }

        void Evaluate(bool dayEnded)
        {
            switch (def.goal)
            {
                case ChallengeGoal.ReachSize:
                    progress = Mathf.Clamp01(gs.progress.tier / def.target);
                    status = $"Now a {gs.progress.Current.name} · goals {gs.progress.GoalsDone}/{gs.progress.goals.Count}";
                    if (gs.progress.tier >= def.target) Finish(true);
                    break;
                case ChallengeGoal.DaySatisfaction:
                    progress = Mathf.Clamp01(gs.satisfaction.score / def.target);
                    status = $"Satisfaction now {gs.satisfaction.Percent}% · judged at midnight";
                    if (dayEnded && gs.satisfaction.yesterday >= def.target) Finish(true);
                    break;
                case ChallengeGoal.RidersDay:
                    progress = Mathf.Clamp01(gs.stats.ridersToday / def.target);
                    status = $"{Fmt.Count(gs.stats.ridersToday)} trips today";
                    if (gs.stats.ridersToday >= def.target) Finish(true);
                    break;
                case ChallengeGoal.ProfitDay:
                {
                    float today = gs.econ.today.OperatingNet;
                    progress = Mathf.Clamp01(today / def.target);
                    status = $"So far today {Fmt.MoneySigned(today)} · judged at midnight";
                    var y = gs.econ.Yesterday;
                    if (dayEnded && y != null && y.OperatingNet >= def.target) Finish(true);
                    break;
                }
                case ChallengeGoal.ServedResidents:
                    progress = Mathf.Clamp01(gs.city.servedPopulation / def.target);
                    status = $"{Fmt.Count(gs.city.servedPopulation)} residents near a station";
                    if (gs.city.servedPopulation >= def.target) Finish(true);
                    break;
                case ChallengeGoal.EventShare:
                {
                    CouncilRequest r = null;
                    foreach (var o in gs.gov.open)
                        if (o.id == eventRequestId) r = o;
                    if (r == null)
                        foreach (var o in gs.gov.history)
                            if (o.id == eventRequestId) r = o;
                    if (r == null) break;
                    float share = r.wanted > 1f ? r.served / r.wanted : 0f;
                    progress = Mathf.Clamp01(share / def.starShares[0]);
                    status = r.IsOpen ? r.status : $"The fans have gone home: {Fmt.Pct(share)} carried";
                    if (!r.IsOpen)
                    {
                        stars = share >= def.starShares[2] ? 3 : share >= def.starShares[1] ? 2 : share >= def.starShares[0] ? 1 : 0;
                        Finish(stars > 0);
                    }
                    break;
                }
            }
        }

        void Finish(bool success)
        {
            if (finished) return;
            finishedAt = gs.clock.totalMinutes;
            finished = true;
            won = success;
            if (success && def.goal != ChallengeGoal.EventShare)
            {
                double used = DaysUsed;
                stars = used <= def.starDays[0] ? 3 : used <= def.starDays[1] ? 2 : 1;
            }
            if (!success) stars = 0;
            progress = success ? 1f : progress;
            Challenges.RaiseFinished(this);
        }
    }

    /// <summary>The challenge list and how each one starts.</summary>
    public static class Challenges
    {
        public static readonly ChallengeDef[] All =
        {
            new ChallengeDef
            {
                id = "boomtown", title = "Boomtown", cityName = "Port Avalon", seed = 2024, startTier = 0,
                blurb = "A small town with big ambitions. Grow it all the way to a Big city in a week. Every dawn counts.",
                goal = ChallengeGoal.ReachSize, target = 4, days = 7, starDays = new[] { 4f, 5f }
            },
            new ChallengeDef
            {
                id = "thrift", title = "Thrift Mayor", cityName = "Greywater", seed = 777, startTier = 3,
                blurb = "Your predecessor ran long, half-empty trains on cheap tickets. The new mayor wants the metro to pay its way before the budget vote.",
                prebuiltLines = 4, trainsPerKm = 0.8f, wagons = 6, fare = 1.8f, mayorAgenda = (int)Agenda.Thrift, money = 2_000_000f,
                blockedRequests = new[] { RequestKind.FareCap, RequestKind.NoSurcharge },
                goal = ChallengeGoal.ProfitDay, target = 1_500_000f, days = 4, starDays = new[] { 1.5f, 2.5f }
            },
            new ChallengeDef
            {
                id = "coverage", title = "Every Neighbourhood", cityName = "Harrowmere", seed = 8080, startTier = 4,
                blurb = "A growth mayor promised a station near every home. Make the promise come true.",
                prebuiltLines = 5, trainsPerKm = 0.4f, wagons = 4, mayorAgenda = (int)Agenda.Growth, money = 8_000_000f,
                goal = ChallengeGoal.ServedResidents, target = 1_250_000f, days = 5, starDays = new[] { 2.5f, 3.5f }
            },
            new ChallengeDef
            {
                id = "matchday", title = "Matchday", cityName = "Aldwick", seed = 1337, startTier = 2,
                blurb = "The cup final is coming and the stadium has no station. City Hall expects every fan home safe.",
                prebuiltLines = 3, trainsPerKm = 0.45f, wagons = 3, money = 4_000_000f,
                goal = ChallengeGoal.EventShare, days = 4, starShares = new[] { 0.65f, 0.8f, 0.92f }
            },
            new ChallengeDef
            {
                id = "rush", title = "Rush Hour Rescue", cityName = "Kestrel Bay", seed = 4242, startTier = 3,
                blurb = "The last operator bought the lines but not the trains. Platforms overflow every morning. Fix it.",
                prebuiltLines = 4, trainsPerKm = 0.08f, wagons = 3, money = 3_000_000f,
                goal = ChallengeGoal.DaySatisfaction, target = 0.76f, days = 4, starDays = new[] { 1.5f, 2.5f }
            },
            new ChallengeDef
            {
                id = "shoestring", title = "Shoestring", cityName = "Lindmere", seed = 99, startTier = 0, difficulty = 2,
                blurb = "Demanding rules and a tiny budget. Every station has to earn its keep.",
                money = 2_100_000f, goal = ChallengeGoal.ReachSize, target = 2, days = 6, starDays = new[] { 3f, 4f }
            }
        };

        public static event Action<ChallengeRun> Finished;
        public static void RaiseFinished(ChallengeRun run) => Finished?.Invoke(run);

        public static ChallengeDef Get(string id)
        {
            foreach (var c in All)
                if (c.id == id) return c;
            return null;
        }

        public static int Next(ChallengeDef d)
        {
            int i = Array.IndexOf(All, d);
            return i >= 0 && i + 1 < All.Length ? i + 1 : -1;
        }

        public static int MaxStars => All.Length * 3;

        public static int TotalStars(Metro.Platform.PlayerProfile p)
        {
            int stars = 0;
            foreach (var c in All) stars += p.ChallengeStars(c.id);
            return stars;
        }

        public static string MenuSummary() => $"{TotalStars(Metro.Platform.Achievements.Profile)} of {MaxStars} stars";

        /// <summary>Builds the starting situation of a challenge.</summary>
        public static GameState Create(ChallengeDef d)
        {
            var opts = new NewGameOptions { seed = d.seed, cityName = d.cityName, difficulty = d.difficulty, startTier = d.startTier };
            var gs = new GameState(opts);
            if (d.prebuiltLines > 0)
            {
                AutoPlanner.BuildNetwork(gs, d.prebuiltLines, true, d.trainsPerKm, d.wagons);
                gs.trains.RefreshAllPoses();
            }
            if (d.money >= 0f) gs.econ.money = d.money;
            if (d.fare > 0f) gs.econ.baseFare = d.fare;
            if (d.mayorAgenda >= 0) gs.gov.mayor.agenda = (Agenda)d.mayorAgenda;
            if (d.blockedRequests != null)
                foreach (var k in d.blockedRequests) gs.gov.blocked.Add(k);
            // Let the player look around first: City Hall's first letter arrives in the afternoon.
            if (gs.gov.started) gs.gov.nextProposalAt = Math.Max(gs.gov.nextProposalAt, gs.clock.totalMinutes + 9 * 60.0);
            // Growing the city is only part of the size challenges; elsewhere the map stays put.
            gs.progress.holdTierUps = d.goal != ChallengeGoal.ReachSize;
            var run = new ChallengeRun(gs, d);
            gs.challenge = run;
            if (d.goal == ChallengeGoal.EventShare)
            {
                var r = gs.gov.Propose(RequestKind.Event);
                if (r != null)
                {
                    gs.gov.Accept(r);
                    run.eventRequestId = r.id;
                    run.end = r.deadline + 30.0;
                }
            }
            gs.EnsureFresh();
            return gs;
        }
    }
}
