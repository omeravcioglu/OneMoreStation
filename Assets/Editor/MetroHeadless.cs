using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Metro.EditorTools
{
    /// <summary>Batch-mode entry points: compile checks and headless simulation runs for balancing.</summary>
    public static class MetroHeadless
    {
        static string Arg(string name, string fallback)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return fallback;
        }

        // ------------------------------------------------------------------ progression bot

        public static void RunProgressionTest()
        {
            int code = 0;
            try
            {
                int seed = int.Parse(Arg("-seed", "2024"));
                int days = int.Parse(Arg("-days", "30"));
                int calib = int.Parse(Arg("-calib", "0"));
                int difficulty = int.Parse(Arg("-difficulty", "1"));
                Progression(seed, days, calib, difficulty);
            }
            catch (Exception e)
            {
                Debug.LogError("[Metro] Progression test failed: " + e);
                code = 1;
            }
            EditorApplication.Exit(code);
        }

        static int Open(GameState gs)
        {
            int n = 0;
            foreach (var d in gs.city.districts)
                if (d.revealed) n++;
            return n;
        }

        static string Goals(ProgressionSystem p)
        {
            var sb = new StringBuilder();
            foreach (var g in p.goals) sb.Append(g.done ? "[x] " : "[ ] ").Append(g.title).Append(" (").Append(g.status).Append(") ");
            return sb.ToString();
        }

        /// <summary>
        /// A simple bot plays a normal game from Small town and logs how fast the city grows.
        /// With -calib N the bot instead spends exactly N days at every size (goals ignored),
        /// which shows what a reasonable network achieves at each size when tuning the goals.
        /// </summary>
        static void Progression(int seed, int maxDays, int calibDays, int difficulty)
        {
            var sb = new StringBuilder();
            log = sb;
            tried.Clear();
            lastTry.Clear();
            lastPointTry.Clear();
            botNotes.Clear();
            botFailures.Clear();
            var gs = new GameState(new NewGameOptions { seed = seed, cityName = "Growtown", difficulty = difficulty });
            var p = gs.progress;
            p.holdTierUps = calibDays > 0;
            int tierStartDay = gs.clock.Day;
            int extraDays = int.Parse(Arg("-extra", "0"));
            int maxDay = -1;
            int bankruptDay = int.Parse(Arg("-bankrupt", "0"));
            bool drained = false;
            float bestRiders = 0f, bestProfit = float.MinValue, bestSat = 0f, bestCov = 0f;
            var a0 = gs.city.playArea;
            sb.AppendLine($"[Metro] Start: {p.Current.name}, map {a0.width:0}x{a0.height:0} km, {Open(gs)}/{gs.city.districts.Count} districts open, pop {Fmt.Count(gs.city.revealedPopulation)}, money {Fmt.Money(gs.econ.money)}");
            p.TierReached += info =>
            {
                var a = info.newArea;
                sb.AppendLine($"[Metro] >>> Day {gs.clock.DayNumber} {gs.clock.TimeString}: now a {ProgressionSystem.Tiers[info.tier].name}; map {a.width:0}x{a.height:0} km; +{info.newDistricts.Count} districts ({Open(gs)} open); grant {Fmt.Money(info.grant)}; pop {Fmt.Count(gs.city.revealedPopulation)}");
            };
            var gov = gs.gov;
            gov.RequestChanged += r =>
            {
                if (r.state == RequestState.Active && gs.clock.totalMinutes - r.issuedAt > 1) return;
                string extra = r.kind == RequestKind.Event ? $" (fans {Fmt.Count(r.served)}/{Fmt.Count(r.wanted)})" : "";
                sb.AppendLine($"           hall Day {gs.clock.DayNumber} {gs.clock.TimeString}: [{r.state}{(r.mandate ? ", mandate" : "")}] {r.title}{extra} · approval {gov.approval:0}");
            };
            gov.SessionHeld += rep =>
            {
                string el = rep.election != null ? $" ELECTION: {rep.election.winner.name} ({GovernmentSystem.AgendaName(rep.election.winner.agenda)}) {(rep.election.incumbentWon ? "re-elected" : "wins")} {Fmt.Pct(rep.election.incumbentShare)}" : "";
                string ch = rep.challengerAnnounced != null ? $" challenger {rep.challengerAnnounced.name}" : "";
                sb.AppendLine($"[Metro] SESSION week {rep.week}: approval {rep.approvalBefore:0} -> {rep.approvalNow:0} ({rep.verdict}), done {rep.done} failed {rep.failed} declined {rep.declined}, subsidy x{rep.subsidyFactor:0.00}{(rep.ultimatumIssued ? " ULTIMATUM" : "")}{el}{ch}");
            };
            gov.BailoutGranted += grant => sb.AppendLine($"[Metro] BAILOUT Day {gs.clock.DayNumber}: {Fmt.Money(grant)}");
            gov.LicenceRevoked += reason => sb.AppendLine($"[Metro] GAME OVER Day {gs.clock.DayNumber}: {reason}");
            var line = AutoPlanner.BuildStarterLine(gs, int.Parse(Arg("-starter", "99")));
            sb.AppendLine($"[Metro] Starter line: {(line != null ? line.stops.Count + " stops, " + Fmt.Km(line.path.length) : "FAILED")}; money {Fmt.Money(gs.econ.money)}");
            if (line != null)
            {
                gs.build.BuildDepot(line.stops[line.stops.Count / 2]);
                gs.build.AddTrain(line);
                gs.build.AddTrain(line);
            }
            sb.AppendLine($"[Metro] After depot + 2 trains: money {Fmt.Money(gs.econ.money)}; goals: {Goals(p)}");
            for (int hour = 0; hour < maxDays * 24; hour++)
            {
                gs.Advance(60f);
                if (gs.gov.gameOver) break;
                if (bankruptDay > 0 && !drained && gs.clock.DayNumber >= bankruptDay)
                {
                    drained = true;
                    gs.econ.money = -6_000_000;
                    sb.AppendLine($"[Metro] TEST: money set to -$6M on day {gs.clock.DayNumber}");
                }
                Bot(gs);
                BotCityHall(gs);
                if (gs.clock.MinuteOfDay < 59.5f && gs.stats.LastDay != null)
                {
                    var ds = gs.stats.LastDay;
                    var y = gs.econ.Yesterday;
                    float opNet = y != null ? y.OperatingNet : 0f;
                    bestRiders = Mathf.Max(bestRiders, ds.riders);
                    bestProfit = Mathf.Max(bestProfit, opNet);
                    bestSat = Mathf.Max(bestSat, gs.satisfaction.yesterday);
                    bestCov = Mathf.Max(bestCov, gs.city.CoverageRatio);
                    sb.AppendLine($"[Metro] Day {ds.day + 1}: {p.Current.name}, riders {Fmt.Count(ds.riders)}, op {Fmt.MoneySigned(opNet)}, net {Fmt.MoneySigned(ds.net)}, money {Fmt.Money(gs.econ.money)}, sat {Fmt.Pct(gs.satisfaction.yesterday)}, lines {gs.net.LineCount}/{(p.MaxLines >= 99 ? "inf" : p.MaxLines.ToString())}, trains {gs.trains.AliveCount}, stations {gs.net.StationCount}, open {Open(gs)}, pop {Fmt.Count(gs.city.revealedPopulation)}, cov {Fmt.Pct(gs.city.CoverageRatio)}");
                    if (calibDays == 0) sb.AppendLine($"           goals: {Goals(p)}");
                    if (gs.gov.started) sb.AppendLine($"           city hall: approval {gs.gov.approval:0} ({gs.gov.Standing}), mayor {gs.gov.mayor.name} ({GovernmentSystem.AgendaName(gs.gov.mayor.agenda)}), open {gs.gov.open.Count}, done {gs.gov.completed}, failed {gs.gov.failed}, declined {gs.gov.declined}, subsidy x{gs.gov.SubsidyFactor:0.00}, permits x{gs.gov.PermitFactor:0.00}");
                    if (calibDays > 0 && gs.clock.Day - tierStartDay >= calibDays && !p.IsMax)
                    {
                        sb.AppendLine($"[Metro] CALIB {p.Current.name}: best riders {Fmt.Count(bestRiders)}, best op profit {Fmt.MoneySigned(bestProfit)}, best sat {Fmt.Pct(bestSat)}, best cov {Fmt.Pct(bestCov)}");
                        bestRiders = 0f;
                        bestProfit = float.MinValue;
                        bestSat = bestCov = 0f;
                        tierStartDay = gs.clock.Day;
                        p.TierUp();
                    }
                }
                if (p.IsMax && maxDay < 0)
                {
                    maxDay = gs.clock.Day;
                    sb.AppendLine($"[Metro] Reached {p.Current.name} on day {gs.clock.DayNumber}. Bot: {botNotes}");
                }
                if (p.IsMax && (calibDays == 0 || gs.clock.Day - tierStartDay >= calibDays) && gs.clock.Day - maxDay >= extraDays) break;
            }
            if (!p.IsMax) sb.AppendLine($"[Metro] Still a {p.Current.name} after {maxDays} days. Bot: {botNotes}");
            Debug.Log(sb.ToString());
        }

        static readonly StringBuilder botNotes = new StringBuilder();
        /// <summary>Cash the bot leaves untouched (0 in the progression test).</summary>
        static float botReserve;
        /// <summary>Whether the bot grows the network toward busy districts (off when a challenge is about running it better).</summary>
        static bool botExpand = true;
        static readonly Dictionary<string, int> botFailures = new Dictionary<string, int>();

        static void BotFailed(string what, string why)
        {
            string key = what + ": " + why;
            botFailures.TryGetValue(key, out int n);
            botFailures[key] = n + 1;
            if (n == 0) botNotes.Append('[').Append(key).Append("] ");
        }

        static void Bot(GameState gs)
        {
            var p = gs.progress;
            var b = gs.build;
            var net = gs.net;
            foreach (var l in new List<MetroLine>(net.AliveLines()))
            {
                int want = l.trains.Count < 2 ? 2 - l.trains.Count : (l.MaxSegLoad > 0.75f || l.avgWaitRecent > 6f ? 1 : 0);
                for (int i = 0; i < want; i++)
                {
                    if (gs.econ.money < b.TrainCost(l.wagons) * 2f + botReserve) break;
                    if (!net.LineHasDepot(l)) b.BuildDepot(l.stops[l.stops.Count / 2]);
                    var r = b.AddTrain(l);
                    if (!r.ok && r.error.Contains("full"))
                    {
                        bool built = false;
                        foreach (int sid in l.stops)
                        {
                            var st = net.GetStation(sid);
                            if (st != null && st.depotId < 0)
                            {
                                built = b.BuildDepot(sid).ok;
                                break;
                            }
                        }
                        if (!built)
                            foreach (var d in net.AliveDepots())
                                if (b.ExpandDepot(d.id).ok) break;
                    }
                }
                if (p.Has(Feature.Wagons) && l.MaxSegLoad > 0.9f && l.wagons < b.MaxWagonsForLine(l) && gs.econ.money > 500_000) b.SetWagons(l, l.wagons + 1);
            }
            bool wantServed = false;
            foreach (var goal in p.goals)
            {
                if (goal.done) continue;
                if (goal.kind == GoalKind.ConnectDistrict && goal.districtId >= 0 && gs.econ.money > 700_000)
                {
                    var d = gs.city.districts[goal.districtId];
                    if (d.Coverage < goal.target && Try(gs, d, 6))
                    {
                        if (net.LineCount < p.MaxLines) NewLineThrough(gs, d);
                        else ExtendNearest(gs, d);
                    }
                }
                if (goal.kind == GoalKind.Upgrade && gs.econ.money > 300_000)
                {
                    Station busiest = null;
                    foreach (var s in net.AliveStations())
                        if (s.lines.Count > 0 && (busiest == null || s.entriesToday > busiest.entriesToday)) busiest = s;
                    if (busiest != null) b.Upgrade(busiest.id, StationUpgrade.Capacity);
                }
                if (goal.kind == GoalKind.ServedResidents) wantServed = true;
            }
            District target = null;
            float covLimit = wantServed ? 0.35f : 0.2f;
            foreach (var d in gs.city.districts)
            {
                if (!d.revealed || !gs.city.InPlayArea(d.center) || d.Coverage > covLimit || d.population < 20000f) continue;
                if (tried.TryGetValue(d.id, out int when) && gs.clock.Day - when < 2) continue;
                if (target == null || d.population > target.population) target = d;
            }
            if (target != null && botExpand)
            {
                if (net.LineCount < p.MaxLines && gs.econ.money > 1_500_000 + botReserve && Try(gs, target, 0)) NewLineThrough(gs, target);
                else if (gs.econ.money > (wantServed ? 900_000 : 2_000_000) + botReserve && Try(gs, target, 0)) ExtendNearest(gs, target);
            }
            foreach (var s in net.AliveStations())
                if (p.Has(Feature.Upgrades) && s.Crowding > 0.9f && s.capacityLevel < 3 && gs.econ.money > 1_000_000) b.Upgrade(s.id, StationUpgrade.Capacity);
        }

        /// <summary>The bot answers City Hall: accept what it can plausibly do, then work on it.</summary>
        static void BotCityHall(GameState gs)
        {
            var gov = gs.gov;
            var b = gs.build;
            foreach (var r in new List<CouncilRequest>(gov.open))
            {
                if (r.state == RequestState.Proposed)
                {
                    if (r.mandate || BotWouldAccept(gs, r)) gov.Accept(r);
                    else gov.Decline(r);
                    continue;
                }
                if (r.state != RequestState.Active) continue;
                switch (r.kind)
                {
                    case RequestKind.StationNear:
                    case RequestKind.Event:
                    {
                        var lm = gov.LandmarkById(r.landmarkId);
                        if (lm != null && gov.ServedStationNear(lm.pos) == null && gs.econ.money > 600_000 && TryPoint(gs, lm.id)) ExtendNearestPoint(gs, lm.pos, lm.name);
                        break;
                    }
                    case RequestKind.ConnectDistrict:
                    {
                        var d = r.districtId >= 0 ? gs.city.districts[r.districtId] : null;
                        if (d != null && d.Coverage < r.target && gs.econ.money > 700_000 && Try(gs, d, 6))
                        {
                            if (gs.net.LineCount < gs.progress.MaxLines) NewLineThrough(gs, d);
                            else ExtendNearest(gs, d);
                        }
                        break;
                    }
                    case RequestKind.Accessibility:
                        foreach (var s in gs.net.AliveStations())
                        {
                            if (s.elevators || s.lines.Count == 0 || gs.econ.money < 400_000) continue;
                            b.Upgrade(s.id, StationUpgrade.Elevators);
                            break;
                        }
                        break;
                    case RequestKind.FareCap:
                        if (gs.econ.baseFare > r.target) gs.econ.baseFare = r.target;
                        break;
                    case RequestKind.NoSurcharge:
                        gs.econ.peakSurcharge = 0f;
                        break;
                    case RequestKind.CrowdRelief:
                    {
                        var st = gs.net.GetStation(r.stationId);
                        if (st == null || gs.econ.money < 500_000) break;
                        if (st.capacityLevel < 3) b.Upgrade(st.id, StationUpgrade.Capacity);
                        break;
                    }
                    case RequestKind.Levy:
                    case RequestKind.RepayBailout:
                        if (gs.econ.money > 400_000) gov.Pay(r, (float)gs.econ.money - 300_000f);
                        break;
                }
            }
        }

        /// <summary>A sensible player only says yes to what they can actually deliver.</summary>
        static bool BotWouldAccept(GameState gs, CouncilRequest r)
        {
            var last = gs.stats.LastDay;
            float best = Mathf.Max(last != null ? last.riders : 0f, gs.stats.ridersToday);
            double money = gs.econ.money;
            switch (r.kind)
            {
                case RequestKind.StationNear:
                case RequestKind.Event:
                case RequestKind.ConnectDistrict: return money > 900_000;
                case RequestKind.Accessibility: return money > 1_000_000;
                case RequestKind.CrowdRelief: return money > 800_000 && gs.Has(Feature.Upgrades);
                case RequestKind.FareCap:
                case RequestKind.NoSurcharge: return true;
                case RequestKind.Ridership: return best >= r.target * 0.93f;
                case RequestKind.ServedResidents: return gs.city.servedPopulation >= r.target * 0.96f;
                case RequestKind.Satisfaction: return gs.satisfaction.score >= r.target - 0.02f;
                case RequestKind.ProfitDay:
                {
                    var y = gs.econ.Yesterday;
                    return y != null && y.OperatingNet >= r.target * 0.9f;
                }
                case RequestKind.Interchanges: return gs.net.LineCount < gs.progress.MaxLines && money > 1_500_000;
                default: return true;
            }
        }

        static readonly Dictionary<int, double> lastPointTry = new Dictionary<int, double>();

        static bool TryPoint(GameState gs, int key)
        {
            double now = gs.clock.totalMinutes;
            if (lastPointTry.TryGetValue(key, out double t) && now - t < 4 * 60.0) return false;
            lastPointTry[key] = now;
            return true;
        }

        static void ExtendNearestPoint(GameState gs, Vector2 target, string what)
        {
            MetroLine best = null;
            float bd = float.MaxValue;
            foreach (var l in gs.net.AliveLines())
            {
                if (l.loop) continue;
                float d = Mathf.Min(Vector2.Distance(gs.net.stations[l.stops[0]].pos, target), Vector2.Distance(gs.net.stations[l.stops[l.stops.Count - 1]].pos, target));
                if (d < bd)
                {
                    bd = d;
                    best = l;
                }
            }
            if (best == null) return;
            int before = gs.net.StationCount;
            if (!AutoPlanner.ExtendToward(gs, best, target, out string why)) BotFailed("extend to landmark", why);
            else log?.AppendLine($"           bot {gs.clock.TimeString}: extended {best.name} toward {what} (+{gs.net.StationCount - before} stations)");
        }

        static StringBuilder log;
        static readonly Dictionary<int, int> tried = new Dictionary<int, int>();
        static readonly Dictionary<int, double> lastTry = new Dictionary<int, double>();

        /// <summary>Don't hammer the same district every hour: retry at most every few game hours.</summary>
        static bool Try(GameState gs, District d, int hours)
        {
            double now = gs.clock.totalMinutes;
            if (lastTry.TryGetValue(d.id, out double t) && now - t < Mathf.Max(hours, 6) * 60.0) return false;
            lastTry[d.id] = now;
            tried[d.id] = gs.clock.Day;
            return true;
        }

        static void NewLineThrough(GameState gs, District d)
        {
            Vector2 target = d.center;
            var hub = gs.net.NearestStation(gs.city.districts[0].center, 3f);
            Vector2 hubPos = hub != null ? hub.pos : gs.city.districts[0].center;
            Vector2 dir = (hubPos - target).sqrMagnitude > 0.01f ? (hubPos - target).normalized : Vector2.right;
            Vector2 end = hubPos + dir * Mathf.Min(6f, Vector2.Distance(target, hubPos));
            var area = gs.city.playArea;
            end = new Vector2(Mathf.Clamp(end.x, area.xMin + 1f, area.xMax - 1f), Mathf.Clamp(end.y, area.yMin + 1f, area.yMax - 1f));
            int before = gs.net.StationCount;
            var line = AutoPlanner.BuildAlong(gs, new List<Vector2> { target, hubPos, end });
            if (line == null)
            {
                BotFailed("new line", gs.build.LineLicenceProblem() ?? "BuildAlong failed");
                return;
            }
            int added = gs.net.StationCount - before;
            if (added == 0)
            {
                gs.build.DeleteLine(line);
                BotFailed("new line", "no new stations, removed");
                return;
            }
            gs.build.BuildDepot(line.stops[line.stops.Count / 2]);
            gs.build.AddTrain(line);
            gs.build.AddTrain(line);
            log?.AppendLine($"           bot {gs.clock.TimeString}: new {line.name} through {d.name}: {line.stops.Count} stops ({added} new), {Fmt.Pct(d.Coverage)} of the district served");
        }

        static void ExtendNearest(GameState gs, District district)
        {
            Vector2 target = district.center;
            MetroLine best = null;
            float bd = float.MaxValue;
            foreach (var l in gs.net.AliveLines())
            {
                if (l.loop) continue;
                float d = Mathf.Min(Vector2.Distance(gs.net.stations[l.stops[0]].pos, target), Vector2.Distance(gs.net.stations[l.stops[l.stops.Count - 1]].pos, target));
                if (d < bd)
                {
                    bd = d;
                    best = l;
                }
            }
            if (best == null) return;
            int before = gs.net.StationCount;
            if (!AutoPlanner.ExtendToward(gs, best, target, out string why)) BotFailed("extend", why);
            else log?.AppendLine($"           bot {gs.clock.TimeString}: extended {best.name} toward {district.name} (+{gs.net.StationCount - before} stations)");
        }

        // ------------------------------------------------------------------ challenge calibration

        /// <summary>
        /// Plays every challenge (or just -challenge id) twice: once without touching anything, which
        /// must fail, and once with the bot, which should earn at least one star. Logs the days used.
        /// </summary>
        public static void RunChallengeTest()
        {
            int code = 0;
            try
            {
                string only = Arg("-challenge", "");
                bool calib = Arg("-calib", "0") != "0";
                var sb = new StringBuilder();
                int problems = 0;
                foreach (var def in Challenges.All)
                {
                    if (only.Length > 0 && def.id != only) continue;
                    if (calib)
                    {
                        // Unreachable goals: shows what doing nothing and the bot achieve over the whole time limit.
                        var c = def.Copy();
                        c.trainsPerKm = float.Parse(Arg("-tpk", c.trainsPerKm.ToString(System.Globalization.CultureInfo.InvariantCulture)), System.Globalization.CultureInfo.InvariantCulture);
                        c.wagons = int.Parse(Arg("-wagons", c.wagons.ToString()));
                        c.money = float.Parse(Arg("-money", c.money.ToString(System.Globalization.CultureInfo.InvariantCulture)), System.Globalization.CultureInfo.InvariantCulture);
                        c.fare = float.Parse(Arg("-fare", c.fare.ToString(System.Globalization.CultureInfo.InvariantCulture)), System.Globalization.CultureInfo.InvariantCulture);
                        c.prebuiltLines = int.Parse(Arg("-lines", c.prebuiltLines.ToString()));
                        c.target = c.goal == ChallengeGoal.ReachSize ? 99f : float.MaxValue;
                        if (c.starShares != null) c.starShares = new[] { 2f, 2f, 2f };
                        PlayChallenge(c, false, sb);
                        PlayChallenge(c, true, sb);
                        continue;
                    }
                    var idle = PlayChallenge(def, false, sb);
                    var bot = PlayChallenge(def, true, sb);
                    if (idle.won)
                    {
                        problems++;
                        sb.AppendLine($"[Metro] PROBLEM {def.id}: won without doing anything");
                    }
                    if (!bot.won)
                    {
                        problems++;
                        sb.AppendLine($"[Metro] PROBLEM {def.id}: the bot failed");
                    }
                }
                sb.AppendLine(problems == 0 ? "[Metro] Challenge test OK" : $"[Metro] Challenge test: {problems} problem(s)");
                Debug.Log(sb.ToString());
            }
            catch (Exception e)
            {
                Debug.LogError("[Metro] Challenge test failed: " + e);
                code = 1;
            }
            EditorApplication.Exit(code);
        }

        static ChallengeRun PlayChallenge(ChallengeDef def, bool play, StringBuilder sb)
        {
            log = play ? sb : null;
            tried.Clear();
            lastTry.Clear();
            lastPointTry.Clear();
            botNotes.Clear();
            botFailures.Clear();
            var gs = Challenges.Create(def);
            gs.build.Undo.enabled = false;
            var run = gs.challenge;
            botExpand = def.goal == ChallengeGoal.ReachSize || def.goal == ChallengeGoal.ServedResidents;
            if (play && def.goal == ChallengeGoal.ReachSize && gs.net.LineCount == 0)
            {
                // Like a sensible new player: a short first line through the centre, a depot and two trains.
                var first = AutoPlanner.BuildStarterLine(gs, int.Parse(Arg("-starter", "5")));
                if (first != null)
                {
                    gs.build.BuildDepot(first.stops[first.stops.Count / 2]);
                    gs.build.AddTrain(first);
                    gs.build.AddTrain(first);
                }
            }
            sb.AppendLine($"[Metro] === {def.title} ({(play ? "bot" : "idle")}): {gs.progress.Current.name}, {gs.net.LineCount} lines, {gs.trains.AliveCount} trains, money {Fmt.Money(gs.econ.money)}, pop {Fmt.Count(gs.city.revealedPopulation)}, served {Fmt.Count(gs.city.servedPopulation)}, day {gs.clock.DayNumber} {gs.clock.TimeString}");
            int lastDay = gs.clock.Day;
            if (play)
                gs.gov.RequestChanged += r =>
                {
                    if (r.state == RequestState.Active && gs.clock.totalMinutes - r.issuedAt > 1) return;
                    sb.AppendLine($"[Metro]     hall day {gs.clock.DayNumber} {gs.clock.TimeString}: [{r.state}] {r.title} · money {Fmt.Money(gs.econ.money)}");
                };
            for (int hour = 0; hour < 24 * 12 && !run.finished; hour++)
            {
                gs.Advance(60f);
                if (play)
                {
                    // Like a player chasing a City Hall goal: keep enough cash to say yes to the next request.
                    botReserve = 0f;
                    foreach (var goal in gs.progress.goals)
                        if (!goal.done && goal.kind == GoalKind.CouncilRequests) botReserve = 900_000f;
                    ChallengeBot(gs, def);
                    Bot(gs);
                    BotCityHall(gs);
                }
                if (gs.clock.Day != lastDay)
                {
                    lastDay = gs.clock.Day;
                    var y = gs.econ.Yesterday;
                    var ds = gs.stats.LastDay;
                    sb.AppendLine($"[Metro]   day {gs.clock.DayNumber - 1} done: {gs.progress.Current.name}, riders {Fmt.Count(ds != null ? ds.riders : 0f)}, sat {Fmt.Pct(gs.satisfaction.yesterday)}, op {Fmt.MoneySigned(y != null ? y.OperatingNet : 0f)}, money {Fmt.Money(gs.econ.money)}, served {Fmt.Count(gs.city.servedPopulation)}, lines {gs.net.LineCount}, trains {gs.trains.AliveCount}; {run.status}");
                    if (y != null)
                    {
                        var lsb = new StringBuilder("[Metro]     ledger:");
                        for (int i = 0; i < DayLedger.Count; i++)
                            if (Mathf.Abs(y.amounts[i]) > 1f) lsb.Append(' ').Append((LedgerCat)i).Append(' ').Append(Fmt.MoneySigned(y.amounts[i]));
                        sb.AppendLine(lsb.ToString());
                    }
                    if (def.goal == ChallengeGoal.ReachSize) sb.AppendLine("[Metro]     goals: " + Goals(gs.progress));
                }
            }
            sb.AppendLine($"[Metro]   RESULT {def.id} {(play ? "bot" : "idle")}: {(run.won ? "WON" : "lost")} after {run.DaysUsed:0.00} days, {run.stars} stars; {run.status}{(play ? "; bot: " + botNotes : "")}");
            botReserve = 0f;
            botExpand = true;
            return run;
        }

        /// <summary>What a player would do for each kind of goal, on top of the general bot.</summary>
        static void ChallengeBot(GameState gs, ChallengeDef def)
        {
            var b = gs.build;
            var net = gs.net;
            switch (def.goal)
            {
                case ChallengeGoal.ProfitDay:
                    // Charge a little more; once a day, after the morning rush, take a train off quiet lines.
                    if (gs.econ.baseFare < Balance.DefaultFare * 1.3f) gs.econ.baseFare = Balance.DefaultFare * 1.3f;
                    if (Mathf.Abs(gs.clock.MinuteOfDay - 10 * 60) < 30)
                        foreach (var l in new List<MetroLine>(net.AliveLines()))
                            if (l.trains.Count > 3 && l.peakLoadToday < 0.6f) b.RemoveTrain(l);
                    break;
                case ChallengeGoal.DaySatisfaction:
                    // Cheaper tickets, no rush-hour surcharge, lifts at the busiest stations.
                    if (gs.econ.baseFare > 1.6f) gs.econ.baseFare = 1.6f;
                    gs.econ.peakSurcharge = 0f;
                    if (gs.econ.money > 700_000)
                    {
                        Station busiest = null;
                        foreach (var s in net.AliveStations())
                            if (!s.elevators && s.lines.Count > 0 && (busiest == null || s.entriesToday > busiest.entriesToday)) busiest = s;
                        if (busiest != null) b.Upgrade(busiest.id, StationUpgrade.Elevators);
                    }
                    break;
                case ChallengeGoal.ServedResidents:
                {
                    if (gs.city.servedPopulation >= def.target || gs.econ.money < 900_000) break;
                    District target = null;
                    foreach (var d in gs.city.districts)
                    {
                        if (!d.revealed || !gs.city.InPlayArea(d.center) || d.Coverage > 0.5f || d.population < 15000f) continue;
                        if (lastTry.TryGetValue(d.id, out double t) && gs.clock.totalMinutes - t < 3 * 60.0) continue;
                        if (target == null || d.population * (1f - d.Coverage) > target.population * (1f - target.Coverage)) target = d;
                    }
                    if (target == null) break;
                    lastTry[target.id] = gs.clock.totalMinutes;
                    if (net.LineCount < gs.progress.MaxLines && gs.econ.money > 1_500_000) NewLineThrough(gs, target);
                    else ExtendNearest(gs, target);
                    break;
                }
            }
        }

        // ------------------------------------------------------------------ audio levels

        /// <summary>
        /// Renders every music mood offline at the default settings and measures it next to the sound
        /// effects: peak and RMS in dBFS, and how often the soft clipper is driven hard. With
        /// -wavdir &lt;folder&gt; it also writes 40-second previews and the effects as WAV files.
        /// </summary>
        public static void AudioCheck()
        {
            int code = 0;
            try
            {
                var defaults = new GameSettings();
                string wavDir = Arg("-wavdir", "");
                if (wavDir.Length > 0) System.IO.Directory.CreateDirectory(wavDir);
                const int rate = 48000;
                var sb = new StringBuilder();
                sb.AppendLine($"[Metro] Audio check at the default settings: master {defaults.masterVolume:0.00}, music {defaults.musicVolume:0.00}, effects {defaults.sfxVolume:0.00}");
                double musicRmsSum = 0;
                int moods = 0;
                foreach (MusicMood mood in Enum.GetValues(typeof(MusicMood)))
                {
                    var synth = new MusicSynth(rate);
                    synth.targetMood = (int)mood;
                    synth.SetVolume(defaults.musicVolume);
                    int warm = rate * 3, len = rate * 40;
                    var buf = new float[len * 2];
                    double sum = 0;
                    float peak = 0f;
                    int hot = 0;
                    for (int i = 0; i < warm + len; i++)
                    {
                        synth.Render(out float l, out float r);
                        if (i < warm) continue;
                        l *= defaults.masterVolume;
                        r *= defaults.masterVolume;
                        int k = i - warm;
                        buf[2 * k] = l;
                        buf[2 * k + 1] = r;
                        peak = Mathf.Max(peak, Mathf.Max(Mathf.Abs(l), Mathf.Abs(r)));
                        sum += l * l + r * r;
                        // x / (1 + |x|) passes 0.5 when the input reaches full scale: the clipper is working hard.
                        if (Mathf.Abs(l) > 0.5f || Mathf.Abs(r) > 0.5f) hot++;
                    }
                    double rms = Math.Sqrt(sum / (len * 2.0));
                    musicRmsSum += Db(rms);
                    moods++;
                    sb.AppendLine($"[Metro]   music {mood,-8} peak {Db(peak),6:0.0} dBFS   RMS {Db(rms),6:0.0} dBFS   crest {Db(peak) - Db(rms),4:0.0} dB   driven hard {hot * 100.0 / len:0.00}% of the time");
                    if (wavDir.Length > 0) WriteWav(System.IO.Path.Combine(wavDir, "music_" + mood.ToString().ToLowerInvariant() + ".wav"), buf, rate, 2);
                }
                // In game: Sfx.volume comes from the effects setting, and the listener applies the master volume.
                float sfxScale = Sfx.VolumeFor(defaults.sfxVolume) * defaults.masterVolume;
                double sfxRmsSum = 0;
                int effects = 0;
                foreach (var (name, samples, vol) in Sfx.Bank())
                {
                    float peak = 0f;
                    double sum = 0;
                    int active = 0;
                    var outBuf = new float[samples.Length];
                    for (int i = 0; i < samples.Length; i++)
                    {
                        float v = samples[i] * vol * sfxScale;
                        outBuf[i] = v;
                        peak = Mathf.Max(peak, Mathf.Abs(v));
                        if (Mathf.Abs(v) > 0.001f)
                        {
                            sum += v * v;
                            active++;
                        }
                    }
                    double rms = active > 0 ? Math.Sqrt(sum / active) : 0.0;
                    sfxRmsSum += Db(rms);
                    effects++;
                    sb.AppendLine($"[Metro]   sfx   {name,-8} peak {Db(peak),6:0.0} dBFS   RMS while sounding {Db(rms),6:0.0} dBFS   {samples.Length * 1000 / Sfx.SampleRate} ms");
                    if (wavDir.Length > 0) WriteWav(System.IO.Path.Combine(wavDir, "sfx_" + name + ".wav"), outBuf, Sfx.SampleRate, 1);
                }
                sb.AppendLine($"[Metro]   average: music RMS {musicRmsSum / moods:0.0} dBFS, effects RMS {sfxRmsSum / effects:0.0} dBFS (effects {sfxRmsSum / effects - musicRmsSum / moods:+0.0;-0.0} dB over the music)");
                Debug.Log(sb.ToString());
            }
            catch (Exception e)
            {
                Debug.LogError("[Metro] Audio check failed: " + e);
                code = 1;
            }
            EditorApplication.Exit(code);
        }

        /// <summary>
        /// Renders the soundtrack offline (for the trailer): -out &lt;file.wav&gt; -seconds N (default 80),
        /// moving through the moods of a day (-moods Morning,Rush,Day,Evening,Night by default) at full
        /// music volume, with a short fade in and out. 48 kHz, 16-bit stereo.
        /// </summary>
        public static void RenderMusic()
        {
            int code = 0;
            try
            {
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                string outPath = System.IO.Path.GetFullPath(Arg("-out", System.IO.Path.Combine(Application.dataPath, "..", "Builds", "Trailer", "music.wav")));
                float seconds = Mathf.Max(5f, float.Parse(Arg("-seconds", "80"), inv));
                var moods = new List<MusicMood>();
                foreach (var name in Arg("-moods", "Morning,Rush,Day,Evening,Night").Split(','))
                    if (Enum.TryParse(name.Trim(), true, out MusicMood m)) moods.Add(m);
                if (moods.Count == 0) moods.Add(MusicMood.Day);
                const int rate = 48000;
                var synth = new MusicSynth(rate);
                synth.SetVolume(1f);
                int total = (int)(seconds * rate);
                var buf = new float[total * 2];
                float peak = 0f;
                double sum = 0;
                for (int i = 0; i < total; i++)
                {
                    // Each mood gets an equal share; the synth changes at the next two-bar phrase.
                    synth.targetMood = (int)moods[Math.Min(moods.Count - 1, (int)((long)i * moods.Count / total))];
                    synth.Render(out float l, out float r);
                    float t = i / (float)rate;
                    float fade = Mathf.Clamp01(t / 1.5f) * Mathf.Clamp01((seconds - t) / 3f);
                    l *= fade;
                    r *= fade;
                    buf[2 * i] = l;
                    buf[2 * i + 1] = r;
                    peak = Mathf.Max(peak, Mathf.Max(Mathf.Abs(l), Mathf.Abs(r)));
                    sum += l * l + r * r;
                }
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(outPath));
                WriteWav(outPath, buf, rate, 2);
                Debug.Log($"[Metro] Music rendered: {outPath}, {seconds:0} s, {string.Join(" > ", moods)}, peak {Db(peak):0.0} dBFS, RMS {Db(Math.Sqrt(sum / (total * 2.0))):0.0} dBFS");
            }
            catch (Exception e)
            {
                Debug.LogError("[Metro] Music render failed: " + e);
                code = 1;
            }
            EditorApplication.Exit(code);
        }

        static double Db(double v) => 20.0 * Math.Log10(Math.Max(v, 1e-9));

        /// <summary>16-bit PCM WAV.</summary>
        static void WriteWav(string path, float[] data, int rate, int channels)
        {
            using (var w = new System.IO.BinaryWriter(System.IO.File.Create(path)))
            {
                int bytes = data.Length * 2;
                w.Write(Encoding.ASCII.GetBytes("RIFF"));
                w.Write(36 + bytes);
                w.Write(Encoding.ASCII.GetBytes("WAVE"));
                w.Write(Encoding.ASCII.GetBytes("fmt "));
                w.Write(16);
                w.Write((short)1);
                w.Write((short)channels);
                w.Write(rate);
                w.Write(rate * channels * 2);
                w.Write((short)(channels * 2));
                w.Write((short)16);
                w.Write(Encoding.ASCII.GetBytes("data"));
                w.Write(bytes);
                foreach (float f in data) w.Write((short)Mathf.Clamp(Mathf.RoundToInt(f * 32767f), -32768, 32767));
            }
        }

        public static void CompileCheck()
        {
            Debug.Log("[Metro] Compile check OK");
            EditorApplication.Exit(0);
        }

        public static void RunSimTest()
        {
            int code = 0;
            try
            {
                int seed = int.Parse(Arg("-seed", "1234"));
                int days = int.Parse(Arg("-days", "3"));
                int lines = int.Parse(Arg("-lines", "3"));
                bool free = Arg("-free", "1") == "1";
                int reveal = int.Parse(Arg("-reveal", "0"));
                float tpk = float.Parse(Arg("-tpk", "0.5"), System.Globalization.CultureInfo.InvariantCulture);
                int wagons = int.Parse(Arg("-wagons", "4"));
                bool hourly = Arg("-hourly", "1") == "1";
                Run(seed, days, lines, free, reveal, tpk, wagons, hourly);
            }
            catch (Exception e)
            {
                Debug.LogError("[Metro] Sim test failed: " + e);
                code = 1;
            }
            EditorApplication.Exit(code);
        }

        static void Run(int seed, int days, int lineCount, bool free, int reveal, float tpk, int wagons, bool hourly)
        {
            var sb = new StringBuilder();
            var sw = Stopwatch.StartNew();
            int tier = int.Parse(Arg("-tier", (ProgressionSystem.Tiers.Length - 1).ToString()));
            var gs = new GameState(new NewGameOptions { seed = seed, cityName = "Testville", difficulty = 1, startTier = tier });
            if (reveal > 0) gs.growth.RevealSilently(reveal);
            sb.AppendLine($"[Metro] City generated in {sw.ElapsedMilliseconds} ms: {gs.city.districts.Count} districts, " +
                          $"revealed pop {Fmt.Count(gs.city.revealedPopulation)}, revealed jobs {Fmt.Count(gs.city.revealedJobs)}");
            float totalPop = 0f, totalJobs = 0f;
            for (int i = 0; i < gs.city.CellCount; i++)
            {
                totalPop += gs.city.resBase[i];
                totalJobs += gs.city.jobBase[i];
            }
            sb.AppendLine($"[Metro] Full-grown city: pop {Fmt.Count(totalPop)}, jobs {Fmt.Count(totalJobs)}; rivers {gs.city.water.rivers.Count}, lakes {gs.city.water.lakes.Count}, parks {gs.city.parks.Count}, borders {gs.city.borders.Count}");
            foreach (var d in gs.city.districts)
                if (d.revealOrder < 8)
                    sb.AppendLine($"   #{d.revealOrder} {d.name} ({d.type}) pop {Fmt.Count(d.population)} jobs {Fmt.Count(d.jobs)} revealed={d.revealed}");

            double money0 = gs.econ.money;
            sw.Restart();
            AutoPlanner.BuildNetwork(gs, lineCount, free, tpk, wagons);
            sb.AppendLine($"[Metro] Auto network in {sw.ElapsedMilliseconds} ms: {gs.net.StationCount} stations, {gs.net.LineCount} lines, " +
                          $"{gs.trains.AliveCount} trains, {gs.net.edges.Count} edges, spent {Fmt.Money(money0 - gs.econ.money)}");
            foreach (var l in gs.net.AliveLines())
                sb.AppendLine($"   {l.name}: {l.stops.Count} stops, {Fmt.Km(l.path.length)}, {l.trains.Count} trains x{l.wagons}, cycle {Fmt.F1(l.cycleTime)} min, headway {Fmt.F1(l.headway)} min, loop={l.loop}");
            sb.AppendLine($"[Metro] Routing {Fmt.F1(gs.routing.lastComputeMs)} ms, demand {Fmt.F1(gs.demand.lastComputeMs)} ms, daily trips potential {Fmt.Count(gs.demand.dailyTrips)}, coverage {Fmt.Pct(gs.city.CoverageRatio)}");
            Debug.Log(sb.ToString());
            sb.Clear();

            sw.Restart();
            int lastHour = -1;
            double endMinute = gs.clock.totalMinutes + days * 1440.0;
            while (gs.clock.totalMinutes < endMinute)
            {
                gs.Advance(5f);
                int hour = (int)(gs.clock.totalMinutes / 60.0);
                if (hour != lastHour && (hourly || hour % 24 == 9 || hour % 24 == 18))
                {
                    lastHour = hour;
                    Station worst = null;
                    foreach (var s in gs.net.AliveStations())
                        if (worst == null || s.Crowding > worst.Crowding) worst = s;
                    MetroLine busiest = null;
                    foreach (var l in gs.net.AliveLines())
                        if (busiest == null || l.MaxSegLoad > busiest.MaxSegLoad) busiest = l;
                    sb.AppendLine($"D{gs.clock.DayNumber} {gs.clock.TimeString} riders/h {Fmt.Count(gs.stats.HourEntries)} today {Fmt.Count(gs.stats.ridersToday)} " +
                                  $"wait {Fmt.Count(gs.stats.totalWaiting)} onboard {Fmt.Count(gs.stats.totalOnboard)} " +
                                  $"sat {gs.satisfaction.Percent}% money {Fmt.Money(gs.econ.money)} " +
                                  $"worst {(worst != null ? worst.name + " " + Fmt.Pct(worst.Crowding) : "-")} " +
                                  $"busiest {(busiest != null ? busiest.name + " " + Fmt.Pct(busiest.MaxSegLoad) + " wait " + Fmt.F1(busiest.avgWaitRecent) : "-")} " +
                                  $"aband/h {Fmt.Count(gs.stats.HourAbandoned)} turned/h {Fmt.Count(gs.stats.HourTurnedAway)} denied/h {Fmt.Count(gs.stats.HourDenied)}");
                }
            }
            double secs = sw.Elapsed.TotalSeconds;
            sb.AppendLine($"[Metro] Simulated {days} days in {secs:0.00} s real time");
            foreach (var d in gs.stats.days)
            {
                sb.AppendLine($"[Metro] Day {d.day + 1}: riders {Fmt.Count(d.riders)} delivered {Fmt.Count(d.delivered)} abandoned {Fmt.Count(d.abandoned)} turned {Fmt.Count(d.turnedAway)} " +
                              $"income {Fmt.Money(d.income)} expenses {Fmt.Money(d.expenses)} net {Fmt.MoneySigned(d.net)} sat {Fmt.Pct(d.satisfaction)} pop {Fmt.Count(d.population)} cov {Fmt.Pct(d.coverage)}");
            }
            foreach (var led in gs.econ.history)
            {
                var parts = new StringBuilder();
                for (int i = 0; i < DayLedger.Count; i++)
                    if (Mathf.Abs(led.amounts[i]) > 1f) parts.Append($"{(LedgerCat)i}={Fmt.Money(led.amounts[i])} ");
                sb.AppendLine($"[Metro] Ledger day {led.day + 1}: {parts}");
            }
            sb.AppendLine("[Metro] Feed:");
            foreach (var m in gs.advisor.messages)
                sb.AppendLine($"   {GameClock.FormatMinute((float)(m.time % 1440))} [{m.CategoryLabel}] {m.text}");
            Debug.Log(sb.ToString());
        }
    }
}
