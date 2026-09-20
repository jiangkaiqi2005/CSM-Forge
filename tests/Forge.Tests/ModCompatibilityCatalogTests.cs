using System;
using CsmForge.Core;

namespace CsmForge.Tests
{
    /// <summary>
    /// WP-3.2 step 1: the catalog is the single audited source for hardcoded mod-compatibility
    /// facts. These tests pin the entries consumers rely on, so an accidental edit here is a
    /// reviewable compatibility decision rather than a silent behavior change.
    /// </summary>
    public static class ModCompatibilityCatalogTests
    {
        [Case] public static void DependencyAndBlockedEntriesArePinned()
        {
            Assert.Equal("CitiesHarmony.Mod", ModCompatibilityCatalog.Default.DependencyModType);
            Assert.Equal("TrafficManager.Lifecycle.TrafficManagerMod", ModCompatibilityCatalog.Default.BlockedModType);
        }

        [Case] public static void ClientOnlyAllowlistIsPinned()
        {
            Assert.Equal(9, ModCompatibilityCatalog.Default.ClientOnlyModTypes.Length);
            Assert.True(Array.IndexOf(ModCompatibilityCatalog.Default.ClientOnlyModTypes, "LoadingScreenMod.Mod") >= 0);
            Assert.True(Array.IndexOf(ModCompatibilityCatalog.Default.ClientOnlyModTypes, "FPSCamera.Mod") >= 0);
            Assert.True(Array.IndexOf(ModCompatibilityCatalog.Default.ClientOnlyModTypes, "PrecisionEngineering.Mod") >= 0);
        }

        [Case] public static void SynchronizedModTierIsPinned()
        {
            string[] known = ModCompatibilityCatalog.Default.KnownSynchronizedModTypes;
            Assert.Equal(5, known.Length);
            foreach (string type in known)
            {
                Assert.True(type != null && type.Length > 0);
            }
            Assert.Equal("GameAnarchy.Mod", ModCompatibilityCatalog.Default.GameAnarchyUserModType);
            Assert.Equal("EightyOne2.Mod", ModCompatibilityCatalog.Default.EightyOne2UserModType);
        }

        [Case] public static void GameAnarchySurfaceIsPinned()
        {
            Assert.Equal("GameAnarchy", ModCompatibilityCatalog.Default.GameAnarchy.AssemblyName);
            Assert.Equal("1.3.1.0", ModCompatibilityCatalog.Default.GameAnarchy.SupportedVersion);
            Assert.Equal("GameAnarchy.ModSettings.ModSetting", ModCompatibilityCatalog.Default.GameAnarchy.SettingsTypeName);
            Assert.Equal(2, ModCompatibilityCatalog.Default.GameAnarchy.HolderTypeNames.Length);
            Assert.Equal(5, ModCompatibilityCatalog.Default.GameAnarchy.RequiredTypeNames.Length);
            Assert.Equal(7, ModCompatibilityCatalog.Default.GameAnarchy.LocalOnlySettings.Length);
            // The aggregated rejection message the host UI shows is built from this list.
            Assert.True(Array.IndexOf(ModCompatibilityCatalog.Default.GameAnarchy.UnsupportedBooleanSettings, "RemoveNoisePollution") >= 0);
            Assert.True(Array.IndexOf(ModCompatibilityCatalog.Default.GameAnarchy.UnsupportedBooleanSettings, "RemoveAirportBuildingFire") >= 0);
            // 实机加载检查校正：黑名单实际 29 项（文档曾误记 28）
            Assert.Equal(29, ModCompatibilityCatalog.Default.GameAnarchy.UnsupportedBooleanSettings.Length);
            Assert.Equal(100L, ModCompatibilityCatalog.Default.GameAnarchy.FixedOilDepletionRate);
            Assert.Equal(100L, ModCompatibilityCatalog.Default.GameAnarchy.FixedOreDepletionRate);
            Assert.Equal(0L, ModCompatibilityCatalog.Default.GameAnarchy.FixedSpreadFireProbability);
        }

