using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CsmForge.Core
{
    /// <summary>
    /// Bounded per-phase timing window. The host runs ~16 verification polls per simulation tick,
    /// so "it feels laggy" needs numbers, not guesses: this accumulates count/total/max per named
    /// phase and emits one line per window. Sample values are caller-supplied ticks (Stopwatch
    /// timestamp deltas), converted to milliseconds only when reporting. Owner-thread only.
    /// </summary>
    public sealed class PerfWindow
    {
        private sealed class Phase
        {
            public long Samples;
            public long TotalTicks;
            public long MaxTicks;
        }

        private readonly Dictionary<string, Phase> phases = new Dictionary<string, Phase>(StringComparer.Ordinal);
        private readonly List<string> order = new List<string>();
        private readonly long frequency;
        private readonly long windowTicks;
        private long windowStart;
        private long frames;
        private long windowTotalFrameTicks;
        private long windowMaxFrameTicks;

        public PerfWindow(long nowTicks, long frequency, long windowMilliseconds)
        {
            if (frequency < 1) throw new ArgumentOutOfRangeException("frequency");
            if (windowMilliseconds < 1) throw new ArgumentOutOfRangeException("windowMilliseconds");
            this.frequency = frequency;
            windowTicks = frequency * windowMilliseconds / 1000;
            if (windowTicks < 1) windowTicks = 1;
            windowStart = nowTicks;
        }

        public bool Record(string phase, long elapsedTicks)
        {
            Check.NotNull(phase, "phase");
            if (elapsedTicks < 0) elapsedTicks = 0;
            Phase entry;
            if (!phases.TryGetValue(phase, out entry))
            {
                if (phases.Count >= 64) return false; // bounded: never grow without limit
                entry = new Phase();
                phases.Add(phase, entry);
                order.Add(phase);
            }
            entry.Samples++;
            entry.TotalTicks += elapsedTicks;
            if (elapsedTicks > entry.MaxTicks) entry.MaxTicks = elapsedTicks;
            return true;
        }

        /// <summary>Counts a completed simulation frame; max frame time is the lag tell.</summary>
        public void RecordFrame(long frameTicks)
        {
            if (frameTicks < 0) frameTicks = 0;
            frames++;
            windowTotalFrameTicks += frameTicks;
            if (frameTicks > windowMaxFrameTicks) windowMaxFrameTicks = frameTicks;
        }

        public bool ShouldFlush(long nowTicks)
        {
            return nowTicks - windowStart >= windowTicks;
        }

        /// <summary>Emits the window report and resets it. Returns null when nothing was recorded.</summary>
        public string Flush(long nowTicks)
        {
            if (phases.Count == 0 && frames == 0) { windowStart = nowTicks; return null; }
            double windowMs = (nowTicks - windowStart) * 1000.0 / frequency;
            StringBuilder value = new StringBuilder();
            value.Append("window_ms=").Append(windowMs.ToString("0", CultureInfo.InvariantCulture));
            value.Append("; frames=").Append(frames.ToString(CultureInfo.InvariantCulture));
            if (frames > 0)
            {
                value.Append("; frame.avgMs=").Append(Ms(windowTotalFrameTicks / frames).ToString("0.###", CultureInfo.InvariantCulture));
                value.Append("; frame.maxMs=").Append(Ms(windowMaxFrameTicks).ToString("0.###", CultureInfo.InvariantCulture));
            }
            foreach (string name in order)
            {
                Phase entry = phases[name];
                value.Append("; ").Append(name).Append(".count=").Append(entry.Samples.ToString(CultureInfo.InvariantCulture));
                value.Append("; ").Append(name).Append(".avgMs=")
                    .Append(Ms(entry.TotalTicks / (entry.Samples == 0 ? 1 : entry.Samples)).ToString("0.####", CultureInfo.InvariantCulture));
                value.Append("; ").Append(name).Append(".maxMs=").Append(Ms(entry.MaxTicks).ToString("0.###", CultureInfo.InvariantCulture));
            }
            phases.Clear(); order.Clear();
            frames = 0; windowTotalFrameTicks = 0; windowMaxFrameTicks = 0;
            windowStart = nowTicks;
            return value.ToString();
        }

        private double Ms(long ticks) { return ticks * 1000.0 / frequency; }
    }
}
