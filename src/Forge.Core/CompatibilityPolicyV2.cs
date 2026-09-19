using System;
using System.Collections.Generic;

namespace CsmForge.Core
{
    public enum CompatibilityRequirementV2
    {
        Exact = 0,
        OptionalExact = 1,
        OptionalAny = 2,
        HostOnly = 3,
        Reject = 4
    }

    public sealed class CompatibilityRuleV2
    {
        public string Prefix { get; private set; }
        public CompatibilityRequirementV2 HostRequirement { get; private set; }
        public bool AllowClientExtra { get; private set; }

        public CompatibilityRuleV2(string prefix, CompatibilityRequirementV2 hostRequirement, bool allowClientExtra)
        {
            Check.CanonicalId(prefix, 128, "prefix", "Invalid compatibility prefix."); // D2: shared canonical guard
            Check.OutOfRange(!Enum.IsDefined(typeof(CompatibilityRequirementV2), hostRequirement), "hostRequirement");
            Prefix = prefix;
            HostRequirement = hostRequirement;
            AllowClientExtra = allowClientExtra;
        }
    }

    /// <summary>
    /// Prefix-rule room policy. The default remains strict exact matching. Rules only relax
    /// components whose semantics have been explicitly audited (for example extra client DLC
    /// ownership or known client-only UI mods). Unknown components still fail closed.
    /// </summary>
    public sealed class CompatibilityPolicyV2
    {
        private readonly CompatibilityManifest host;
        private readonly CompatibilityRuleV2[] rules;
        private readonly string[] hostErrors;
        private readonly Dictionary<string, ComponentFingerprint> expected;

        public CompatibilityPolicyV2(CompatibilityManifest hostManifest, IEnumerable<CompatibilityRuleV2> policyRules)
        {
            Check.NotNull(hostManifest, "hostManifest"); Check.NotNull(policyRules, "policyRules"); // WP-2: per-argument reporting
            host = hostManifest;
            expected = host.Components; // D1-1: reuse the manifest's pre-validated dictionary; no per-Evaluate re-Collect
            List<CompatibilityRuleV2> collected = new List<CompatibilityRuleV2>();
            Dictionary<string, bool> seen = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (CompatibilityRuleV2 rule in policyRules)
            {
                Check.Condition(rule == null || seen.ContainsKey(rule.Prefix), "policyRules", "Invalid or duplicate compatibility rule.");
                seen.Add(rule.Prefix, true);
                collected.Add(rule);
            }
            collected.Sort(delegate(CompatibilityRuleV2 a, CompatibilityRuleV2 b)
            {
                int length = b.Prefix.Length.CompareTo(a.Prefix.Length);
                return length != 0 ? length : StringComparer.Ordinal.Compare(a.Prefix, b.Prefix);
            });
            rules = collected.ToArray();

            List<string> errors = new List<string>();
            foreach (ComponentFingerprint entry in host.Entries)
                if (Resolve(entry.Id).HostRequirement == CompatibilityRequirementV2.Reject)
                    errors.Add("host-unsupported:" + entry.Id);
            errors.Sort(StringComparer.Ordinal);
            hostErrors = errors.ToArray();
        }

        public string[] HostErrors { get { return (string[])hostErrors.Clone(); } }

        public string[] Evaluate(CompatibilityManifest remote)
        {
            Check.NotNull(remote, "remote");
            List<string> errors = new List<string>();
            if (!host.GameBuildHash.Equals(remote.GameBuildHash)) errors.Add("game-build-mismatch");
            if (!host.SchemaHash.Equals(remote.SchemaHash)) errors.Add("schema-mismatch");
            for (int i = 0; i < hostErrors.Length; i++) errors.Add(hostErrors[i]);

            Dictionary<string, ComponentFingerprint> expected = this.expected;             // D1-1: cached at construction
            Dictionary<string, ComponentFingerprint> actual = remote.Components;           // D1-1: pre-validated by the manifest

            foreach (KeyValuePair<string, ComponentFingerprint> pair in expected)
            {
                CompatibilityRuleV2 rule = Resolve(pair.Key);
                ComponentFingerprint value;
                bool present = actual.TryGetValue(pair.Key, out value);
                switch (rule.HostRequirement)
                {
                    case CompatibilityRequirementV2.Exact:
                        if (!present) errors.Add("missing:" + pair.Key);
                        else if (!pair.Value.Matches(value)) errors.Add("fingerprint-mismatch:" + pair.Key);
                        break;
                    case CompatibilityRequirementV2.OptionalExact:
                        if (present && !pair.Value.Matches(value)) errors.Add("fingerprint-mismatch:" + pair.Key);
                        break;
                    case CompatibilityRequirementV2.OptionalAny:
                        break;
                    case CompatibilityRequirementV2.HostOnly:
                        if (present) errors.Add("client-forbidden:" + pair.Key);
                        break;
                    case CompatibilityRequirementV2.Reject:
                        break;
                }
            }

            foreach (KeyValuePair<string, ComponentFingerprint> pair in actual)
            {
                if (expected.ContainsKey(pair.Key)) continue;
                CompatibilityRuleV2 rule = Resolve(pair.Key);
                if (!rule.AllowClientExtra) errors.Add("unsupported-extra:" + pair.Key);
            }

            errors.Sort(StringComparer.Ordinal);
            return errors.ToArray();
        }

        private CompatibilityRuleV2 Resolve(string id)
        {
            for (int i = 0; i < rules.Length; i++)
                if (id.StartsWith(rules[i].Prefix, StringComparison.Ordinal)) return rules[i];
            return StrictDefault;
        }

        private static readonly CompatibilityRuleV2 StrictDefault =
            new CompatibilityRuleV2("_", CompatibilityRequirementV2.Exact, false);
    }
}
