using System;
using System.Collections.Generic;
using CsmForge.Core;

namespace CsmForge.Runtime.Cities1
{
    /// <summary>
    /// Read-only snapshot of the stable identity maps owned by core Authority domains. DLC and Mod
    /// adapters use this to reference existing Building/Net/etc. entities without ever putting a
    /// process-local CS1 native slot on the wire.
    /// </summary>
    internal sealed class CoreEntityReferenceSnapshot
    {
        private readonly Dictionary<ushort, Dictionary<uint, EntityIdentityV2>> byNative =
            new Dictionary<ushort, Dictionary<uint, EntityIdentityV2>>();
        private readonly Dictionary<ushort, Dictionary<EntityIdentityV2, uint>> byEntity =
            new Dictionary<ushort, Dictionary<EntityIdentityV2, uint>>();

        private CoreEntityReferenceSnapshot(SavedEntityDomainV2[] domains)
        {
            if (domains == null) throw new ArgumentNullException("domains");
            for (int i = 0; i < domains.Length; i++)
            {
                SavedEntityDomainV2 domain = domains[i];
                Dictionary<uint, EntityIdentityV2> native = new Dictionary<uint, EntityIdentityV2>();
                Dictionary<EntityIdentityV2, uint> entity = new Dictionary<EntityIdentityV2, uint>();
                EntityMapEntryV2[] entries = domain.Entries;
                for (int j = 0; j < entries.Length; j++)
                {
                    EntityMapEntryV2 entry = entries[j];
                    native.Add(entry.NativeId, entry.Identity);
                    entity.Add(entry.Identity, entry.NativeId);
                }
                byNative.Add(domain.DomainId, native);
                byEntity.Add(domain.DomainId, entity);
            }
        }

        public static CoreEntityReferenceSnapshot Capture()
        {
            return new CoreEntityReferenceSnapshot(EntityMapSaveCodecV2.Decode(RuntimeServices.EntityMaps.EncodeCurrent()));
        }

        public bool TryGetIdentity(ushort domainId, uint nativeId, out EntityIdentityV2 identity)
        {
            identity = default(EntityIdentityV2);
            Dictionary<uint, EntityIdentityV2> values;
            return nativeId != 0 && byNative.TryGetValue(domainId, out values) && values.TryGetValue(nativeId, out identity);
        }

        public bool TryGetNative(ushort domainId, EntityIdentityV2 identity, out uint nativeId)
        {
            nativeId = 0;
            Dictionary<EntityIdentityV2, uint> values;
            return identity.IsValid && byEntity.TryGetValue(domainId, out values) && values.TryGetValue(identity, out nativeId);
        }
    }
}
