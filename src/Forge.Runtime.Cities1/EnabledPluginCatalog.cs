using System;
using System.Collections.Generic;
using System.Reflection;
using ColossalFramework;
using ColossalFramework.Plugins;
using ICities;

namespace CsmForge.Runtime.Cities1
{
    /// <summary>
    /// One immutable snapshot of the plugins CS1 reports as enabled. Assembly presence is never
    /// treated as activation: disabled Workshop assemblies may still be loaded into the AppDomain.
    /// </summary>
    internal sealed class EnabledPluginCatalog
    {
        internal sealed class Entry
        {
            private readonly Assembly[] assemblies;
            public ulong PublishedFileId { get; private set; }
            public string UserModTypeName { get; private set; }
            public Assembly[] Assemblies { get { return (Assembly[])assemblies.Clone(); } }

            internal Entry(ulong publishedFileId, IUserMod userMod, Assembly[] assemblies)
            {
                PublishedFileId = publishedFileId;
                UserModTypeName = userMod == null ? null : userMod.GetType().FullName;
                this.assemblies = assemblies == null ? new Assembly[0] : (Assembly[])assemblies.Clone();
            }
        }

        private readonly Entry[] entries;

        private EnabledPluginCatalog(Entry[] value)
        {
            entries = value ?? new Entry[0];
        }

        public Entry[] Entries { get { return (Entry[])entries.Clone(); } }

        public bool ContainsUserMod(string exactTypeName)
        {
            if (string.IsNullOrEmpty(exactTypeName)) return false;
            for (int i = 0; i < entries.Length; i++)
                if (StringComparer.Ordinal.Equals(entries[i].UserModTypeName, exactTypeName)) return true;
            return false;
        }

        public static EnabledPluginCatalog Capture()
        {
            PluginManager manager = Singleton<PluginManager>.instance;
            if (manager == null) throw new InvalidOperationException("CS1 PluginManager is unavailable.");
            List<Entry> result = new List<Entry>();
            foreach (PluginManager.PluginInfo plugin in manager.GetPluginsInfo())
            {
                if (plugin == null || !plugin.isEnabled) continue;
                IUserMod userMod = plugin.userModInstance as IUserMod;
                List<Assembly> discovered = plugin.GetAssemblies();
                List<Assembly> assemblies = new List<Assembly>();
                if (discovered != null)
                    for (int i = 0; i < discovered.Count; i++)
                        if (discovered[i] != null && !assemblies.Contains(discovered[i])) assemblies.Add(discovered[i]);
                if (assemblies.Count == 0 && userMod != null) assemblies.Add(userMod.GetType().Assembly);
                result.Add(new Entry(plugin.publishedFileID.AsUInt64, userMod, assemblies.ToArray()));
            }
            return new EnabledPluginCatalog(result.ToArray());
        }
    }
}
