using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace CsmForge.Core
{
    public enum DiagnosticCode
    {
        Committed, Rejected, Duplicate, IdentityRejected, StaleSession,
        Gap, HashMismatch, WorldFault, SnapshotInstalled, Disconnected
    }

    public sealed class DiagnosticRecord
    {
        public long Timestamp { get; private set; }
        public DiagnosticCode Code { get; private set; }
        public SessionStamp Stamp { get; private set; }
        public Guid Peer { get; private set; }
        public ulong Revision { get; private set; }
        public ulong RequestId { get; private set; }
        internal DiagnosticRecord(DiagnosticCode code, SessionStamp stamp, Guid peer, ulong revision, ulong request)
        {
            Timestamp = Stopwatch.GetTimestamp();
            Code = code; Stamp = stamp; Peer = peer; Revision = revision; RequestId = request;
        }
    }

    /// <summary>Bounded, process-local monotonic diagnostics. No packet text, secrets or saves.</summary>
    public sealed class DiagnosticRing
    {
        private readonly object gate = new object();
        private readonly DiagnosticRecord[] records;
        private int next;
        private int count;
        public DiagnosticRing(int capacity)
        {
            if (capacity < 1 || capacity > 4096) throw new ArgumentOutOfRangeException("capacity");
            records = new DiagnosticRecord[capacity];
        }
        public void Record(DiagnosticCode code, SessionStamp stamp, Guid peer, ulong revision, ulong request)
        {
            lock (gate)
            {
                records[next] = new DiagnosticRecord(code, stamp, peer, revision, request);
                next = (next + 1) % records.Length;
                if (count < records.Length) count++;
            }
        }
        public DiagnosticRecord[] Read()
        {
            lock (gate)
            {
                DiagnosticRecord[] result = new DiagnosticRecord[count];
                int first = (next - count + records.Length) % records.Length;
                for (int i = 0; i < count; i++) result[i] = records[(first + i) % records.Length];
                return result;
            }
        }
    }

    internal sealed class ThreadOwner
    {
        private readonly int id = Thread.CurrentThread.ManagedThreadId;
        public void AssertCurrent()
        {
            if (Thread.CurrentThread.ManagedThreadId != id)
                throw new InvalidOperationException("World/session access must run on its owning thread.");
        }
    }

    /// <summary>Transport threads may post bounded immutable work; only the game owner drains it.</summary>
    public sealed class BoundedInbox<T> where T : class
    {
        private readonly object gate = new object();
        private readonly Queue<T> queue;
        private readonly int capacity;
        public BoundedInbox(int capacity)
        {
            if (capacity < 1 || capacity > 4096) throw new ArgumentOutOfRangeException("capacity");
            this.capacity = capacity;
            queue = new Queue<T>(capacity);
        }
        public bool TryPost(T item)
        {
            if (item == null) throw new ArgumentNullException("item");
            lock (gate)
            {
                if (queue.Count == capacity) return false;
                queue.Enqueue(item);
                return true;
            }
        }
        public bool TryTake(out T item)
        {
            lock (gate)
            {
                if (queue.Count == 0) { item = null; return false; }
                item = queue.Dequeue();
                return true;
            }
        }
        public int Count { get { lock (gate) return queue.Count; } }
    }
}
