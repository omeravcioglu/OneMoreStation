using System;
using System.Collections.Generic;
using UnityEngine;

namespace Metro
{
    public struct BuildResult
    {
        public bool ok;
        public string error;
        public float cost;

        public static BuildResult Fail(string e) => new BuildResult { ok = false, error = e };
        public static BuildResult Ok(float cost = 0f) => new BuildResult { ok = true, cost = cost };
    }

    /// <summary>A stop of a line being drawn: an existing station or a new one at pos.</summary>
    public struct PlannedStop
    {
        public int stationId;
        public Vector2 pos;
        public bool diagonalFirst;   // bend of the segment arriving at this stop

        public bool IsNew => stationId < 0;
    }

    public enum StationUpgrade { Platform, Capacity, Elevators, Concourse }

    /// <summary>Validated, paid-for changes to the network. Every UI/tool action goes through here.</summary>
    public sealed class ConstructionService
    {
        readonly GameState gs;
        public bool freeBuild;

        public ConstructionService(GameState gs)
        {
            this.gs = gs;
        }

        MetroNetwork Net => gs.net;
        CityModel City => gs.city;
        Economy Econ => gs.econ;
        float Day => (float)(gs.clock.totalMinutes / Balance.MinutesPerDay);

        /// <summary>Cost multiplier: difficulty times City Hall's permit factor.</summary>
        public float M => freeBuild ? 0f : gs.CostMultiplier * gs.gov.PermitFactor;
        public float StationCost => Balance.StationCost * M;
        public float DepotCost => Balance.DepotCost * M;
        public float DepotExpandCost => Balance.DepotExpandCost * M;
        public float WagonCost => Balance.WagonCost * M;
        public float TrainCost(int wagons) => (Balance.TrainCost + wagons * Balance.WagonCost) * M;

        public float UpgradeCost(Station s, StationUpgrade u)
        {
            switch (u)
            {
                case StationUpgrade.Platform: return s.platformLevel >= 3 ? 0f : Balance.PlatformUpgradeCost(s.platformLevel + 1) * M;
                case StationUpgrade.Capacity: return s.capacityLevel >= 3 ? 0f : Balance.CapacityUpgradeCost(s.capacityLevel + 1) * M;
                case StationUpgrade.Elevators: return Balance.ElevatorCost * M;
                case StationUpgrade.Concourse: return Balance.ConcourseCost * M;
            }
            return 0f;
        }

        bool Pay(float cost, LedgerCat cat)
        {
            if (cost <= 0f) return true;
            return Econ.TrySpend(cost, cat);
        }

        string NoMoney(float cost) => $"Not enough money: this costs {Fmt.Money(cost)}.";

        // ------------------------------------------------------------------ validation & estimates

        public bool ValidStationSite(Vector2 p, out string reason, int ignoreStation = -1)
        {
            if (!City.InBounds(p, 0.3f) || !City.InPlayArea(p, 0.25f))
            {
                reason = "Beyond the city limits. Grow the city to open more of the map.";
                return false;
            }
            if (!City.IsRevealed(p))
            {
                reason = "This district hasn't been built yet. The city will get there.";
                return false;
            }
            if (City.water.IsWater(p, 0.08f))
            {
                reason = "Stations can't be built on water.";
                return false;
            }
            var near = Net.NearestStation(p, Balance.MinStationSpacing, ignoreStation);
            if (near != null)
            {
                reason = $"Too close to {near.name}.";
                return false;
            }
            reason = null;
            return true;
        }

        public float SegmentCost(Vector2 a, Vector2 b, bool diagonalFirst, int sa, int sb, out float length, out float water)
        {
            if (sa >= 0 && sb >= 0)
            {
                var e = Net.GetEdge(sa, sb);
                if (e != null)
                {
                    length = e.length;
                    water = e.waterLength;
                    return (e.LandLength * Balance.TunnelPerKm + e.waterLength * Balance.BridgePerKm) * Balance.SharedTrackFactor * M;
                }
            }
            var path = LineGeometry.OctilinearPath(a, b, diagonalFirst);
            length = Geo.PolylineLength(path);
            water = City.water.WaterRanges(path, null);
            return ((length - water) * Balance.TunnelPerKm + water * Balance.BridgePerKm) * M;
        }

