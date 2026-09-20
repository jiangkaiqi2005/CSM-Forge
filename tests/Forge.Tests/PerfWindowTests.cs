using System;
using CsmForge.Core;

namespace CsmForge.Tests
{
    /// <summary>WP-1.5: the per-phase timing window that turns "it feels laggy" into numbers.</summary>
    public static class PerfWindowTests
    {
        private const long Freq = 1000; // 1000 ticks per second -> 1 tick = 1 ms

        [Case] public static void AccumulatesPerPhaseCountAverageAndMax()
        {
            PerfWindow window = new PerfWindow(0, Freq, 1000);
            window.Record("districts", 10);
            window.Record("districts", 30);
            window.Record("net", 5);
            string report = window.Flush(1000);
            Assert.True(report.IndexOf("districts.count=2", StringComparison.Ordinal) >= 0);
            Assert.True(report.IndexOf("districts.avgMs=20", StringComparison.Ordinal) >= 0);
            Assert.True(report.IndexOf("districts.maxMs=30", StringComparison.Ordinal) >= 0);
            Assert.True(report.IndexOf("net.count=1", StringComparison.Ordinal) >= 0);
        }

        [Case] public static void FrameAverageAndMaxAreReported()
        {
            PerfWindow window = new PerfWindow(0, Freq, 1000);
            window.RecordFrame(16);
            window.RecordFrame(48);
            string report = window.Flush(1000);
            Assert.True(report.IndexOf("frames=2", StringComparison.Ordinal) >= 0);
            Assert.True(report.IndexOf("frame.avgMs=32", StringComparison.Ordinal) >= 0);
            Assert.True(report.IndexOf("frame.maxMs=48", StringComparison.Ordinal) >= 0);
        }

        [Case] public static void FlushResetsTheWindow()
        {
            PerfWindow window = new PerfWindow(0, Freq, 1000);
            window.Record("zones", 7);
            Assert.True(window.Flush(1000) != null);
            Assert.True(window.Flush(2000) == null); // nothing recorded since
        }

        [Case] public static void ShouldFlushOnlyAfterTheWindowElapses()
        {
            PerfWindow window = new PerfWindow(0, Freq, 1000);
            Assert.True(!window.ShouldFlush(999));
            Assert.True(window.ShouldFlush(1000));
        }

        [Case] public static void PhaseCountIsBounded()
        {
            PerfWindow window = new PerfWindow(0, Freq, 1000);
            for (int i = 0; i < 100; i++) window.Record("phase-" + i, 1);
            // never grows past the cap; extra names are dropped rather than growing unbounded
            Assert.True(!window.Record("phase-overflow", 1));
        }

        [Case] public static void NegativeSamplesAreClampedNotPropagated()
        {
            PerfWindow window = new PerfWindow(0, Freq, 1000);
            window.Record("x", -5);
            string report = window.Flush(1000);
            Assert.True(report.IndexOf("x.avgMs=0", StringComparison.Ordinal) >= 0);
        }
    }
}
