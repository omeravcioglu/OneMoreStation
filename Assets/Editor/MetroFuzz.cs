using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Metro.EditorTools
{
    /// <summary>
    /// Randomised stress test: thousands of build / demolish / train / upgrade operations
    /// interleaved with simulation time, checking invariants after every step.
    /// Run: -executeMethod Metro.EditorTools.MetroFuzz.Run [-seed N] [-ops N]
    /// </summary>
    public static class MetroFuzz
    {
        static string Arg(string name, string fallback)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return fallback;
        }

        public static void Run()
        {
            int code = 0;
            try
            {
                int seed = int.Parse(Arg("-seed", "7"));
                int ops = int.Parse(Arg("-ops", "3000"));
                int failures = 0;
                int maxTier = ProgressionSystem.Tiers.Length - 1;
                for (int round = 0; round < 3; round++) failures += Fuzz(seed + round * 1000, ops, maxTier, 0);
                // A city that starts small and is forced through every size while being built on at random.
                failures += Fuzz(seed + 3000, ops, 0, ops / (maxTier + 1));
                Debug.Log(failures == 0 ? "[Metro] FUZZ PASSED" : $"[Metro] FUZZ FAILED with {failures} problems");
                if (failures > 0) code = 1;
            }
            catch (Exception e)
            {
                Debug.LogError("[Metro] Fuzz crashed: " + e);
                code = 1;
            }
            EditorApplication.Exit(code);
        }

        static int Fuzz(int seed, int ops, int startTier, int growEvery)
        {
            var rng = new System.Random(seed);
            var gs = new GameState(new NewGameOptions { seed = seed, cityName = "Fuzzville", difficulty = 1, startTier = startTier });
            gs.growth.RevealSilently(8);
            gs.econ.money = 1e10;
            var b = gs.build;
            var counts = new Dictionary<string, int>();
            int problems = 0;
            var sb = new StringBuilder();

            Vector2 RandomPoint()
            {
                var revealed = new List<District>();
                foreach (var d in gs.city.districts)
                    if (d.revealed) revealed.Add(d);
                var dd = revealed[rng.Next(revealed.Count)];
                return dd.center + new Vector2((float)(rng.NextDouble() * 6 - 3), (float)(rng.NextDouble() * 6 - 3));
            }

            MetroLine RandomLine()
            {
                var ls = new List<MetroLine>(gs.net.AliveLines());
                return ls.Count == 0 ? null : ls[rng.Next(ls.Count)];
            }

            Station RandomStation()
            {
                var ss = new List<Station>(gs.net.AliveStations());
                return ss.Count == 0 ? null : ss[rng.Next(ss.Count)];
            }

            List<PlannedStop> RandomPlan(int n, int avoidLine = -1)
            {
                var plan = new List<PlannedStop>();
                for (int i = 0; i < n; i++)
                {
                    PlannedStop p;
                    if (rng.NextDouble() < 0.3 && gs.net.StationCount > 0)
                    {
                        var s = RandomStation();
                        p = new PlannedStop { stationId = s.id, pos = s.pos };
                    }
                    else p = new PlannedStop { stationId = -1, pos = RandomPoint() };
                    p.diagonalFirst = rng.NextDouble() < 0.5;
                    plan.Add(p);
                }
                return plan;
            }

            int rejectedOutside = 0;
            for (int i = 0; i < ops; i++)
            {
                int op = rng.Next(16);
                string name = "";
                try
                {
                    if (growEvery > 0 && i > 0 && i % growEvery == 0 && !gs.progress.IsMax)
                    {
                        var before = gs.city.playArea;
                        gs.progress.TierUp();
                        var after = gs.city.playArea;
                        if (after.xMin > before.xMin + 1e-3f || after.yMin > before.yMin + 1e-3f || after.xMax < before.xMax - 1e-3f || after.yMax < before.yMax - 1e-3f)
                        {
                            problems++;
                            sb.AppendLine($"  op {i}: the map shrank on growing to {gs.progress.Current.name}: {before} -> {after}");
                        }
                    }
                    switch (op)
                    {
                        case 0:
                        case 1:
                            name = "BuildLine";
                            b.BuildLine(RandomPlan(rng.Next(2, 7)), rng.NextDouble() < 0.15, rng.NextDouble() < 0.5, out _);
                            break;
                        case 2:
                        {
                            name = "ExtendLine";
                            var l = RandomLine();
                            if (l != null) b.ExtendLine(l, rng.NextDouble() < 0.5, RandomPlan(rng.Next(0, 3)), rng.NextDouble() < 0.1, rng.NextDouble() < 0.5);
                            break;
                        }
                        case 3:
                        {
                            name = "InsertStation";
                            var edges = new List<TrackEdge>(gs.net.edges.Values);
                            if (edges.Count > 0)
                            {
                                var e = edges[rng.Next(edges.Count)];
                                b.InsertStation(e.a, e.b, Geo.SampleAt(e.path, e.cum, (float)(e.length * rng.NextDouble()), out _), out _);
                            }
                            break;
                        }
                        case 4:
                        {
                            name = "Demolish";
                            var s = RandomStation();
                            if (s != null) b.DemolishStation(s.id);
                            break;
                        }
                        case 5:
                        {
                            name = "DeleteLine";
                            var l = RandomLine();
                            if (l != null && rng.NextDouble() < 0.3) b.DeleteLine(l);
                            break;
                        }
                        case 6:
                        case 7:
                        {
                            name = "AddTrain";
                            var l = RandomLine();
                            if (l != null)
                            {
                                if (!gs.net.LineHasDepot(l)) b.BuildDepot(l.stops[rng.Next(l.stops.Count)]);
                                var r = b.AddTrain(l);
                                if (!r.ok && r.error.Contains("full"))
                                    foreach (var d in gs.net.AliveDepots())
                                        if (b.ExpandDepot(d.id).ok) break;
                            }
                            break;
                        }
                        case 8:
                        {
                            name = "RemoveTrain";
                            var l = RandomLine();
                            if (l != null) b.RemoveTrain(l);
                            break;
                        }
                        case 9:
                        {
                            name = "SetWagons";
                            var l = RandomLine();
                            if (l != null) b.SetWagons(l, rng.Next(1, 10));
                            break;
                        }
                        case 10:
                        {
                            name = "Upgrade";
                            var s = RandomStation();
                            if (s != null) b.Upgrade(s.id, (StationUpgrade)rng.Next(4));
                            break;
                        }
                        case 11:
                        {
                            name = "Truncate";
                            var l = RandomLine();
                            if (l != null) b.TruncateLine(l, rng.NextDouble() < 0.5);
                            break;
                        }
                        case 12:
                        {
                            name = "Depot";
                            var s = RandomStation();
                            if (s != null)
                            {
                                if (s.depotId >= 0 && rng.NextDouble() < 0.3) b.RemoveDepot(s.depotId);
                                else b.BuildDepot(s.id);
                            }
                            break;
                        }
                        case 13:
                        {
                            name = "CityHall";
                            var gov = gs.gov;
                            if (gov.open.Count == 0 || rng.NextDouble() < 0.2) gov.Propose();
                            if (gov.open.Count > 0)
                            {
                                var r = gov.open[rng.Next(gov.open.Count)];
                                double a = rng.NextDouble();
                                if (a < 0.45) gov.Accept(r);
                                else if (a < 0.65) gov.Decline(r);
                                else if (a < 0.8) gov.Pay(r);
                                else gov.Endorse(rng.Next(-1, 2));
                            }
                            break;
                        }
                        case 14:
                        {
                            // Build or tear down something, then take it back at once: the network and the
                            // money must be exactly as they were.
                            name = "Undo";
                            string before = Fingerprint(gs);
                            double money0 = gs.econ.money;
                            var l = RandomLine();
                            BuildResult r = default;
                            switch (rng.Next(5))
                            {
                                case 0: r = b.BuildLine(RandomPlan(rng.Next(2, 6)), false, rng.NextDouble() < 0.5, out _); break;
                                case 1: if (l != null) r = b.ExtendLine(l, rng.NextDouble() < 0.5, RandomPlan(rng.Next(1, 3)), false, rng.NextDouble() < 0.5); break;
                                case 2: if (l != null) r = b.DeleteLine(l); break;
                                case 3:
                                {
                                    var s = RandomStation();
                                    if (s != null) r = b.DemolishStation(s.id);
                                    break;
                                }
                                default: if (l != null) r = b.TruncateLine(l, rng.NextDouble() < 0.5); break;
                            }
                            if (r.ok)
                            {
                                if (!b.Undo.Undo(out string msg))
                                {
                                    problems++;
                                    if (problems <= 12) sb.AppendLine($"  op {i} Undo: refused right after a change: {msg}");
                                }
                                else
                                {
                                    counts.TryGetValue("UndoChecked", out int uc);
                                    counts["UndoChecked"] = uc + 1;
                                    string after = Fingerprint(gs);
                                    if (after != before)
                                    {
                                        problems++;
                                        if (problems <= 12) sb.AppendLine($"  op {i} Undo: network differs after undo ({msg})\n    before {before}\n    after  {after}");
                                    }
                                    if (Math.Abs(gs.econ.money - money0) > 1.0)
                                    {
                                        problems++;
                                        if (problems <= 12) sb.AppendLine($"  op {i} Undo: money {money0:0} -> {gs.econ.money:0} after undo ({msg})");
                                    }
                                }
                            }
                            else if (b.Undo.CanUndo && rng.NextDouble() < 0.5) b.Undo.Undo(out _);
                            break;
                        }
                        default:
                            name = "Advance";
                            gs.Advance((float)(rng.NextDouble() * 90.0));
                            break;
                    }
                    gs.Advance((float)(rng.NextDouble() * 3.0));
                    counts.TryGetValue(name, out int c);
                    counts[name] = c + 1;
                    foreach (var st in gs.net.AliveStations())
                        if (!gs.city.InPlayArea(st.pos, -0.01f)) rejectedOutside++;
                    if (rejectedOutside > 0)
                    {
                        problems++;
                        sb.AppendLine($"  op {i} {name}: a station stands outside the map ({gs.city.playArea})");
                        rejectedOutside = 0;
                    }
                    string bad = Check(gs);
                    if (bad != null)
                    {
                        problems++;
                        if (problems <= 12) sb.AppendLine($"  op {i} {name}: {bad}");
                    }
                }
                catch (Exception e)
                {
                    problems++;
                    if (problems <= 12) sb.AppendLine($"  op {i} {name}: EXCEPTION {e.GetType().Name}: {e.Message}\n{e.StackTrace}");
                }
            }
            // Save/load round-trip of the fuzzed state (with a pencil plan).
            try
            {
                var plan = new PencilPlan { name = "Fuzz plan" };
                for (int k = 0; k < 4; k++)
                {
                    plan.points.Add(RandomPoint());
                    plan.diagonalFirst.Add(k % 2 == 0);
                }
                gs.plans.Add(plan);
                var data = SaveSystem.Capture(gs);
                var json = JsonUtility.ToJson(data);
                var back = SaveSystem.Restore(JsonUtility.FromJson<SaveData>(json));
                if (back.net.StationCount != gs.net.StationCount || back.net.LineCount != gs.net.LineCount || back.trains.AliveCount != gs.trains.AliveCount)
                {
                    problems++;
                    sb.AppendLine($"  save/load mismatch: stations {gs.net.StationCount}/{back.net.StationCount} lines {gs.net.LineCount}/{back.net.LineCount} trains {gs.trains.AliveCount}/{back.trains.AliveCount}");
                }
                if (back.plans.Count != gs.plans.Count || back.plans[0].points.Count != 4 || back.plans[0].name != "Fuzz plan" || back.plans[0].diagonalFirst[2] != true)
                {
                    problems++;
                    sb.AppendLine($"  save/load pencil plans mismatch: {gs.plans.Count}/{back.plans.Count}");
                }
                if (back.progress.tier != gs.progress.tier || back.city.playArea != gs.city.playArea || back.progress.GoalsDone != gs.progress.GoalsDone)
                {
                    problems++;
                    sb.AppendLine($"  save/load progression mismatch: tier {gs.progress.tier}/{back.progress.tier} area {gs.city.playArea}/{back.city.playArea} goals {gs.progress.GoalsDone}/{back.progress.GoalsDone}");
                }
                if (Mathf.Abs(back.gov.approval - gs.gov.approval) > 0.01f || back.gov.open.Count != gs.gov.open.Count || back.gov.completed != gs.gov.completed
                    || back.gov.mayor.name != gs.gov.mayor.name || back.gov.landmarks.Count != gs.gov.landmarks.Count || back.gov.gameOver != gs.gov.gameOver)
                {
                    problems++;
                    sb.AppendLine($"  save/load City Hall mismatch: approval {gs.gov.approval:0.00}/{back.gov.approval:0.00} open {gs.gov.open.Count}/{back.gov.open.Count} done {gs.gov.completed}/{back.gov.completed} mayor {gs.gov.mayor.name}/{back.gov.mayor.name} landmarks {gs.gov.landmarks.Count}/{back.gov.landmarks.Count}");
                }
                back.Advance(120f);
                string bad = Check(back);
                if (bad != null)
                {
                    problems++;
                    sb.AppendLine("  after reload: " + bad);
                }
            }
            catch (Exception e)
            {
                problems++;
                sb.AppendLine("  save/load EXCEPTION " + e);
            }

            var opsText = new StringBuilder();
            foreach (var kv in counts) opsText.Append(kv.Key).Append('=').Append(kv.Value).Append(' ');
            Debug.Log($"[Metro] Fuzz seed {seed} (from {ProgressionSystem.Tiers[startTier].name}, now {gs.progress.Current.name}): {problems} problems; final {gs.net.StationCount} stations, {gs.net.LineCount} lines, {gs.trains.AliveCount} trains, day {gs.clock.DayNumber}, riders today {Fmt.Count(gs.stats.ridersToday)}\n  ops: {opsText}\n{sb}");
            return problems;
        }

        /// <summary>What undo must restore: stations and their upgrades, lines, train counts, depots, track.</summary>
        static string Fingerprint(GameState gs)
        {
            var sb = new StringBuilder();
            foreach (var s in gs.net.AliveStations())
                sb.Append('s').Append(s.id).Append(s.depotId >= 0 ? "d" : "").Append(s.platformLevel).Append(s.capacityLevel)
                  .Append(s.elevators ? 'e' : '-').Append(s.concourse ? 'c' : '-').Append(';');
            foreach (var l in gs.net.AliveLines())
            {
                sb.Append('l').Append(l.id).Append(l.loop ? "o" : "").Append('w').Append(l.wagons).Append('t').Append(l.trains.Count).Append(':');
                foreach (int s in l.stops) sb.Append(s).Append(',');
                sb.Append(';');
            }
            foreach (var d in gs.net.AliveDepots()) sb.Append('d').Append(d.id).Append('@').Append(d.stationId).Append('L').Append(d.level).Append(';');
            sb.Append('e').Append(gs.net.edges.Count);
            return sb.ToString();
        }

        static string Check(GameState gs)
        {
            var net = gs.net;
            if (net.LineCount > gs.progress.MaxLines) return $"{net.LineCount} lines with a licence for {gs.progress.MaxLines}";
            foreach (var l in net.AliveLines())
            {
                if (l.stops.Count < 2) return $"{l.name} has {l.stops.Count} stops";
                var seen = new HashSet<int>();
                foreach (int s in l.stops)
                {
                    if (net.GetStation(s) == null) return $"{l.name} references dead station {s}";
                    if (!seen.Add(s)) return $"{l.name} visits station {s} twice";
                    if (!net.stations[s].lines.Contains(l.id)) return $"station {s} doesn't list {l.name}";
                }
                for (int k = 0; k < l.SegmentCount; k++)
                    if (net.GetEdge(l.stops[k], l.stops[(k + 1) % l.stops.Count]) == null) return $"{l.name} missing edge at segment {k}";
                if (l.path == null || l.path.stopS.Count != (l.loop ? l.stops.Count : l.stops.Count)) return $"{l.name} path out of sync";
                if (float.IsNaN(l.path.length) || l.path.length <= 0f) return $"{l.name} bad length";
                foreach (int tid in l.trains)
                {
                    var t = gs.trains.Get(tid);
                    if (t == null) return $"{l.name} lists dead train {tid}";
                    if (t.lineId != l.id) return $"train {tid} line mismatch";
                    if (t.stopIndex < 0 || t.stopIndex >= l.stops.Count) return $"train {tid} stopIndex {t.stopIndex} out of range ({l.stops.Count})";
                    if (float.IsNaN(t.pos.x) || float.IsNaN(t.s)) return $"train {tid} NaN position";
                    if (t.cargo.total < -0.01f || float.IsNaN(t.cargo.total)) return $"train {tid} bad cargo {t.cargo.total}";
                }
            }
            foreach (var t in gs.trains.trains)
            {
                if (!t.alive) continue;
                var l = net.GetLine(t.lineId);
                if (l == null) return $"live train {t.id} on dead line {t.lineId}";
                if (!l.trains.Contains(t.id)) return $"train {t.id} not listed by its line";
            }
            var pax = gs.pax;
            for (int s = 0; s < net.stations.Count && s < pax.cap; s++)
            {
                float w = pax.waitingTotal[s];
                if (float.IsNaN(w) || w < -0.5f) return $"station {s} waiting {w}";
            }
            if (double.IsNaN(gs.econ.money)) return "money NaN";
            var gov = gs.gov;
            if (float.IsNaN(gov.approval) || gov.approval < -0.01f || gov.approval > 100.01f) return $"approval out of range {gov.approval}";
            if (float.IsNaN(gov.SubsidyFactor) || float.IsNaN(gov.PermitFactor)) return "City Hall factors NaN";
            foreach (var r in gov.open)
            {
                if (r.state != RequestState.Proposed && r.state != RequestState.Active) return $"open request {r.id} in state {r.state}";
                if (gov.history.Contains(r)) return $"request {r.id} both open and closed";
                if (r.landmarkId >= 0 && gov.LandmarkById(r.landmarkId) == null) return $"request {r.id} lost its landmark";
                if (float.IsNaN(r.progress) || r.progress < -0.01f || r.progress > 1.01f) return $"request {r.id} progress {r.progress}";
            }
            foreach (var l in gov.landmarks)
                if (!gs.city.InBounds(l.pos)) return $"landmark {l.name} off the map";
            if (float.IsNaN(gs.satisfaction.score)) return "satisfaction NaN";
            return null;
        }
    }
}
