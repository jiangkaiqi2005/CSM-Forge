using System;

namespace CsmForge.Core
{
    /// <summary>
    /// Owner-thread cadence for expensive verifications that must not run on every simulation tick.
    /// Fires at most once per interval; Force() schedules an immediate fire at the next check so safe
    /// points (snapshot save, authority publication that can cascade into another domain) are never
    /// delayed by the interval. The caller supplies monotonic elapsed milliseconds, not wall-clock
    /// or remote timestamps. Single-thread contract: like PeerLiveness, no synchronization is provided.
    /// </summary>
    public sealed class VerificationCadence
    {
        private readonly long intervalMilliseconds;
        private long lastVerify;
        private bool forced;

        public VerificationCadence(long nowMilliseconds, long intervalMilliseconds)
        {
            Check.OutOfRange(nowMilliseconds < 0, "nowMilliseconds");
            Check.OutOfRange(intervalMilliseconds < 1, "intervalMilliseconds");
            this.intervalMilliseconds = intervalMilliseconds;
            lastVerify = nowMilliseconds;
        }

        /// <summary>Consumes the window when it fires: the next window starts at this check.</summary>
        public bool ShouldVerify(long nowMilliseconds)
        {
            if (nowMilliseconds < lastVerify) throw new ArgumentOutOfRangeException("nowMilliseconds", "Clock must be monotonic.");
            if (!forced && nowMilliseconds - lastVerify < intervalMilliseconds) return false;
            forced = false;
            lastVerify = nowMilliseconds;
            return true;
        }

        /// <summary>Schedules an immediate fire at the next ShouldVerify, regardless of the window.</summary>
        public void Force() { forced = true; }
    }
}
