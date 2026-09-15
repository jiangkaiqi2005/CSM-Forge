using System;
using System.Threading;
using LiteNetLib;

namespace CsmForge.Transport.LiteNet
{
    public abstract class LiteNetTransportBase : IDisposable
    {
        public const int MaxPacketBytes = 64 * 1024;
        public const int DefaultEventLimit = 512;
        public const int DefaultQueueBytes = 4 * 1024 * 1024;

        protected readonly object Gate = new object();
        protected readonly BoundedTransportQueue Queue;
        protected NetManager Manager;
        private Thread worker;
        private volatile bool running;
        private bool disposed;

        protected LiteNetTransportBase(int eventLimit, int queueBytes)
        {
            Queue = new BoundedTransportQueue(eventLimit, queueBytes);
        }

        public bool IsRunning { get { return running; } }

        protected void StartWorker()
        {
            lock (Gate)
            {
                if (disposed) throw new ObjectDisposedException(GetType().Name);
                if (running) throw new InvalidOperationException("Transport worker already running.");
                running = true;
                worker = new Thread(PollLoop) { IsBackground = true, Name = "CSM-Forge LiteNet transport" };
                worker.Start();
            }
        }

        public bool TryTake(out TransportEvent value) { return Queue.TryTake(out value); }
        protected bool Enqueue(TransportEvent value) { return Queue.TryAdd(value); }

        protected static byte[] ReadPacket(NetPacketReader reader)
        {
            if (reader == null) throw new ArgumentNullException("reader");
            byte[] bytes = reader.GetRemainingBytes();
            return bytes == null || bytes.Length == 0 || bytes.Length > MaxPacketBytes ? null : bytes;
        }

        private void PollLoop()
        {
            try
            {
                while (running)
                {
                    NetManager current;
                    lock (Gate) current = Manager;
                    if (current == null) break;
                    current.PollEvents();
                    Thread.Sleep(10);
                }
            }
            catch (Exception error)
            {
                Enqueue(new TransportEvent(TransportEventKind.Error, Guid.Empty, null,
                    "poll:" + error.GetType().Name));
            }
            finally { running = false; }
        }

        public virtual void Stop()
        {
            Thread join;
            NetManager manager;
            lock (Gate)
            {
                running = false;
                join = worker;
                worker = null;
                manager = Manager;
                Manager = null;
            }
            try { if (manager != null && manager.IsRunning) manager.Stop(); } catch { }
            if (join != null && join != Thread.CurrentThread)
            {
                try { join.Join(2000); } catch { }
            }
        }

        public void Dispose()
        {
            lock (Gate)
            {
                if (disposed) return;
                disposed = true;
            }
            Stop();
            Queue.Clear();
        }
    }
}
