using System;

namespace CsmForge.Core
{
    /// <summary>Classification for defensive checks, feeding evidence-driven gate pruning (D4).</summary>
    public enum GateCategory
    {
        /// <summary>Remote-input validation at a trust boundary (codec, manifest, snapshot chunk).</summary>
        Wire = 0,
        /// <summary>State-root / digest invariants that prove convergence.</summary>
        Invariant = 1,
        /// <summary>Session/role/phase transition guards whose failure degrades a peer.</summary>
        Lifecycle = 2,
        /// <summary>Owner-thread affinity assertions.</summary>
        Thread = 3,
        /// <summary>Constructor-time argument validation.</summary>
        Contract = 4
    }

    /// <summary>
    /// Saturating per-category counters for gate firings. Diagnosis-only: recording a category
    /// never changes control flow; the E3/E4 runbook reads the distribution to decide which
    /// gates are prunable, which need downgraded responses and which are load-bearing.
    /// </summary>
    public static class GateCounter
    {
        private static readonly object Gate = new object();
        private static readonly long[] Counts = new long[5];

        public static void Record(GateCategory category)
        {
            int index = (int)category;
            if (index < 0 || index >= Counts.Length) return;
            lock (Gate) Counts[index]++;
        }

        public static long Count(GateCategory category)
        {
            int index = (int)category;
            if (index < 0 || index >= Counts.Length) return 0;
            lock (Gate) return Counts[index];
        }

        public static void Reset()
        {
            lock (Gate) Array.Clear(Counts, 0, Counts.Length);
        }
    }
}
