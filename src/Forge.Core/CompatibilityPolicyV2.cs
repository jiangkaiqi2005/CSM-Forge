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
            if (string.IsNullOrEmpty(prefix) || prefix.Length > 128) throw new ArgumentException("Invalid compatibility prefix.", "prefix");
            foreach (char value in prefix)
                if (!((value >= 'a' && value <= 'z') || (value >= '0' && value <= '9') || value == '.' || value == ':' || value == '-' || value == '_'))
                    throw new ArgumentException("Compatibility prefixes use canonical lowercase ASCII.", "prefix");
            if (!Enum.IsDefined(typeof(CompatibilityRequirementV2), hostRequirement)) throw new ArgumentOutOfRangeException("hostRequirement");
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

        public CompatibilityPolicyV2(CompatibilityManifest hostManifest, IEnumerable<CompatibilityRuleV2> policyRules)
        {
            if (hostManifest == null || policyRules == null) throw new ArgumentNullException("hostManifest");
            host = hostManifest;
            List<CompatibilityRuleV2> collected = new List<CompatibilityRuleV2>();
            Dictionary<string, bool> seen = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (CompatibilityRuleV2 rule in policyRules)
            {
                if (rule == null || seen.ContainsKey(rule.Prefix)) throw new ArgumentException("Invalid or duplicate compatibility rule.", "policyRules");
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
            if (remote == null) throw new ArgumentNullException("remote");
            List<string> errors = new List<string>();
            if (!host.GameBuildHash.Equals(remote.GameBuildHash)) errors.Add("game-build-mismatch");
            if (!host.SchemaHash.Equals(remote.SchemaHash)) errors.Add("schema-mismatch");
            for (int i = 0; i < hostErrors.Length; i++) errors.Add(hostErrors[i]);

            Dictionary<string, ComponentFingerprint> expected = CompatibilityManifest.Collect(host.Entries);
            Dictionary<string, ComponentFingerprint> actual = CompatibilityManifest.Collect(remote.Entries);

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
