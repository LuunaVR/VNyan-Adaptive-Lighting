using System;
using System.Diagnostics;

namespace VNyanAdaptiveLighting.Diagnostics
{
    /// <summary>Lightweight rolling-average timers. No allocations after construction.</summary>
    public sealed class PerformanceMonitor
    {
        public sealed class Timer
        {
            private readonly Stopwatch _sw = new Stopwatch();
            private double _avgMs;
            private bool _has;
            public double LastMs { get; private set; }
            public double AverageMs => _avgMs;
            public long Count { get; private set; }

            public void Begin() => _sw.Restart();

            public void End()
            {
                _sw.Stop();
                LastMs = _sw.Elapsed.TotalMilliseconds;
                _avgMs = _has ? _avgMs * 0.95 + LastMs * 0.05 : LastMs;
                _has = true;
                Count++;
            }
        }

        public readonly Timer Frame = new Timer();
        public readonly Timer Sample = new Timer();

        private int _samples;
        private float _windowStart;
        public float MeasuredSampleRate { get; private set; }

        public void RecordSample(float now)
        {
            _samples++;
            if (now - _windowStart >= 1f)
            {
                MeasuredSampleRate = _samples / Math.Max(0.001f, now - _windowStart);
                _samples = 0;
                _windowStart = now;
            }
        }
    }
}
