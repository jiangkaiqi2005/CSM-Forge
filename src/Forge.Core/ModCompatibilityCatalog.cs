using System;
using System.Collections.Generic;
using System.Globalization;

namespace CsmForge.Core
{
    /// <summary>
    /// WP-3.2 step 2: immutable compatibility facts with a built-in set (pinned by
    /// ModCompatibilityCatalogTests) and a strict JSON reader, so an external
    /// compat/mods/*.json document with the same schema can replace the built-in set.
    /// Parsing fails closed: any missing or malformed field rejects the whole document and
    /// the built-in set stays in force - a bad file can never widen what is accepted.
    /// </summary>
    public sealed class ModCompatibilityDocument
    {
        public string DependencyModType { get; private set; }
        public string BlockedModType { get; private set; }
        public string[] ClientOnlyModTypes { get; private set; }
        public string DemandControllerUserModType { get; private set; }
        public string GameAnarchyUserModType { get; private set; }
        public string InfiniteGoodsUserModType { get; private set; }
        public string EightyOne2UserModType { get; private set; }
        public string NetworkMultitoolUserModType { get; private set; }
        public string[] KnownSynchronizedModTypes { get; private set; }
        public GameAnarchySurfaceData GameAnarchy { get; private set; }
        public InfiniteGoodsSurfaceData InfiniteGoods { get; private set; }

        private ModCompatibilityDocument() { }

        private ModCompatibilityDocument(string dependencyModType, string blockedModType,
            string[] clientOnlyModTypes, string demandController, string gameAnarchy, string infiniteGoods,
            string eightyOne2, string networkMultitool, GameAnarchySurfaceData ga, InfiniteGoodsSurfaceData ig)
        {
            DependencyModType = dependencyModType; BlockedModType = blockedModType;
            ClientOnlyModTypes = clientOnlyModTypes;
            DemandControllerUserModType = demandController; GameAnarchyUserModType = gameAnarchy;
            InfiniteGoodsUserModType = infiniteGoods; EightyOne2UserModType = eightyOne2;
            NetworkMultitoolUserModType = networkMultitool;
            KnownSynchronizedModTypes = new[]
            {
                demandController, gameAnarchy, infiniteGoods, eightyOne2, networkMultitool
            };
            GameAnarchy = ga; InfiniteGoods = ig;
        }

        public static ModCompatibilityDocument BuiltIn()
        {
            return new ModCompatibilityDocument(
                "CitiesHarmony.Mod",
                "TrafficManager.Lifecycle.TrafficManagerMod",
                new[]
                {
                    "LoadingScreenMod.Mod", "MyFirstMod.DestroyChirperMod", "RemoveChirper.RemoveChirper",
                    "ChirpRemover.ChirpRemover", "MoreAspectRatios.MoreAspectRatios", "FPSCamera.Mod",
                    "AchieveIt.ModInfo", "ACME.Mod", "PrecisionEngineering.Mod"
                },
                "DemandController.DemandController", "GameAnarchy.Mod", "InfiniteGoodsMod.ModIdentity",
                "EightyOne2.Mod", "NetworkMultitool.Mod",
                GameAnarchySurfaceData.BuiltIn(), InfiniteGoodsSurfaceData.BuiltIn());
        }

        /// <summary>Strict schema reader; every field is required and bounds-checked.</summary>
        public static bool TryParseJson(JsonNode node, out ModCompatibilityDocument document)
        {
            document = null;
            if (node == null || node.Kind != JsonKind.Object) return false;
            string dependency, blocked, demand, gameAnarchy, infiniteGoods, eightyOne2, multiTool;
            string[] clientOnly;
            if (!RequireText(node, "dependencyModType", out dependency)) return false;
            if (!RequireText(node, "blockedModType", out blocked)) return false;
            if (!RequireTextArray(node, "clientOnlyModTypes", 512, 256, out clientOnly)) return false;
            if (!RequireText(node, "demandControllerUserModType", out demand)) return false;
            if (!RequireText(node, "gameAnarchyUserModType", out gameAnarchy)) return false;
            if (!RequireText(node, "infiniteGoodsUserModType", out infiniteGoods)) return false;
            if (!RequireText(node, "eightyOne2UserModType", out eightyOne2)) return false;
            if (!RequireText(node, "networkMultitoolUserModType", out multiTool)) return false;

            JsonNode gameAnarchyNode;
            if (!node.TryGet("gameAnarchy", out gameAnarchyNode) || gameAnarchyNode == null ||
                gameAnarchyNode.Kind != JsonKind.Object) return false;
            GameAnarchySurfaceData ga;
            if (!GameAnarchySurfaceData.TryParse(gameAnarchyNode, out ga)) return false;

            JsonNode infiniteGoodsNode;
            if (!node.TryGet("infiniteGoods", out infiniteGoodsNode) || infiniteGoodsNode == null ||
                infiniteGoodsNode.Kind != JsonKind.Object) return false;
            InfiniteGoodsSurfaceData ig;
            if (!InfiniteGoodsSurfaceData.TryParse(infiniteGoodsNode, out ig)) return false;

            document = new ModCompatibilityDocument(dependency, blocked, clientOnly, demand, gameAnarchy,
                infiniteGoods, eightyOne2, multiTool, ga, ig);
            return true;
        }

