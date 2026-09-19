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
            Assert.Equal("CitiesHarmony.Mod", ModCompatibilityCatalog.DependencyModType);
            Assert.Equal("TrafficManager.Lifecycle.TrafficManagerMod", ModCompatibilityCatalog.BlockedModType);
        }

        [Case] public static void ClientOnlyAllowlistIsPinned()
        {
            Assert.Equal(9, ModCompatibilityCatalog.ClientOnlyModTypes.Length);
            Assert.True(Array.IndexOf(ModCompatibilityCatalog.ClientOnlyModTypes, "LoadingScreenMod.Mod") >= 0);
            Assert.True(Array.IndexOf(ModCompatibilityCatalog.ClientOnlyModTypes, "FPSCamera.Mod") >= 0);
            Assert.True(Array.IndexOf(ModCompatibilityCatalog.ClientOnlyModTypes, "PrecisionEngineering.Mod") >= 0);
        }

        [Case] public static void SynchronizedModTierIsPinned()
        {
            string[] known = ModCompatibilityCatalog.KnownSynchronizedModTypes;
            Assert.Equal(5, known.Length);
            foreach (string type in known)
            {
                Assert.True(type != null && type.Length > 0);
            }
            Assert.Equal("GameAnarchy.Mod", ModCompatibilityCatalog.GameAnarchyUserModType);
            Assert.Equal("EightyOne2.Mod", ModCompatibilityCatalog.EightyOne2UserModType);
        }

        [Case] public static void GameAnarchySurfaceIsPinned()
        {
            Assert.Equal("GameAnarchy", ModCompatibilityCatalog.GameAnarchy.AssemblyName);
            Assert.Equal("1.3.1.0", ModCompatibilityCatalog.GameAnarchy.SupportedVersion);
            Assert.Equal("GameAnarchy.ModSettings.ModSetting", ModCompatibilityCatalog.GameAnarchy.SettingsTypeName);
            Assert.Equal(2, ModCompatibilityCatalog.GameAnarchy.HolderTypeNames.Length);
            Assert.Equal(5, ModCompatibilityCatalog.GameAnarchy.RequiredTypeNames.Length);
            Assert.Equal(7, ModCompatibilityCatalog.GameAnarchy.LocalOnlySettings.Length);
            // The aggregated rejection message the host UI shows is built from this list.
            Assert.True(Array.IndexOf(ModCompatibilityCatalog.GameAnarchy.UnsupportedBooleanSettings, "RemoveNoisePollution") >= 0);
            Assert.True(Array.IndexOf(ModCompatibilityCatalog.GameAnarchy.UnsupportedBooleanSettings, "RemoveAirportBuildingFire") >= 0);
            Assert.Equal(100L, ModCompatibilityCatalog.GameAnarchy.FixedOilDepletionRate);
            Assert.Equal(100L, ModCompatibilityCatalog.GameAnarchy.FixedOreDepletionRate);
            Assert.Equal(0L, ModCompatibilityCatalog.GameAnarchy.FixedSpreadFireProbability);
        }

        [Case] public static void InfiniteGoodsSurfaceIsPinned()
        {
            string[] unsupported = ModCompatibilityCatalog.InfiniteGoods.UnsupportedServicePointSettings;
            Assert.Equal(10, unsupported.Length);
            Assert.True(Array.IndexOf(unsupported, "PedestrianServicePointGoods") >= 0);
        }
    }
}
