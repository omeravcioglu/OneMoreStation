using System;
using System.Collections.Generic;
using UnityEngine;

namespace Metro
{
    public sealed class DayStats
    {
        public int day;
        public float riders, delivered, abandoned, turnedAway, denied, transfers;
        public float income, expenses, net;
        public float satisfaction, population, coverage;
        public int stations, lines, trains;
        public float[] hourly = new float[24];
    }

    /// <summary>Rolling statistics: per-minute window, hourly buckets, daily history and safety fines.</summary>
    public sealed class StatsTracker
    {
        readonly GameState gs;

        public readonly float[] hourRiders = new float[24];
        public readonly float[] hourWaiting = new float[24];
        public readonly float[] yesterdayHourRiders = new float[24];
        public float ridersToday, deliveredToday, abandonedToday, turnedAwayToday, deniedToday, transfersToday, boardingsToday;
        public float totalWaiting, totalOnboard;
        public float recordRiders;
        public readonly List<DayStats> days = new List<DayStats>();

        const int Window = 60;
        readonly float[] rEntries = new float[Window], rTurned = new float[Window], rAband = new float[Window];
        readonly float[] rDenied = new float[Window], rBoard = new float[Window];
        int rIdx;
        public float HourEntries, HourTurnedAway, HourAbandoned, HourDenied, HourBoardings;

        public StatsTracker(GameState gs)
        {
            this.gs = gs;
        }

        public DayStats LastDay => days.Count > 0 ? days[days.Count - 1] : null;

        /// <summary>Riders per day extrapolated from the last hour (used in the HUD).</summary>
        public float RidersPerHour => HourEntries;

        public void MinuteTick()
        {
            var p = gs.pax;
            PushWindow(p.mEntries, p.mTurnedAway, p.mAbandoned, p.mDenied, p.mBoardings);
            ridersToday += p.mEntries;
            deliveredToday += p.mDelivered;
            abandonedToday += p.mAbandoned;
            turnedAwayToday += p.mTurnedAway;
            deniedToday += p.mDenied;
            transfersToday += p.mTransfers;
            boardingsToday += p.mBoardings;
            int m = (Mathf.RoundToInt(gs.clock.MinuteOfDay) - 1 + Balance.MinutesPerDay) % Balance.MinutesPerDay;
            int h = m / 60;
            hourRiders[h] += p.mEntries;
            p.mEntries = p.mTurnedAway = p.mAbandoned = p.mDenied = p.mBoardings = p.mDelivered = p.mTransfers = p.mFares = 0f;

            totalWaiting = p.TotalWaiting;
            totalOnboard = gs.trains.TotalOnboard;
            hourWaiting[h] = Mathf.Max(hourWaiting[h], totalWaiting);

            CheckSafety();
        }

        void PushWindow(float entries, float turned, float aband, float denied, float board)
        {
            HourEntries += entries - rEntries[rIdx];
            HourTurnedAway += turned - rTurned[rIdx];
            HourAbandoned += aband - rAband[rIdx];
            HourDenied += denied - rDenied[rIdx];
            HourBoardings += board - rBoard[rIdx];
            rEntries[rIdx] = entries;
            rTurned[rIdx] = turned;
            rAband[rIdx] = aband;
            rDenied[rIdx] = denied;
            rBoard[rIdx] = board;
            rIdx = (rIdx + 1) % Window;
            HourEntries = Mathf.Max(0f, HourEntries);
            HourTurnedAway = Mathf.Max(0f, HourTurnedAway);
            HourAbandoned = Mathf.Max(0f, HourAbandoned);
            HourDenied = Mathf.Max(0f, HourDenied);
            HourBoardings = Mathf.Max(0f, HourBoardings);
        }

        void CheckSafety()
        {
            double now = gs.clock.totalMinutes;
            foreach (var s in gs.net.AliveStations())
            {
                float crowd = s.Crowding;
                if (crowd > s.peakCrowdToday) s.peakCrowdToday = crowd;
                if (crowd >= 1f) s.criticalMinutes++;
                else s.criticalMinutes = Mathf.Max(0, s.criticalMinutes - 2);
                if (s.criticalMinutes >= Balance.OvercrowdingFineMinutes && now - s.lastFineMinute > 360.0)
                {
                    float fine = Balance.OvercrowdingFine * gs.FineMultiplier;
                    gs.econ.Charge(fine, LedgerCat.Fines);
                    s.lastFineMinute = now;
                    gs.advisor.Post(MsgCategory.Safety, MsgSeverity.Critical,
                        $"Safety inspectors fined us {Fmt.Money(fine)} for dangerous overcrowding at {s.name}.", station: s.id);
                }
            }
        }

        public void HourTick() { }

        public void EndDay(int day)
        {
            var y = gs.econ.today;
            var ds = new DayStats
            {
                day = day,
                riders = ridersToday,
                delivered = deliveredToday,
                abandoned = abandonedToday,
                turnedAway = turnedAwayToday,
                denied = deniedToday,
                transfers = transfersToday,
                income = y.Income,
                expenses = y.Expenses,
                net = y.Net,
                satisfaction = gs.satisfaction.score,
                population = gs.city.revealedPopulation,
                coverage = gs.city.CoverageRatio,
                stations = gs.net.StationCount,
                lines = gs.net.LineCount,
                trains = gs.trains.AliveCount
            };
            Array.Copy(hourRiders, ds.hourly, 24);
            days.Add(ds);
            if (days.Count > 120) days.RemoveAt(0);
            Array.Copy(hourRiders, yesterdayHourRiders, 24);
            Array.Clear(hourRiders, 0, 24);
            Array.Clear(hourWaiting, 0, 24);
            bool record = ridersToday > recordRiders && ridersToday > 1000f && days.Count > 1;
            recordRiders = Mathf.Max(recordRiders, ridersToday);
            if (record)
                gs.advisor.Post(MsgCategory.Operations, MsgSeverity.Good, $"New ridership record: {Fmt.Count(ridersToday)} trips yesterday!");

            foreach (var s in gs.net.AliveStations())
            {
                s.entriesYesterday = s.entriesToday;
                s.boardingsYesterday = s.boardingsToday;
                s.entriesToday = s.boardingsToday = s.transfersToday = s.turnedAwayToday = s.abandonedToday = 0f;
                s.peakCrowdYesterday = s.peakCrowdToday;
                s.peakCrowdToday = 0f;
            }
            float dayNow = (float)(gs.clock.totalMinutes / Balance.MinutesPerDay);
            foreach (var l in gs.net.AliveLines())
            {
                l.boardingsYesterday = l.boardingsToday;
                l.deniedYesterday = l.deniedToday;
                l.boardingsToday = l.deniedToday = 0f;
                l.waitSum = l.waitWeight = l.loadSum = l.loadWeight = 0f;
                l.peakLoadToday = 0f;
                if (l.trains.Count == 0 && dayNow - l.builtDay > 0.75f)
                {
                    float fine = Balance.NoServiceFinePerDay * gs.FineMultiplier;
                    gs.econ.Charge(fine, LedgerCat.Fines);
                    gs.advisor.Post(MsgCategory.City, MsgSeverity.Warning,
                        $"The city fined us {Fmt.Money(fine)}: the {l.name} was built but has no trains running.", line: l.id);
                }
            }
            ridersToday = deliveredToday = abandonedToday = turnedAwayToday = deniedToday = transfersToday = boardingsToday = 0f;
        }
    }
}