        /// <summary>Total cost of a planned chain of stops starting at an optional anchor station.</summary>
        public float PlanCost(IList<PlannedStop> stops, int anchorStation, bool closeLoop, bool closingDiagonal, out float km, out float waterKm)
        {
            float c = 0f;
            km = 0f;
            waterKm = 0f;
            bool hasPrev = false;
            Vector2 prevPos = Vector2.zero;
            int prevId = -1;
            var anchor = Net.GetStation(anchorStation);
            if (anchor != null)
            {
                hasPrev = true;
                prevPos = anchor.pos;
                prevId = anchor.id;
            }
            for (int i = 0; i < stops.Count; i++)
            {
                var s = stops[i];
                if (s.IsNew) c += StationCost;
                if (hasPrev)
                {
                    c += SegmentCost(prevPos, s.pos, s.diagonalFirst, prevId, s.stationId, out float L, out float W);
                    km += L;
                    waterKm += W;
                }
                hasPrev = true;
                prevPos = s.pos;
                prevId = s.stationId;
            }
            if (closeLoop && stops.Count > 0)
            {
                Vector2 firstPos;
                int firstId;
                if (anchor != null)
                {
                    firstPos = anchor.pos;
                    firstId = anchor.id;
                }
                else
                {
                    firstPos = stops[0].pos;
                    firstId = stops[0].stationId;
                }
                c += SegmentCost(prevPos, firstPos, closingDiagonal, prevId, firstId, out float L, out float W);
                km += L;
                waterKm += W;
            }
            return c;
        }

        bool ValidatePlan(IList<PlannedStop> stops, out string error)
        {
            for (int i = 0; i < stops.Count; i++)
            {
                var s = stops[i];
                if (s.IsNew)
                {
                    if (!ValidStationSite(s.pos, out error)) return false;
                    for (int j = 0; j < i; j++)
                    {
                        if (stops[j].IsNew && Vector2.Distance(stops[j].pos, s.pos) < Balance.MinStationSpacing)
                        {
                            error = "Two new stations are too close together.";
                            return false;
                        }
                    }
                }
                else if (Net.GetStation(s.stationId) == null)
                {
                    error = "That station no longer exists.";
                    return false;
                }
            }
            error = null;
            return true;
        }

        List<int> Materialize(IList<PlannedStop> stops)
        {
            var ids = new List<int>(stops.Count);
            foreach (var s in stops) ids.Add(s.IsNew ? Net.CreateStation(s.pos, Day).id : s.stationId);
            return ids;
        }

        void SetBendIfNew(int a, int b, bool diagonalFirstFromA)
        {
            if (Net.GetEdge(a, b) == null) Net.SetBend(a, b, diagonalFirstFromA);
        }

        // ------------------------------------------------------------------ lines

        public string LineLicenceProblem()
        {
            int max = gs.progress.MaxLines;
            if (Net.LineCount < max) return null;
            var next = gs.progress.Next;
            string more = next != null ? $" Grow into a {next.name} to open more." : "";
            return max == 1 ? "Your metro licence covers one line for now." + more : $"Your metro licence covers {max} lines for now." + more;
        }

        BuildResult BuildLineImpl(IList<PlannedStop> stops, bool loop, bool closingDiagonal, out MetroLine line)
        {
            line = null;
            string licence = LineLicenceProblem();
            if (licence != null) return BuildResult.Fail(licence);
            if (stops.Count < 2) return BuildResult.Fail("A line needs at least two stations.");
            var seen = new HashSet<int>();
            foreach (var s in stops)
                if (!s.IsNew && !seen.Add(s.stationId)) return BuildResult.Fail("A line can't visit the same station twice.");
            if (loop && stops.Count < 3) loop = false;
            if (!ValidatePlan(stops, out string err)) return BuildResult.Fail(err);
            float cost = PlanCost(stops, -1, loop, closingDiagonal, out _, out _);
            if (!Econ.CanAfford(cost)) return BuildResult.Fail(NoMoney(cost));

            var ids = Materialize(stops);
            for (int i = 1; i < ids.Count; i++) SetBendIfNew(ids[i - 1], ids[i], stops[i].diagonalFirst);
            if (loop) SetBendIfNew(ids[ids.Count - 1], ids[0], closingDiagonal);
            line = Net.CreateLine(ids, loop, Net.NextFreeColor(), Day);
            Net.Rebuild();
            Pay(cost, LedgerCat.Construction);
            gs.MarkNetworkChanged();
            gs.RaiseLineCreated(line);
            return BuildResult.Ok(cost);
        }

