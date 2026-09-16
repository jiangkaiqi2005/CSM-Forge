using System;

namespace CsmForge.Runtime.Cities1
{
    public sealed class RuntimeScope : IDisposable
    {
        private readonly Action release;
        private bool disposed;

        internal RuntimeScope(Action release)
        {
            if (release == null) throw new ArgumentNullException("release");
            this.release = release;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            release();
        }
    }

    /// <summary>
    /// Per-game-thread capture/apply ownership. Inspired by the CSM-CQU cleanup fixes but
    /// deliberately independent from the legacy IgnoreHelper/Command pipeline.
    /// </summary>
    public static class RuntimeScopeGuard
    {
        [ThreadStatic] private static int applyDepth;
        [ThreadStatic] private static int captureDepth;
        [ThreadStatic] private static ushort activeDomain;
        [ThreadStatic] private static uint activeGeneration;

        public static bool IsApplying { get { return applyDepth > 0; } }
        public static bool IsCapturing { get { return captureDepth > 0; } }
        public static ushort ActiveDomain { get { return activeDomain; } }
        public static uint ActiveGeneration { get { return activeGeneration; } }

        public static RuntimeScope EnterApply(LoadIdentity load, ushort domainId)
        {
            if (!load.IsValid || domainId == 0) throw new ArgumentException("Apply scope identity is incomplete.");
            if (applyDepth == 0)
            {
                activeDomain = domainId;
                activeGeneration = load.Generation;
            }
            else if (activeDomain != domainId || activeGeneration != load.Generation)
                throw new InvalidOperationException("Nested apply scope crossed a domain or load generation boundary.");
            applyDepth++;
            return new RuntimeScope(delegate
            {
                if (applyDepth <= 0) throw new InvalidOperationException("Apply scope underflow.");
                applyDepth--;
                if (applyDepth == 0)
                {
                    activeDomain = 0;
                    activeGeneration = 0;
                }
            });
        }

        public static RuntimeScope EnterCapture(LoadIdentity load)
        {
            if (!load.IsValid) throw new ArgumentException("Capture scope identity is incomplete.");
            if (captureDepth == 0) activeGeneration = load.Generation;
            else if (activeGeneration != load.Generation)
                throw new InvalidOperationException("Nested capture scope crossed a load generation boundary.");
            captureDepth++;
            return new RuntimeScope(delegate
            {
                if (captureDepth <= 0) throw new InvalidOperationException("Capture scope underflow.");
                captureDepth--;
                if (captureDepth == 0 && applyDepth == 0) activeGeneration = 0;
            });
        }

        public static void EndOfSimulationTick(CitiesLifecycleCoordinator lifecycle, RuntimeEventLog events)
        {
            if (lifecycle == null || events == null) throw new ArgumentNullException("lifecycle");
            if (applyDepth == 0 && captureDepth == 0) return;
            uint generation = activeGeneration;
            applyDepth = 0;
            captureDepth = 0;
            activeDomain = 0;
            activeGeneration = 0;
            events.Record(RuntimeEventCode.ScopeLeak, generation, "runtime scope leaked across simulation tick");
            lifecycle.Fence("runtime scope leak");
        }

        public static Exception Finish(Exception original, Action cleanup)
        {
            if (cleanup == null) return original;
            try
            {
                cleanup();
                return original;
            }
            catch (Exception cleanupFailure)
            {
                if (original == null) return cleanupFailure;
                return new Exception("Runtime cleanup also failed: " + cleanupFailure, original);
            }
        }
    }
}
