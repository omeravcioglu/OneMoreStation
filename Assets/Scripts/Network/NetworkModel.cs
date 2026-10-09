using System.Collections.Generic;
using UnityEngine;

namespace Metro
{
    public static class NetStyle
    {
        public const float LaneSpacing = 2.35f;   // distance between parallel lines, in line half-widths
        public const float StationRadius = 1.6f;  // station marker radius (to the middle of its ink ring), in line half-widths
        public const float LineHalfWidth = 0.075f; // world km before pixel clamping
        public const float LineMinPx = 2.2f;      // 1080p reference pixels (see StrokeWidth.PxScale)
        public const float LineMaxPx = 7.5f;
        public const float CasingUnits = 1.53f;   // the ink casing drawn around every line, in half-widths

        public static StrokeWidth LineWidth => new StrokeWidth(LineHalfWidth, LineMinPx, LineMaxPx);
    }

    public static class LinePalette
    {
        public static readonly string[] Names =
        {
            "Red", "Blue", "Green", "Orange", "Purple", "Yellow", "Pink", "Brown",
            "Teal", "Navy", "Lime", "Sky", "Ruby", "Silver", "Olive", "Coral"
        };

        // Candy colours, each drawn on an ink casing (Doodle Metro).
        public static readonly Color32[] Colors =
        {
            Balance.Hex("#FF5146"), Balance.Hex("#3D86F5"), Balance.Hex("#35B864"), Balance.Hex("#FF8F1F"),
            Balance.Hex("#9A5BF0"), Balance.Hex("#FFC929"), Balance.Hex("#FF62AE"), Balance.Hex("#A8693F"),
            Balance.Hex("#17B8A6"), Balance.Hex("#3949B8"), Balance.Hex("#A3DA3A"), Balance.Hex("#39C5F2"),
            Balance.Hex("#DB2D63"), Balance.Hex("#A3ABB8"), Balance.Hex("#8E9933"), Balance.Hex("#FF7D5E")
        };

        public static int Count => Colors.Length;
    }

    public sealed class Station
    {
        public int id;
        public string name;
        public Vector2 pos;
        public int district;
        public bool alive = true;
        public int platformLevel = 1;
        public int capacityLevel = 1;
        public bool elevators;
        public bool concourse;
        public float builtDay;

        // Derived by MetroNetwork.Rebuild.
        public readonly List<int> lines = new List<int>();
        public int depotId = -1;
        public float laneExtent;

        // Live statistics (not saved).
        public float waiting;
        public float crowdDisplay;
        public float entriesToday, entriesYesterday;
        public float boardingsToday, boardingsYesterday;
        public float transfersToday, turnedAwayToday, abandonedToday;
        public float peakCrowdToday, peakCrowdYesterday;
        public int criticalMinutes;
        public double lastFineMinute = -1e9;

        public int Capacity => Mathf.RoundToInt(Balance.StationCapacity(capacityLevel) * (concourse ? 1.3f : 1f));
        public int MaxWagons => Balance.MaxWagons(platformLevel);
        public bool IsInterchange => lines.Count > 1;
        public float Crowding => Capacity > 0 ? waiting / Capacity : 0f;
        public int UpgradeLevels => (platformLevel - 1) + (capacityLevel - 1) + (elevators ? 1 : 0) + (concourse ? 1 : 0);
    }

    public sealed class MetroLine
    {
        public int id;
        public string name;
        public int colorIndex;
        public bool alive = true;
        public bool loop;
        public int wagons = Balance.DefaultWagons;
        public float builtDay;
        public readonly List<int> stops = new List<int>();

        // Derived.
        public LinePath path;
        public readonly List<int> trains = new List<int>();
        public float[] segLoadFwd = new float[0];
        public float[] segLoadBwd = new float[0];
        public double[] lastArrival = new double[0];
        public float cycleTime;
        public float headway = float.PositiveInfinity;
        public float headwayFwd = float.PositiveInfinity;
        public float headwayBwd = float.PositiveInfinity;

        // Statistics.
        public float boardingsToday, boardingsYesterday, deniedToday, deniedYesterday;
        public float waitSum, waitWeight, loadSum, loadWeight;
        public float avgWaitRecent = 3f, avgLoadRecent;
        public float peakLoadToday;

        public Color32 Color => LinePalette.Colors[colorIndex % LinePalette.Count];
        public string ColorName => LinePalette.Names[colorIndex % LinePalette.Count];
        public string Letter => ColorName.Substring(0, 1);
        public int SegmentCount => stops.Count < 2 ? 0 : (loop ? stops.Count : stops.Count - 1);
        public int Capacity => wagons * Balance.WagonCapacity;
        public float AvgWaitToday => waitWeight > 1f ? waitSum / waitWeight : avgWaitRecent;
        public float AvgLoadToday => loadWeight > 1f ? loadSum / loadWeight : avgLoadRecent;

        public float SegLoad(int seg)
        {
            if (seg < 0 || seg >= segLoadFwd.Length) return 0f;
            return Mathf.Max(segLoadFwd[seg], segLoadBwd[seg]);
        }

        public float MaxSegLoad
        {
            get
            {
                float m = 0f;
                for (int i = 0; i < segLoadFwd.Length; i++) m = Mathf.Max(m, SegLoad(i));
                return m;
            }
        }

        public void EnsureArrays()
        {
            int segs = Mathf.Max(SegmentCount, 0);
            if (segLoadFwd.Length != segs)
            {
                var f = new float[segs];
                var b = new float[segs];
                for (int i = 0; i < Mathf.Min(segs, segLoadFwd.Length); i++)
                {
                    f[i] = segLoadFwd[i];
                    b[i] = segLoadBwd[i];
                }
                segLoadFwd = f;
                segLoadBwd = b;
            }
            if (lastArrival.Length != stops.Count * 2)
            {
                lastArrival = new double[stops.Count * 2];
                for (int i = 0; i < lastArrival.Length; i++) lastArrival[i] = -1;
            }
        }

        public int IndexOfStop(int stationId) => stops.IndexOf(stationId);
    }

    public sealed class TrackEdge
    {
        public readonly int a, b;
        public bool diagonalFirst;
        public bool geometryValid;
        public List<Vector2> path = new List<Vector2>();
        public readonly List<float> cum = new List<float>();
        public float length, waterLength;
        public readonly List<Vector2> waterRanges = new List<Vector2>();
        public readonly List<int> lines = new List<int>();

        public TrackEdge(int a, int b)
        {
            this.a = a;
            this.b = b;
        }

        public float LaneUnits(int lineId)
        {
            int i = lines.IndexOf(lineId);
            if (i < 0) return 0f;
            return (i - (lines.Count - 1) * 0.5f) * NetStyle.LaneSpacing;
        }

        public float LandLength => Mathf.Max(0f, length - waterLength);
    }

    public sealed class Depot
    {
        public int id;
        public int stationId;
        public int level = 1;
        public bool alive = true;
        public float builtDay;
        public Vector2 iconDir = Vector2.up;

        public int Capacity => level * Balance.DepotCapacityPerLevel;
    }
}
