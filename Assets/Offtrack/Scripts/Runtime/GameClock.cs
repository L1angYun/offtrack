using System;
using UnityEngine;

namespace Offtrack.Runtime
{
    /// <summary>The replaceable source of elapsed time. Reset discards any pending paused interval.</summary>
    public interface IGameTimeSource
    {
        double DeltaTime { get; }
        void Reset();
    }

    public sealed class UnityDeltaTimeSource : IGameTimeSource
    {
        public double DeltaTime => UnityEngine.Time.deltaTime;
        public void Reset() { }
    }

    /// <summary>Samples the time source once per automatic update, without accumulating paused time.</summary>
    public sealed class GameClock
    {
        private readonly IGameTimeSource timeSource;
        private readonly double maximumFrameDelta;
        private bool discardNextSample;
        private bool wasPaused;

        public GameClock(GameConfig config, IGameTimeSource timeSource = null)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            config.ValidateRuntime();
            maximumFrameDelta = config.MaximumFrameDelta;
            this.timeSource = timeSource ?? new UnityDeltaTimeSource();
            Reset();
        }

        public double Time { get; private set; }

        public double Tick(bool paused)
        {
            if (paused)
            {
                if (!wasPaused) ResetSample();
                wasPaused = true;
                return 0.0;
            }

            if (wasPaused)
            {
                ResetSample();
                wasPaused = false;
            }

            double sample = timeSource.DeltaTime;
            if (discardNextSample)
            {
                discardNextSample = false;
                return 0.0;
            }

            if (double.IsNaN(sample) || double.IsInfinity(sample) || sample <= 0.0) return 0.0;
            double delta = Math.Min(sample, maximumFrameDelta);
            Time += delta;
            return delta;
        }

        public void Reset()
        {
            Time = 0.0;
            wasPaused = false;
            ResetSample();
        }

        internal void ResetSample()
        {
            timeSource.Reset();
            discardNextSample = true;
        }

        internal void Synchronize(double time)
        {
            Time = time;
        }
    }
}
