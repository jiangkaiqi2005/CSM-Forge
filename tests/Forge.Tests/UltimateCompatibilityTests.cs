using System;
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
        public static void LegacyRoomPolicyRejectsBlockedHostModAtRoomCreation()
        {
            Assert.Throws<InvalidOperationException>(delegate
            {
                new CompatibilityPolicy(Build(), Schema(),
                    new[] { Entry("blocked-mod:1637663252:trafficmanager", 1) }, new ComponentFingerprint[0]);
            });
        }

        [Case]
        public static void ForgeSynchronizedModRemainsStrictExactMatch()
        {
            ComponentFingerprint hostSync = Entry("sync-mod:42:example", 7);
            CompatibilityPolicy policy = new CompatibilityPolicy(Build(), Schema(),
                new[] { hostSync }, new ComponentFingerprint[0]);

            string[] mismatch = policy.Evaluate(new CompatibilityManifest(Build(), Schema(),
                new[] { Entry("sync-mod:42:example", 8) }));
            Assert.True(Contains(mismatch, "fingerprint-mismatch:sync-mod:42:example"));

            string[] missing = policy.Evaluate(new CompatibilityManifest(Build(), Schema(), new ComponentFingerprint[0]));
            Assert.True(Contains(missing, "missing:sync-mod:42:example"));
        }

        [Case]
        public static void ExtraClientSynchronizedModIsRejected()
        {
            CompatibilityPolicy policy = new CompatibilityPolicy(Build(), Schema(),
                new ComponentFingerprint[0], new ComponentFingerprint[0]);
            string[] errors = policy.Evaluate(new CompatibilityManifest(Build(), Schema(),
                new[] { Entry("sync-mod:99:client-extra", 1) }));
            Assert.True(Contains(errors, "unsupported:sync-mod:99:client-extra"));
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
