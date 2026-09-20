using System;
using CsmForge.Core;

namespace CsmForge.Tests
{
    /// <summary>
    /// WP-P2 regression: the value-equivalence helpers the per-tick polls now use to avoid
    /// hashing unchanged state. If any of these compare too loosely the host would skip a real
    /// change and never publish it, so each one is pinned here.
    /// </summary>
    public static class StateEquivalenceTests
    {
        [Case] public static void EconomyCashEquivalenceIgnoresNothingItShouldNot()
        {
            EconomyCashStateV2 a = new EconomyCashStateV2(1234);
            Assert.True(a.Equivalent(new EconomyCashStateV2(1234)));
            Assert.True(!a.Equivalent(new EconomyCashStateV2(1235)));
            Assert.True(!a.Equivalent(null));
            Assert.True(a.Equivalent(new EconomyCashStateV2(1234)));  // repeated compare is stable
        }

        [Case] public static void EconomyControlEquivalenceComparesEveryByte()
        {
            EconomyControlStateV2 a = new EconomyControlStateV2(new byte[] { 1, 2, 3, 4 });
            Assert.True(a.Equivalent(new EconomyControlStateV2(new byte[] { 1, 2, 3, 4 })));
            Assert.True(!a.Equivalent(new EconomyControlStateV2(new byte[] { 1, 2, 3, 5 })));  // last byte
            Assert.True(!a.Equivalent(new EconomyControlStateV2(new byte[] { 9, 2, 3, 4 })));  // first byte
            Assert.True(!a.Equivalent(new EconomyControlStateV2(new byte[] { 1, 2, 3 })));     // length differs
            Assert.True(!a.Equivalent(null));
        }

        [Case] public static void WeatherTargetEquivalenceCoversAllSixTargets()
        {
            WeatherStateV2 baseline = Weather(baseline: true);
            Assert.True(baseline.EquivalentTargets(Weather(baseline: true)));
            // each target field independently must break equivalence
            for (int field = 0; field < 6; field++)
                Assert.True(!baseline.EquivalentTargets(Weather(baseline: true, mutateTarget: field)));
            Assert.True(!baseline.EquivalentTargets(null));
        }

        private static WeatherStateV2 Weather(bool baseline, int mutateTarget = -1)
        {
            float t0 = baseline ? 1f : 1f, t1 = 2f, t2 = 3f, t3 = 4f, t4 = 5f, t5 = 6f;
            switch (mutateTarget)
            {
                case 0: t0 = 99f; break;
                case 1: t1 = 99f; break;
                case 2: t2 = 99f; break;
                case 3: t3 = 99f; break;
                case 4: t4 = 99f; break;
                case 5: t5 = 99f; break;
            }
            return new WeatherStateV2(0f, t0, 0f, t1, 0f, t2, 0f, t3, 0f, t4, 0f, t5);
        }
    }
}
