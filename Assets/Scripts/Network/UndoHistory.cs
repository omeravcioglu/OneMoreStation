using System;
using System.Collections.Generic;
using UnityEngine;

namespace Metro
{
    /// <summary>
    /// Undo for construction (Ctrl+Z). Before every construction action the network is snapshotted;
    /// undoing restores that snapshot in place and refunds exactly what the action cost (or takes back
    /// what it paid out). Actions can be undone in reverse order on the same day, within a few game
    /// hours, so undo stays a "fix my misclick" tool rather than a time machine.
    /// </summary>
    public sealed class UndoHistory
    {
        sealed class StationState
        {
            public bool alive, elevators, concourse;
            public int platform, capacity;
        }

        sealed class LineState
        {
            public bool alive, loop;
            public int color, wagons;
            public string name;
            public int[] stops;
            public int[] trainStops, trainDirs;
        }

        sealed class DepotState
        {
            public bool alive;
            public int level;
        }

        sealed class Entry
        {
            public string label;
            public double time;
            public int day;
            public StationState[] stations;
            public LineState[] lines;
            public DepotState[] depots;
            public Dictionary<long, bool> bends;
            public double moneyBefore, moneyAfter;
            public float[] ledgerBefore, ledgerAfter;
        }

        public const int MaxEntries = 25;
        public const double MaxAgeMinutes = 180.0;

        readonly GameState gs;
        readonly List<Entry> stack = new List<Entry>();
        Entry pending;
        int depth;
        /// <summary>Off for scripted/bot players that never undo.</summary>
        public bool enabled = true;

        /// <summary>Something undoable was just built or changed (any city).</summary>
        public static event Action Recorded;

        public UndoHistory(GameState gs)
        {
            this.gs = gs;
        }

        MetroNetwork Net => gs.net;

        // ------------------------------------------------------------------ recording

        public void Begin()
        {
            if (!enabled) return;
            if (depth++ > 0) return;
            pending = Capture();
        }

        /// <summary>Ends an action; successful ones become undoable with the given description.</summary>
        public void End(bool ok, string label)
        {
            if (!enabled) return;
            if (--depth > 0) return;
            depth = 0;
            if (ok && pending != null)
            {
                pending.label = label ?? "the last change";
                pending.moneyAfter = gs.econ.money;
                pending.ledgerAfter = (float[])gs.econ.today.amounts.Clone();
                stack.Add(pending);
                if (stack.Count > MaxEntries) stack.RemoveAt(0);
                pending = null;
                Recorded?.Invoke();
            }
            pending = null;
        }

        Entry Capture()
        {
            var net = Net;
            var e = new Entry
            {
                time = gs.clock.totalMinutes,
                day = gs.clock.Day,
                stations = new StationState[net.stations.Count],
                lines = new LineState[net.lines.Count],
                depots = new DepotState[net.depots.Count],
                bends = new Dictionary<long, bool>(net.bendPrefs),
                moneyBefore = gs.econ.money,
                ledgerBefore = (float[])gs.econ.today.amounts.Clone()
            };
            for (int i = 0; i < net.stations.Count; i++)
            {
                var s = net.stations[i];
                e.stations[i] = new StationState
                {
                    alive = s.alive, elevators = s.elevators, concourse = s.concourse, platform = s.platformLevel, capacity = s.capacityLevel
                };
            }
            for (int i = 0; i < net.lines.Count; i++)
            {
                var l = net.lines[i];
                var ls = new LineState
                {
                    alive = l.alive, loop = l.loop, color = l.colorIndex, wagons = l.wagons, name = l.name, stops = l.stops.ToArray()
                };
                var stops = new List<int>();
                var dirs = new List<int>();
                foreach (int id in l.trains)
                {
                    var t = gs.trains.Get(id);
                    if (t == null) continue;
                    stops.Add(t.stopIndex);
                    dirs.Add(t.dir);
                }
                ls.trainStops = stops.ToArray();
                ls.trainDirs = dirs.ToArray();
                e.lines[i] = ls;
            }
            for (int i = 0; i < net.depots.Count; i++)
                e.depots[i] = new DepotState { alive = net.depots[i].alive, level = net.depots[i].level };
            return e;
        }

        // ------------------------------------------------------------------ undoing