        [Case] public static void InfiniteGoodsSurfaceIsPinned()
        {
            string[] unsupported = ModCompatibilityCatalog.Default.InfiniteGoods.UnsupportedServicePointSettings;
            Assert.Equal(10, unsupported.Length);
            Assert.True(Array.IndexOf(unsupported, "PedestrianServicePointGoods") >= 0);
        }

        private const string ValidJson = @"{
  ""dependencyModType"": ""CitiesHarmony.Mod"",
  ""blockedModType"": ""TrafficManager.Lifecycle.TrafficManagerMod"",
  ""clientOnlyModTypes"": [""LoadingScreenMod.Mod"", ""FPSCamera.Mod""],
  ""demandControllerUserModType"": ""DemandController.DemandController"",
  ""gameAnarchyUserModType"": ""GameAnarchy.Mod"",
  ""infiniteGoodsUserModType"": ""InfiniteGoodsMod.ModIdentity"",
  ""eightyOne2UserModType"": ""EightyOne2.Mod"",
  ""networkMultitoolUserModType"": ""NetworkMultitool.Mod"",
  ""gameAnarchy"": {
    ""assemblyName"": ""GameAnarchy"",
    ""supportedVersion"": ""1.3.1.0"",
    ""settingsTypeName"": ""GameAnarchy.ModSettings.ModSetting"",
    ""holderTypeNames"": [""GameAnarchy.Patches.BuildingAIPatch"", ""GameAnarchy.Patches.BulldozeToolPatch""],
    ""requiredTypeNames"": [""GameAnarchy.Managers.ModEconomyManager""],
    ""localOnlySettings"": [""SkipIntroEnabled""],
    ""unsupportedBooleanSettings"": [""RemoveNoisePollution"", ""RemoveGarbage""],
    ""fixedOilDepletionRate"": 100,
    ""fixedOreDepletionRate"": 100,
    ""fixedSpreadFireProbability"": 0
  },
  ""mods"": [
    { ""userModType"": ""CitiesHarmony.Mod"", ""category"": ""dependency"" },
    { ""userModType"": ""GameAnarchy.Mod"", ""category"": ""synchronized"", ""settingsTypeName"": ""GameAnarchy.ModSettings.ModSetting"",
      ""holderTypeNames"": [""GameAnarchy.Patches.BuildingAIPatch""], ""localOnlySettings"": [], ""blockedBooleanSettings"": [] }
  ],
  ""infiniteGoods"": { ""unsupportedServicePointSettings"": [""PedestrianServicePointGoods""] }
}";

        [Case] public static void ValidExternalDocumentParsesAndReplacesWholesale()
        {
            ModCompatibilityDocument document;
            Assert.True(ModCompatibilityCatalog.TryParseDocument(ValidJson, out document));
            Assert.Equal("CitiesHarmony.Mod", document.DependencyModType);
            Assert.Equal(2, document.ClientOnlyModTypes.Length);
            Assert.Equal("GameAnarchy.ModSettings.ModSetting", document.GameAnarchy.SettingsTypeName);
            Assert.True(Array.IndexOf(document.GameAnarchy.UnsupportedBooleanSettings, "RemoveNoisePollution") >= 0);
            Assert.Equal(1, document.InfiniteGoods.UnsupportedServicePointSettings.Length);
            Assert.Equal(5, document.KnownSynchronizedModTypes.Length);
            ModEntryData entry;
            Assert.True(document.TryGetModEntry("GameAnarchy.Mod", out entry));
            Assert.Equal("synchronized", entry.Category);
            Assert.Equal("GameAnarchy.Patches.BuildingAIPatch", entry.HolderTypeNames[0]);
            Assert.True(document.TryGetModEntry("CitiesHarmony.Mod", out entry));
            Assert.Equal("dependency", entry.Category);
            Assert.True(!document.TryGetModEntry("Some.Unknown.Mod", out entry));
        }

