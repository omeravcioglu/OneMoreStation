using UnityEngine;

namespace Metro
{
    public enum DayPeriod { Night, EarlyMorning, MorningRush, Midday, EveningRush, Evening }

    /// <summary>Game time. Minute 0 is Monday 00:00 of day 1.</summary>
    public sealed class GameClock
    {
        static readonly string[] DayNames = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
        static readonly string[] DayNamesLong = { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };

        public double totalMinutes;

        public int Day => (int)(totalMinutes / Balance.MinutesPerDay);
        public int DayNumber => Day + 1;
        public float MinuteOfDay => (float)(totalMinutes - Day * (double)Balance.MinutesPerDay);
        public float HourOfDay => MinuteOfDay / 60f;
        public int DayOfWeek => Day % 7;
        public bool IsWeekend => DayOfWeek >= 5;
        public string DayName => DayNames[DayOfWeek];
        public string DayNameLong => DayNamesLong[DayOfWeek];
        public float DayFraction => MinuteOfDay / Balance.MinutesPerDay;

        public string TimeString
        {
            get
            {
                int m = Mathf.FloorToInt(MinuteOfDay);
                return $"{m / 60:00}:{m % 60:00}";
            }
        }

        public bool IsNight
        {
            get
            {
                float m = MinuteOfDay;
                return m >= Balance.NightStart && m < Balance.NightEnd;
            }
        }

        public bool IsPeak => !IsWeekend && IsPeakMinute(MinuteOfDay);

        public static bool IsPeakMinute(float minute)
        {
            float h = minute / 60f;
            return (h >= 7f && h < 9.5f) || (h >= 16.5f && h < 19f);
        }

        public DayPeriod Period
        {
            get
            {
                float h = HourOfDay;
                if (h < 5f || h >= 23f) return DayPeriod.Night;
                if (h < 7f) return DayPeriod.EarlyMorning;
                if (h < 9.5f) return IsWeekend ? DayPeriod.Midday : DayPeriod.MorningRush;
                if (h < 16.5f) return DayPeriod.Midday;
                if (h < 19f) return IsWeekend ? DayPeriod.Midday : DayPeriod.EveningRush;
                return DayPeriod.Evening;
            }
        }

        public string PeriodAdjective
        {
            get
            {
                switch (Period)
                {
                    case DayPeriod.EarlyMorning: return "Early";
                    case DayPeriod.MorningRush: return "Morning";
                    case DayPeriod.Midday: return "Midday";
                    case DayPeriod.EveningRush: return "Evening";
                    case DayPeriod.Evening: return "Late";
                    default: return "Night";
                }
            }
        }

        public static string FormatMinute(float minuteOfDay)
        {
            int m = Mathf.FloorToInt(minuteOfDay) % Balance.MinutesPerDay;
            return $"{m / 60:00}:{m % 60:00}";
        }

        /// <summary>"Saturday (day 6)" for a day index.</summary>
        public static string DayNameOf(int dayIndex) => $"{DayNamesLong[((dayIndex % 7) + 7) % 7]} (day {dayIndex + 1})";

    }
}
