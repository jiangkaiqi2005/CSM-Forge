using System;
using System.Collections.Generic;

namespace CsmForge.Transport.LiteNet
{
    public enum TransportEventKind { Connected, Data, Disconnected, Error }

    public sealed class TransportEvent
    {
        private readonly byte[] payload;
        public TransportEventKind Kind { get; private set; }
        public Guid ConnectionId { get; private set; }
        public byte[] Payload { get { return payload == null ? null : (byte[])payload.Clone(); } }
        public string Detail { get; private set; }

        internal TransportEvent(TransportEventKind kind, Guid connectionId, byte[] bytes, string detail)
        {
            Kind = kind;
            ConnectionId = connectionId;
            payload = bytes == null ? null : (byte[])bytes.Clone();
            Detail = detail;
        }
    }

    internal sealed class BoundedTransportQueue
    {
        private readonly object gate = new object();
        private readonly Queue<TransportEvent> queue = new Queue<TransportEvent>();
        private readonly int maxEvents;
        private readonly int maxBytes;
        private int bytes;

        public BoundedTransportQueue(int maxEvents, int maxBytes)
        {
            if (maxEvents < 8 || maxEvents > 4096) throw new ArgumentOutOfRangeException("maxEvents");
            if (maxBytes < 65536 || maxBytes > 64 * 1024 * 1024) throw new ArgumentOutOfRangeException("maxBytes");
            this.maxEvents = maxEvents;
            this.maxBytes = maxBytes;
        }

        public bool TryAdd(TransportEvent item)
        {
            if (item == null) throw new ArgumentNullException("item");
            byte[] data = item.Payload;
            int size = data == null ? 0 : data.Length;
            lock (gate)
            {
                if (queue.Count >= maxEvents || size > maxBytes - bytes) return false;
                queue.Enqueue(item);
                bytes += size;
                return true;
            }
        }

        public bool TryTake(out TransportEvent item)
        {
            lock (gate)
            {
                if (queue.Count == 0) { item = null; return false; }
                item = queue.Dequeue();
                byte[] data = item.Payload;
                bytes -= data == null ? 0 : data.Length;
                return true;
            }
        }

        public void Clear()
        {
            lock (gate) { queue.Clear(); bytes = 0; }
        }
    }
}