        [Case] public static void MalformedJsonFailsClosed()
        {
            ModCompatibilityDocument document;
            Assert.True(!ModCompatibilityCatalog.TryParseDocument("{ not json", out document));
            Assert.True(document == null);
        }

        [Case] public static void MissingRequiredFieldFailsClosed()
        {
            string json = "{\"dependencyModType\": \"CitiesHarmony.Mod\"}";
            ModCompatibilityDocument document;
            Assert.True(!ModCompatibilityCatalog.TryParseDocument(json, out document));
            Assert.True(document == null);
        }

        [Case] public static void DefaultBuiltInDocumentMatchesThePinnedContract()
        {
            // the built-in document is what every consumer reads when no external file loads
            Assert.Equal(9, ModCompatibilityCatalog.Default.ClientOnlyModTypes.Length);
            Assert.True(Array.IndexOf(ModCompatibilityCatalog.Default.GameAnarchy.UnsupportedBooleanSettings, "RemoveNoisePollution") >= 0);
            Assert.Equal("GameAnarchy.ModSettings.ModSetting", ModCompatibilityCatalog.Default.GameAnarchy.SettingsTypeName);
        }


        [Case] public static void GenericSynchronizedEntryRequiresSettingsTypeName()
        {
            string json = "{\"dependencyModType\": \"CitiesHarmony.Mod\", \"blockedModType\": \"x\", " +
                "\"demandControllerUserModType\": \"a\", \"gameAnarchyUserModType\": \"b\", " +
                "\"infiniteGoodsUserModType\": \"c\", \"eightyOne2UserModType\": \"d\", " +
                "\"networkMultitoolUserModType\": \"e\", " +
                "\"gameAnarchy\": { \"assemblyName\": \"GameAnarchy\", \"supportedVersion\": \"1.3.1.0\", " +
                "\"settingsTypeName\": \"s\", \"holderTypeNames\": [\"h\"], \"requiredTypeNames\": [\"r\"], " +
                "\"localOnlySettings\": [\"l\"], \"unsupportedBooleanSettings\": [\"u\"], " +
                "\"fixedOilDepletionRate\": 100, \"fixedOreDepletionRate\": 100, \"fixedSpreadFireProbability\": 0 }, " +
                "\"infiniteGoods\": { \"unsupportedServicePointSettings\": [] }, " +
                "\"mods\": [ { \"userModType\": \"My.Generic.Mod\", \"category\": \"synchronized\" } ] }";
            ModCompatibilityDocument document;
            Assert.True(!ModCompatibilityCatalog.TryParseDocument(json, out document)); // no settingsTypeName
        }

        [Case] public static void InvalidCategoryFailsClosed()
        {
            string json = "{\"dependencyModType\": \"CitiesHarmony.Mod\", \"blockedModType\": \"x\", " +
                "\"demandControllerUserModType\": \"a\", \"gameAnarchyUserModType\": \"b\", " +
                "\"infiniteGoodsUserModType\": \"c\", \"eightyOne2UserModType\": \"d\", " +
                "\"networkMultitoolUserModType\": \"e\", " +
                "\"gameAnarchy\": { \"assemblyName\": \"GameAnarchy\", \"supportedVersion\": \"1.3.1.0\", " +
                "\"settingsTypeName\": \"s\", \"holderTypeNames\": [\"h\"], \"requiredTypeNames\": [\"r\"], " +
                "\"localOnlySettings\": [\"l\"], \"unsupportedBooleanSettings\": [\"u\"], " +
                "\"fixedOilDepletionRate\": 100, \"fixedOreDepletionRate\": 100, \"fixedSpreadFireProbability\": 0 }, " +
                "\"infiniteGoods\": { \"unsupportedServicePointSettings\": [] }, " +
                "\"mods\": [ { \"userModType\": \"My.Generic.Mod\", \"category\": \"magical\" } ] }";
            ModCompatibilityDocument document;
            Assert.True(!ModCompatibilityCatalog.TryParseDocument(json, out document));
            Assert.True(document == null);
        }
    }
}
