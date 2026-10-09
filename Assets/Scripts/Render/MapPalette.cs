using UnityEngine;

namespace Metro
{
    /// <summary>The Doodle Metro storybook map: sand land, candy water and parks, ink outlines.</summary>
    public static class MapPalette
    {
        public static readonly Color32 Background = Balance.Hex("#CFE3F2");   // sky beyond the region
        public static readonly Color32 Land = Balance.Hex("#F3E4C0");
        public static readonly Color32 BuiltUp = Balance.Hex("#EAD7AE");
        public static readonly Color32 Park = Balance.Hex("#A9DE88");
        public static readonly Color32 ParkEdge = Balance.Hex("#6FAE4F");
        public static readonly Color32 Forest = Balance.Hex("#93CC72");
        public static readonly Color32 TreeLight = Balance.Hex("#9FDB7C");
        public static readonly Color32 TreeDark = Balance.Hex("#7FCF5C");
        public static readonly Color32 Water = Balance.Hex("#6DCBF5");
        public static readonly Color32 Shore = Balance.Hex("#3F8FC0");
        public static readonly Color32 WaterHighlight = Balance.Hex("#C4EEFF");
        public static readonly Color32 RiverBank = Balance.Hex("#A9DE88");
        public static readonly Color32 Beach = Balance.Hex("#FFE6A8");
        public static readonly Color32 Border = Balance.Hex("#D3C9B8");
        public static readonly Color32 Fog = Balance.Hex("#FFFFFF");
        public static readonly Color32 FogHatch = Balance.Hex("#E6F0FA");
        public static readonly Color32 Uncharted = Balance.Hex("#FBF6EA");
        public static readonly Color32 UnchartedHatch = Balance.Hex("#B9A57C");
        public static readonly Color32 CityLimits = Balance.Hex("#2B1E3F");
        public static readonly Color32 Ink = Balance.Hex("#2B1E3F");
        public static readonly Color32 InkSoft = Balance.Hex("#7A6E8F");
        public static readonly Color32 White = new Color32(255, 255, 255, 255);
        public static readonly Color32 CrowdGreen = Balance.Hex("#7EE0A8");
        public static readonly Color32 CrowdYellow = Balance.Hex("#FFD84D");
        public static readonly Color32 CrowdOrange = Balance.Hex("#FF9F45");
        public static readonly Color32 CrowdRed = Balance.Hex("#FF5A4E");
        public static readonly Color32 Sweat = Balance.Hex("#4FC3F7");
        public static readonly Color32 Accent = Balance.Hex("#3D86F5");
        public static readonly Color32 Highlighter = Balance.Hex("#FFE45C");
        public static readonly Color32 Invalid = Balance.Hex("#FF5A4E");
        public static readonly Color32 Catchment = Balance.Hex("#3D86F5");
        public static readonly Color32 CatchmentFill = Balance.Hex("#4FC3F7");
        public static readonly Color32 BridgeCasing = Balance.Hex("#E9C48A");
        public static readonly Color32 BridgeEdge = Balance.Hex("#2B1E3F");
        public static readonly Color32 BridgePlank = Balance.Hex("#C69356");
        public static readonly Color32 Depot = Balance.Hex("#2B1E3F");
        public static readonly Color32 DepotRoof = Balance.Hex("#FF9A3D");
        public static readonly Color32 DepotDoor = Balance.Hex("#FFD23F");
        public static readonly Color32 Pencil = Balance.Hex("#7A6E8F");
        public static readonly Color32 Cream = Balance.Hex("#FFF6E3");
        public static readonly Color32 Night = Balance.Hex("#3C3A78");
        public static readonly Color32 Dawn = Balance.Hex("#FF9F7A");

        /// <summary>Settings → Game → colour-blind friendly crowding: a blue → yellow → orange → vermilion ramp (Okabe-Ito).</summary>
        public static bool ColorBlindSafe;

        static readonly Color32 CbBlue = new Color32(86, 180, 233, 255);
        static readonly Color32 CbYellow = new Color32(240, 228, 66, 255);
        static readonly Color32 CbOrange = new Color32(230, 159, 0, 255);
        static readonly Color32 CbVermilion = new Color32(213, 94, 0, 255);

        public static Color32 CrowdColor(float c)
        {
            if (ColorBlindSafe)
            {
                if (c <= 0.4f) return CbBlue;
                if (c <= 0.7f) return Lerp(CbBlue, CbYellow, (c - 0.4f) / 0.3f);
                if (c <= 0.9f) return Lerp(CbYellow, CbOrange, (c - 0.7f) / 0.2f);
                if (c <= 1.0f) return Lerp(CbOrange, CbVermilion, (c - 0.9f) / 0.1f);
                return CbVermilion;
            }
            // Moods, not a gradient: chill, busy, squished, meltdown.
            if (c <= 0.4f) return CrowdGreen;
            if (c <= 0.7f) return CrowdYellow;
            if (c < 0.9f) return CrowdOrange;
            return CrowdRed;
        }

        public static Color32 Lerp(Color32 a, Color32 b, float t) => Color32.Lerp(a, b, Mathf.Clamp01(t));

        public static Color32 Darken(Color32 c, float k)
        {
            float f = 1f - Mathf.Clamp01(k);
            return new Color32((byte)(c.r * f), (byte)(c.g * f), (byte)(c.b * f), c.a);
        }

        public static Color32 WithAlpha(Color32 c, float a) => new Color32(c.r, c.g, c.b, (byte)Mathf.Clamp(a * 255f, 0f, 255f));

        public static Color ToColor(Color32 c) => c;
    }
}
