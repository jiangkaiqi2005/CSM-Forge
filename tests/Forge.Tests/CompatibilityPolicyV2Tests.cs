using System;
using CsmForge.Core;

namespace CsmForge.Tests
{
    public static class CompatibilityPolicyV2Tests
    {
        [Case]
        public static void DlcCanBeRequiredByHostWhileClientMayOwnExtras()
        {
            ComponentFingerprint hostDlc = Entry("dlc:expansion:3", 3);
            CompatibilityManifest host = Manifest(hostDlc);
            CompatibilityPolicyV2 policy = new CompatibilityPolicyV2(host, new[]
            {
                new CompatibilityRuleV2("dlc:", CompatibilityRequirementV2.Exact, true)
            });
            Assert.Equal(0, policy.Evaluate(Manifest(hostDlc, Entry("dlc:expansion:7", 7))).Length);
            Assert.True(Contains(policy.Evaluate(Manifest()), "missing:dlc:expansion:3"));
        }

        [Case]
        public static void ClientOnlyModsMayDifferOrExistOnOnePeer()
        {
            CompatibilityManifest host = Manifest(Entry("client-mod:loading-screen", 1));
            CompatibilityPolicyV2 policy = new CompatibilityPolicyV2(host, new[]
            {
                new CompatibilityRuleV2("client-mod:", CompatibilityRequirementV2.OptionalAny, true)
            });
            Assert.Equal(0, policy.Evaluate(Manifest()).Length);
            Assert.Equal(0, policy.Evaluate(Manifest(Entry("client-mod:loading-screen", 99), Entry("client-mod:fps-camera", 7))).Length);
        }

        [Case]
        public static void UnknownModsStillFailClosed()
        {
            CompatibilityManifest host = Manifest();
            CompatibilityPolicyV2 policy = new CompatibilityPolicyV2(host, new CompatibilityRuleV2[0]);
            Assert.True(Contains(policy.Evaluate(Manifest(Entry("mod:unknown", 1))), "unsupported-extra:mod:unknown"));
        }

        [Case]
        public static void RejectedHostComponentFailsBeforeJoin()
        {
            CompatibilityManifest host = Manifest(Entry("blocked-mod:tmpe", 1));
            CompatibilityPolicyV2 policy = new CompatibilityPolicyV2(host, new[]
            {
                new CompatibilityRuleV2("blocked-mod:", CompatibilityRequirementV2.Reject, false)
            });
            Assert.True(Contains(policy.HostErrors, "host-unsupported:blocked-mod:tmpe"));
            Assert.True(Contains(policy.Evaluate(Manifest()), "host-unsupported:blocked-mod:tmpe"));
        }

        [Case]
        public static void LongestPrefixRuleWins()
        {
            CompatibilityManifest host = Manifest(Entry("mod:safe:one", 1));
            CompatibilityPolicyV2 policy = new CompatibilityPolicyV2(host, new[]
            {
                new CompatibilityRuleV2("mod:", CompatibilityRequirementV2.Exact, false),
                new CompatibilityRuleV2("mod:safe:", CompatibilityRequirementV2.OptionalAny, true)
            });
            Assert.Equal(0, policy.Evaluate(Manifest()).Length);
        }

        private static CompatibilityManifest Manifest(params ComponentFingerprint[] entries)
        {
            return new CompatibilityManifest(Hash256.Compute(new byte[] { 1 }), Hash256.Compute(new byte[] { 2 }), entries);
        }

        private static ComponentFingerprint Entry(string id, byte marker)
        {
            return new ComponentFingerprint(id, Hash256.Compute(new byte[] { marker }), Hash256.Compute(new byte[] { 0 }));
        }

        private static bool Contains(string[] values, string expected)
        {
            for (int i = 0; i < values.Length; i++) if (values[i] == expected) return true;
            return false;
        }
    }
}
