using System;
using System.IO;
using System.Reflection;
using CsmForge.Core;

namespace CsmForge.Tests
{
    /// <summary>WP-3.2 fixture: a mod-like settings surface.</summary>
    public sealed class SurfaceFixture
    {
        public bool RemoveNoisePollution { get; set; }
        public bool RemoveGarbage { get; set; }
        public int OilDepletionRate { get; set; }
        public int OreDepletionRate { get; set; }
        public int BuildingSpreadFireProbability { get; set; }
        public int CurrentUnlockMode { get; set; }
        public long BigValue { get; set; }
        public string UiLabel { get; set; }   // unsupported type: excluded from the surface
        public bool SkipIntro { get; set; }   // local-only: excluded from the surface
    }

    /// <summary>
    /// WP-3.2 regression: the generic settings-surface codec preserves the known-mod bridge
    /// contract - canonical surface selection, memoized capture with schema fingerprint,
    /// strict apply (schema/count/trailing), and aggregated supported-configuration messages.
    /// </summary>
    public static class SettingsSurfaceCodecTests
    {
        private const uint Magic = 0x31414746u;

        private static SettingsSurfaceCodec Codec()
        {
            return new SettingsSurfaceCodec(
                typeof(SurfaceFixture).GetProperties(BindingFlags.Instance | BindingFlags.Public),
                new[] { "SkipIntro" },
                new[] { "RemoveNoisePollution", "RemoveGarbage" },
                new[]
                {
                    new FixedValueRule("OilDepletionRate", 100, "OilDepletionRate/OreDepletionRate (both rates must be 100)"),
                    new FixedValueRule("OreDepletionRate", 100, "OilDepletionRate/OreDepletionRate (both rates must be 100)"),
                    new FixedValueRule("BuildingSpreadFireProbability", 0, "BuildingSpreadFireProbability/TreeSpreadFireProbability (fire-spread overrides)"),
                    new FixedValueRule("CurrentUnlockMode", 0, "CurrentUnlockMode/CurrentMilestoneLevel (milestone/unlock overrides)")
                },
                Magic, 4096, "Test Surface");
        }

        private static SurfaceFixture CompatibleInstance()
        {
            // fixture defaults satisfy the fixed-value rules (like a conforming mod config)
            return new SurfaceFixture { OilDepletionRate = 100, OreDepletionRate = 100, BigValue = 42 };
        }

        [Case] public static void SurfaceExcludesUnsupportedAndLocalOnlyProperties()
        {
            // 9 properties on the fixture; string and local-only are excluded -> 7 remain
            Assert.Equal(7, Codec().PropertyCount);
            Assert.True(Codec().HasProperty("BigValue"));
            Assert.True(!Codec().HasProperty("UiLabel"));
            Assert.True(!Codec().HasProperty("SkipIntro"));
        }

        [Case] public static void CaptureApplyRoundTripPreservesValues()
        {
            SettingsSurfaceCodec codec = Codec();
            SurfaceFixture first = CompatibleInstance();
            first.OilDepletionRate = 100; first.OreDepletionRate = 100; first.BigValue = 42;
            byte[] state = codec.Capture(first);

            SurfaceFixture second = CompatibleInstance();
            second.OilDepletionRate = 7;
            long[] values = codec.ValidateState(state);
            codec.WriteValues(second, values);
            Assert.Equal(100, second.OilDepletionRate);
            Assert.Equal(42L, second.BigValue);
            Assert.True(second.RemoveNoisePollution == false);
        }

        [Case] public static void BlockedOptionsAggregateIntoOneMessage()
        {
            SettingsSurfaceCodec codec = Codec();
            SurfaceFixture instance = CompatibleInstance();
            instance.RemoveNoisePollution = true;
            instance.RemoveGarbage = true;
            try
            {
                codec.ValidateSupported(instance);
                throw new Exception("Expected InvalidOperationException.");
            }
            catch (InvalidOperationException error)
            {
                Assert.True(error.Message.IndexOf("RemoveNoisePollution", StringComparison.Ordinal) >= 0);
                Assert.True(error.Message.IndexOf("RemoveGarbage", StringComparison.Ordinal) >= 0);
                Assert.True(error.Message.StartsWith("Test Surface options are not supported in Forge multiplayer:", StringComparison.Ordinal));
            }
        }

        [Case] public static void FixedValueViolationsShareTheirLabel()
        {
            SettingsSurfaceCodec codec = Codec();
            SurfaceFixture instance = CompatibleInstance();
            instance.OilDepletionRate = 50;
            instance.OreDepletionRate = 60;
            instance.CurrentUnlockMode = 1;
            try
            {
                codec.ValidateSupported(instance);
                throw new Exception("Expected InvalidOperationException.");
            }
            catch (InvalidOperationException error)
            {
                Assert.True(error.Message.IndexOf("both rates must be 100", StringComparison.Ordinal) >= 0);
                Assert.True(error.Message.IndexOf("milestone/unlock overrides", StringComparison.Ordinal) >= 0);
            }
        }

        [Case] public static void ApplyRejectsSchemaMismatchAndTrailingBytes()
        {
            SettingsSurfaceCodec codec = Codec();
            byte[] state = codec.Capture(CompatibleInstance());

            SettingsSurfaceCodec other = new SettingsSurfaceCodec(
                typeof(SurfaceFixture).GetProperties(BindingFlags.Instance | BindingFlags.Public),
                new string[0], new string[0], new FixedValueRule[0], 0x31414747u, 4096, "Other Surface");
            Assert.Throws<InvalidDataException>(delegate { other.ValidateState(state); });

            byte[] padded = new byte[state.Length + 1];
            Array.Copy(state, padded, state.Length);
            Assert.Throws<InvalidDataException>(delegate { codec.ValidateState(padded); });
        }

        [Case] public static void LocalOnlyDifferenceIsInvisibleToTheSurface()
        {
            SettingsSurfaceCodec codec = Codec();
            SurfaceFixture one = CompatibleInstance();
            SurfaceFixture two = CompatibleInstance();
            two.SkipIntro = true; // local-only: captured bytes must not depend on it
            byte[] first = codec.Capture(one);
            byte[] second = codec.Capture(two);
            Assert.True(first.Length == second.Length);
            for (int i = 0; i < first.Length; i++) Assert.True(first[i] == second[i]);
        }
    }
}