        BuildResult ExtendLineImpl(MetroLine line, bool atEnd, IList<PlannedStop> stops, bool closeLoop, bool closingDiagonal)
        {
            if (line == null || !line.alive) return BuildResult.Fail("That line no longer exists.");
            if (line.loop) return BuildResult.Fail("Circle lines can't be extended.");
            if (stops.Count == 0 && !closeLoop) return BuildResult.Fail("Nothing to build.");
            foreach (var s in stops)
                if (!s.IsNew && line.stops.Contains(s.stationId)) return BuildResult.Fail("The line already stops there.");
            if (closeLoop && line.stops.Count + stops.Count < 3) closeLoop = false;
            if (!ValidatePlan(stops, out string err)) return BuildResult.Fail(err);
            int anchor = atEnd ? line.stops[line.stops.Count - 1] : line.stops[0];
            int other = atEnd ? line.stops[0] : line.stops[line.stops.Count - 1];

            // The loop closes back to the far terminal of the line.
            float cost = PlanCost(stops, anchor, false, false, out _, out _);
            if (closeLoop)
            {
                Vector2 lastPos = stops.Count > 0 ? stops[stops.Count - 1].pos : Net.stations[anchor].pos;
                int lastId = stops.Count > 0 ? stops[stops.Count - 1].stationId : anchor;
                cost += SegmentCost(lastPos, Net.stations[other].pos, closingDiagonal, lastId, other, out _, out _);
            }
            if (!Econ.CanAfford(cost)) return BuildResult.Fail(NoMoney(cost));

            var ids = Materialize(stops);
            int prev = anchor;
            for (int i = 0; i < ids.Count; i++)
            {
                SetBendIfNew(prev, ids[i], stops[i].diagonalFirst);
                prev = ids[i];
            }
            if (closeLoop) SetBendIfNew(prev, other, closingDiagonal);
            if (atEnd) line.stops.AddRange(ids);
            else
            {
                ids.Reverse();
                line.stops.InsertRange(0, ids);
            }
            if (closeLoop) line.loop = true;
            Net.Rebuild();
            gs.trains.OnLineChanged(line);
            Pay(cost, LedgerCat.Construction);
            gs.MarkNetworkChanged();
            return BuildResult.Ok(cost);
        }

        /// <summary>Adds a station on an existing track between a and b (all lines on that track stop there).</summary>
        BuildResult InsertStationImpl(int a, int b, Vector2 near, out Station station)
        {
            station = null;
            if (!gs.Has(Feature.InsertStation)) return BuildResult.Fail(ProgressionSystem.LockedMessage(Feature.InsertStation));
            var e = Net.GetEdge(a, b);
            if (e == null) return BuildResult.Fail("There's no track there.");
            Geo.DistToPolyline(near, e.path, out int seg, out float t);
            if (seg < 0) return BuildResult.Fail("There's no track there.");
            Vector2 p = Vector2.Lerp(e.path[seg], e.path[seg + 1], t);
            if (!ValidStationSite(p, out string reason)) return BuildResult.Fail(reason);
            float cost = StationCost;
            if (!Econ.CanAfford(cost)) return BuildResult.Fail(NoMoney(cost));

            station = Net.CreateStation(p, Day);
            bool diag = e.diagonalFirst;
            Net.SetBend(e.a, station.id, diag);
            Net.SetBend(station.id, e.b, diag);
            var touched = Net.InsertIntoEdge(a, b, station.id);
            Net.Rebuild();
            foreach (var l in touched) gs.trains.OnLineChanged(l);
            Pay(cost, LedgerCat.Construction);
            gs.MarkNetworkChanged();
            return BuildResult.Ok(cost);
        }

