using System;
using System.Collections.Generic;
using System.Reflection;

namespace CsmForge.Runtime.Cities1
{
    /// <summary>
    /// Public result-state integration point for third-party mods. Adapters expose absolute
    /// state only; Forge never accepts command/tool replay through this API.
    /// </summary>
    public interface IForgeStateAdapterV1
    {
        string AdapterId { get; }
        uint SchemaVersion { get; }
        byte[] CaptureAbsolute();
        void ApplyAbsolute(byte[] state);
    }

    internal sealed class ForgeStateAdapterRegistration
    {
        public IForgeStateAdapterV1 Adapter;
        public string AdapterId;
        public uint SchemaVersion;
        public Assembly Assembly;
    }

    public static class ForgeExtensionApi
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, IForgeStateAdapterV1> Adapters =
            new Dictionary<string, IForgeStateAdapterV1>(StringComparer.Ordinal);
        private static bool frozen;

        public static void Register(IForgeStateAdapterV1 adapter)
        {
            if (adapter == null) throw new ArgumentNullException("adapter");
            ValidateId(adapter.AdapterId);
            if (adapter.SchemaVersion == 0) throw new ArgumentOutOfRangeException("adapter", "SchemaVersion must be non-zero.");
            lock (Gate)
            {
                if (frozen) throw new InvalidOperationException("Forge state adapter registration is frozen for the active multiplayer session.");
                IForgeStateAdapterV1 existing;
                if (Adapters.TryGetValue(adapter.AdapterId, out existing))
                {
                    if (existing.GetType() != adapter.GetType()) throw new InvalidOperationException("A different adapter already owns this AdapterId.");
                    Adapters[adapter.AdapterId] = adapter;
                    return;
                }
                if (Adapters.Count >= 128) throw new InvalidOperationException("Forge state adapter limit exceeded.");
                Adapters.Add(adapter.AdapterId, adapter);
            }
        }

        public static bool Unregister(string adapterId)
        {
            if (string.IsNullOrEmpty(adapterId)) return false;
            lock (Gate)
            {
                if (frozen) return false;
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

        internal static ForgeStateAdapterRegistration[] FreezeForSession()
        {
            lock (Gate)
            {
                frozen = true;
                List<ForgeStateAdapterRegistration> result = new List<ForgeStateAdapterRegistration>();
                foreach (KeyValuePair<string, IForgeStateAdapterV1> pair in Adapters)
                    result.Add(new ForgeStateAdapterRegistration
                    {
                        Adapter = pair.Value,
                        AdapterId = pair.Key,
                        SchemaVersion = pair.Value.SchemaVersion,
                        Assembly = pair.Value.GetType().Assembly
                    });
                result.Sort(delegate(ForgeStateAdapterRegistration a, ForgeStateAdapterRegistration b)
                { return StringComparer.Ordinal.Compare(a.AdapterId, b.AdapterId); });
                return result.ToArray();
            }
        }

        internal static ForgeStateAdapterRegistration[] SnapshotRegistrations()
        {
            lock (Gate)
            {
                List<ForgeStateAdapterRegistration> result = new List<ForgeStateAdapterRegistration>();
                foreach (KeyValuePair<string, IForgeStateAdapterV1> pair in Adapters)
                    result.Add(new ForgeStateAdapterRegistration
                    {
                        Adapter = pair.Value,
                        AdapterId = pair.Key,
                        SchemaVersion = pair.Value.SchemaVersion,
                        Assembly = pair.Value.GetType().Assembly
                    });
                result.Sort(delegate(ForgeStateAdapterRegistration a, ForgeStateAdapterRegistration b)
                { return StringComparer.Ordinal.Compare(a.AdapterId, b.AdapterId); });
                return result.ToArray();
            }
        }

        internal static void UnfreezeAfterSession()
        {
            lock (Gate) frozen = false;
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
