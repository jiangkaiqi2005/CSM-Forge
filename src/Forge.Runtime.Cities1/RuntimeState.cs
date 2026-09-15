using System;
using System.Collections.Generic;
using System.Threading;
using CsmForge.Core;
using ICities;

namespace CsmForge.Runtime.Cities1
{
    public enum CitiesRuntimeRole
    {
        Disabled,
        SinglePlayer,
        HostPreparing,
        HostLive,
        ClientLoading,
        ClientReplicaLive,
        ClientRecovering,
        WorldFenced,
        Unloading
    }

    public enum RuntimeEventCode
    {
        Enabled,
        Disabled,
        LoadingCreated,
        LevelLoaded,
        LevelUnloading,
        Released,
        ThreadingCreated,
        ThreadingReleased,
        BeforeSimulationTick,
        AfterSimulationTick,
        SaveMetadataLoaded,
        SaveMetadataSaved,
        PatchReady,
        PatchFailed,
        StaleWorkRejected,
        ScopeLeak,
        Error
    }

    public struct LoadIdentity : IEquatable<LoadIdentity>
    {
        public readonly Guid WorldId;
        public readonly ulong Epoch;
        public readonly uint Generation;

        public LoadIdentity(Guid worldId, ulong epoch, uint generation)
        {
            if (worldId == Guid.Empty || epoch == 0 || generation == 0)
                throw new ArgumentException("Load identity is incomplete.");
            WorldId = worldId;
            Epoch = epoch;
            Generation = generation;
        }

        public bool IsValid { get { return WorldId != Guid.Empty && Epoch != 0 && Generation != 0; } }
        public bool Equals(LoadIdentity other)
        {
            return WorldId == other.WorldId && Epoch == other.Epoch && Generation == other.Generation;
        }
        public override bool Equals(object obj) { return obj is LoadIdentity && Equals((LoadIdentity)obj); }
        public override int GetHashCode() { return WorldId.GetHashCode() ^ Epoch.GetHashCode() ^ Generation.GetHashCode(); }
    }

    public sealed class RuntimeEvent
    {
        public DateTime Utc { get; internal set; }
        public RuntimeEventCode Code { get; internal set; }
        public uint Generation { get; internal set; }
        public int ThreadId { get; internal set; }
        public string Detail { get; internal set; }
    }

    public sealed class RuntimeEventLog
    {
        private readonly object gate = new object();
        private readonly RuntimeEvent[] events;
        private int next;
        private int count;

        public RuntimeEventLog(int capacity)
        {
            if (capacity < 16 || capacity > 2048) throw new ArgumentOutOfRangeException("capacity");
            events = new RuntimeEvent[capacity];
        }

        public void Record(RuntimeEventCode code, uint generation, string detail)
        {
            if (detail != null && detail.Length > 240) detail = detail.Substring(0, 240);
            lock (gate)
            {
                events[next] = new RuntimeEvent
                {
                    Utc = DateTime.UtcNow,
                    Code = code,
                    Generation = generation,
                    ThreadId = Thread.CurrentThread.ManagedThreadId,
                    Detail = detail
                };
                next = (next + 1) % events.Length;
                if (count < events.Length) count++;
            }
        }

        public RuntimeEvent[] Read()
        {
            lock (gate)
            {
                RuntimeEvent[] result = new RuntimeEvent[count];
                int first = (next - count + events.Length) % events.Length;
                for (int i = 0; i < count; i++) result[i] = events[(first + i) % events.Length];
                return result;
            }
        }
    }

    public sealed class CitiesLifecycleCoordinator
    {
        private readonly object gate = new object();
        private readonly RuntimeEventLog events;
        private uint generation;
        private LoadIdentity current;
        private CitiesRuntimeRole role = CitiesRuntimeRole.Disabled;
        private bool enabled;

        public CitiesLifecycleCoordinator(RuntimeEventLog events)
        {
            if (events == null) throw new ArgumentNullException("events");
            this.events = events;
        }

        public CitiesRuntimeRole Role { get { lock (gate) return role; } }
        public LoadIdentity Current { get { lock (gate) return current; } }
        public bool Enabled { get { lock (gate) return enabled; } }

