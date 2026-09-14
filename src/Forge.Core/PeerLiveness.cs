using System;

namespace CsmForge.Core
{
    public enum LinkHealth { Healthy, Degraded, Expired }

    /// <summary>
    /// Caller supplies monotonic elapsed milliseconds, not wall-clock or remote timestamps.
    /// A fresh authenticated heartbeat improves link health but NEVER grants edit readiness.
    /// The coordinator must suspend/close the associated host/replica on degraded/expired.
    /// </summary>
    public sealed class PeerLiveness
    {
        private readonly long degradeAfter;
        private readonly long expireAfter;
        private long lastSeen;
        private long lastObserved;
        public LinkHealth Health { get; private set; }

        public PeerLiveness(long nowMilliseconds, long degradeAfterMilliseconds, long expireAfterMilliseconds)
        {
            if (nowMilliseconds < 0 || degradeAfterMilliseconds < 1 || expireAfterMilliseconds <= degradeAfterMilliseconds)
                throw new ArgumentOutOfRangeException("nowMilliseconds");
            lastSeen = nowMilliseconds; lastObserved = nowMilliseconds;
            degradeAfter = degradeAfterMilliseconds; expireAfter = expireAfterMilliseconds;
        }
        private void Observe(long now)
        {
            if (now < lastObserved) throw new ArgumentOutOfRangeException("now", "Clock must be monotonic.");
            lastObserved = now;
        }
        public LinkHealth Poll(long nowMilliseconds)
        {
            Observe(nowMilliseconds);
            if (Health == LinkHealth.Expired) return Health;
            long elapsed = nowMilliseconds - lastSeen;
            Health = elapsed >= expireAfter ? LinkHealth.Expired : elapsed >= degradeAfter ? LinkHealth.Degraded : LinkHealth.Healthy;
            return Health;
        }
        public bool AuthenticatedHeartbeat(long nowMilliseconds)
        {
            // Check expiry first: a heartbeat cannot resurrect an expired connection incarnation.
            if (Poll(nowMilliseconds) == LinkHealth.Expired) return false;
            lastSeen = nowMilliseconds;
            Health = LinkHealth.Healthy;
            return true;
        }
    }
}
