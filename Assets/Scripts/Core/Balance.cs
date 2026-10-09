using UnityEngine;

namespace Metro
{
    /// <summary>
    /// Every tunable number of the game lives here so balancing happens in one place.
    /// Units: distance in km, time in game minutes, money in dollars.
    /// </summary>
    public static class Balance
    {
        // ---------------- Time ----------------
        public const float MinutesPerRealSecond = 2f;          // game minutes per real second at 1x
        public static readonly float[] Speeds = { 1f, 2f, 4f };
        public const float NightBoost = 6f;                     // the city sleeps 01:00-05:00, skip it faster
        public const float SkipToDawnSpeed = 16f;               // "Skip to dawn" once the city is ready to grow
        public const int NightStart = 60;
        public const int NightEnd = 300;
        public const int MinutesPerDay = 1440;
        public const int StartMinute = 5 * 60 + 30;             // new games start Monday 05:30

        // ---------------- Map ----------------
        public const float MapWidth = 64f;
        public const float MapHeight = 44f;
        public const float CellSize = 0.5f;
        public const float WalkRadius = 1.0f;                   // station catchment radius
        public const float MinStationSpacing = 0.45f;

        // ---------------- Trains ----------------
        public const float TrainMaxSpeed = 1.33f;               // km per minute (~80 km/h)
        public const float TrainAccel = 3.0f;                   // km per minute^2 (~0.8 m/s^2)
        public const float DwellBase = 0.4f;
        public const float PaxPerWagonPerMinute = 90f;          // boarding/alighting speed
        public const float DwellMax = 2.0f;
        public const float Turnaround = 1.0f;
        public const int WagonCapacity = 150;
        public const int MinWagons = 2;
        public const int DefaultWagons = 3;
        public const int DepotCapacityPerLevel = 10;
        public const int MaxDepotLevel = 3;

        public static int MaxWagons(int platformLevel) => platformLevel <= 1 ? 4 : platformLevel == 2 ? 6 : 8;
        public static int StationCapacity(int level) => level <= 1 ? 900 : level == 2 ? 2000 : 3800;

        // ---------------- Routing (generalised minutes) ----------------
        public const float BoardPenalty = 0.5f;
        public const float TransferPenalty = 4f;
        public const float TransferPenaltyConcourse = 1.5f;
        public const float MaxWaitCost = 20f;
        public const float AccessWalk = 6f;
        public const float EgressWalk = 5f;
        public const float DwellEstimate = 0.45f;

        // ---------------- Demand ----------------
        public const float CommuteTripsPerResident = 0.5f;    // each direction, per weekday
        public const float OtherTripsPerResident = 0.85f;
        public const float OtherTripsPerJob = 0.25f;
        public const float CommuteDecayKm = 9f;
        public const float OtherDecayKm = 5f;
        public const float ValueOfTime = 0.22f;                 // $ per minute
        public const float ModeShareSlope = 0.075f;
        public const float MaxModeShare = 0.85f;
        public const float CarSpeedBase = 34f;                  // km/h in an empty city
        public const float CarCongestionPop = 3_000_000f;       // population at which car speed halves
        public const float CarOverhead = 11f;                   // parking, walking to the car...
        public const float CarCostPerKm = 0.3f;
        public const float CarFixedCost = 3f;
        public const float WeekendCommuteFactor = 0.3f;
        public const float WeekendOtherFactor = 1.25f;

        // ---------------- Passengers ----------------
        public const float BaseAbandonPerMinute = 0.0015f;
        public const float CrowdAbandonPerMinute = 0.03f;
        public const float NoServiceAbandonPerMinute = 0.08f;

        // ---------------- Construction costs ----------------
        public const float StationCost = 110_000f;
        public const float TunnelPerKm = 80_000f;
        public const float BridgePerKm = 260_000f;
        public const float SharedTrackFactor = 0.5f;            // extra tracks along an existing corridor
        public const float DepotCost = 200_000f;
        public const float DepotExpandCost = 140_000f;
        public const float TrainCost = 90_000f;
        public const float WagonCost = 18_000f;
        public const float ElevatorCost = 45_000f;
        public const float ConcourseCost = 120_000f;
        public static float PlatformUpgradeCost(int toLevel) => toLevel == 2 ? 90_000f : 190_000f;
        public static float CapacityUpgradeCost(int toLevel) => toLevel == 2 ? 80_000f : 170_000f;
        public const float DemolishRefund = 0.35f;
        public const float TrainResale = 0.6f;

