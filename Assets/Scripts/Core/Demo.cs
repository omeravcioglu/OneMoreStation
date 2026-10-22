namespace Metro
{
    /// <summary>
    /// The free Steam demo. It covers the first two city sizes (Small town and Growing town) with the
    /// guided start. When the town is ready to become a Market town, the next dawn shows "End of the demo"
    /// instead of growing. A safety cap ends the demo in the evening after about 20 minutes of unpaused play,
    /// so a slow first session still sees the ending.
    /// Demo builds define METRO_DEMO (MetroBuild.BuildDemo). Any build started with -demo behaves the same,
    /// which is how the autotests check it.
    /// </summary>
    public static class Demo
    {
#if METRO_DEMO
        public static readonly bool On = true;
#else
        public static readonly bool On = Platform.PlatformServices.HasArg("-demo");
#endif
        /// <summary>The last city size in the demo (Growing town). Reaching the next one ends the demo.</summary>
        public const int LastTier = 1;
        /// <summary>Unpaused real seconds after which the demo ends at the next evening.</summary>
        public static float PlayCapSeconds = 20f * 60f;
        /// <summary>A capped demo ends once the evening rush is over (19:30), or at night.</summary>
        public const int EveningMinute = 19 * 60 + 30;
        /// <summary>The town every guided demo starts in: a good-looking map with a river through the centre.</summary>
        public const int StartSeed = 2024;
        public const string StartCity = "Port Avalon";
        public const string StoreUrl = "https://store.steampowered.com/app/5383480/One_More_Station/";
    }
}
