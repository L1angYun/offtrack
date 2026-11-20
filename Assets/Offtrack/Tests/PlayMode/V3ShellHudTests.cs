using System.Collections;
using System.Linq;
using NUnit.Framework;
using Offtrack.Core;
using Offtrack.Runtime;
using Offtrack.Runtime.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Offtrack.Tests.PlayMode
{
    public sealed class V3ShellHudTests
    {
        private static readonly string[] Legacy = { "Timer", "Progress", "ProgressTrack", "Cooldown", "CooldownBar", "Idle", "IdlePanel", "Play", "PlayButton" };

        private static Graphic InvalidShellGraphic(Transform frame, Transform record) => frame.GetComponentsInChildren<Graphic>()
            .FirstOrDefault(graphic => graphic.isActiveAndEnabled && !graphic.transform.IsChildOf(record) && PaletteAssert.IsDanger(graphic.color));

        private static string[] Meter(Transform frame) => frame.GetComponentsInChildren<Graphic>(true)
            .Where(graphic => graphic.name == "TimerRemaining" || graphic.name.StartsWith("LaneDot_")
                || graphic.transform.parent.name.StartsWith("Life_") || graphic.name.StartsWith("Life_"))
            .Select(graphic => $"{graphic.name}:{graphic.gameObject.activeInHierarchy}:{(Color32)graphic.color}:{(graphic is Text text ? text.text : null)}").ToArray();

        [UnityTest]
        public IEnumerator ShellStaysFreeOfHazardColorsAndLegacyWidgetsAcrossTheJourney()
        {
            yield return SceneManager.LoadSceneAsync(OfftrackScenes.Main);
            var shell = Object.FindAnyObjectByType<AdShell>();
            var flow = shell.Flow;
            shell.AutomaticTick = flow.AutomaticTick = false;
            flow.SetApplicationFocus(true);
            var frame = shell.Stage.parent;
            var record = shell.Stage.Find("RecordGroup");
            Assert.That(record, Is.Not.Null);
            var status = frame.Find("StatusTime").GetComponent<Text>();
            var originalColor = status.color;
            try
            {
                foreach (var shade in new[] { Palette.Danger, Palette.Danger60 })
                {
                    status.color = shade;
                    Assert.That(InvalidShellGraphic(frame, record), Is.SameAs(status));
                }
            }
            finally { status.color = originalColor; }

            void Check(string state)
            {
                Assert.That(InvalidShellGraphic(frame, record), Is.Null, state);
                Assert.That(frame.GetComponentsInChildren<Transform>().Where(item => Legacy.Contains(item.name)), Is.Empty, state);
                Assert.That(frame.GetComponentsInChildren<Text>().Any(text => text.text == "\u70b9\u6211\u5f00\u59cb\uff01\uff01"), Is.False, state);
            }

            shell.Advance(flow.Config.Shell.BootDuration);
            shell.Advance(.31);
            Assert.That(shell.State, Is.EqualTo(AdShellState.Lottery));
            Check("lottery");
            shell.ActivatePrimary();
            Check("lottery pressed");
            SceneTransition.AutomaticTick = false;
            try
            {
                shell.Advance(.31);
                SceneTransition.Advance(.200001);
                shell.Advance(.18);
                SceneTransition.Advance(.181);
                Assert.That(shell.State, Is.EqualTo(AdShellState.Guide));
                Check("guide");
                Assert.That(shell.StartPlaying(), Is.True);
                for (int stage = 0; stage < 4; stage++)
                {
                    flow.Advance(stage == 0 ? .05 : .10);
                    Check("HUD entrance " + stage);
                }
                flow.Advance(flow.PreparationLength);
                Check("playing");
                shell.Advance(flow.Config.Shell.SkipCountdownDuration);
                Check("skip ready");
                Assert.That(shell.RequestSkip(), Is.True);
                Assert.That(shell.State, Is.EqualTo(AdShellState.ConfirmSkip));
                Check("confirmation");
                AssertFrozen();
                shell.ActivatePrimary();
                shell.Advance(.201);
                shell.Advance(flow.Config.Shell.StayResumeDelay);
                float originalOpacity = AudioListener.volume;
                while (flow.Simulation.DamageCount == 0 && flow.State == RunState.Playing) flow.Advance(ChartBot.Step);
                Assert.That(flow.Simulation.DamageCount, Is.Positive);
                Check("hit");
                Assert.That(AudioListener.volume, Is.EqualTo(originalOpacity));
                while (flow.State == RunState.Playing) flow.Advance(ChartBot.Step);
                flow.Advance(flow.StopDuration + .001);
                Assert.That(shell.State, Is.EqualTo(AdShellState.FailDialog));
                Check("failure");
                AssertFrozen();
                Assert.That(shell.Retry(), Is.True);
                M1GameplayTests.FinishRetry(flow);
                M1GameplayTests.FinishNeedleDrop(flow);
                ChartBot.Play(flow, ChartBot.WinningPresses(flow));
                flow.Advance(flow.StopDuration + .001);
                Assert.That(shell.State, Is.EqualTo(AdShellState.Fragment));
                Check("fragment");
                AssertFrozen();
            }
            finally { SceneTransition.AutomaticTick = true; }

            void AssertFrozen()
            {
                Assert.That(Object.FindAnyObjectByType<HudView>().MeterVisible, Is.True);
                Assert.That(frame.Find("TimerRemaining").gameObject.activeInHierarchy, Is.True);
                var expected = Meter(frame);
                Assert.That(expected.Length, Is.GreaterThan(4));
                double judge = flow.Simulation.Time;
                shell.Advance(1.1);
                flow.Advance(1.1);
                Assert.That(flow.Simulation.Time, Is.EqualTo(judge));
                Assert.That(Meter(frame), Is.EqualTo(expected));
            }
        }
    }
}
