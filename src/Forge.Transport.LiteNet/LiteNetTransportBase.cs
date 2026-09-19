using CsmForge.Core;
using System;
using System.Threading;
using LiteNetLib;

namespace CsmForge.Transport.LiteNet
{
    public abstract class LiteNetTransportBase : IDisposable
    {
        // Protocol v2 allows up to 64 KiB of application payload plus its fixed frame
        // header and integrity trailer. Keep the transport envelope larger than the
        // protocol payload ceiling so the delivery layer never truncates a legal frame.
        public const int MaxPacketBytes = 128 * 1024;
        public const int DefaultEventLimit = 512;
        public const int DefaultQueueBytes = 4 * 1024 * 1024;

        protected readonly object Gate = new object();
        private readonly BoundedTransportQueue queue;
        protected NetManager Manager;
        private Thread worker;
        private volatile bool running;
        private bool disposed;

        protected LiteNetTransportBase(int eventLimit, int queueBytes)
        {
            queue = new BoundedTransportQueue(eventLimit, queueBytes);
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

        public bool TryTake(out TransportEvent value) { return queue.TryTake(out value); }
        protected bool Enqueue(TransportEvent value) { return queue.TryAdd(value); }

        protected static byte[] ReadPacket(NetPacketReader reader)
        {
            Check.NotNull(reader, "reader");
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
            queue.Clear();
        }
    }
}
