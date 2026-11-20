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
    public sealed class V3FeelTests
    {
        [UnityTest]
        public IEnumerator LandingFeedbackAndJudgeTimingsRemainOnTheOperationClock()
        {
            yield return SceneManager.LoadSceneAsync(OfftrackScenes.Main);
            var shell = Object.FindAnyObjectByType<AdShell>();
            var flow = shell.Flow;
            shell.AutomaticTick = false;
            flow.SetApplicationFocus(true);
            flow.AutomaticTick = false;
            shell.Advance(flow.Config.Shell.BootDuration);
            Assert.That(shell.AcceptLottery(), Is.True);
            Assert.That(shell.StartPlaying(), Is.True);
            flow.Advance(flow.PreparationLength);
            var tonearm = Object.FindAnyObjectByType<TonearmView>();
            var audio = Object.FindAnyObjectByType<GameAudio>();
            var ring = GameObject.Find("RecordFeedback").transform.Find("LaneFeedback_0").GetComponent<FlatShape>();
            LandingEvent? landed = null;
            flow.Landed += landing => landed = landing;
            Assert.That(flow.Move(-1).Result, Is.EqualTo("jump"));
            Assert.That(audio.LastEffect, Is.EqualTo(GameAudio.JumpClip));
            Assert.That(flow.Simulation.Jump.End, Is.EqualTo(.22).Within(1e-9));
            Assert.That(flow.Simulation.ReadyAt, Is.EqualTo(.57).Within(1e-9));
            Assert.That(flow.Config.EarlyWindow, Is.EqualTo(.115));
            Assert.That(flow.Config.LateWindow, Is.EqualTo(.145));
            flow.Advance(.219);
            Assert.That(landed, Is.Null);
            Assert.That(ring.gameObject.activeSelf, Is.False);
            flow.Advance(.001);
            Assert.That(landed?.Time, Is.EqualTo(.22).Within(1e-9));
            Assert.That(landed?.Lane, Is.Zero);
            Assert.That(audio.LastEffect, Is.EqualTo(GameAudio.DropClip));
            Assert.That(tonearm.LocalFeedbackOffset.y, Is.InRange(-2f, -1f));
            Assert.That((Color32)ring.color, Is.EqualTo(Palette.Bone60));
            flow.Advance(.149);
            Assert.That(ring.gameObject.activeSelf, Is.True);
            flow.Advance(.002);
            Assert.That(ring.gameObject.activeSelf, Is.False);
            Assert.That(tonearm.LocalFeedbackOffset, Is.EqualTo(Vector2.zero));
        }
    }
}