        BuildResult DeleteLineImpl(MetroLine line)
        {
            if (line == null || !line.alive) return BuildResult.Fail("That line no longer exists.");
            float refund = line.trains.Count * TrainCost(line.wagons) * Balance.TrainResale;
            gs.trains.RemoveAll(line);
            line.alive = false;
            Net.Rebuild();
            if (refund > 0f) Econ.Earn(refund, LedgerCat.Sales);
            gs.MarkNetworkChanged();
            return BuildResult.Ok(-refund);
        }

        BuildResult TruncateLineImpl(MetroLine line, bool atEnd)
        {
            if (line == null || !line.alive) return BuildResult.Fail("That line no longer exists.");
            if (line.loop)
            {
                line.loop = false;
            }
            else
            {
                if (line.stops.Count <= 2) return BuildResult.Fail("A line needs at least two stations. Delete it instead.");
                line.stops.RemoveAt(atEnd ? line.stops.Count - 1 : 0);
            }
            Net.Rebuild();
            gs.trains.OnLineChanged(line);
            gs.MarkNetworkChanged();
            return BuildResult.Ok();
        }

        BuildResult SetLineColorImpl(MetroLine line, int colorIndex)
        {
            if (line == null) return BuildResult.Fail("No line selected.");
            line.colorIndex = ((colorIndex % LinePalette.Count) + LinePalette.Count) % LinePalette.Count;
            line.name = line.ColorName + " Line";
            Net.version++;
            return BuildResult.Ok();
        }

        // ------------------------------------------------------------------ stations

        BuildResult DemolishStationImpl(int stationId)
        {
            var st = Net.GetStation(stationId);
            if (st == null) return BuildResult.Fail("That station no longer exists.");
            var touched = Net.RemoveStationFromLines(stationId);
            gs.pax.ClearStation(stationId);
            st.alive = false;
            if (st.depotId >= 0)
            {
                var d = Net.GetDepot(st.depotId);
                if (d != null) d.alive = false;
            }
            Net.Rebuild();
            float refund = StationCost * Balance.DemolishRefund;
            foreach (var l in touched)
            {
                if (!l.alive)
                {
                    refund += l.trains.Count * TrainCost(l.wagons) * Balance.TrainResale;
                    gs.trains.RemoveAll(l);
                }
                else gs.trains.OnLineChanged(l);
            }
            if (refund > 0f) Econ.Earn(refund, LedgerCat.Sales);
            gs.MarkNetworkChanged();
            return BuildResult.Ok(-refund);
        }

        BuildResult UpgradeImpl(int stationId, StationUpgrade u)
        {
            var st = Net.GetStation(stationId);
            if (st == null) return BuildResult.Fail("That station no longer exists.");
            if (!gs.Has(Feature.Upgrades)) return BuildResult.Fail(ProgressionSystem.LockedMessage(Feature.Upgrades));
            switch (u)
            {
                case StationUpgrade.Platform:
                    if (st.platformLevel >= 3) return BuildResult.Fail("Platforms are already at full length.");
                    break;
                case StationUpgrade.Capacity:
                    if (st.capacityLevel >= 3) return BuildResult.Fail("This station is already fully expanded.");
                    break;
                case StationUpgrade.Elevators:
                    if (st.elevators) return BuildResult.Fail("This station already has elevators.");
                    break;
                case StationUpgrade.Concourse:
                    if (st.concourse) return BuildResult.Fail("This station already has a transfer concourse.");
                    if (st.lines.Count < 2) return BuildResult.Fail("A transfer concourse only helps where lines meet.");
                    break;
            }
            float cost = UpgradeCost(st, u);
            if (!Pay(cost, LedgerCat.Construction)) return BuildResult.Fail(NoMoney(cost));
            switch (u)
            {
                case StationUpgrade.Platform: st.platformLevel++; break;
                case StationUpgrade.Capacity: st.capacityLevel++; break;
                case StationUpgrade.Elevators: st.elevators = true; break;
                case StationUpgrade.Concourse:
                    st.concourse = true;
                    gs.MarkServiceChanged();
                    break;
            }
            Net.version++;
            return BuildResult.Ok(cost);
        }

