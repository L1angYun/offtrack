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
    public sealed class V3SkipFlowTests
    {
        [UnityTest]
        public IEnumerator MontageAndClosingSkipOneShotPerPress()
        {
            var scenes = new[]
            {
                (Scene: OfftrackScenes.CutsceneC, Shots: new[] { 2.0, 4 }, Next: OfftrackScenes.CutsceneB),
                (Scene: OfftrackScenes.CutsceneB, Shots: new[] { 5, 7.3, 10.2 }, Next: OfftrackScenes.Title)
            };
            foreach (var entry in scenes)
            {
                yield return SceneManager.LoadSceneAsync(entry.Scene, LoadSceneMode.Single);
                var cutscene = Object.FindAnyObjectByType<CutsceneController>();
                cutscene.AutomaticTick = false;
                yield return null;
                Assert.That(cutscene.IsShowingSkipHint, Is.False, "Only the opening shows the hint: " + entry.Scene);
                Assert.That(cutscene.SkipButton, Is.Not.Null);
                Assert.That(cutscene.SkipButton.gameObject.activeInHierarchy, Is.True);
                foreach (var shot in entry.Shots)
                {
                    cutscene.SkipShot();
                    Assert.That(cutscene.Time, Is.EqualTo(shot).Within(1e-9), entry.Scene);
                    Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(entry.Scene), "One press skips one shot.");
                }
                cutscene.SkipShot();
                Assert.That(cutscene.Time, Is.EqualTo(cutscene.Duration), "A press in the last shot ends the scene.");
                Assert.That(cutscene.SkipButton.gameObject.activeSelf, Is.False, "Nothing is left to skip.");
                cutscene.Advance(0);
                yield return V3JourneyTests.FinishTransition(entry.Next);
            }
        }

        [UnityTest]
        public IEnumerator OpeningHintEndsOnItsOwnAndOnlyTheButtonEndsItEarly()
        {
            yield return SceneManager.LoadSceneAsync(OfftrackScenes.CutsceneA, LoadSceneMode.Single);
            var cutscene = Object.FindAnyObjectByType<CutsceneController>();
            yield return null;
            Assert.That(cutscene.IsShowingSkipHint, Is.True);
            Assert.That(cutscene.Time, Is.Zero, "The opening's clock waits for the hint.");
            cutscene.SkipShot();
            Assert.That(cutscene.IsShowingSkipHint, Is.False, "The button ends the hint first.");
            Assert.That(cutscene.Time, Is.Zero, "Ending the hint starts the opening from its first frame.");
            cutscene.SkipShot();
            Assert.That(cutscene.Time, Is.EqualTo(3.2).Within(1e-9));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator TitleShowsTheCreditsAndTheLevelShowsTheControls()
        {
            yield return SceneManager.LoadSceneAsync(OfftrackScenes.Title, LoadSceneMode.Single);
            var title = Object.FindAnyObjectByType<TitleController>();
            Assert.That(title.Credits, Is.Not.Null);
            Assert.That(title.Credits.text, Is.EqualTo("策划：柳演锡\n程序：Ryō & Vanty\n音乐：银斑"));
            Assert.That(title.Credits.gameObject.activeInHierarchy, Is.True);
            Assert.That(title.Credits.raycastTarget, Is.False, "The credits never take a press.");
            Assert.That(title.Credits.color, Is.EqualTo((Color)Palette.Bone40));

            yield return SceneManager.LoadSceneAsync(OfftrackScenes.Main, LoadSceneMode.Single);
            var shell = Object.FindAnyObjectByType<AdShell>();
            var flow = shell.Flow;
            flow.AutomaticTick = shell.AutomaticTick = false;
            flow.SetApplicationFocus(true);
            var hud = flow.GetComponent<HudView>();
            yield return null;
            Assert.That(hud.ControlsHintText, Is.Empty, "The loading page stays uncluttered.");
            shell.Advance(flow.Config.Shell.BootDuration);
            Assert.That(shell.State, Is.EqualTo(AdShellState.Lottery));
            Assert.That(hud.ControlsHintText, Is.Empty, "The lottery stays uncluttered.");
            Assert.That(shell.AcceptLottery(), Is.True);
            Assert.That(hud.ControlsHintText, Is.EqualTo("↑ ↓ 切换音轨　点击左 / 右半屏也可"), "The controls show from the first guide.");
            ChartBot.StartImmediately(flow);
            Assert.That(hud.ControlsHintText, Is.Not.Empty, "The controls stay on screen while playing.");
        }
    }
}
