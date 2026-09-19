using CsmForge.Core;
using System;
using ICities;

namespace CsmForge.Runtime.Cities1
{
    public sealed class CitiesGameThreadScheduler
    {
        private readonly object gate = new object();
        private readonly CitiesLifecycleCoordinator lifecycle;
        private readonly RuntimeEventLog events;
        private IThreading threading;

        public CitiesGameThreadScheduler(CitiesLifecycleCoordinator lifecycle, RuntimeEventLog events)
        {
            Check.NotNull(lifecycle, "lifecycle"); Check.NotNull(events, "events"); // WP-2: per-argument reporting
            this.lifecycle = lifecycle;
            this.events = events;
        }

        public void Attach(IThreading value)
        {
            Check.NotNull(value, "value");
            lock (gate) threading = value;
            events.Record(RuntimeEventCode.ThreadingCreated, lifecycle.Current.Generation, null);
        }

        public void Detach()
        {
            lock (gate) threading = null;
            events.Record(RuntimeEventCode.ThreadingReleased, lifecycle.Current.Generation, null);
        }

        public bool QueueMain(LoadIdentity identity, Action action)
        {
            return Queue(identity, action, false);
        }

        public bool QueueSimulation(LoadIdentity identity, Action action)
        {
            return Queue(identity, action, true);
        }

        private bool Queue(LoadIdentity identity, Action action, bool simulation)
        {
            Check.NotNull(action, "action");
            if (!lifecycle.IsCurrent(identity))
            {
                events.Record(RuntimeEventCode.StaleWorkRejected, identity.Generation, "enqueue");
                return false;
            }

            IThreading current;
            lock (gate) current = threading;
            if (current == null) return false;

            Action guarded = delegate
            {
                if (!lifecycle.IsCurrent(identity))
                {
                    events.Record(RuntimeEventCode.StaleWorkRejected, identity.Generation, "execute");
                    return;
                }
                try { action(); }
                catch (Exception error)
                {
                    events.Record(RuntimeEventCode.Error, identity.Generation,
                        "scheduled-" + (simulation ? "simulation" : "main") + ": " + error.GetType().Name);
                    lifecycle.Fence("scheduled game-thread work failed");
                }
            };

            try
            {
                if (simulation) current.QueueSimulationThread(guarded);
                else current.QueueMainThread(guarded);
                return true;
            }
            catch (Exception error)
            {
                events.Record(RuntimeEventCode.Error, identity.Generation, "queue: " + error.GetType().Name);
                lifecycle.Fence("game-thread queue failed");
                return false;
            }
        }
    }
}
