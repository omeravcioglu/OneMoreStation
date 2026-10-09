using System;
using System.Collections.Generic;
using Metro.UI;
using UnityEngine;

namespace Metro.Platform
{
    public sealed class AchievementDef
    {
        public string id, title, description;
        public IconKind icon;
        public Color32 color;
        public bool hidden;
        /// <summary>For lifetime goals: target value and how far the player is (shown as progress).</summary>
        public float progressMax;
        public Func<PlayerProfile, float> progress;
    }

    /// <summary>
    /// Achievements: tracked locally in the player profile (so they work offline and show in-game)
    /// and mirrored to Steam when it's available. API names match Docs/STEAM.md.
    /// </summary>
    public static class Achievements
    {
        static readonly Color32 Gold = new Color32(222, 162, 34, 255);
        static readonly Color32 Blue = new Color32(47, 111, 237, 255);
        static readonly Color32 Green = new Color32(36, 150, 90, 255);
        static readonly Color32 Purple = new Color32(124, 72, 168, 255);
        static readonly Color32 Red = new Color32(204, 64, 68, 255);
        static readonly Color32 Teal = new Color32(20, 140, 126, 255);

        static AchievementDef A(string id, string title, string desc, IconKind icon, Color32 c, bool hidden = false) =>
            new AchievementDef { id = id, title = title, description = desc, icon = icon, color = c, hidden = hidden };

        public static readonly AchievementDef[] All =
        {
            A("ACH_FIRST_LINE", "Opening Day", "Open your first metro line.", IconKind.Line, Blue),
            A("ACH_GUIDED_START", "Graduate", "Finish the guided start.", IconKind.Check, Green),
            A("ACH_RIDERS_10K", "Ten Thousand a Day", "Carry 10,000 trips in a single day.", IconKind.People, Blue),
            A("ACH_RIDERS_100K", "Rush Hour Hero", "Carry 100,000 trips in a single day.", IconKind.People, Gold),
            A("ACH_RIDERS_500K", "Half a Million", "Carry 500,000 trips in a single day.", IconKind.People, Red),
            A("ACH_GROWING_TOWN", "Growing Up", "Grow your town into a Growing town.", IconKind.Star, Green),
            A("ACH_MARKET_TOWN", "Open for Business", "Grow into a Market town.", IconKind.Star, Green),
            A("ACH_CITY", "City Lights", "Grow into a City.", IconKind.Star, Blue),
            A("ACH_BIG_CITY", "Big City Life", "Grow into a Big city.", IconKind.Star, Purple),
            A("ACH_METROPOLIS", "Metropolis", "Grow your city all the way to a Metropolis.", IconKind.Star, Gold),
            A("ACH_METROPOLIS_HARD", "Iron Rails", "Reach Metropolis on Demanding difficulty.", IconKind.Trophy, Red),
            A("ACH_BOOMTOWN", "Boomtown", "Become a City within your first five days.", IconKind.Calendar, Gold),
            A("ACH_INTERCHANGE", "Change Here", "Build a station where two lines meet.", IconKind.Insert, Teal),
            A("ACH_LOOP", "Going in Circles", "Build a circle line.", IconKind.Coverage, Teal),
            A("ACH_BRIDGE", "Over the River", "Lay track across water.", IconKind.Line, Teal),
            A("ACH_LINES_8", "Spaghetti Junction", "Run trains on eight lines at once.", IconKind.Line, Purple),
            A("ACH_STATIONS_100", "Century", "Have 100 stations in one network.", IconKind.Station, Purple),
            A("ACH_LONG_LINE", "End of the Line", "Build a line longer than 30 km.", IconKind.Train, Blue),
            A("ACH_FULLY_LOADED", "Fully Loaded", "Give one station every upgrade.", IconKind.Station, Gold),
            A("ACH_HAPPY", "Happy Riders", "End a day with 90% rider satisfaction.", IconKind.Smile, Green),
            A("ACH_PROFIT", "In the Black", "Make $1M operating profit in one day.", IconKind.Finance, Gold),
            A("ACH_FIRST_REQUEST", "At Your Service", "Complete a City Hall request.", IconKind.Hall, Purple),
            new AchievementDef
            {
                id = "ACH_REQUESTS_25", title = "Public Servant", description = "Complete 25 City Hall requests (across all your cities).",
                icon = IconKind.Hall, color = Gold, progressMax = 25, progress = p => p.requestsDone
            },
            A("ACH_APPROVAL_90", "Friends at City Hall", "Reach 90% City Hall approval.", IconKind.Hall, Green),
            A("ACH_EVENT", "Matchday", "Get the crowd to a big event and home again.", IconKind.Calendar, Red),
            A("ACH_ELECTION", "Kingmaker", "Endorse the winner of a mayoral election.", IconKind.Ballot, Purple),
            A("ACH_BAILOUT", "Too Big to Fail", "Get bailed out by City Hall.", IconKind.Warning, Red, true),
            A("ACH_COMEBACK", "Back from the Brink", "Repay a City Hall bailout.", IconKind.Finance, Green, true),
            A("ACH_MONTH", "A Month of Service", "Run the same city for 30 days.", IconKind.Calendar, Blue),
            new AchievementDef
            {
                id = "ACH_TRIPS_10M", title = "Ten Million Journeys", description = "Carry 10 million trips in total, across all your cities.",
                icon = IconKind.Trophy, color = Gold, progressMax = 10_000_000, progress = p => (float)p.totalTrips
            },
            A("ACH_CHALLENGE", "Challenge Accepted", "Complete a challenge.", IconKind.Trophy, Blue),
            new AchievementDef
            {
                id = "ACH_CHALLENGE_STARS", title = "Star Collector", description = "Earn every star in Challenges.",
                icon = IconKind.Star, color = Teal, progressMax = 18, progress = p => Challenges.TotalStars(p)
            }
        };