        // ------------------------------------------------------------------ depots

        BuildResult BuildDepotImpl(int stationId)
        {
            var st = Net.GetStation(stationId);
            if (st == null) return BuildResult.Fail("Pick a station for the depot.");
            if (st.depotId >= 0) return BuildResult.Fail("This station already has a depot.");
            if (st.lines.Count == 0) return BuildResult.Fail("Depots must sit on a line.");
            float cost = DepotCost;
            if (!Pay(cost, LedgerCat.Construction)) return BuildResult.Fail(NoMoney(cost));
            Net.CreateDepot(st.id, Day);
            Net.Rebuild();
            gs.MarkServiceChanged();
            return BuildResult.Ok(cost);
        }

        BuildResult ExpandDepotImpl(int depotId)
        {
            var d = Net.GetDepot(depotId);
            if (d == null) return BuildResult.Fail("That depot no longer exists.");
            if (!gs.Has(Feature.DepotExpansion)) return BuildResult.Fail(ProgressionSystem.LockedMessage(Feature.DepotExpansion));
            if (d.level >= Balance.MaxDepotLevel) return BuildResult.Fail("This depot is already at full size.");
            float cost = DepotExpandCost;
            if (!Pay(cost, LedgerCat.Construction)) return BuildResult.Fail(NoMoney(cost));
            d.level++;
            Net.version++;
            return BuildResult.Ok(cost);
        }

        BuildResult RemoveDepotImpl(int depotId)
        {
            var d = Net.GetDepot(depotId);
            if (d == null) return BuildResult.Fail("That depot no longer exists.");
            d.alive = false;
            Net.Rebuild();
            float refund = DepotCost * Balance.DemolishRefund;
            Econ.Earn(refund, LedgerCat.Sales);
            return BuildResult.Ok(-refund);
        }

        // ------------------------------------------------------------------ trains

        public int MaxWagonsForLine(MetroLine line)
        {
            int max = 8;
            foreach (int sid in line.stops)
            {
                var st = Net.GetStation(sid);
                if (st != null) max = Mathf.Min(max, st.MaxWagons);
            }
            return Mathf.Max(Balance.MinWagons, max);
        }

        public string CanAddTrain(MetroLine line)
        {
            if (line == null || !line.alive) return "No line selected.";
            if (!Net.LineHasDepot(line)) return $"The {line.name} needs a depot at one of its stations first.";
            if (gs.trains.AliveCount >= Net.DepotCapacity) return "All depots are full. Build or expand a depot.";
            return null;
        }

        BuildResult AddTrainImpl(MetroLine line)
        {
            string why = CanAddTrain(line);
            if (why != null) return BuildResult.Fail(why);
            float cost = TrainCost(line.wagons);
            if (!Pay(cost, LedgerCat.Construction)) return BuildResult.Fail(NoMoney(cost));
            gs.trains.AddTrain(line);
            gs.MarkServiceChanged();
            return BuildResult.Ok(cost);
        }

        BuildResult RemoveTrainImpl(MetroLine line)
        {
            if (line == null || line.trains.Count == 0) return BuildResult.Fail("No trains to remove.");
            float refund = TrainCost(line.wagons) * Balance.TrainResale;
            gs.trains.RemoveTrain(line);
            Econ.Earn(refund, LedgerCat.Sales);
            gs.MarkServiceChanged();
            return BuildResult.Ok(-refund);
        }

