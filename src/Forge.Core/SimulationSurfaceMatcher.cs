using System;
using System.Collections.Generic;

namespace CsmForge.Core
{
    /// <summary>
    /// WP-3.1: conservative presentation-only detector for third-party mod assemblies. A mod
    /// whose visible type surface never names a simulation manager or tool is very likely
    /// UI/presentation-only and safe to classify as a client mod (no state sync needed).
    ///
    /// Deliberately one-sided: suffix matching is broad, so false positives ("MyUIManager"
    /// naming its own panel controller) fall back to exact-match classification — the safe
    /// direction. False negatives would desync, hence no allowlist of "looks like UI".
    /// Explicit declarations (ForgeCompatibilityApi / future manifests) always override this
    /// heuristic, and an unknown mod that does touch the simulation stays exact-match.
    /// </summary>
    public static class SimulationSurfaceMatcher
    {
        private static readonly string[] SimulationSuffixes = { "Manager", "Tool", "Simulation", "AI" };

        public static bool TouchesSimulationSurface(IEnumerable<string> typeSimpleNames)
        {
            if (typeSimpleNames == null) return true; // cannot prove absence -> fail closed
            foreach (string name in typeSimpleNames)
            {
                if (string.IsNullOrEmpty(name)) continue;
                for (int i = 0; i < SimulationSuffixes.Length; i++)
                    if (name.EndsWith(SimulationSuffixes[i], StringComparison.Ordinal)) return true;
            }
            return false;
        }
    }
}
