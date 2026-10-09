using System.Collections.Generic;
using UnityEngine;

namespace Metro
{
    public enum LedgerCat { Fares, Subsidy, Ads, Grants, Sales, Salaries, Maintenance, Construction, Fines, Interest }

    public sealed class DayLedger
    {
        public const int Count = 10;
        public int day;
        public float[] amounts = new float[Count];
        public float riders;

        public void Add(LedgerCat c, float v) => amounts[(int)c] += v;
        public float Get(LedgerCat c) => amounts[(int)c];

        public float Income
        {
            get
            {
                float s = 0f;
                for (int i = 0; i < Count; i++)
                    if (amounts[i] > 0f) s += amounts[i];
                return s;
            }
        }

        public float Expenses
        {
            get
            {
                float s = 0f;
                for (int i = 0; i < Count; i++)
                    if (amounts[i] < 0f) s -= amounts[i];
                return s;
            }
        }

        public float Net => Income - Expenses;
        /// <summary>What running the network earned: no building costs, resale or one-off grants.</summary>
        public float OperatingNet => Net + Mathf.Max(0f, -Get(LedgerCat.Construction)) - Mathf.Max(0f, Get(LedgerCat.Sales)) - Mathf.Max(0f, Get(LedgerCat.Grants));

        public static bool IsIncome(LedgerCat c) => c <= LedgerCat.Sales;

        public static string Label(LedgerCat c)
        {
            switch (c)
            {
                case LedgerCat.Fares: return "Ticket sales";
                case LedgerCat.Subsidy: return "City subsidy";
                case LedgerCat.Ads: return "Advertising";
                case LedgerCat.Grants: return "Grants & bonuses";
                case LedgerCat.Sales: return "Asset sales";
                case LedgerCat.Salaries: return "Staff salaries";
                case LedgerCat.Maintenance: return "Maintenance";
                case LedgerCat.Construction: return "Construction";
                case LedgerCat.Fines: return "Fines & City Hall";
                case LedgerCat.Interest: return "Loan interest";
            }
            return c.ToString();
        }
    }

    /// <summary>Cash, fares, advertising, running costs, loans and the daily ledger.</summary>
    public sealed class Economy
    {
        readonly GameState gs;
        public double money;
        public float baseFare = Balance.DefaultFare;
        public float peakSurcharge;
        public int adLevel = 1;
        public float loan;
        public int daysInRed;
        public DayLedger today = new DayLedger();
        public readonly List<DayLedger> history = new List<DayLedger>();

        float cachedSalaries, cachedMaintenance;
        int cacheKey = int.MinValue;

        public Economy(GameState gs)
        {
            this.gs = gs;
        }

        public float CurrentFare(bool peak) => baseFare * (peak ? 1f + peakSurcharge : 1f);
        public bool CanAfford(float cost) => money + 0.5 >= cost;

        public bool TrySpend(float amount, LedgerCat cat)
        {
            if (!CanAfford(amount)) return false;
            Charge(amount, cat);
            return true;
        }

        public void Charge(float amount, LedgerCat cat)
        {
            money -= amount;
            today.Add(cat, -amount);
        }

        public void Earn(float amount, LedgerCat cat)
        {
            money += amount;
            today.Add(cat, amount);
        }

        public DayLedger Yesterday => history.Count > 0 ? history[history.Count - 1] : null;

        public void DailyRunningCosts(out float salaries, out float maintenance)
        {
            int key = gs.net.version * 7919 + gs.trains.AliveCount * 31 + WagonTotal();
            if (key == cacheKey)
            {
                salaries = cachedSalaries;
                maintenance = cachedMaintenance;
                return;
            }
            var net = gs.net;
            int trains = 0;
            float wagons = 0f;
            foreach (var l in net.AliveLines())
            {
                trains += l.trains.Count;
                wagons += l.trains.Count * l.wagons;
            }
            salaries = trains * Balance.DriverSalaryPerTrain;
            maintenance = wagons * Balance.TrainMaintenancePerWagon;
            foreach (var s in net.AliveStations())
            {
                if (s.lines.Count == 0) continue;
                salaries += Balance.StationStaff + Balance.StationStaffPerUpgrade * s.UpgradeLevels;
            }
            net.TrackTotals(out float tunnel, out float bridge);
            maintenance += tunnel * Balance.TunnelMaintenancePerKm + bridge * Balance.BridgeMaintenancePerKm;
            foreach (var d in net.AliveDepots()) maintenance += Balance.DepotUpkeep * d.level;
            cachedSalaries = salaries;
            cachedMaintenance = maintenance;
            cacheKey = key;
        }

        int WagonTotal()
        {
            int w = 0;
            foreach (var l in gs.net.AliveLines()) w += l.wagons * 131 + l.trains.Count;
            return w;
        }

        public float DailyRunningTotal
        {
            get
            {
                DailyRunningCosts(out float s, out float m);
                return s + m + loan * Balance.LoanDailyInterest;
            }
        }

        public void MinuteTick()
        {
            DailyRunningCosts(out float s, out float m);
            float perMinute = 1f / Balance.MinutesPerDay;
            if (s > 0f) Charge(s * perMinute, LedgerCat.Salaries);
            if (m > 0f) Charge(m * perMinute, LedgerCat.Maintenance);
            if (loan > 0f) Charge(loan * Balance.LoanDailyInterest * perMinute, LedgerCat.Interest);
        }

        /// <summary>Rough replacement value of the network, which backs the credit line.</summary>
        public float NetworkValue()
        {
            var net = gs.net;
            float v = net.StationCount * Balance.StationCost;
            net.TrackTotals(out float t, out float b);
            v += t * Balance.TunnelPerKm + b * Balance.BridgePerKm;
            v += gs.trains.AliveCount * Balance.TrainCost;
            foreach (var d in net.AliveDepots()) v += Balance.DepotCost * d.level;
            return v;
        }

        public float LoanLimit => Balance.LoanBaseLimit + NetworkValue() * 0.25f;

        public bool Borrow()
        {
            if (loan + Balance.LoanStep > LoanLimit + 1f) return false;
            loan += Balance.LoanStep;
            money += Balance.LoanStep;
            return true;
        }

        public bool Repay()
        {
            if (loan <= 0f) return false;
            float a = Mathf.Min(Balance.LoanStep, loan);
            if (money < a) return false;
            loan -= a;
            money -= a;
            return true;
        }

        public void EndDay()
        {
            today.riders = gs.stats.LastDay != null ? gs.stats.LastDay.riders : 0f;
            history.Add(today);
            if (history.Count > 120) history.RemoveAt(0);
            daysInRed = money < 0 ? daysInRed + 1 : 0;
            today = new DayLedger { day = gs.clock.Day };
        }
    }
}
