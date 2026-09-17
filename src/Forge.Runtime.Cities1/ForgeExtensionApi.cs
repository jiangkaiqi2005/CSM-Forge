using System;
using System.Collections.Generic;
using System.Reflection;
using CsmForge.Core;

namespace CsmForge.Runtime.Cities1
{
    /// <summary>V1 for global/non-entity absolute state. No command replay is accepted.</summary>
    public interface IForgeStateAdapterV1
    {
        string AdapterId { get; }
        uint SchemaVersion { get; }
        byte[] CaptureAbsolute();
        void ApplyAbsolute(byte[] state);
    }

    /// <summary>
    /// V2 for entity-bearing state. Adapters must encode Forge EntityIdentityV2 values in their
    /// payload rather than process-local manager indices. The context owns the native-ID mapping.
    /// </summary>
    public interface IForgeStateAdapterV2
    {
        string AdapterId { get; }
        uint SchemaVersion { get; }
        byte[] CaptureAbsolute(IForgeAdapterContextV1 context);
        void ApplyAbsolute(IForgeAdapterContextV1 context, byte[] state);
    }

    public interface IForgeAdapterContextV1
    {
        bool IsAuthoritative { get; }
        bool TryGetIdentity(uint nativeId, out EntityIdentityV2 identity);
        bool TryGetNative(EntityIdentityV2 identity, out uint nativeId);
        EntityIdentityV2 GetOrAllocateIdentity(uint nativeId);
        void BindKnownIdentity(EntityIdentityV2 identity, uint nativeId);
        bool RetireIdentity(EntityIdentityV2 identity);
    }

    internal sealed class ForgeStateAdapterRegistration
    {
        public IForgeStateAdapterV1 AdapterV1;
        public IForgeStateAdapterV2 AdapterV2;
        public string AdapterId;
        public uint SchemaVersion;
        public Assembly Assembly;
    }

    internal sealed class ForgeAdapterContextV1 : IForgeAdapterContextV1
    {
        private readonly EntityIdMapV2 ids;
        public bool IsAuthoritative { get; private set; }

        public ForgeAdapterContextV1(string adapterId, bool authoritative)
        {
            if (string.IsNullOrEmpty(adapterId)) throw new ArgumentException("Adapter id is missing.", "adapterId");
            IsAuthoritative = authoritative;
            ids = ExtensionIdentityServices.Maps.GetOrAttach(adapterId);
        }

        public bool TryGetIdentity(uint nativeId, out EntityIdentityV2 identity)
        {
            return ids.TryGetIdentity(nativeId, out identity);
        }

        public bool TryGetNative(EntityIdentityV2 identity, out uint nativeId)
        {
            return ids.TryGetNative(identity, out nativeId);
        }

        public EntityIdentityV2 GetOrAllocateIdentity(uint nativeId)
        {
            if (!IsAuthoritative) throw new InvalidOperationException("Replica adapters cannot allocate authoritative Forge identities.");
            EntityIdentityV2 existing;
            if (ids.TryGetIdentity(nativeId, out existing)) return existing;
            return ids.Allocate(nativeId);
        }

        public void BindKnownIdentity(EntityIdentityV2 identity, uint nativeId)
        {
            if (IsAuthoritative) throw new InvalidOperationException("Host adapters cannot bind identities supplied by a replica projection.");
            ids.BindKnown(identity, nativeId);
        }

        public bool RetireIdentity(EntityIdentityV2 identity)
        {
            return ids.Retire(identity);
        }
    }

    public static class ForgeExtensionApi
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, ForgeStateAdapterRegistration> Adapters =
            new Dictionary<string, ForgeStateAdapterRegistration>(StringComparer.Ordinal);

        public static void Register(IForgeStateAdapterV1 adapter)
        {
            if (adapter == null) throw new ArgumentNullException("adapter");
            RegisterCore(adapter.AdapterId, adapter.SchemaVersion, adapter.GetType().Assembly, adapter, null);
        }

        public static void Register(IForgeStateAdapterV2 adapter)
        {
            if (adapter == null) throw new ArgumentNullException("adapter");
            RegisterCore(adapter.AdapterId, adapter.SchemaVersion, adapter.GetType().Assembly, null, adapter);
        }

        public static bool Unregister(string adapterId)
        {
            if (string.IsNullOrEmpty(adapterId)) return false;
            lock (Gate)
            {
                if (RuntimeServices.Multiplayer.Status.Mode != MultiplayerSessionMode.Offline) return false;
                return Adapters.Remove(adapterId);
            }
        }

        public static string[] RegisteredAdapterIds
        {
            get
            {
                lock (Gate)
                {
                    string[] result = new string[Adapters.Count];
                    Adapters.Keys.CopyTo(result, 0);
                    Array.Sort(result, StringComparer.Ordinal);
                    return result;
                }
            }
        }

        internal static ForgeStateAdapterRegistration[] SnapshotRegistrations()
        {
            lock (Gate)
            {
                ForgeStateAdapterRegistration[] result = new ForgeStateAdapterRegistration[Adapters.Count];
                int index = 0;
                foreach (ForgeStateAdapterRegistration registration in Adapters.Values) result[index++] = registration;
                Array.Sort(result, delegate(ForgeStateAdapterRegistration a, ForgeStateAdapterRegistration b)
                { return StringComparer.Ordinal.Compare(a.AdapterId, b.AdapterId); });
                return result;
            }
        }

        private static void RegisterCore(string adapterId, uint schemaVersion, Assembly assembly,
            IForgeStateAdapterV1 v1, IForgeStateAdapterV2 v2)
        {
            ValidateId(adapterId);
            if (schemaVersion == 0) throw new ArgumentOutOfRangeException("schemaVersion", "SchemaVersion must be non-zero.");
            if (assembly == null) throw new ArgumentNullException("assembly");
            lock (Gate)
            {
                EnsureSessionMutable();
                ForgeStateAdapterRegistration existing;
                if (Adapters.TryGetValue(adapterId, out existing))
                {
                    Type oldType = existing.AdapterV2 != null ? existing.AdapterV2.GetType() : existing.AdapterV1.GetType();
                    Type newType = v2 != null ? v2.GetType() : v1.GetType();
                    if (oldType != newType || existing.SchemaVersion != schemaVersion)
                        throw new InvalidOperationException("A different adapter or schema already owns this AdapterId.");
                }
                else if (Adapters.Count >= 128) throw new InvalidOperationException("Forge state adapter limit exceeded.");
                Adapters[adapterId] = new ForgeStateAdapterRegistration
                {
                    AdapterId = adapterId,
                    SchemaVersion = schemaVersion,
                    Assembly = assembly,
                    AdapterV1 = v1,
                    AdapterV2 = v2
                };
            }
        }

        private static void EnsureSessionMutable()
        {
            if (RuntimeServices.Multiplayer.Status.Mode != MultiplayerSessionMode.Offline)
                throw new InvalidOperationException("Forge state adapter registration is frozen for the active multiplayer session.");
        }

        private static void ValidateId(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 80) throw new ArgumentException("Invalid Forge adapter id.", "AdapterId");
            foreach (char c in value)
                if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '.' || c == '-' || c == '_'))
                    throw new ArgumentException("Forge adapter ids use canonical lowercase ASCII.", "AdapterId");
        }
    }
}