        // Steam stats (configure as INT stats in Steamworks; see Docs/STEAM.md).
        public const string StatTrips = "stat_total_trips";
        public const string StatRequests = "stat_requests_done";
        public const string StatCities = "stat_cities_founded";
        public const string StatDays = "stat_days_run";

        public static PlayerProfile Profile { get; private set; } = new PlayerProfile();
        public static event Action<AchievementDef> Unlocked;

        static GameState bound;
        static float checkTimer, saveTimer;
        static bool dirty, initialised;

        public static AchievementDef Def(string id)
        {
            foreach (var a in All)
                if (a.id == id) return a;
            return null;
        }

        public static int UnlockedCount
        {
            get
            {
                int n = 0;
                foreach (var a in All)
                    if (Profile.Has(a.id)) n++;
                return n;
            }
        }

        public static void Init()
        {
            if (initialised) return;
            initialised = true;
            Profile = PlayerProfile.Load();
            // Anything earned while offline reaches Steam now; anything Steam knows about is kept locally.
            var plat = PlatformServices.Current;
            foreach (var a in All)
            {
                if (Profile.Has(a.id)) plat.UnlockAchievement(a.id);
                else if (plat.IsAchievementUnlocked(a.id))
                {
                    Profile.Add(a.id, PlayerProfile.Now);
                    dirty = true;
                }
            }
            PushStats();
            GameEvents.CityEntered += Bind;
            GameEvents.CityLeft += _ => Unbind();
            GameEvents.CityFounded += _ =>
            {
                Profile.citiesFounded++;
                dirty = true;
                PushStats();
            };
            GameEvents.GuidedStartFinished += () => Unlock("ACH_GUIDED_START");
            GameEvents.ChallengeFinished += OnChallenge;
            GameEvents.GameOver += (_, __) =>
            {
                Profile.gamesLost++;
                dirty = true;
            };
            if (dirty) SaveProfile();
        }