        // ---------------- Operating costs (per day) ----------------
        public const float DriverSalaryPerTrain = 6_000f;
        public const float TrainMaintenancePerWagon = 1_200f;
        public const float StationStaff = 2_000f;
        public const float StationStaffPerUpgrade = 600f;
        public const float TunnelMaintenancePerKm = 400f;
        public const float BridgeMaintenancePerKm = 900f;
        public const float DepotUpkeep = 3_000f;

        // ---------------- Income ----------------
        public const float DefaultFare = 2.40f;
        public const float MinFare = 0.5f;
        public const float MaxFare = 6.0f;
        public const float SubsidyPerRider = 1.80f;             // city operating subsidy, scaled by satisfaction
        public static readonly float[] AdRevenuePerBoarding = { 0f, 0.06f, 0.12f, 0.20f };
        public static readonly float[] AdSatisfactionPenalty = { 0f, 0.02f, 0.05f, 0.09f };
        public static readonly string[] AdLevelNames = { "None", "Light", "Standard", "Heavy" };

        // ---------------- Finance ----------------
        public const float LoanStep = 500_000f;
        public const float LoanBaseLimit = 2_000_000f;
        public const float LoanDailyInterest = 0.002f;

        // ---------------- Fines ----------------
        public const float OvercrowdingFine = 30_000f;
        public const int OvercrowdingFineMinutes = 25;          // continuous critical minutes before a fine
        public const float NoServiceFinePerDay = 25_000f;

        // ---------------- Growth ----------------
        public const float InitialDistrictMaturity = 0.4f;
        public const float DistrictMaturityPerHour = 0.012f;
        public const float CityGrowthPerDay = 0.012f;           // long-term densification
        public const float MaxCityGrowth = 1.6f;
        public const float TransitGrowthBonus = 0.25f;          // well served cells grow larger

        // ---------------- City Hall ----------------
        public const float ApprovalStart = 55f;
        public const float ApprovalDrift = 0.2f;                // nightly pull of approval towards the city's mood
        public const int CouncilSessionMinute = 6 * 60;         // weekly sessions on Monday mornings
        public const float ProposalIntervalMin = 0.6f;          // days between requests
        public const float ProposalIntervalMax = 1.3f;
        public const float ProposalAnswerHours = 10f;
        public const float HoldMinutes = 24f * 60f;             // fare cap / no surcharge must hold this long
        public const float EventArrivalMinutes = 120f;
        public const float EventDepartMinutes = 90f;
        public const float EventServedShare = 0.6f;             // share of fans the metro must carry
        public const float MandateChance = 0.2f;
        public const float MandateFine = 150_000f;
        public const int ElectionEveryDays = 14;
        public const float UltimatumApproval = 25f;
        public const float EndorseSwing = 8f;
        public const float BankruptBase = 3_000_000f;          // plus a quarter of the network's value
        public const int BailoutDaysInRed = 3;
        public const float BailoutMinimum = 2_500_000f;
        public const float BailoutCushion = 2_000_000f;
        public const float BailoutApprovalCost = 15f;
        public const int BailoutRepayDays = 14;

        // ---------------- Difficulty ----------------
        public static float StartMoney(int difficulty) => difficulty == 0 ? 3_200_000f : difficulty == 1 ? 2_500_000f : 2_000_000f;
        public static float CostMultiplier(int difficulty) => difficulty == 0 ? 0.85f : difficulty == 1 ? 1f : 1.2f;
        public static float GrowthPace(int difficulty) => difficulty == 0 ? 0.8f : difficulty == 1 ? 1f : 1.25f;
        public static float FineMultiplier(int difficulty) => difficulty == 0 ? 0.5f : difficulty == 1 ? 1f : 1.5f;
        public static readonly string[] DifficultyNames = { "Relaxed", "Standard", "Demanding" };

        public static Color32 Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex.StartsWith("#") ? hex : "#" + hex, out Color c);
            return c;
        }
    }
}
