namespace CsmForge.Core
{
    /// <summary>
    /// WP-3.2 step 1: the single audited source for every hardcoded mod-compatibility fact —
    /// previously scattered across KnownModBridgeRegistry, CompatibilityCollector and the
    /// individual known-mod bridges. Pure data, no game references; the JSON externalization
    /// (WP-3.2 step 2) replaces this class file-for-file once consumers only read it.
    ///
    /// Content is pinned by ModCompatibilityCatalogTests: changing an entry here is an
    /// auditable compatibility decision, not an accident.
    /// </summary>
    public static class ModCompatibilityCatalog
    {
        /// <summary>Infrastructure mod every Forge installation needs; allowed to differ.</summary>
        public const string DependencyModType = "CitiesHarmony.Mod";

        /// <summary>Known-broken-in-multiplayer mod, always rejected.</summary>
        public const string BlockedModType = "TrafficManager.Lifecycle.TrafficManagerMod";

        /// <summary>UserMod type names treated as client-only (no state sync needed).</summary>
        public static readonly string[] ClientOnlyModTypes =
        {
            "LoadingScreenMod.Mod", "MyFirstMod.DestroyChirperMod", "RemoveChirper.RemoveChirper",
            "ChirpRemover.ChirpRemover", "MoreAspectRatios.MoreAspectRatios", "FPSCamera.Mod", "AchieveIt.ModInfo",
            "ACME.Mod", "PrecisionEngineering.Mod"
        };

        /// <summary>UserMod type names with an audited Forge bridge (synchronized mod tier).</summary>
        public static readonly string[] KnownSynchronizedModTypes =
        {
            DemandControllerUserModType, GameAnarchyUserModType, InfiniteGoodsUserModType,
            EightyOne2UserModType, NetworkMultitoolUserModType
        };

        public const string DemandControllerUserModType = "DemandController.DemandController";
        public const string GameAnarchyUserModType = "GameAnarchy.Mod";
        public const string InfiniteGoodsUserModType = "InfiniteGoodsMod.ModIdentity";
        public const string EightyOne2UserModType = "EightyOne2.Mod";
        public const string NetworkMultitoolUserModType = "NetworkMultitool.Mod";

        /// <summary>Audited Game Anarchy 1.3.1 build: settings surface the Forge bridge reflects on.</summary>
        public static class GameAnarchy
        {
            public const string AssemblyName = "GameAnarchy";
            public const string SupportedVersion = "1.3.1.0";
            public const string SettingsTypeName = "GameAnarchy.ModSettings.ModSetting";

            /// <summary>Static holders of the live settings instance, tried in order.</summary>
            public static readonly string[] HolderTypeNames =
            {
                "GameAnarchy.Patches.BuildingAIPatch", "GameAnarchy.Patches.BulldozeToolPatch"
            };

            /// <summary>Types whose presence pins the audited build (bridge availability gate).</summary>
            public static readonly string[] RequiredTypeNames =
            {
                "GameAnarchy.Managers.ModEconomyManager", "GameAnarchy.Managers.CityServicesManager",
                "GameAnarchy.Managers.FireControlManager", "GameAnarchy.Extension.OilAndOreResourceExtension",
                "GameAnarchy.Extension.MilestonesExtension"
            };

            /// <summary>UI-only settings that never need syncing.</summary>
            public static readonly string[] LocalOnlySettings =
            {
                "AchievementSystemEnabled", "SkipIntroEnabled", "OptionsPanelCategoriesHorizontalOffset",
                "OptionsPanelCategoriesUpdated", "ToolButtonPresent", "ToolButtonPositionX", "ToolButtonPositionY"
            };

            /// <summary>Cheat options whose persistent writes have no Forge authority domain.</summary>
            public static readonly string[] UnsupportedBooleanSettings =
            {
                "UnlockInfoViews", "UnlockBasicRoads", "UnlockAllRoads", "UnlockTrainTrack", "UnlockMetroTrack",
                "UnlockPolicies", "UnlockPublicTransport", "UnlockUniqueBuildings", "UnlockLandscaping",
                "RemoveNoisePollution", "RemoveGroundPollution", "RemoveWaterPollution", "RemoveDeath",
                "RemoveGarbage", "RemoveCrime", "MaximizeAttractiveness", "MaximizeEntertainment",
                "MaximizeLandValue", "MaximizeEducationCoverage", "MaximizeFireCoverage",
                "RemovePlayerBuildingFire", "RemoveResidentialBuildingFire", "RemoveIndustrialBuildingFire",
                "RemoveCommercialBuildingFire", "RemoveOfficeBuildingFire", "RemoveParkBuildingFire",
                "RemoveMuseumFire", "RemoveCampusBuildingFire", "RemoveAirportBuildingFire"
            };

            public const long FixedOilDepletionRate = 100;
            public const long FixedOreDepletionRate = 100;
            public const long FixedSpreadFireProbability = 0;
        }

        /// <summary>Audited Infinite Goods build: service-point options with no authority domain.</summary>
        public static class InfiniteGoods
        {
            public static readonly string[] UnsupportedServicePointSettings =
            {
                "PedestrianServicePointGoods", "PedestrianServicePointLuxuryProducts",
                "CargoServicePointSpecializedIndustryOil", "CargoServicePointSpecializedIndustryOre",
                "CargoServicePointSpecializedIndustryGrain", "CargoServicePointSpecializedIndustryLogs",
                "CargoServicePointGenericIndustryPetrol", "CargoServicePointGenericIndustryCoal",
                "CargoServicePointGenericIndustryFood", "CargoServicePointGenericIndustryLumber"
            };
        }
    }
}