        public static void Unlock(string id)
        {
            var def = Def(id);
            if (def == null || Profile.Has(id)) return;
            Profile.Add(id, PlayerProfile.Now);
            PlatformServices.Current.UnlockAchievement(id);
            SaveProfile();
            Debug.Log("[Metro] Achievement unlocked: " + id);
            Unlocked?.Invoke(def);
        }

        public static void SaveProfile()
        {
            dirty = false;
            saveTimer = 0f;
            if (Profile.Save()) CloudSync.PushProfile();
        }

        static void PushStats()
        {
            var plat = PlatformServices.Current;
            plat.SetStat(StatTrips, (int)Math.Min(int.MaxValue, Profile.totalTrips));
            plat.SetStat(StatRequests, Profile.requestsDone);
            plat.SetStat(StatCities, Profile.citiesFounded);
            plat.SetStat(StatDays, Profile.daysRun);
        }

        // ------------------------------------------------------------------ tracking a city

        static void Bind(GameState gs)
        {
            Unbind();
            bound = gs;
            gs.LineCreated += OnLineCreated;
            gs.DayEnded += OnDayEnded;
            gs.progress.TierReached += OnTier;
            gs.gov.RequestChanged += OnRequest;
            gs.gov.SessionHeld += OnSession;
            gs.gov.BailoutGranted += OnBailout;
            checkTimer = 0f;
        }

        static void Unbind()
        {
            if (bound == null) return;
            bound.LineCreated -= OnLineCreated;
            bound.DayEnded -= OnDayEnded;
            bound.progress.TierReached -= OnTier;
            bound.gov.RequestChanged -= OnRequest;
            bound.gov.SessionHeld -= OnSession;
            bound.gov.BailoutGranted -= OnBailout;
            bound = null;
            if (dirty) SaveProfile();
        }

        static void OnLineCreated(MetroLine l)
        {
            Profile.linesOpened++;
            dirty = true;
            Unlock("ACH_FIRST_LINE");
            if (l.loop) Unlock("ACH_LOOP");
        }

        static void OnDayEnded(int day)
        {
            var gs = bound;
            if (gs == null) return;
            var last = gs.stats.LastDay;
            float riders = last != null ? last.riders : 0f;
            Profile.daysRun++;
            Profile.totalTrips += riders;
            Profile.bestDayTrips = Mathf.Max(Profile.bestDayTrips, riders);
            if (!Inherited(gs))
            {
                if (gs.satisfaction.yesterday >= 0.9f && gs.net.LineCount > 0 && riders > 1000f) Unlock("ACH_HAPPY");
                var y = gs.econ.Yesterday;
                if (y != null && y.OperatingNet >= 1_000_000f) Unlock("ACH_PROFIT");
            }
            if (Profile.totalTrips >= 10_000_000) Unlock("ACH_TRIPS_10M");
            else
            {
                // Show progress toasts in Steam at every new million.
                long before = (long)((Profile.totalTrips - riders) / 1_000_000), after = (long)(Profile.totalTrips / 1_000_000);
                if (after > before) PlatformServices.Current.IndicateProgress("ACH_TRIPS_10M", (uint)after * 1_000_000u, 10_000_000u);
            }
            PushStats();
            PlatformServices.Current.StoreStats(false);
            SaveProfile();
        }

        static void OnTier(TierReachedInfo info)
        {
            if (info.tier >= ProgressionSystem.Tiers.Length - 1)
            {
                Profile.metropolises++;
                dirty = true;
            }
            Check(bound);
        }

        static void OnRequest(CouncilRequest r)
        {
            if (r.state != RequestState.Done) return;
            Profile.requestsDone++;
            dirty = true;
            Unlock("ACH_FIRST_REQUEST");
            if (r.kind == RequestKind.Event) Unlock("ACH_EVENT");
            if (r.kind == RequestKind.RepayBailout) Unlock("ACH_COMEBACK");
            if (Profile.requestsDone >= 25) Unlock("ACH_REQUESTS_25");
            else if (Profile.requestsDone % 5 == 0) PlatformServices.Current.IndicateProgress("ACH_REQUESTS_25", (uint)Profile.requestsDone, 25u);
            PushStats();
        }

