using System.Globalization;
using UnityEngine;

namespace Metro
{
    /// <summary>Human-friendly number formatting for the UI and advisor messages.</summary>
    public static class Fmt
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string Money(double v)
        {
            string sign = v < 0 ? "-" : "";
            double a = System.Math.Abs(v);
            if (a >= 1e9) return sign + "$" + (a / 1e9).ToString(a >= 1e10 ? "0.0" : "0.00", Inv) + "B";
            if (a >= 1e6) return sign + "$" + (a / 1e6).ToString(a >= 1e7 ? "0.0" : "0.00", Inv) + "M";
            if (a >= 1e4) return sign + "$" + (a / 1e3).ToString("0", Inv) + "k";
            if (a >= 1e3) return sign + "$" + (a / 1e3).ToString("0.0", Inv) + "k";
            return sign + "$" + a.ToString("0", Inv);
        }

        public static string MoneySigned(double v) => (v >= 0 ? "+" : "") + Money(v);

        public static string Fare(float v) => "$" + v.ToString("0.00", Inv);

        public static string Count(double v)
        {
            double a = System.Math.Abs(v);
            string sign = v < 0 ? "-" : "";
            if (a >= 1e6) return sign + (a / 1e6).ToString(a >= 1e7 ? "0.0" : "0.00", Inv) + "M";
            if (a >= 1e4) return sign + (a / 1e3).ToString("0", Inv) + "k";
            if (a >= 1e3) return sign + (a / 1e3).ToString("0.0", Inv) + "k";
            return sign + a.ToString("0", Inv);
        }

        public static string Int(double v) => System.Math.Round(v).ToString("#,0", Inv);

        public static string Pct(float v01) => Mathf.RoundToInt(v01 * 100f) + "%";

        public static string Km(float v) => v.ToString(v < 10f ? "0.0" : "0", Inv) + " km";

        public static string Minutes(float v)
        {
            if (float.IsInfinity(v) || float.IsNaN(v)) return "—";
            if (v < 1f) return "<1 min";
            if (v < 90f) return v.ToString("0", Inv) + " min";
            return (v / 60f).ToString("0.0", Inv) + " h";
        }

        public static string F1(float v) => v.ToString("0.0", Inv);
        public static string F0(float v) => v.ToString("0", Inv);
    }
}
