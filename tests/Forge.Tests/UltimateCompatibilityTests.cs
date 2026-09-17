using CsmForge.Core;

namespace CsmForge.Tests
{
    public static class UltimateCompatibilityTests
    {
        [Case]
        public static void LegacyRoomPolicyAllowsExtraClientDlcAndAssets()
        {
            ComponentFingerprint hostDlc = Entry("dlc:expansion:2", 2);
            CompatibilityPolicy policy = new CompatibilityPolicy(Build(), Schema(), new[] { hostDlc }, new ComponentFingerprint[0]);
            CompatibilityManifest client = new CompatibilityManifest(Build(), Schema(), new[]
            {
                hostDlc, Entry("dlc:expansion:9", 9), Entry("asset:123:client-decoration", 4)
            });
            Assert.Equal(0, policy.Evaluate(client).Length);
        }

        [Case]
        public static void LegacyRoomPolicyAllowsKnownClientOnlyModDifferences()
        {
            CompatibilityPolicy policy = new CompatibilityPolicy(Build(), Schema(),
                new[] { Entry("client-mod:1:loading", 1) }, new ComponentFingerprint[0]);
            CompatibilityManifest client = new CompatibilityManifest(Build(), Schema(),
                new[] { Entry("client-mod:1:loading", 99), Entry("client-mod:2:fps", 8) });
            Assert.Equal(0, policy.Evaluate(client).Length);
        }

        [Case]
        public static void LegacyRoomPolicyRejectsBlockedHostMod()
        {
            CompatibilityPolicy policy = new CompatibilityPolicy(Build(), Schema(),
                new[] { Entry("blocked-mod:1637663252:trafficmanager", 1) }, new ComponentFingerprint[0]);
            string[] errors = policy.Evaluate(new CompatibilityManifest(Build(), Schema(), new ComponentFingerprint[0]));
            Assert.True(Contains(errors, "host-unsupported:blocked-mod:1637663252:trafficmanager"));
        }

        private static ComponentFingerprint Entry(string id, byte marker)
        {
            return new ComponentFingerprint(id, Hash256.Compute(new byte[] { marker }), Hash256.Compute(new byte[] { 0 }));
        }
        private static Hash256 Build() { return Hash256.Compute(new byte[] { 1 }); }
        private static Hash256 Schema() { return Hash256.Compute(new byte[] { 2 }); }
        private static bool Contains(string[] values, string expected)
        {
            for (int i = 0; i < values.Length; i++) if (values[i] == expected) return true;
            return false;
        }
    }
}