        static void OnSession(CouncilReport rep)
        {
            if (rep.election != null && rep.election.endorsed >= 0 && rep.election.endorsedWon) Unlock("ACH_ELECTION");
        }

        static void OnBailout(float grant) => Unlock("ACH_BAILOUT");

        static void OnChallenge(ChallengeRun run)
        {
            if (!run.won) return;
            Unlock("ACH_CHALLENGE");
            if (Challenges.TotalStars(Profile) >= Challenges.MaxStars) Unlock("ACH_CHALLENGE_STARS");
        }

        /// <summary>Called every frame while a city is being played; checks state-based achievements now and then.</summary>
        public static void Tick(float dt)
        {
            if (bound == null) return;
            checkTimer -= dt;
            saveTimer += dt;
            if (checkTimer <= 0f)
            {
                checkTimer = 2f;
                Check(bound);
            }
            if (dirty && saveTimer > 30f) SaveProfile();
        }

        /// <summary>
        /// A challenge that starts with a ready-made network: its trips, profit and structures weren't
        /// built by the player, so they don't count (what the player does there still does).
        /// </summary>
        static bool Inherited(GameState gs) => gs.challenge != null && gs.challenge.def.prebuiltLines > 0;

        static void Check(GameState gs)
        {
            if (gs == null) return;
            var net = gs.net;
            if (gs.gov.started && gs.gov.approval >= 90f) Unlock("ACH_APPROVAL_90");
            if (gs.gov.completed >= 1) Unlock("ACH_FIRST_REQUEST");
            if (Inherited(gs)) return;
            float riders = gs.stats.ridersToday;
            if (riders >= 10_000f) Unlock("ACH_RIDERS_10K");
            if (riders >= 100_000f) Unlock("ACH_RIDERS_100K");
            if (riders >= 500_000f) Unlock("ACH_RIDERS_500K");
            int tier = gs.progress.tier;
            bool realGame = !gs.options.demo && gs.options.earnedSizes;
            // City sizes only count when they were earned from a small town.
            if (realGame)
            {
                if (tier >= 1) Unlock("ACH_GROWING_TOWN");
                if (tier >= 2) Unlock("ACH_MARKET_TOWN");
                if (tier >= 3) Unlock("ACH_CITY");
                if (tier >= 4) Unlock("ACH_BIG_CITY");
                if (tier >= 5) Unlock("ACH_METROPOLIS");
                if (tier >= 5 && gs.options.difficulty == 2) Unlock("ACH_METROPOLIS_HARD");
                if (tier >= 3 && gs.clock.DayNumber <= 5) Unlock("ACH_BOOMTOWN");
            }
            if (gs.clock.DayNumber >= 31) Unlock("ACH_MONTH");
            int running = 0;
            foreach (var l in net.AliveLines())
            {
                if (l.trains.Count > 0) running++;
                if (l.loop) Unlock("ACH_LOOP");
                if (l.path != null && l.path.length >= 30f) Unlock("ACH_LONG_LINE");
            }
            if (net.LineCount > 0) Unlock("ACH_FIRST_LINE");
            if (running >= 8) Unlock("ACH_LINES_8");
            if (net.StationCount >= 100) Unlock("ACH_STATIONS_100");
            foreach (var s in net.AliveStations())
            {
                if (s.lines.Count >= 2) Unlock("ACH_INTERCHANGE");
                if (s.platformLevel >= 3 && s.capacityLevel >= 3 && s.elevators && s.concourse) Unlock("ACH_FULLY_LOADED");
            }
            net.TrackTotals(out _, out float bridge);
            if (bridge > 0.01f) Unlock("ACH_BRIDGE");
        }

        /// <summary>Flushes the profile (quitting the game).</summary>
        public static void Flush()
        {
            if (dirty) SaveProfile();
        }
    }
}