        BuildResult SetWagonsImpl(MetroLine line, int wagons)
        {
            if (line == null || !line.alive) return BuildResult.Fail("No line selected.");
            if (wagons != line.wagons && !gs.Has(Feature.Wagons)) return BuildResult.Fail(ProgressionSystem.LockedMessage(Feature.Wagons));
            int max = MaxWagonsForLine(line);
            if (wagons < Balance.MinWagons) return BuildResult.Fail($"Trains need at least {Balance.MinWagons} wagons.");
            if (wagons > max) return BuildResult.Fail($"Platforms on this line only fit {max} wagons. Lengthen platforms to run longer trains.");
            int delta = wagons - line.wagons;
            if (delta == 0) return BuildResult.Ok();
            int n = line.trains.Count;
            if (delta > 0)
            {
                float cost = delta * n * WagonCost;
                if (!Pay(cost, LedgerCat.Construction)) return BuildResult.Fail(NoMoney(cost));
                line.wagons = wagons;
                gs.trains.RefreshCapacity(line);
                return BuildResult.Ok(cost);
            }
            float refund = -delta * n * WagonCost * Balance.TrainResale;
            line.wagons = wagons;
            gs.trains.RefreshCapacity(line);
            if (refund > 0f) Econ.Earn(refund, LedgerCat.Sales);
            return BuildResult.Ok(-refund);
        }
        // ------------------------------------------------------------------ undoable entry points

        /// <summary>Construction history for Ctrl+Z (see UndoHistory).</summary>
        public UndoHistory Undo => undo ??= new UndoHistory(gs);
        UndoHistory undo;

        BuildResult Recorded(Func<BuildResult> action, Func<string> label)
        {
            Undo.Begin();
            BuildResult r = default;
            try
            {
                r = action();
            }
            finally
            {
                Undo.End(r.ok, r.ok ? label() : null);
            }
            return r;
        }

        static string LineName(MetroLine l) => l != null ? "the " + l.name : "the line";
        string StationName(int id) => Net.GetStation(id)?.name ?? "the station";

        public BuildResult BuildLine(IList<PlannedStop> stops, bool loop, bool closingDiagonal, out MetroLine line)
        {
            MetroLine made = null;
            var r = Recorded(() => BuildLineImpl(stops, loop, closingDiagonal, out made), () => "building " + LineName(made));
            line = made;
            return r;
        }

        public BuildResult ExtendLine(MetroLine line, bool atEnd, IList<PlannedStop> stops, bool closeLoop, bool closingDiagonal) =>
            Recorded(() => ExtendLineImpl(line, atEnd, stops, closeLoop, closingDiagonal), () => "extending " + LineName(line));

        public BuildResult InsertStation(int a, int b, Vector2 near, out Station station)
        {
            Station made = null;
            var r = Recorded(() => InsertStationImpl(a, b, near, out made), () => "adding " + (made != null ? made.name : "a station"));
            station = made;
            return r;
        }

        public BuildResult DeleteLine(MetroLine line)
        {
            string name = LineName(line);
            return Recorded(() => DeleteLineImpl(line), () => "deleting " + name);
        }

        public BuildResult TruncateLine(MetroLine line, bool atEnd) => Recorded(() => TruncateLineImpl(line, atEnd), () => "shortening " + LineName(line));

        public BuildResult SetLineColor(MetroLine line, int colorIndex)
        {
            string name = LineName(line);
            return Recorded(() => SetLineColorImpl(line, colorIndex), () => "recolouring " + name);
        }

        public BuildResult DemolishStation(int stationId)
        {
            string name = StationName(stationId);
            return Recorded(() => DemolishStationImpl(stationId), () => "demolishing " + name);
        }

        public BuildResult Upgrade(int stationId, StationUpgrade u) => Recorded(() => UpgradeImpl(stationId, u), () => "upgrading " + StationName(stationId));

        public BuildResult BuildDepot(int stationId) => Recorded(() => BuildDepotImpl(stationId), () => "the depot at " + StationName(stationId));

        public BuildResult ExpandDepot(int depotId) => Recorded(() => ExpandDepotImpl(depotId), () => "expanding a depot");

        public BuildResult RemoveDepot(int depotId) => Recorded(() => RemoveDepotImpl(depotId), () => "removing a depot");

        public BuildResult AddTrain(MetroLine line) => Recorded(() => AddTrainImpl(line), () => "a new train on " + LineName(line));

        public BuildResult RemoveTrain(MetroLine line) => Recorded(() => RemoveTrainImpl(line), () => "removing a train from " + LineName(line));

        public BuildResult SetWagons(MetroLine line, int wagons) => Recorded(() => SetWagonsImpl(line, wagons), () => "changing the wagons on " + LineName(line));
    }
}