        private static bool RequireText(JsonNode node, string key, out string value)
        {
            value = null;
            JsonNode field;
            if (!node.TryGet(key, out field) || field == null || field.Kind != JsonKind.String ||
                field.Text.Length == 0 || field.Text.Length > 256) return false;
            value = field.Text; return true;
        }

        private static bool RequireTextArray(JsonNode node, string key, int maximumEntries, int maximumEntryLength, out string[] values)
        { return ModCompatibilityDocumentParse.RequireTextArray(node, key, maximumEntries, maximumEntryLength, out values); }
    }

    public sealed class GameAnarchySurfaceData
    {
        public string AssemblyName { get; private set; }
        public string SupportedVersion { get; private set; }
        public string SettingsTypeName { get; private set; }
        public string[] HolderTypeNames { get; private set; }
        public string[] RequiredTypeNames { get; private set; }
        public string[] LocalOnlySettings { get; private set; }
        public string[] UnsupportedBooleanSettings { get; private set; }
        public long FixedOilDepletionRate { get; private set; }
        public long FixedOreDepletionRate { get; private set; }
        public long FixedSpreadFireProbability { get; private set; }

        private GameAnarchySurfaceData() { }

        public static GameAnarchySurfaceData BuiltIn()
        {
            return new GameAnarchySurfaceData
            {
                AssemblyName = "GameAnarchy",
                SupportedVersion = "1.3.1.0",
                SettingsTypeName = "GameAnarchy.ModSettings.ModSetting",
                HolderTypeNames = new[]
                {
                    "GameAnarchy.Patches.BuildingAIPatch", "GameAnarchy.Patches.BulldozeToolPatch"
                },
                RequiredTypeNames = new[]
                {
                    "GameAnarchy.Managers.ModEconomyManager", "GameAnarchy.Managers.CityServicesManager",
                    "GameAnarchy.Managers.FireControlManager", "GameAnarchy.Extension.OilAndOreResourceExtension",
                    "GameAnarchy.Extension.MilestonesExtension"
                },
                LocalOnlySettings = new[]
                {
                    "AchievementSystemEnabled", "SkipIntroEnabled", "OptionsPanelCategoriesHorizontalOffset",
                    "OptionsPanelCategoriesUpdated", "ToolButtonPresent", "ToolButtonPositionX", "ToolButtonPositionY"
                },
                UnsupportedBooleanSettings = new[]
                {
                    "UnlockInfoViews", "UnlockBasicRoads", "UnlockAllRoads", "UnlockTrainTrack", "UnlockMetroTrack",
                    "UnlockPolicies", "UnlockPublicTransport", "UnlockUniqueBuildings", "UnlockLandscaping",
                    "RemoveNoisePollution", "RemoveGroundPollution", "RemoveWaterPollution", "RemoveDeath",
                    "RemoveGarbage", "RemoveCrime", "MaximizeAttractiveness", "MaximizeEntertainment",
                    "MaximizeLandValue", "MaximizeEducationCoverage", "MaximizeFireCoverage",
                    "RemovePlayerBuildingFire", "RemoveResidentialBuildingFire", "RemoveIndustrialBuildingFire",
                    "RemoveCommercialBuildingFire", "RemoveOfficeBuildingFire", "RemoveParkBuildingFire",
                    "RemoveMuseumFire", "RemoveCampusBuildingFire", "RemoveAirportBuildingFire"
                },
                FixedOilDepletionRate = 100, FixedOreDepletionRate = 100, FixedSpreadFireProbability = 0
            };
        }

