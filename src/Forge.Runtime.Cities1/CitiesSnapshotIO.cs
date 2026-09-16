using System;
using System.IO;
using System.Reflection;
using ColossalFramework.UI;
using CsmForge.Checkpoints;
using CsmForge.Core;

namespace CsmForge.Runtime.Cities1
{
    public sealed class CitiesSnapshotSaveOperation : IDisposable
    {
        private const string Prefix = "CSM_Forge_Snapshot_";
        private readonly LoadIdentity load;
        private readonly ulong revision;
        private readonly Hash256 root;
        private readonly Guid snapshotId;
        private readonly string saveName;
        private string path;
        private bool started;
        private bool complete;
        private bool disposed;
        private SnapshotFileDescriptor descriptor;

        public CitiesSnapshotSaveOperation(LoadIdentity load, ulong revision, Hash256 root)
        {
            if (!load.IsValid || root == null) throw new ArgumentException("Snapshot save identity is incomplete.");
            this.load = load; this.revision = revision; this.root = root;
            snapshotId = Guid.NewGuid();
            saveName = Prefix + snapshotId.ToString("N");
        }

        public Guid SnapshotId { get { return snapshotId; } }
        public bool Started { get { return started; } }
        public bool Complete { get { return complete; } }
        public SnapshotFileDescriptor Descriptor { get { return descriptor; } }

        public void Start()
        {
            if (disposed) throw new ObjectDisposedException("CitiesSnapshotSaveOperation");
            if (started) throw new InvalidOperationException("Snapshot save already started.");
            if (!RuntimeServices.Lifecycle.IsCurrent(load)) throw new InvalidOperationException("Snapshot save belongs to a stale load generation.");
            if (SavePanel.isSaving) throw new InvalidOperationException("CS1 is already saving another game.");
            SavePanel panel = UIView.library.Get<SavePanel>("SavePanel");
            if (panel == null) throw new InvalidOperationException("CS1 SavePanel is unavailable.");
            path = GetSavePath(panel, saveName);
            if (string.IsNullOrEmpty(path)) throw new IOException("CS1 did not provide a snapshot save path.");
            try { if (File.Exists(path)) File.Delete(path); } catch (Exception error) { throw new IOException("Could not remove stale snapshot file.", error); }
            RuntimeServices.Metadata.Update(load, revision, root);
            panel.SaveGame(saveName);
            started = true;
        }

        public bool Poll()
        {
            if (disposed) throw new ObjectDisposedException("CitiesSnapshotSaveOperation");
            if (!started) throw new InvalidOperationException("Snapshot save has not started.");
            if (complete) return true;
            if (!RuntimeServices.Lifecycle.IsCurrent(load)) throw new InvalidOperationException("Snapshot save outlived its load generation.");
            if (SavePanel.isSaving) return false;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) throw new IOException("CS1 snapshot save finished without a readable file.");
            descriptor = SnapshotFileDescriptor.FromFile(snapshotId, revision, root, path);
            complete = true;
            return true;
        }

        public void Dispose()
        {
            disposed = true;
        }

        private static string GetSavePath(SavePanel panel, string name)
        {
            MethodInfo method = typeof(SavePanel).GetMethod("GetSavePathName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new Type[] { typeof(string), typeof(bool) }, null);
            if (method == null) throw new MissingMethodException("SavePanel.GetSavePathName(string,bool)");
            return method.Invoke(panel, new object[] { name, false }) as string;
        }
    }
}
