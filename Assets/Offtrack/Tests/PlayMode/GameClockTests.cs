using NUnit.Framework;
using Offtrack.Runtime;
using UnityEngine;

namespace Offtrack.Tests.PlayMode
{
    public sealed class GameClockTests
    {
        [Test]
        public void ClockClampsFramesAndDiscardsPausedSamplesRatherThanCatchingUp()
        {
            var config = ScriptableObject.CreateInstance<GameConfig>();
            try
            {
                var source = new SampleTimeSource();
                var clock = new GameClock(config, source);
                source.Sample = 0.20;
                Assert.That(clock.Tick(false), Is.Zero, "Initialization must discard a stale first sample.");
                Assert.That(clock.Tick(false), Is.EqualTo(0.04), "Single-frame simulation time is capped at 40 ms.");
                source.Sample = 0.02;
                Assert.That(clock.Tick(false), Is.EqualTo(0.02));
                Assert.That(clock.Time, Is.EqualTo(0.06).Within(1e-12));

                source.Sample = 10.0;
                Assert.That(clock.Tick(true), Is.Zero);
                Assert.That(clock.Time, Is.EqualTo(0.06).Within(1e-12));
                Assert.That(clock.Tick(false), Is.Zero, "The resumed sample still spans the pause.");
                source.Sample = 0.02;
                Assert.That(clock.Tick(false), Is.EqualTo(0.02));
                Assert.That(clock.Time, Is.EqualTo(0.08).Within(1e-12));

                source.Sample = -1.0;
                Assert.That(clock.Tick(false), Is.Zero, "A backward source sample cannot rewind simulation time.");
                clock.Reset();
                Assert.That(clock.Time, Is.Zero);
                Assert.That(source.ResetCalls, Is.GreaterThanOrEqualTo(2));
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        private sealed class SampleTimeSource : IGameTimeSource
        {
            public double Sample;
            public int ResetCalls;
            public double DeltaTime => Sample;
            public void Reset() { ResetCalls++; }
        }
    }
}
