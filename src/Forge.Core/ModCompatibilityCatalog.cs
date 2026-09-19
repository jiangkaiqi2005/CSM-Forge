using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

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
        public IList<ModEntryData> Mods { get; private set; }

        /// <summary>WP-3.2b: generic per-mod entry lookup (userModType -> manifest entry).</summary>
        public bool TryGetModEntry(string userModType, out ModEntryData entry)
        {
            entry = null;
            if (userModType == null || Mods == null) return false;
            for (int i = 0; i < Mods.Count; i++)
            {
                if (Mods[i] == null || !StringComparer.Ordinal.Equals(Mods[i].UserModType, userModType)) continue;
                entry = Mods[i]; return true;
            }
            return false;
        }

        private ModCompatibilityDocument() { }

        private ModCompatibilityDocument(string dependencyModType, string blockedModType,
            string[] clientOnlyModTypes, string demandController, string gameAnarchy, string infiniteGoods,
            string eightyOne2, string networkMultitool, GameAnarchySurfaceData ga, InfiniteGoodsSurfaceData ig,
            IList<ModEntryData> mods)
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
            Mods = mods ?? new List<ModEntryData>();
        }

        public static ModCompatibilityDocument BuiltIn()
        {
            string dependency = "CitiesHarmony.Mod";
            string blocked = "TrafficManager.Lifecycle.TrafficManagerMod";
            string demand = "DemandController.DemandController";
            string gameAnarchy = "GameAnarchy.Mod";
            string infiniteGoods = "InfiniteGoodsMod.ModIdentity";
            string eightyOne2 = "EightyOne2.Mod";
            string networkMultitool = "NetworkMultitool.Mod";
            string[] clientOnly =
            {
                "LoadingScreenMod.Mod", "MyFirstMod.DestroyChirperMod", "RemoveChirper.RemoveChirper",
                "ChirpRemover.ChirpRemover", "MoreAspectRatios.MoreAspectRatios", "FPSCamera.Mod",
                "AchieveIt.ModInfo", "ACME.Mod", "PrecisionEngineering.Mod"
            };
            GameAnarchySurfaceData ga = GameAnarchySurfaceData.BuiltIn();
            InfiniteGoodsSurfaceData ig = InfiniteGoodsSurfaceData.BuiltIn();

            List<ModEntryData> mods = new List<ModEntryData>
            {
                new ModEntryData(dependency, "dependency"),
                new ModEntryData(blocked, "blocked"),
                new ModEntryData(demand, "synchronized", "DemandController.Settings"),
                new ModEntryData(gameAnarchy, "synchronized", ga.SettingsTypeName,
                    ga.AssemblyName, ga.SupportedVersion, ga.HolderTypeNames,
                    ga.LocalOnlySettings, ga.UnsupportedBooleanSettings,
                    new[] { "OilDepletionRate/OreDepletionRate (both rates must be 100)", "BuildingSpreadFireProbability/TreeSpreadFireProbability (fire-spread overrides)", "CurrentUnlockMode/CurrentMilestoneLevel (milestone/unlock overrides)" },
                    new[] { "OilDepletionRate", "OreDepletionRate", "BuildingSpreadFireProbability", "TreeSpreadFireProbability", "CurrentUnlockMode", "CurrentMilestoneLevel" },
                    new[] { ga.FixedOilDepletionRate, ga.FixedOilDepletionRate, ga.FixedSpreadFireProbability, ga.FixedSpreadFireProbability, 0L, 0L }),
                new ModEntryData(infiniteGoods, "synchronized"),
                new ModEntryData(eightyOne2, "synchronized"),
                new ModEntryData(networkMultitool, "synchronized")
            };
            foreach (string clientOnlyEntry in clientOnly)
                mods.Add(new ModEntryData(clientOnlyEntry, "client-only"));

            return new ModCompatibilityDocument(
                dependency, blocked, clientOnly, demand, gameAnarchy, infiniteGoods,
                eightyOne2, networkMultitool, ga, ig, mods);
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

            List<ModEntryData> mods = new List<ModEntryData>();
            JsonNode modsNode;
            if (node.TryGet("mods", out modsNode) && modsNode != null)
            {
                if (modsNode.Kind != JsonKind.Array || modsNode.Items.Count > 512) return false;
                for (int i = 0; i < modsNode.Items.Count; i++)
                {
                    ModEntryData entry;
                    if (!ModEntryData.TryParse(modsNode.Items[i], out entry)) return false;
                    mods.Add(entry);
                }
            }

            document = new ModCompatibilityDocument(dependency, blocked, clientOnly, demand, gameAnarchy,
                infiniteGoods, eightyOne2, multiTool, ga, ig, mods);
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


    /// <summary>
    /// WP-3.2b: one generic mod entry of the multi-mod manifest. Category is one of
    /// "client-only" / "synchronized" / "blocked" / "dependency"; the settings surface is
    /// required for "synchronized" and ignored otherwise. Unknown fields are ignored
    /// (forward compatible); any malformed field rejects the whole document.
    /// </summary>
    public sealed class ModEntryData
    {
        public string UserModType { get; private set; }
        public string Category { get; private set; }
        public string AssemblyName { get; private set; }
        public string SupportedVersion { get; private set; }
        public string SettingsTypeName { get; private set; }
        public string[] HolderTypeNames { get; private set; }
        public string[] LocalOnlySettings { get; private set; }
        public string[] BlockedBooleanSettings { get; private set; }
        public string[] FixedValueLabels { get; private set; }
        public string[] FixedValuePropertyNames { get; private set; }
        public long[] FixedValueRequired { get; private set; }
        public string HolderFieldName { get; private set; }

        private ModEntryData() { }

        internal ModEntryData(string userModType, string category)
        { UserModType = userModType; Category = category; }

        internal ModEntryData(string userModType, string category, string settingsTypeName)
        { UserModType = userModType; Category = category; SettingsTypeName = settingsTypeName; }

        internal ModEntryData(string userModType, string category, string settingsTypeName,
            string assemblyName, string supportedVersion, string[] holderTypeNames,
            string[] localOnlySettings, string[] blockedBooleanSettings,
            string[] fixedValueLabels, string[] fixedValuePropertyNames, long[] fixedValueRequired)
        {
            UserModType = userModType; Category = category; SettingsTypeName = settingsTypeName;
            AssemblyName = assemblyName; SupportedVersion = supportedVersion;
            HolderTypeNames = holderTypeNames; LocalOnlySettings = localOnlySettings;
            BlockedBooleanSettings = blockedBooleanSettings;
            FixedValueLabels = fixedValueLabels; FixedValuePropertyNames = fixedValuePropertyNames;
            FixedValueRequired = fixedValueRequired;
        }

        public static bool TryParse(JsonNode node, out ModEntryData entry)
        {
            entry = null;
            if (node == null || node.Kind != JsonKind.Object) return false;
            ModEntryData result = new ModEntryData();
            string userModType, category;
            if (!ModCompatibilityDocumentParse.RequireText(node, "userModType", out userModType)) return false;
            if (!ModCompatibilityDocumentParse.RequireText(node, "category", out category)) return false;
            result.UserModType = userModType; result.Category = category;
            if (result.Category != "client-only" && result.Category != "synchronized" &&
                result.Category != "blocked" && result.Category != "dependency") return false;

            JsonNode field;
            if (node.TryGet("assemblyName", out field) && field.Kind == JsonKind.String) result.AssemblyName = field.Text;
            if (node.TryGet("supportedVersion", out field) && field.Kind == JsonKind.String) result.SupportedVersion = field.Text;
            if (node.TryGet("settingsTypeName", out field) && field.Kind == JsonKind.String) result.SettingsTypeName = field.Text;
            if (node.TryGet("holderFieldName", out field) && field.Kind == JsonKind.String && field.Text.Length > 0 && field.Text.Length <= 256)
                result.HolderFieldName = field.Text;

            string[] holders;
            if (ModCompatibilityDocumentParse.RequireTextArray(node, "holderTypeNames", 8, 256, out holders))
                result.HolderTypeNames = holders;

            string[] localOnly;
            if (ModCompatibilityDocumentParse.RequireTextArray(node, "localOnlySettings", 512, 256, out localOnly))
                result.LocalOnlySettings = localOnly;

            string[] blocked;
            if (ModCompatibilityDocumentParse.RequireTextArray(node, "blockedBooleanSettings", 512, 256, out blocked))
                result.BlockedBooleanSettings = blocked;

            // fixedValues: parallel arrays of property name / required value / shared violation label
            JsonNode fixedNames, fixedRequired, fixedLabels;
            bool hasNames = node.TryGet("fixedValuePropertyNames", out fixedNames) && fixedNames != null && fixedNames.Kind == JsonKind.Array;
            bool hasRequired = node.TryGet("fixedValueRequired", out fixedRequired) && fixedRequired != null && fixedRequired.Kind == JsonKind.Array;
            bool hasLabels = node.TryGet("fixedValueLabels", out fixedLabels) && fixedLabels != null && fixedLabels.Kind == JsonKind.Array;
            if (hasNames || hasRequired || hasLabels)
            {
                if (!hasNames || !hasRequired || !hasLabels) return false;
                if (fixedNames.Items.Count != fixedRequired.Items.Count ||
                    fixedNames.Items.Count != fixedLabels.Items.Count ||
                    fixedNames.Items.Count > 128) return false;
                var propertyNames = new string[fixedNames.Items.Count];
                var labels = new string[fixedNames.Items.Count];
                var required = new long[fixedNames.Items.Count];
                for (int i = 0; i < fixedNames.Items.Count; i++)
                {
                    JsonNode nameNode = fixedNames.Items[i], labelNode = fixedLabels.Items[i], valueNode = fixedRequired.Items[i];
                    if (nameNode == null || nameNode.Kind != JsonKind.String || nameNode.Text.Length == 0 || nameNode.Text.Length > 256) return false;
                    if (labelNode == null || labelNode.Kind != JsonKind.String || labelNode.Text.Length == 0 || labelNode.Text.Length > 256) return false;
                    if (valueNode == null || valueNode.Kind != JsonKind.Number || !valueNode.HasInteger) return false;
                    if (valueNode.Integer < 0 || valueNode.Integer > 1000) return false;
                    propertyNames[i] = nameNode.Text; labels[i] = labelNode.Text; required[i] = valueNode.Integer;
                }
                result.FixedValuePropertyNames = propertyNames;
                result.FixedValueLabels = labels;
                result.FixedValueRequired = required;
            }

            // synchronized entries must carry a settings type name
            if (result.Category == "synchronized" &&
                (result.SettingsTypeName == null || result.SettingsTypeName.Length == 0 || result.SettingsTypeName.Length > 256)) return false;
            entry = result; return true;
        }
    }

    public static class ModCompatibilityDocumentParse
    {
        public static bool RequireText(JsonNode node, string key, out string value)
        {
            value = null;
            JsonNode field;
            if (!node.TryGet(key, out field) || field == null || field.Kind != JsonKind.String ||
                field.Text.Length == 0 || field.Text.Length > 256) return false;
            value = field.Text; return true;
        }

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
        private static readonly object Gate = new object();
        private static ModCompatibilityDocument current = ModCompatibilityDocument.BuiltIn();
        private static bool initialized;

        public static ModCompatibilityDocument Default { get { lock (Gate) return current; } }

        /// <summary>
        /// WP-3.2: loads every compat/*.json in sorted order and lets the first document that
        /// parses strictly replace the built-in set. Any failure (missing directory, malformed
        /// file, unknown schema) leaves the built-in set in force - a bad file can never widen
        /// what is accepted. Call once at mod load, before any consumer reads Default; later
        /// calls are ignored so session state stays consistent.
        /// </summary>
        public static bool TryInitializeFromDirectory(string directory)
        {
            lock (Gate)
            {
                if (initialized) return false;
                initialized = true;
            }
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return false;
            List<string> files = new List<string>();
            foreach (string file in Directory.GetFiles(directory, "*.json"))
                files.Add(file);
            files.Sort(StringComparer.Ordinal);
            foreach (string file in files)
            {
                string json;
                try { json = File.ReadAllText(file); }
                catch (Exception) { continue; }
                ModCompatibilityDocument document;
                if (TryParseDocument(json, out document))
                {
                    lock (Gate) current = document;
                    return true;
                }
            }
            return false;
        }

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