        public void Enable()
        {
            lock (gate)
            {
                enabled = true;
                role = CitiesRuntimeRole.SinglePlayer;
            }
            events.Record(RuntimeEventCode.Enabled, generation, null);
        }

        public void Disable()
        {
            uint value;
            lock (gate)
            {
                enabled = false;
                InvalidateLocked();
                role = CitiesRuntimeRole.Disabled;
                value = generation;
            }
            events.Record(RuntimeEventCode.Disabled, value, null);
        }

        public void LoadingCreated()
        {
            events.Record(RuntimeEventCode.LoadingCreated, Current.Generation, null);
        }

        public LoadIdentity LevelLoaded(LoadMode mode, Guid? persistedWorldId, ulong? persistedEpoch)
        {
            LoadIdentity identity;
            lock (gate)
            {
                if (!enabled) throw new InvalidOperationException("Runtime is disabled.");
                if (generation == uint.MaxValue) throw new InvalidOperationException("Load generation exhausted.");
                generation++;
                Guid world = persistedWorldId.HasValue && persistedWorldId.Value != Guid.Empty ? persistedWorldId.Value : Guid.NewGuid();
                ulong epoch = persistedEpoch.HasValue && persistedEpoch.Value != 0 ? persistedEpoch.Value : NewEpoch();
                current = new LoadIdentity(world, epoch, generation);
                role = CitiesRuntimeRole.SinglePlayer;
                identity = current;
            }
            events.Record(RuntimeEventCode.LevelLoaded, identity.Generation, mode.ToString());
            return identity;
        }

        public void BeginUnload()
        {
            uint value;
            lock (gate)
            {
                role = CitiesRuntimeRole.Unloading;
                InvalidateLocked();
                value = generation;
            }
            events.Record(RuntimeEventCode.LevelUnloading, value, null);
        }

        public void Released()
        {
            events.Record(RuntimeEventCode.Released, Current.Generation, null);
        }

        public bool IsCurrent(LoadIdentity identity)
        {
            lock (gate)
                return enabled && current.IsValid && identity.Equals(current) && role != CitiesRuntimeRole.Unloading && role != CitiesRuntimeRole.Disabled;
        }

        public bool TryTransition(LoadIdentity identity, CitiesRuntimeRole next)
        {
            lock (gate)
            {
                if (!enabled || !current.IsValid || !identity.Equals(current) || role == CitiesRuntimeRole.WorldFenced ||
                    role == CitiesRuntimeRole.Unloading || role == CitiesRuntimeRole.Disabled) return false;
                role = next;
                return true;
            }
        }

        public void Fence(string reason)
        {
            uint value;
            lock (gate)
            {
                if (role != CitiesRuntimeRole.Disabled) role = CitiesRuntimeRole.WorldFenced;
                value = generation;
            }
            events.Record(RuntimeEventCode.Error, value, "fenced: " + reason);
        }

        private void InvalidateLocked()
        {
            current = default(LoadIdentity);
        }

        private static ulong NewEpoch()
        {
            byte[] bytes = Guid.NewGuid().ToByteArray();
            ulong value = BitConverter.ToUInt64(bytes, 0) ^ BitConverter.ToUInt64(bytes, 8);
            return value == 0 ? 1UL : value;
        }
    }

    public static class RuntimeServices
    {
        public static readonly RuntimeEventLog Events = new RuntimeEventLog(256);
        public static readonly CitiesLifecycleCoordinator Lifecycle = new CitiesLifecycleCoordinator(Events);
        public static readonly CitiesGameThreadScheduler Scheduler = new CitiesGameThreadScheduler(Lifecycle, Events);
        public static readonly CitiesPatchCoordinator Patches = new CitiesPatchCoordinator(Events);
        public static readonly ForgeSaveMetadataStore Metadata = new ForgeSaveMetadataStore(Events);
        public static readonly CitiesMultiplayerSession Multiplayer = new CitiesMultiplayerSession(Lifecycle, Events);

        public static void Enable()
        {
            Lifecycle.Enable();
            Patches.InstallWhenReady();
        }

        public static void Disable()
        {
            Multiplayer.RequestStop();
            Scheduler.Detach();
            Patches.Uninstall();
            Lifecycle.Disable();
        }
    }
}