        public static bool TryParse(JsonNode node, out GameAnarchySurfaceData surface)
        {
            surface = null;
            if (node == null || node.Kind != JsonKind.Object) return false;
            GameAnarchySurfaceData result = new GameAnarchySurfaceData();
            JsonNode field;
            if (!node.TryGet("assemblyName", out field) || field.Kind != JsonKind.String) return false;
            result.AssemblyName = field.Text;
            if (!node.TryGet("supportedVersion", out field) || field.Kind != JsonKind.String) return false;
            result.SupportedVersion = field.Text;
            if (!node.TryGet("settingsTypeName", out field) || field.Kind != JsonKind.String) return false;
            result.SettingsTypeName = field.Text;
            string[] holders;
            if (!ModCompatibilityDocumentParse.RequireTextArray(node, "holderTypeNames", 8, 256, out holders)) return false;
            result.HolderTypeNames = holders;
            string[] required;
            if (!ModCompatibilityDocumentParse.RequireTextArray(node, "requiredTypeNames", 64, 256, out required)) return false;
            result.RequiredTypeNames = required;
            string[] localOnly;
            if (!ModCompatibilityDocumentParse.RequireTextArray(node, "localOnlySettings", 512, 256, out localOnly)) return false;
            result.LocalOnlySettings = localOnly;
            string[] blocked;
            if (!ModCompatibilityDocumentParse.RequireTextArray(node, "unsupportedBooleanSettings", 512, 256, out blocked)) return false;
            result.UnsupportedBooleanSettings = blocked;
            long oil, ore, fire;
            if (!ModCompatibilityDocumentParse.TryLong(node, "fixedOilDepletionRate", 0, 1000, out oil)) return false;
            if (!ModCompatibilityDocumentParse.TryLong(node, "fixedOreDepletionRate", 0, 1000, out ore)) return false;
            if (!ModCompatibilityDocumentParse.TryLong(node, "fixedSpreadFireProbability", 0, 1000, out fire)) return false;
            result.FixedOilDepletionRate = oil; result.FixedOreDepletionRate = ore; result.FixedSpreadFireProbability = fire;
            surface = result; return true;
        }
    }

    public sealed class InfiniteGoodsSurfaceData
    {
        public string[] UnsupportedServicePointSettings { get; private set; }

        public static InfiniteGoodsSurfaceData BuiltIn()
        {
            return new InfiniteGoodsSurfaceData
            {
                UnsupportedServicePointSettings = new[]
                {
                    "PedestrianServicePointGoods", "PedestrianServicePointLuxuryProducts",
                    "CargoServicePointSpecializedIndustryOil", "CargoServicePointSpecializedIndustryOre",
                    "CargoServicePointSpecializedIndustryGrain", "CargoServicePointSpecializedIndustryLogs",
                    "CargoServicePointGenericIndustryPetrol", "CargoServicePointGenericIndustryCoal",
                    "CargoServicePointGenericIndustryFood", "CargoServicePointGenericIndustryLumber"
                }
            };
        }

        public static bool TryParse(JsonNode node, out InfiniteGoodsSurfaceData surface)
        {
            surface = null;
            if (node == null || node.Kind != JsonKind.Object) return false;
            string[] entries;
            if (!ModCompatibilityDocumentParse.RequireTextArray(node, "unsupportedServicePointSettings", 512, 256, out entries))
                return false;
            surface = new InfiniteGoodsSurfaceData { UnsupportedServicePointSettings = entries };
            return true;
        }
    }

    /// <summary>Parse-side helpers shared by the typed readers (not a document itself).</summary>
    internal static class ModCompatibilityDocumentParse
    {
        public static bool TryLong(JsonNode node, string key, long minimum, long maximum, out long value)
        {
            value = 0;
            JsonNode field;
            if (!node.TryGet(key, out field) || field == null || field.Kind != JsonKind.Number) return false;
            if (!field.HasInteger) return false;
            if (field.Integer < minimum || field.Integer > maximum) return false;
            value = field.Integer; return true;
        }

        public static bool RequireTextArray(JsonNode node, string key, int maximumEntries, int maximumEntryLength, out string[] values)
        {
            values = null;
            JsonNode field;
            if (!node.TryGet(key, out field) || field == null || field.Kind != JsonKind.Array ||
                field.Items.Count > maximumEntries) return false;
            string[] result = new string[field.Items.Count];
            for (int i = 0; i < result.Length; i++)
            {
                JsonNode entry = field.Items[i];
                if (entry == null || entry.Kind != JsonKind.String || entry.Text.Length == 0 || entry.Text.Length > maximumEntryLength)
                    return false;
                result[i] = entry.Text;
            }
            values = result; return true;
        }
    }

    /// <summary>
    /// WP-3.2: consumers read <see cref="Default"/>; the built-in set is pinned by tests and a
    /// parsed external document (same schema) replaces it wholesale - never partially.
    /// </summary>
    public static class ModCompatibilityCatalog
    {
        public static readonly ModCompatibilityDocument Default = ModCompatibilityDocument.BuiltIn();

        /// <summary>Parses a compat document; a smaller/older document never widens acceptance.</summary>
        public static bool TryParseDocument(string json, out ModCompatibilityDocument document)
        {
            document = null;
            JsonNode node;
            try { node = MiniJson.Parse(json); }
            catch (JsonParseException) { return false; }
            catch (ArgumentException) { return false; }
            return ModCompatibilityDocument.TryParseJson(node, out document);
        }
    }
}
