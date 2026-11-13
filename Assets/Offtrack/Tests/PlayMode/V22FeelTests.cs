using System.Collections;
using System.Linq;
using NUnit.Framework;
using Offtrack.Core;
using Offtrack.Runtime;
using Offtrack.Runtime.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Offtrack.Tests.PlayMode
{
    /// <summary>V2-2 feel and feedback: machine phases, needle response, meter HUD and sound.</summary>
    public sealed class V22FeelTests
    {
        private GameFlow flow;
        private AdShell shell;
        private HudView hud;
        private TonearmView arm;
        private ProceduralAudio sound;

        [UnitySetUp]
        public IEnumerator LoadMain()
        {
            Time.timeScale = 1;
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            flow = Object.FindAnyObjectByType<GameFlow>();
            shell = Object.FindAnyObjectByType<AdShell>();
            hud = Object.FindAnyObjectByType<HudView>();
            arm = Object.FindAnyObjectByType<TonearmView>();
            sound = Object.FindAnyObjectByType<ProceduralAudio>();
            flow.AutomaticTick = shell.AutomaticTick = false;
            flow.SetApplicationFocus(true);
            shell.Advance(flow.Config.Shell.BootDuration);
            Assert.That(shell.AcceptLottery(), Is.True);
            yield return null;
        }

        [Test]
        public void NeedleDropKeepsTheJudgeClockAtZeroAndLandsExactlyOnTimeZero()
        {
            Assert.That(shell.StartPlaying(), Is.True);
            Assert.That(flow.Phase, Is.EqualTo(MachinePhase.Starting));
            Assert.That(flow.State, Is.EqualTo(RunState.Playing));
            flow.Advance(.1);
            Assert.That(flow.Simulation.Time, Is.Zero, "Preparation happens before the first judged sample.");
            Assert.That(flow.Clock.Time, Is.Zero);
            Assert.That(flow.Move(-1).Result, Is.EqualTo("inactive"), "No jump while the needle drops.");
            Assert.That(flow.Simulation.Lane, Is.EqualTo(flow.Config.StartLane));
            // One oversized step: the surplus beyond the drop is discarded, so time zero is observable.
            flow.Advance(1.0);
            Assert.That(flow.Phase, Is.EqualTo(MachinePhase.Running));
            Assert.That(flow.Simulation.Time, Is.Zero);
            Assert.That(flow.DiscRotationDegrees, Is.EqualTo(0).Within(1e-9),
                "At judge time zero the platter is exactly where an immediate start would be.");
            flow.Advance(.25);
            Assert.That(flow.Simulation.Time, Is.EqualTo(.25).Within(1e-12));
        }

        [Test]
        public void PlatterSpinsUpContinuouslyFromRestToFullSpeed()
        {
            double omega = 360.0 / (flow.Config.BeatsPerRevolution * 60.0 / flow.Config.Bpm);
            double drop = flow.Config.Machine.StartDuration;
            double parked = flow.DiscRotationDegrees;
            Assert.That(parked, Is.EqualTo(omega * drop / 2).Within(1e-9));
            shell.StartPlaying();
            Assert.That(flow.DiscRotationDegrees, Is.EqualTo(parked).Within(1e-9), "No jump when the drop begins.");
            double previous = parked;
            double previousSpeed = 0;
            for (int i = 0; i < 6; i++)
            {
                flow.Advance(drop / 6 - 1e-9);
                double speed = previous - flow.DiscRotationDegrees;
                Assert.That(speed, Is.GreaterThan(previousSpeed), "Uniform clockwise acceleration.");
                previousSpeed = speed;
                previous = flow.DiscRotationDegrees;
            }
            Assert.That(previousSpeed / (drop / 6), Is.LessThanOrEqualTo(omega * 1.0001));
        }

        [Test]
        public void JumpHidesTheContactPointWhileAirborneAndShowsItAgainOnLanding()
        {
            flow.StartRun();
            flow.Advance(.6);
            Assert.That(arm.TipVisible, Is.True);
            Assert.That(flow.Move(-1).Result, Is.EqualTo("jump"));
            flow.Advance(.11);
            Assert.That(flow.Simulation.Jump, Is.Not.Null);
            Assert.That(arm.TipVisible, Is.False);
            Assert.That(arm.HeadScale, Is.GreaterThan(1f), "The raised headshell reads closer.");
            Assert.That(arm.Lift, Is.GreaterThan(.9f));
            flow.Advance(.12);
            Assert.That(flow.Simulation.Jump, Is.Null);
            Assert.That(arm.TipVisible, Is.True);
            Assert.That(arm.HeadScale, Is.LessThan(1f), "The landing press makes the end of immunity visible.");
            Assert.That((Color32)arm.ShellColor, Is.EqualTo(Palette.Bone60), "The headshell is dim while settling.");
            flow.Advance(.4);
            Assert.That((Color32)arm.ShellColor, Is.EqualTo(Palette.Bone), "Ready again.");
        }

        [Test]
        public void LiftIsAtLeastTwiceThePrototypeCssDistance()
        {
            var view = flow.Config.View;
            Assert.That(new Vector2(view.LiftX, view.LiftY).magnitude, Is.GreaterThanOrEqualTo(2 * new Vector2(10, 21).magnitude - .01f));
        }

        [Test]
        public void EveryRefusedPressIsAnsweredByTheMachine()
        {
            int refused = 0;
            flow.MoveAttempted += (direction, result) => { if (result.Result != "jump") refused++; };
            flow.StartRun();
            flow.Advance(.1);
            Assert.That(flow.Move(-1).Result, Is.EqualTo("jump"));
            flow.Advance(.05);
            Assert.That(flow.Move(1).Result, Is.EqualTo("airborne"));
            Assert.That(refused, Is.EqualTo(1));
            Assert.That(arm.RejectedResponses, Is.EqualTo(1));
            Assert.That(sound.LastEffect, Is.EqualTo(ProceduralAudio.RejectClip));
            flow.Advance(.045);
            Assert.That(arm.NudgeOffset.magnitude, Is.InRange(2f, 4.01f), "A 3-4 px push toward the requested lane.");
            Assert.That(arm.NudgeOffset.y, Is.LessThan(0), "+1 asks for the inner lane, which is lower on screen.");
            flow.Advance(.1);
            Assert.That(arm.NudgeOffset, Is.EqualTo(Vector2.zero));
            flow.Advance(.1);
            Assert.That(flow.Move(-1).Result, Is.EqualTo("cooldown"));
            Assert.That(refused, Is.EqualTo(2));
            flow.Advance(1);
            Assert.That(flow.Move(-1).Result, Is.EqualTo("edge"));
            Assert.That(refused, Is.EqualTo(3));
        }

        [Test]
        public void CooldownBarRefillsOverTheWholeJumpCycle()
        {
            flow.StartRun();
            flow.Advance(.1);
            Assert.That(HudView.CooldownFor(flow), Is.EqualTo(1));
            flow.Move(1);
            double cycle = flow.Config.JumpDuration + flow.Config.SettleDuration;
            flow.Advance(cycle / 2);
            Assert.That(HudView.CooldownFor(flow), Is.EqualTo(.5f).Within(.02f));
            Assert.That(hud.NeedleStatusText, Is.EqualTo(HudView.NeedleSettling));
            flow.Advance(cycle / 2 + .01);
            Assert.That(HudView.CooldownFor(flow), Is.EqualTo(1));
            Assert.That(hud.NeedleStatusText, Is.EqualTo(HudView.NeedleReady));
        }

        [Test]
        public void TheMeterReplacesTheCallToActionWhenTheRunStarts()
        {
            var idle = GameObject.Find("ReferenceFrame").transform.Find("Idle").gameObject;
            Assert.That(idle.activeSelf, Is.True);
            Assert.That(hud.MeterVisible, Is.False);
            shell.StartPlaying();
            Assert.That(idle.activeSelf, Is.False);
            Assert.That(hud.MeterVisible, Is.True);
            Assert.That(hud.NeedleStatusText, Is.EqualTo(HudView.NeedleDropping));
            flow.Advance(flow.Config.Machine.StartDuration);
            Assert.That(hud.NeedleStatusText, Is.EqualTo(HudView.NeedleReady));
            flow.Move(-1);
            flow.Advance(.05);
            Assert.That(hud.NeedleStatusText, Is.EqualTo(HudView.NeedleAirborne));
        }

        [Test]
        public void TimerShowsAStableValueAndTheRemainingTime()
        {
            flow.StartRun();
            flow.Advance(6.7);
            Assert.That(hud.TimerValueText, Is.EqualTo("06.7"));
            Assert.That(hud.RemainingText, Is.EqualTo("还剩 29.3 秒"));
            Assert.That(hud.TimerText, Is.EqualTo("06.7 / 36s"));
        }

        [Test]
        public void TheTwoFailureKindsHaveDifferentNotices()
        {
            Assert.That(HudView.NoticeFor("jumpedTooLate"), Is.EqualTo(HudView.LateJumpNotice));
            Assert.That(HudView.NoticeFor("landedTooEarly"), Is.EqualTo(HudView.EarlyLandingNotice));
            Assert.That(HudView.LateJumpNotice, Is.Not.EqualTo(HudView.EarlyLandingNotice));
        }

        [Test]
        public void AHitShowsOneStaticNoticeHollowsACellAndTheNoticeExpires()
        {
            flow.StartRun();
            int lives = flow.Simulation.Lives;
            double lastDamage = double.NaN;
            flow.DamageTaken += _ => lastDamage = flow.PresentationTime;
            while (flow.Simulation.Lives == lives) flow.Advance(1.0 / 60);
            Assert.That(hud.DamageNoticeText, Is.EqualTo(HudView.LateJumpNotice));
            var frame = GameObject.Find("ReferenceFrame").transform;
            Assert.That(frame.Find("Life_" + (lives - 1) + "/Hollow").gameObject.activeSelf, Is.True);
            Assert.That((Color32)frame.Find("Life_" + (lives - 1)).GetComponent<FlatShape>().color, Is.EqualTo(Palette.Ink));
            Assert.That(arm.TipRadius, Is.EqualTo(flow.Config.View.TipHitRadius), "The hit enlarges the tip only.");
            Assert.That((Color32)arm.TipColor, Is.EqualTo(Palette.Red));
            for (int step = 0; step < 20; step++)
            {
                flow.Advance(.05);
                Assert.That(hud.DamageNoticeText, Is.Not.Empty, "Static for its whole duration: no blinking.");
            }
            // Later hits restart the notice; wait until 2.5 s have passed since the most recent one.
            for (int step = 0; step < 400 && flow.Phase != MachinePhase.Stopped
                && flow.PresentationTime - lastDamage < flow.Config.ShellView.DamageNoticeSeconds + .05; step++)
                flow.Advance(.05);
            Assert.That(hud.DamageNoticeText, Is.Empty);
        }

        [Test]
        public void TheFailureDialogAppearsOnlyAfterTheMachineHasStopped()
        {
            shell.StartPlaying();
            flow.Advance(flow.Config.Machine.StartDuration);
            while (flow.State == RunState.Playing) flow.Advance(1.0 / 60);
            Assert.That(flow.Simulation.Result.Won, Is.False);
            Assert.That(flow.Phase, Is.EqualTo(MachinePhase.Stopping));
            Assert.That(shell.State, Is.EqualTo(AdShellState.Playing), "No dialog while the machine stops.");
            Assert.That(shell.Restart(), Is.False, "No restart mid-stop.");
            float spin = (float)flow.DiscRotationDegrees;
            flow.Advance(flow.Config.Machine.StopDuration - .01);
            Assert.That(shell.State, Is.EqualTo(AdShellState.Playing));
            Assert.That(arm.Swing, Is.LessThan(.1f), "The arm is back on its rest.");
            Assert.That((float)flow.DiscRotationDegrees, Is.LessThan(spin), "The platter coasts down.");
            flow.Advance(.02);
            Assert.That(flow.Phase, Is.EqualTo(MachinePhase.Stopped));
            Assert.That(shell.State, Is.EqualTo(AdShellState.FailDialog));
            Assert.That(hud.DamageNoticeText, Is.Empty);
        }

        [Test]
        public void PausingFreezesTheStopAndTheDrop()
        {
            shell.StartPlaying();
            flow.Advance(.2);
            double progress = flow.PhaseProgress;
            flow.SetApplicationFocus(false);
            flow.Advance(.3);
            Assert.That(flow.PhaseProgress, Is.EqualTo(progress));
            flow.SetApplicationFocus(true);
            flow.Advance(.1);
            Assert.That(flow.PhaseProgress, Is.GreaterThan(progress));

            // The stop: a skip confirmation opened mid-stop freezes it, and the dialog waits for it.
            M1GameplayTests.FinishNeedleDrop(flow);
            for (int frame = 0; frame < 3000 && flow.State == RunState.Playing; frame++) flow.Advance(1.0 / 60);
            Assert.That(flow.Phase, Is.EqualTo(MachinePhase.Stopping));
            flow.Advance(flow.StopDuration * .4);
            double stopProgress = flow.PhaseProgress;
            shell.Advance(flow.Config.Shell.SkipCountdownDuration);
            Assert.That(shell.RequestSkip(), Is.True);
            flow.Advance(flow.StopDuration * 2);
            Assert.That(flow.PhaseProgress, Is.EqualTo(stopProgress), "A modal freezes the stop.");
            Assert.That(shell.State, Is.EqualTo(AdShellState.ConfirmSkip));
            Assert.That(shell.Stay(), Is.True);
            shell.Advance(flow.Config.Shell.StayResumeDelay + .01);
            flow.Advance(flow.StopDuration);
            Assert.That(flow.Phase, Is.EqualTo(MachinePhase.Stopped));
            Assert.That(shell.State, Is.EqualTo(AdShellState.FailDialog));
        }

        [Test]
        public void GeneratedClipsArePlayableAndFinite()
        {
            Assert.That(sound, Is.Not.Null);
            Assert.That(Object.FindObjectsByType<AudioListener>().Length, Is.EqualTo(1));
            Assert.That(sound.VoiceCount, Is.GreaterThanOrEqualTo(8));
            foreach (var name in new[] { ProceduralAudio.JumpClip, ProceduralAudio.DamageClip, ProceduralAudio.RejectClip,
                ProceduralAudio.BeatClip, ProceduralAudio.KickClip, ProceduralAudio.SnareClip, ProceduralAudio.HatClip })
            {
                var clip = sound.Clips[name];
                Assert.That(clip.samples, Is.GreaterThan(100), name);
                var data = new float[clip.samples];
                Assert.That(clip.GetData(data, 0), Is.True, name);
                Assert.That(data.All(sample => !float.IsNaN(sample) && Mathf.Abs(sample) <= 1f), Is.True, name);
                Assert.That(data.Max(Mathf.Abs), Is.GreaterThan(.05f), name + " must be audible.");
            }
        }

        [UnityTest]
        public IEnumerator GrooveRunsOnlyWhilePlayingAndResynchronisesAfterAPause()
        {
            yield return null;
            Assert.That(sound.GrooveActive, Is.False, "Idle is silent.");
            flow.StartRun();
            yield return null;
            Assert.That(sound.GrooveActive, Is.True);
            int scheduled = sound.ScheduledGrooveEvents;
            Assert.That(scheduled, Is.GreaterThan(0), "The first beat at judge time zero is queued.");
            flow.SetApplicationFocus(false);
            yield return null;
            Assert.That(sound.GrooveActive, Is.False);
            Assert.That(sound.CancelledGrooves, Is.EqualTo(1));
            Assert.That(sound.GetComponentsInChildren<AudioSource>().Any(source => source.isPlaying), Is.False,
                "Cancelling stops every queued and sounding voice.");
            int whilePaused = sound.ScheduledGrooveEvents;
            yield return null;
            Assert.That(sound.ScheduledGrooveEvents, Is.EqualTo(whilePaused), "Nothing is queued while paused.");
            flow.SetApplicationFocus(true);
            // 2.1 s: the next eighth note (about 2.14 s) falls inside the look-ahead window.
            flow.Advance(2.1);
            yield return null;
            Assert.That(sound.GrooveActive, Is.True);
            // Resynchronised from judge time 2.0 without catching up the skipped interval: at most
            // the events of one look-ahead window (4 voices per eighth note) are queued at once.
            int queued = sound.ScheduledGrooveEvents - whilePaused;
            Assert.That(queued, Is.GreaterThan(0));
            Assert.That(queued, Is.LessThanOrEqualTo(4 * ((int)(flow.Config.Audio.ScheduleAhead / (30.0 / flow.Config.Bpm)) + 1)));
        }

        [Test]
        public void TheLaneChoosesTheLeadingDrumAndLiftingDropsTheGroove()
        {
            flow.StartRun();
            flow.Advance(.1);
            var sim = flow.Simulation;
            Assert.That(sound.GrooveGain(sim.Lane, sim), Is.EqualTo(1f));
            Assert.That(sound.GrooveGain((sim.Lane + 1) % 3, sim), Is.EqualTo(flow.Config.Audio.UnfeaturedLaneGain));
            flow.Move(-1);
            flow.Advance(.05);
            Assert.That(sound.GrooveGain(sim.Lane, sim), Is.EqualTo(flow.Config.Audio.AirborneGain).Within(1e-6));
        }
    }
}