        Entry Last => stack.Count > 0 ? stack[stack.Count - 1] : null;

        bool Fresh(Entry e) => e != null && e.day == gs.clock.Day && gs.clock.totalMinutes - e.time <= MaxAgeMinutes;

        public bool CanUndo => Fresh(Last);

        /// <summary>What Ctrl+Z would undo right now, or null.</summary>
        public string NextLabel => CanUndo ? Last.label : null;

        /// <summary>Why nothing can be undone (for the toast).</summary>
        public string WhyNot => stack.Count == 0 ? "Nothing to undo." : "Too late to undo: only changes from the last few hours of today can be undone.";

        public bool Undo(out string message)
        {
            var e = Last;
            if (!Fresh(e))
            {
                message = WhyNot;
                if (e != null) stack.Clear();
                return false;
            }
            stack.RemoveAt(stack.Count - 1);
            Restore(e);
            // Refund exactly what the action cost (or take back what it paid out).
            double delta = e.moneyAfter - e.moneyBefore;
            gs.econ.money -= delta;
            var today = gs.econ.today.amounts;
            for (int i = 0; i < today.Length && i < e.ledgerBefore.Length && i < e.ledgerAfter.Length; i++)
                today[i] -= e.ledgerAfter[i] - e.ledgerBefore[i];
            string money = delta < -1.0 ? $" ({Fmt.Money(-delta)} refunded)" : delta > 1.0 ? $" ({Fmt.Money(delta)} paid back)" : "";
            message = $"Undone: {e.label}{money}.";
            return true;
        }

        void Restore(Entry e)
        {
            var net = Net;
            // Stations: built after the snapshot disappear; the rest get their state back.
            for (int i = 0; i < net.stations.Count; i++)
            {
                var s = net.stations[i];
                if (i >= e.stations.Length)
                {
                    if (s.alive) gs.pax.ClearStation(s.id);
                    s.alive = false;
                    continue;
                }
                var ss = e.stations[i];
                if (s.alive && !ss.alive) gs.pax.ClearStation(s.id);
                s.alive = ss.alive;
                s.platformLevel = ss.platform;
                s.capacityLevel = ss.capacity;
                s.elevators = ss.elevators;
                s.concourse = ss.concourse;
            }
            for (int i = 0; i < net.depots.Count; i++)
            {
                var d = net.depots[i];
                if (i >= e.depots.Length)
                {
                    d.alive = false;
                    continue;
                }
                d.alive = e.depots[i].alive;
                d.level = e.depots[i].level;
            }
            net.bendPrefs.Clear();
            foreach (var kv in e.bends) net.bendPrefs[kv.Key] = kv.Value;

            // Lines: remember which ones change so only their trains are reset.
            var changed = new List<MetroLine>();
            for (int i = 0; i < net.lines.Count; i++)
            {
                var l = net.lines[i];
                if (i >= e.lines.Length)
                {
                    if (l.alive || l.trains.Count > 0) changed.Add(l);
                    l.alive = false;
                    continue;
                }
                var ls = e.lines[i];
                bool same = l.alive == ls.alive && l.loop == ls.loop && l.wagons == ls.wagons && l.trains.Count == ls.trainStops.Length && SameStops(l.stops, ls.stops);
                if (!same) changed.Add(l);
                l.alive = ls.alive;
                l.loop = ls.loop;
                l.colorIndex = ls.color;
                l.name = ls.name;
                l.wagons = ls.wagons;
                l.stops.Clear();
                l.stops.AddRange(ls.stops);
            }
            net.Rebuild();
            foreach (var l in changed)
            {
                gs.trains.RemoveAll(l);
                if (!l.alive || l.id >= e.lines.Length) continue;
                var ls = e.lines[l.id];
                for (int k = 0; k < ls.trainStops.Length; k++) gs.trains.RestoreTrain(l, ls.trainStops[k], ls.trainDirs[k]);
                gs.trains.RefreshCapacity(l);
            }
            net.version++;
            gs.MarkNetworkChanged();
            gs.trains.RefreshAllPoses();
        }

        static bool SameStops(List<int> a, int[] b)
        {
            if (a.Count != b.Length) return false;
            for (int i = 0; i < b.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }

        public void Clear()
        {
            stack.Clear();
            pending = null;
            depth = 0;
        }
    }
}
