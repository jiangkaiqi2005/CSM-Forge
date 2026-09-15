using System;
using System.Reflection;
using ColossalFramework;
using ColossalFramework.Packaging;
using ColossalFramework.Threading;
using ColossalFramework.UI;

namespace CsmForge.Runtime.Cities1
{
    public sealed class CitiesReceivedWorldLoader
    {
        private sealed class PreparedWorld
        {
            public SaveGameMetaData MetaData;
            public SimulationMetaData Simulation;
        }

        private readonly object gate = new object();
        private int generation;
        private bool loading;
        private Action<LoadIdentity> completed;
        private Action<Exception> failed;

        public bool IsLoading { get { lock (gate) return loading; } }

        public void Start(byte[] world, Action<LoadIdentity> completed, Action<Exception> failed)
        {
            if (world == null || world.Length == 0) throw new ArgumentException("Received snapshot is empty.", "world");
            if (completed == null || failed == null) throw new ArgumentNullException("completed");
            PreparedWorld prepared = Prepare(world);
            int token;
            lock (gate)
            {
                if (loading) throw new InvalidOperationException("A Forge snapshot is already loading.");
                loading = true;
                generation++;
                token = generation;
                this.completed = completed;
                this.failed = failed;
            }

            ThreadHelper.dispatcher.Dispatch(delegate
            {
                try
                {
                    lock (gate) if (!loading || generation != token) return;
                    Singleton<LoadingManager>.Ensure();
                    if (Singleton<LoadingManager>.instance.LoadLevel(prepared.MetaData.assetRef, "Game", "InGame", prepared.Simulation) == null)
                        throw new InvalidOperationException("CS1 did not start loading the Forge snapshot.");
                }
                catch (Exception error)
                {
                    Fail(token, error);
                }
            });
        }

        public void NotifyLevelLoaded(LoadIdentity identity)
        {
            Action<LoadIdentity> callback = null;
            int token;
            lock (gate)
            {
                if (!loading) return;
                token = generation;
                callback = completed;
                completed = null;
                failed = null;
                loading = false;
            }
            if (callback == null) return;
            try
            {
                Singleton<SimulationManager>.instance.m_ThreadingWrapper.QueueSimulationThread(delegate
                {
                    lock (gate) if (generation != token) return;
                    callback(identity);
                });
            }
            catch (Exception error)
            {
                RuntimeServices.Events.Record(RuntimeEventCode.Error, identity.Generation,
                    "snapshot-load-completion:" + error.GetType().Name);
            }
        }

        public void CancelPending()
        {
            lock (gate)
            {
                generation++;
                loading = false;
                completed = null;
                failed = null;
            }
        }

        private void Fail(int token, Exception error)
        {
            Action<Exception> callback = null;
            lock (gate)
            {
                if (generation != token) return;
                callback = failed;
                completed = null;
                failed = null;
                loading = false;
            }
            if (callback != null) callback(error);
        }

        private static PreparedWorld Prepare(byte[] world)
        {
            Package package = new Package("CSM_Forge_Received", world);
            object implementation = GetField(package, "m_PackageImplementation");
            if (implementation == null) throw new InvalidOperationException("Received Package implementation is unavailable.");
            SetField(implementation, "m_PackagePath", "");
            Singleton<LoadingManager>.Ensure();
            Package.Asset asset = package.Find(package.packageMainAsset);
            if (asset == null) throw new InvalidOperationException("Received snapshot has no main asset.");
            SaveGameMetaData metaData = asset.Instantiate<SaveGameMetaData>();
            if (metaData == null) throw new InvalidOperationException("Received snapshot has no SaveGameMetaData.");
            LoadPanel panel = UIView.GetAView().panelsLibrary.Get<LoadPanel>("LoadPanel");
            return new PreparedWorld
            {
                MetaData = metaData,
                Simulation = new SimulationMetaData
                {
                    m_CityName = metaData.cityName,
                    m_updateMode = SimulationManager.UpdateMode.LoadGame,
                    m_environment = panel == null ? null : panel.m_forceEnvironment
                }
            };
        }

        private static object GetField(object target, string name)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null) throw new MissingFieldException(target.GetType().FullName, name);
            return field.GetValue(target);
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null) throw new MissingFieldException(target.GetType().FullName, name);
            field.SetValue(target, value);
        }
    }
}
