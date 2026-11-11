using System.Collections;
using NUnit.Framework;
using Offtrack.Core;
using Offtrack.Runtime;
using Offtrack.Runtime.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Offtrack.Tests.PlayMode
{
    public sealed class M2RealTimeSmokeTests
    {
        [UnityTest]
        public IEnumerator FreshSceneUsesDefaultAutomaticUnityClockWithoutTestOverrides()
        {
            // Deliberately no fixture setup, time injection, tick switches, focus overrides,
            // manual simulation steps, or timeScale changes: exercise the shipped update path.
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            var flow = Object.FindAnyObjectByType<GameFlow>();
            var shell = Object.FindAnyObjectByType<AdShell>();
            var disc = Object.FindAnyObjectByType<DiscController>();
            Assert.That(flow, Is.Not.Null);
            Assert.That(shell, Is.Not.Null);
            Assert.That(disc, Is.Not.Null);
            Assert.That(flow.AutomaticTick, Is.True, "Scene serialization must enable the production clock.");
            Assert.That(shell.AutomaticTick, Is.True);

            double deadline = Time.realtimeSinceStartupAsDouble + flow.Config.Shell.BootDuration + 10.0;
            while (shell.State == AdShellState.Boot && Time.realtimeSinceStartupAsDouble < deadline)
                yield return null;
            Assert.That(shell.State, Is.EqualTo(AdShellState.Lottery), "Default Update must finish Boot in real time.");
            Assert.That(shell.AcceptLottery(), Is.True);
            flow.StartRun();
            double initialTime = flow.Simulation.Time;
            float initialAngle = disc.RotationDegrees;
            int initialFrame = Time.frameCount;
            for (int frame = 0; frame < 12; frame++) yield return null;
            int elapsedFrames = Time.frameCount - initialFrame;
            double advanced = flow.Simulation.Time - initialTime;

            Assert.That(flow.AutomaticTick, Is.True);
            Assert.That(flow.State, Is.EqualTo(RunState.Playing));
            Assert.That(elapsedFrames, Is.GreaterThanOrEqualTo(12));
            Assert.That(advanced, Is.GreaterThan(0.0),
                $"The default Unity clock must advance; appFocused={Application.isFocused}, paused={flow.IsPaused}.");
            Assert.That(advanced, Is.LessThanOrEqualTo(elapsedFrames * 0.04 + 1e-9),
                "The real update loop must honor the 40 ms maximum automatic frame delta.");
            Assert.That(flow.Clock.Time, Is.EqualTo(flow.Simulation.Time).Within(1e-9));
            Assert.That(disc.RotationDegrees, Is.LessThan(initialAngle), "Real time must turn the record clockwise.");
            LogAssert.NoUnexpectedReceived();
        }
    }
}
