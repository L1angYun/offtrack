using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Offtrack.Core;
using Offtrack.Runtime;
using Offtrack.Runtime.Presentation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Offtrack.Tests.PlayMode
{
    public sealed class M2JourneyTests
    {
        private GameFlow flow;
        private AdShell shell;
        private Keyboard keyboard;
        private Mouse mouse;
        private Touchscreen touchscreen;
        private InputSettings.BackgroundBehavior previousBackground;
#if UNITY_EDITOR
        private InputSettings.EditorInputBehaviorInPlayMode previousEditorRouting;
#endif

        [UnitySetUp]
        public IEnumerator PrepareInput()
        {
            previousBackground = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
            previousEditorRouting = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            Time.timeScale = 1;
            keyboard = InputSystem.AddDevice<Keyboard>();
            mouse = InputSystem.AddDevice<Mouse>();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator RestoreInput()
        {
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
            if (touchscreen != null && touchscreen.added) InputSystem.RemoveDevice(touchscreen);
            InputSystem.settings.backgroundBehavior = previousBackground;
#if UNITY_EDITOR
            InputSystem.settings.editorInputBehaviorInPlayMode = previousEditorRouting;
#endif
            Time.timeScale = 1;
            if (flow != null)
            {
                flow.AutomaticTick = false;
                flow.SetApplicationFocus(true);
            }
            if (shell != null) shell.AutomaticTick = false;
            LogAssert.NoUnexpectedReceived();
            yield return null;
        }

        [UnityTest]
        public IEnumerator MechanicalBeatLightsAndStatusFollowTheRunAndFreezeDuringSkip()
        {
            yield return LoadMain();
            var turntable = Object.FindAnyObjectByType<TurntableView>();
            Assert.That(turntable, Is.Not.Null);
            var lights = shell.Stage.GetComponentsInChildren<FlatShape>(true)
                .Where(shape => shape.name.StartsWith("BeatLight_", StringComparison.Ordinal)).ToArray();
            Assert.That(lights, Has.Length.EqualTo(4));
            var status = shell.Stage.Find("MachineStatus").GetComponent<Text>();
            shell.Advance(flow.Config.Shell.BootDuration);
            shell.AcceptLottery();
            Assert.That(turntable.ActiveBeat, Is.EqualTo(-1));
            Assert.That(status.text, Is.EqualTo("等待落针"));
            Assert.That(lights.All(light => ((Color32)light.color).Equals(Palette.DarkRed40)), Is.True);

            shell.StartPlaying(); M1GameplayTests.FinishNeedleDrop(flow);
            Assert.That(turntable.ActiveBeat, Is.Zero);
            Assert.That(lights.Count(light => ((Color32)light.color).Equals(Palette.Bone)), Is.EqualTo(1));
            double beatDuration = 60.0 / flow.Config.Bpm;
            flow.Advance(beatDuration - 0.01);
            Assert.That(turntable.ActiveBeat, Is.Zero);
            flow.Advance(0.02);
            Assert.That(turntable.ActiveBeat, Is.EqualTo(1), "The lamp switches on the beat without interpolation.");
            Assert.That(lights.Count(light => ((Color32)light.color).Equals(Palette.Bone)), Is.EqualTo(1));
            Assert.That(flow.Move(-1).Result, Is.EqualTo("jump"));
            flow.Advance(flow.Config.JumpDuration + 0.001);
            Assert.That(status.text, Is.EqualTo("● 正在播放 · 第 1 轨"));

            shell.Advance(flow.Config.Shell.SkipCountdownDuration);
            shell.RequestSkip();
            int frozenBeat = turntable.ActiveBeat;
            var frozenColors = lights.Select(light => (Color32)light.color).ToArray();
            shell.Advance(10);
            flow.Advance(10);
            Assert.That(turntable.ActiveBeat, Is.EqualTo(frozenBeat));
            CollectionAssert.AreEqual(frozenColors, lights.Select(light => (Color32)light.color).ToArray());
            shell.Stay();
            shell.Advance(flow.Config.Shell.StayResumeDelay / 2);
            flow.Advance(10);
            Assert.That(turntable.ActiveBeat, Is.EqualTo(frozenBeat));
            CollectionAssert.AreEqual(frozenColors, lights.Select(light => (Color32)light.color).ToArray());
        }

        [UnityTest]
        public IEnumerator ConfirmSkipSecondaryActionIsDimButLegibleOnItsActualFill()
        {
            yield return LoadMain();
            shell.Advance(flow.Config.Shell.BootDuration);
            shell.AcceptLottery();
            shell.Advance(flow.Config.Shell.SkipCountdownDuration);
            Assert.That(shell.RequestSkip(), Is.True);
            var button = shell.Dialog.SecondaryButton;
            Assert.That(button.gameObject.activeInHierarchy, Is.True);
            var copy = button.GetComponentInChildren<Text>();
            var fill = button.GetComponentsInChildren<FlatShape>()
                .Single(shape => shape.name.EndsWith("_Fill", StringComparison.Ordinal));
            Assert.That(copy.text, Is.EqualTo("忍痛放弃"));
            Assert.That(button.sizeDelta, Is.EqualTo(flow.Config.ShellView.DialogButtonSize),
                "Making the secondary action dim must not reduce its clickable area.");
            double foreground = RelativeLuminance((Color32)copy.color);
            double background = RelativeLuminance((Color32)fill.color);
            double contrast = (Math.Max(foreground, background) + 0.05) / (Math.Min(foreground, background) + 0.05);
            Assert.That(contrast, Is.GreaterThanOrEqualTo(2.2),
                $"The actual secondary label must be findable against its fill; contrast={contrast:F2}:1.");
        }

        [UnityTest]
        public IEnumerator ModalDimmerCoversThePhoneAndChangesVisibilityImmediately()
        {
            yield return LoadMain();
            var frame = Object.FindAnyObjectByType<Canvas>().transform.Find("ReferenceFrame") as RectTransform;
            Assert.That(frame, Is.Not.Null);
            var dimmer = frame.Find("DialogDimmer");
            Assert.That(dimmer, Is.Not.Null);
            var dimmerShape = dimmer.GetComponent<FlatShape>();
            Assert.That(dimmerShape, Is.Not.Null);
            Assert.That((Color32)dimmerShape.color, Is.EqualTo((Color32)Palette.DialogDim));
            Assert.That(dimmer.gameObject.activeSelf, Is.False, "Boot is not a modal.");

            shell.Advance(flow.Config.Shell.BootDuration);
            Assert.That(dimmer.gameObject.activeSelf, Is.True, "Lottery dims on the same state transition.");
            Canvas.ForceUpdateCanvases();
            var phoneCorners = new Vector3[4];
            var dimmerCorners = new Vector3[4];
            frame.GetWorldCorners(phoneCorners);
            dimmerShape.rectTransform.GetWorldCorners(dimmerCorners);
            for (int corner = 0; corner < 4; corner++)
                Assert.That(Vector3.Distance(phoneCorners[corner], dimmerCorners[corner]), Is.LessThan(0.001f),
                    "The status bar, header, record and HUD must all lie below the same full-phone dimmer.");
            Assert.That(dimmer.GetSiblingIndex(), Is.LessThan(shell.Dialog.transform.GetSiblingIndex()));
            Assert.That(dimmer.GetSiblingIndex(), Is.GreaterThan(shell.Stage.GetSiblingIndex()));
            Assert.That(shell.Dialog.GetComponentsInChildren<Transform>(true).Any(item => item.name.Contains("Backdrop")), Is.False);
            foreach (var shape in frame.GetComponentsInChildren<FlatShape>(true))
                if (shape != dimmerShape)
                    Assert.That(shape.color.a, Is.EqualTo(1f), "Only the full-phone dimmer may use alpha: " + shape.name);

            shell.AcceptLottery();
            Assert.That(dimmer.gameObject.activeSelf, Is.False);
            shell.Advance(flow.Config.Shell.SkipCountdownDuration);
            shell.StartPlaying(); M1GameplayTests.FinishNeedleDrop(flow);
            shell.RequestSkip();
            Assert.That(dimmer.gameObject.activeSelf, Is.True);
            shell.Stay();
            Assert.That(dimmer.gameObject.activeSelf, Is.False, "Stay removes dimming immediately while the record waits one beat.");
            Assert.That(flow.IsPaused, Is.True);
            shell.Advance(flow.Config.Shell.StayResumeDelay);
            M1GameplayTests.DriveToEndWithoutInput(flow);
            Assert.That(shell.State, Is.EqualTo(AdShellState.FailDialog));
            Assert.That(dimmer.gameObject.activeSelf, Is.True);
            shell.Retry();
            Assert.That(dimmer.gameObject.activeSelf, Is.False);
        }

        [UnityTest]
        public IEnumerator FullFailureRetryStayGiveUpAndExhibitionLoopUsesTheBuiltScenes()
        {
            yield return SceneManager.LoadSceneAsync("CutsceneA", LoadSceneMode.Single);
            var opening = Object.FindAnyObjectByType<CutsceneController>();
            Assert.That(opening, Is.Not.Null);
            opening.AutomaticTick = false;
            Assert.That(opening.IsOpening, Is.True);
            yield return Press(Key.Space);
            Assert.That(opening.Time, Is.EqualTo(16));
            opening.Advance(0.25);
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("CutsceneA"), "Skip must show the final frame for half a second.");
            opening.Advance(0.25);
            yield return WaitForScene("Main");
            BindMain();

            Assert.That(shell.State, Is.EqualTo(AdShellState.Boot));
            var transitions = new List<AdShellState> { shell.State };
            shell.Machine.StateChanged += transitions.Add;
            shell.Advance(1);
            Assert.That(shell.State, Is.EqualTo(AdShellState.Lottery));
            shell.Advance(8);
            Assert.That(shell.Machine.CanSkip, Is.True);
            yield return Click(shell.Dialog.PrimaryButton);
            Assert.That(shell.State, Is.EqualTo(AdShellState.Idle));
            Assert.That(flow.State, Is.EqualTo(RunState.Idle));
            yield return Press(Key.Space);
            Assert.That(shell.State, Is.EqualTo(AdShellState.Playing));
            M1GameplayTests.DriveToEndWithoutInput(flow);
            Assert.That(shell.State, Is.EqualTo(AdShellState.FailDialog));
            Assert.That(flow.Simulation.Result.Won, Is.False);
            yield return Click(shell.Dialog.PrimaryButton);
            Assert.That(shell.State, Is.EqualTo(AdShellState.Idle));
            Assert.That(flow.State, Is.EqualTo(RunState.Idle));
            Assert.That(shell.Machine.CanSkip, Is.True, "Retry cannot restart the ad countdown.");
            yield return Press(Key.Enter);
            Assert.That(shell.State, Is.EqualTo(AdShellState.Playing));
            M1GameplayTests.FinishNeedleDrop(flow);
            M1GameplayTests.StepFor(flow, 1);

            var disc = Object.FindAnyObjectByType<DiscController>();
            var pool = Object.FindAnyObjectByType<ScratchPool>();
            double frozenTime = flow.Simulation.Time;
            float frozenRotation = disc.RotationDegrees;
            Vector3[] frozenScratchPositions = pool.Items.Select(item => item.transform.position).ToArray();
            Quaternion[] frozenScratchRotations = pool.Items.Select(item => item.transform.rotation).ToArray();
            bool[] frozenScratchActivity = pool.Items.Select(item => item.activeSelf).ToArray();
            yield return Click(shell.SkipButton);
            Assert.That(shell.State, Is.EqualTo(AdShellState.ConfirmSkip));
            Assert.That(flow.IsPaused, Is.True);
            for (int frame = 0; frame < 4; frame++)
            {
                shell.Advance(2);
                flow.Advance(2);
                yield return null;
            }
            Assert.That(flow.Simulation.Time, Is.EqualTo(frozenTime));
            Assert.That(flow.Clock.Time, Is.EqualTo(frozenTime));
            Assert.That(disc.RotationDegrees, Is.EqualTo(frozenRotation));
            CollectionAssert.AreEqual(frozenScratchPositions, pool.Items.Select(item => item.transform.position).ToArray());
            CollectionAssert.AreEqual(frozenScratchRotations, pool.Items.Select(item => item.transform.rotation).ToArray());
            CollectionAssert.AreEqual(frozenScratchActivity, pool.Items.Select(item => item.activeSelf).ToArray());
            yield return Click(shell.Dialog.PrimaryButton);
            Assert.That(shell.State, Is.EqualTo(AdShellState.Playing));
            Assert.That(flow.IsPaused, Is.True, "Stay reveals the frozen record for one configured recovery beat.");
            Assert.That(flow.Simulation.Time, Is.EqualTo(frozenTime), "Closing the dialog must not advance or catch up the clock.");
            shell.Advance(flow.Config.Shell.StayResumeDelay - 0.01);
            flow.Advance(1);
            Assert.That(flow.Simulation.Time, Is.EqualTo(frozenTime));
            Assert.That(disc.RotationDegrees, Is.EqualTo(frozenRotation));
            CollectionAssert.AreEqual(frozenScratchPositions, pool.Items.Select(item => item.transform.position).ToArray());
            Assert.That(flow.Move(-1).Result, Is.EqualTo("inactive"));
            shell.Advance(0.011);
            Assert.That(flow.IsPaused, Is.False);
            flow.Advance(0.02);
            Assert.That(flow.Simulation.Time, Is.EqualTo(frozenTime + 0.02).Within(1e-10));
            yield return Click(shell.SkipButton);
            Assert.That(shell.State, Is.EqualTo(AdShellState.ConfirmSkip));
            yield return Click(shell.Dialog.SecondaryButton);
            yield return WaitForScene("CutsceneB");
            CollectionAssert.AreEqual(new[]
            {
                AdShellState.Boot, AdShellState.Lottery, AdShellState.Idle, AdShellState.Playing,
                AdShellState.FailDialog, AdShellState.Idle, AdShellState.Playing,
                AdShellState.ConfirmSkip, AdShellState.Playing, AdShellState.ConfirmSkip, AdShellState.Leaving
            }, transitions);

            var closing = Object.FindAnyObjectByType<CutsceneController>();
            Assert.That(closing, Is.Not.Null);
            Assert.That(closing.IsOpening, Is.False);
            closing.AutomaticTick = false;
            closing.Skip();
            Assert.That(closing.Time, Is.EqualTo(12));
            closing.Advance(0.5);
            Assert.That(closing.IsEnd, Is.True);
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("CutsceneB"));
            yield return Press(Key.Enter);
            yield return WaitForScene("CutsceneA");
            opening = Object.FindAnyObjectByType<CutsceneController>();
            Assert.That(opening.IsOpening, Is.True);
            Assert.That(opening.IsEnd, Is.False);
            Assert.That(opening.Time, Is.LessThan(16));
            opening.AutomaticTick = false;
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator SuccessfulThirtySixSecondRecordShowsCompleteForThreeSecondsThenClosing()
        {
            yield return LoadMain();
            shell.Advance(1);
            Assert.That(shell.AcceptLottery(), Is.True);
            Assert.That(shell.StartPlaying(), Is.True); M1GameplayTests.FinishNeedleDrop(flow);
            int endedEvents = 0;
            flow.RunEnded += _ => endedEvents++;
            var path = Path.Combine(Application.dataPath, "Offtrack", "Tests", "EditMode", "Fixtures", "bot_early.json");
            var fixture = JsonUtility.FromJson<BotFixture>(File.ReadAllText(path));
            Assert.That(fixture.inputs.Length, Is.EqualTo(14));
            int index = 0;
            for (int frame = 0; frame < 3000 && flow.State == RunState.Playing; frame++)
            {
                while (index < fixture.inputs.Length && fixture.inputs[index].frame == frame)
                {
                    Assert.That(flow.Move(fixture.inputs[index].dir).Result, Is.EqualTo("jump"));
                    index++;
                }
                flow.Advance(1.0 / 60);
            }
            Assert.That(index, Is.EqualTo(14));
            Assert.That(endedEvents, Is.EqualTo(1));
            Assert.That(flow.Simulation.Result.Won, Is.True);
            Assert.That(flow.Simulation.Time, Is.EqualTo(36));
            Assert.That(flow.Simulation.Lives, Is.EqualTo(4));
            Assert.That(flow.Simulation.DamageCount, Is.Zero);
            Assert.That(shell.State, Is.EqualTo(AdShellState.Complete));
            shell.Advance(2.5);
            yield return null;
            Assert.That(shell.State, Is.EqualTo(AdShellState.Complete));
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("Main"));
            shell.Advance(0.5);
            yield return WaitForScene("CutsceneB");
            var closing = Object.FindAnyObjectByType<CutsceneController>();
            Assert.That(closing, Is.Not.Null);
            Assert.That(closing.IsOpening, Is.False);
            closing.AutomaticTick = false;
            closing.Advance(12);
            Assert.That(closing.IsEnd, Is.True, "Natural playback also reaches the black exhibition End state.");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator ShellClicksAreConsumedAndStageInputUsesItsOwnShiftedMidline()
        {
            yield return LoadMain();
            shell.Advance(1);
            Assert.That(shell.State, Is.EqualTo(AdShellState.Lottery));
            foreach (Key key in new[] { Key.LeftArrow, Key.RightArrow, Key.A, Key.D, Key.Space, Key.Enter, Key.R })
            {
                yield return Press(key);
                Assert.That(shell.State, Is.EqualTo(AdShellState.Lottery));
                Assert.That(flow.Simulation.Jumps, Is.Zero);
            }
            yield return Click(StagePosition(0.1f, 0.95f));
            Assert.That(flow.Simulation.Jumps, Is.Zero, "Lottery blocks stage pointers.");
            yield return Click(shell.Dialog.PrimaryButton);
            Assert.That(shell.State, Is.EqualTo(AdShellState.Idle));
            Assert.That(flow.Simulation.Jumps, Is.Zero);
            var hud = Object.FindAnyObjectByType<HudView>();
            yield return Click(hud.StartButton);
            Assert.That(shell.State, Is.EqualTo(AdShellState.Playing));
            Assert.That(flow.Simulation.Jumps, Is.Zero, "Play must consume its pointer press.");
            M1GameplayTests.FinishNeedleDrop(flow);
            yield return Click(shell.SkipButton);
            Assert.That(shell.State, Is.EqualTo(AdShellState.Playing), "Countdown clicks cannot open confirmation.");
            Assert.That(flow.Simulation.Jumps, Is.Zero, "A disabled skip button also consumes its pointer press.");
            yield return Click(new Vector2(1, 1));
            Assert.That(flow.Simulation.Jumps, Is.Zero, "The rest of the screen is not a jump surface.");

            RectTransform stage = shell.Stage;
            Vector2 originalPosition = stage.anchoredPosition;
            try
            {
                stage.anchoredPosition = originalPosition + new Vector2(100, 0);
                Canvas.ForceUpdateCanvases();
                yield return null;
                Vector2 leftOfStageCenter = StagePosition(0.45f);
                Assert.That(leftOfStageCenter.x, Is.GreaterThan(Screen.width / 2f),
                    "The regression point must be right of screen center but left of stage center.");
                yield return Click(leftOfStageCenter);
                Assert.That(flow.Simulation.Jumps, Is.EqualTo(1));
                Assert.That(flow.Simulation.Jump.To, Is.EqualTo(0), "Stage left half must jump outward even right of screen center.");
                M1GameplayTests.StepFor(flow, 0.6);
                yield return Click(StagePosition(0.75f));
                Assert.That(flow.Simulation.Jumps, Is.EqualTo(2));
                Assert.That(flow.Simulation.Jump.To, Is.EqualTo(1));
                M1GameplayTests.StepFor(flow, 0.6);
            }
            finally { stage.anchoredPosition = originalPosition; }

            shell.Advance(8);
            int jumps = flow.Simulation.Jumps;
            double time = flow.Simulation.Time;
            yield return Click(shell.SkipButton);
            Assert.That(shell.State, Is.EqualTo(AdShellState.ConfirmSkip));
            Assert.That(flow.Simulation.Jumps, Is.EqualTo(jumps));
            foreach (Key key in new[] { Key.LeftArrow, Key.RightArrow, Key.A, Key.D, Key.Space, Key.Enter, Key.R })
            {
                yield return Press(key);
                Assert.That(shell.State, Is.EqualTo(AdShellState.ConfirmSkip));
                Assert.That(flow.Simulation.Jumps, Is.EqualTo(jumps));
                Assert.That(flow.Simulation.Time, Is.EqualTo(time));
            }
            yield return Click(StagePosition(0.1f, 0.95f));
            yield return Click(StagePosition(0.9f, 0.95f));
            Assert.That(flow.Simulation.Jumps, Is.EqualTo(jumps), "Confirmation blocks both pointer halves.");
            yield return Click(shell.Dialog.PrimaryButton);
            Assert.That(shell.State, Is.EqualTo(AdShellState.Playing));
            Assert.That(flow.Simulation.Jumps, Is.EqualTo(jumps), "Stay must not also jump after dismissing the modal.");
            Assert.That(flow.IsPaused, Is.True);
            foreach (Key key in new[] { Key.LeftArrow, Key.RightArrow, Key.Space, Key.Enter, Key.R })
            {
                yield return Press(key);
                Assert.That(flow.Simulation.Jumps, Is.EqualTo(jumps));
                Assert.That(flow.Simulation.Time, Is.EqualTo(time), "Recovery must reject restarting as well as jumping.");
            }
            yield return Click(StagePosition(0.1f));
            Assert.That(flow.Simulation.Jumps, Is.EqualTo(jumps));
            shell.Advance(flow.Config.Shell.StayResumeDelay);
            Assert.That(flow.IsPaused, Is.False);
        }

        [UnityTest]
        public IEnumerator SimultaneousTouchesCannotDismissLotteryAndStartTheCoveredPlayButton()
        {
            yield return LoadMain();
            shell.Advance(1);
            Assert.That(shell.State, Is.EqualTo(AdShellState.Lottery));
            touchscreen = InputSystem.AddDevice<Touchscreen>();
            var play = Object.FindAnyObjectByType<Canvas>().transform.Find("ReferenceFrame/Idle/PlayButton") as RectTransform;
            Assert.That(play, Is.Not.Null);
            Assert.That(play.gameObject.activeInHierarchy, Is.False, "Lottery initially covers the Play control.");
            Vector2 acceptPoint = ScreenPoint(shell.Dialog.PrimaryButton);
            Vector2 playPoint = ScreenPoint(play);
            QueueTouch(1, UnityEngine.InputSystem.TouchPhase.Began, acceptPoint);
            QueueTouch(2, UnityEngine.InputSystem.TouchPhase.Began, playPoint);
            yield return null;

            Assert.That(shell.State, Is.EqualTo(AdShellState.Idle),
                "The second finger belongs to the same modal input frame; it cannot click newly revealed Play.");
            Assert.That(flow.State, Is.EqualTo(RunState.Idle));
            Assert.That(flow.Simulation.Jumps, Is.Zero);
            QueueTouch(1, UnityEngine.InputSystem.TouchPhase.Ended, acceptPoint);
            QueueTouch(2, UnityEngine.InputSystem.TouchPhase.Ended, playPoint);
            yield return null;
            yield return Click(play);
            Assert.That(shell.State, Is.EqualTo(AdShellState.Playing), "A fresh press can start the revealed control.");
        }

        [UnityTest]
        public IEnumerator SimultaneousTouchesCannotOpenAndImmediatelyAcceptSkipConfirmation()
        {
            yield return LoadMain();
            shell.Advance(1);
            shell.AcceptLottery();
            shell.Advance(8);
            shell.StartPlaying(); M1GameplayTests.FinishNeedleDrop(flow);
            touchscreen = InputSystem.AddDevice<Touchscreen>();
            // Read the actual confirmation control position, then return to Playing before the presses.
            Assert.That(shell.RequestSkip(), Is.True);
            Vector2 abandonPoint = ScreenPoint(shell.Dialog.SecondaryButton);
            Assert.That(shell.Stay(), Is.True);
            shell.Advance(flow.Config.Shell.StayResumeDelay);
            Vector2 skipPoint = ScreenPoint(shell.SkipButton);
            QueueTouch(1, UnityEngine.InputSystem.TouchPhase.Began, skipPoint);
            QueueTouch(2, UnityEngine.InputSystem.TouchPhase.Began, abandonPoint);
            yield return null;

            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("Main"));
            Assert.That(shell.State, Is.EqualTo(AdShellState.ConfirmSkip),
                "Opening confirmation cannot let a simultaneous finger accept its newly visible abandon control.");
            Assert.That(flow.Simulation.Jumps, Is.Zero);
            QueueTouch(1, UnityEngine.InputSystem.TouchPhase.Ended, skipPoint);
            QueueTouch(2, UnityEngine.InputSystem.TouchPhase.Ended, abandonPoint);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ExternalPauseBlocksShellButtonsButModalPauseStillAllowsStay()
        {
            yield return LoadMain();
            shell.Advance(1);
            flow.SetApplicationFocus(false);
            yield return Click(shell.Dialog.PrimaryButton);
            Assert.That(shell.State, Is.EqualTo(AdShellState.Lottery), "Focus loss must block shell actions as well as jumps.");
            flow.SetApplicationFocus(true);
            Time.timeScale = 0;
            yield return Click(shell.Dialog.PrimaryButton);
            Assert.That(shell.State, Is.EqualTo(AdShellState.Lottery), "An externally paused game must not accept a hidden UI press.");
            Time.timeScale = 1;
            yield return Click(shell.Dialog.PrimaryButton);
            shell.Advance(8);
            shell.StartPlaying(); M1GameplayTests.FinishNeedleDrop(flow);
            yield return Click(shell.SkipButton);
            Assert.That(flow.IsPaused, Is.True);
            Assert.That(shell.State, Is.EqualTo(AdShellState.ConfirmSkip));
            yield return Click(shell.Dialog.PrimaryButton);
            Assert.That(shell.State, Is.EqualTo(AdShellState.Playing), "The shell's own modal pause must still permit Stay.");
            Assert.That(flow.IsPaused, Is.True);
            shell.Advance(flow.Config.Shell.StayResumeDelay);
            Assert.That(flow.IsPaused, Is.False);
            Assert.That(flow.Simulation.Jumps, Is.Zero);
        }

        [UnityTest]
        public IEnumerator FailureDialogRejectsKeyboardAndStageInputUntilRetryReturnsIdle()
        {
            yield return LoadMain();
            shell.Advance(1);
            shell.AcceptLottery();
            shell.StartPlaying(); M1GameplayTests.FinishNeedleDrop(flow);
            M1GameplayTests.DriveToEndWithoutInput(flow);
            double endedTime = flow.Simulation.Time;
            Assert.That(shell.State, Is.EqualTo(AdShellState.FailDialog));
            foreach (Key key in new[] { Key.LeftArrow, Key.RightArrow, Key.Space, Key.Enter, Key.R })
            {
                yield return Press(key);
                Assert.That(shell.State, Is.EqualTo(AdShellState.FailDialog));
                Assert.That(flow.Simulation.Time, Is.EqualTo(endedTime));
                Assert.That(flow.Simulation.Jumps, Is.Zero);
            }
            yield return Click(StagePosition(0.1f, 0.95f));
            Assert.That(shell.State, Is.EqualTo(AdShellState.FailDialog));
            yield return Click(shell.Dialog.PrimaryButton);
            Assert.That(shell.State, Is.EqualTo(AdShellState.Idle));
            Assert.That(flow.State, Is.EqualTo(RunState.Idle));
            Assert.That(flow.Simulation.Time, Is.Zero);
            Assert.That(flow.Simulation.Jumps, Is.Zero);
            Assert.That(flow.Simulation.Lives, Is.EqualTo(4));
        }

        [UnityTest]
        public IEnumerator PublicStartRunRespectsShellModalsAndCanRestartACompletedRecord()
        {
            yield return LoadMain();
            int started = 0;
            flow.RunStarted += () => started++;

            void AssertStartRejected(AdShellState expectedState)
            {
                Assert.That(shell.State, Is.EqualTo(expectedState));
                var simulation = flow.Simulation;
                var runState = flow.State;
                double time = simulation.Time;
                int lives = simulation.Lives;
                int jumps = simulation.Jumps;
                var jump = simulation.Jump;
                var result = simulation.Result;
                int eventCount = started;
                flow.StartRun();
                Assert.That(shell.State, Is.EqualTo(expectedState));
                Assert.That(flow.Simulation, Is.SameAs(simulation));
                Assert.That(flow.State, Is.EqualTo(runState));
                Assert.That(simulation.Time, Is.EqualTo(time));
                Assert.That(simulation.Lives, Is.EqualTo(lives));
                Assert.That(simulation.Jumps, Is.EqualTo(jumps));
                Assert.That(simulation.Jump, Is.SameAs(jump));
                Assert.That(simulation.Result, Is.SameAs(result));
                Assert.That(started, Is.EqualTo(eventCount), "A rejected public start must not emit RunStarted.");
            }

            AssertStartRejected(AdShellState.Boot);
            shell.Advance(1);
            AssertStartRejected(AdShellState.Lottery);
            shell.AcceptLottery();
            shell.Advance(8);
            shell.StartPlaying(); M1GameplayTests.FinishNeedleDrop(flow);
            flow.Advance(1);
            Assert.That(flow.Move(-1).Result, Is.EqualTo("jump"));
            shell.RequestSkip();
            AssertStartRejected(AdShellState.ConfirmSkip);
            shell.Stay();
            AssertStartRejected(AdShellState.Playing);
            Assert.That(shell.Restart(), Is.False, "Recovery cannot be bypassed by restarting.");
            shell.Advance(flow.Config.Shell.StayResumeDelay);
            shell.Restart(); M1GameplayTests.FinishNeedleDrop(flow);
            M1GameplayTests.DriveToEndWithoutInput(flow);
            AssertStartRejected(AdShellState.FailDialog);
            shell.Retry();
            flow.StartRun();
            Assert.That(shell.State, Is.EqualTo(AdShellState.Playing));
            CompleteUsingAcceptedFixture();
            Assert.That(shell.State, Is.EqualTo(AdShellState.Complete));
            shell.Advance(2);
            // V2-2: the machine stop (0.8 s) has finished well inside Complete's 3 s in real time.
            M1GameplayTests.FinishMachineStop(flow);
            int beforeRestart = started;

            flow.StartRun();
            Assert.That(started, Is.EqualTo(beforeRestart + 1));
            Assert.That(shell.State, Is.EqualTo(AdShellState.Playing));
            Assert.That(flow.State, Is.EqualTo(RunState.Playing));
            Assert.That(flow.Simulation.Time, Is.Zero);
            Assert.That(flow.Simulation.Lives, Is.EqualTo(4));
            Assert.That(flow.Simulation.Jumps, Is.Zero);
            shell.Advance(3);
            yield return null;
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("Main"),
                "The preceding completed run must not retain a pending transition after a public restart.");
            Assert.That(shell.State, Is.EqualTo(AdShellState.Playing));
        }

        [UnityTest]
        public IEnumerator DisabledShellFreezesTheRunAndReenableRestoresClockAndEndSubscription()
        {
            yield return LoadMain();
            shell.Advance(1);
            shell.AcceptLottery();
            shell.enabled = false;
            var idleSimulation = flow.Simulation;
            int countdown = shell.Machine.SkipSecondsRemaining;
            Assert.That(shell.StartPlaying(), Is.False, "A disabled adapter cannot advance only its state machine.");
            Assert.That(shell.Restart(), Is.False);
            Assert.That(shell.Retry(), Is.False);
            shell.Advance(100);
            Assert.That(shell.Machine.SkipSecondsRemaining, Is.EqualTo(countdown));
            Assert.That(shell.State, Is.EqualTo(AdShellState.Idle));
            Assert.That(flow.State, Is.EqualTo(RunState.Idle));
            Assert.That(flow.Simulation, Is.SameAs(idleSimulation));
            Assert.That(flow.Simulation.Time, Is.Zero);
            shell.enabled = true;
            var source = new SampleTimeSource { Sample = .02 };
            flow.SetTimeSource(source);
            flow.AutomaticTick = true;
            shell.StartPlaying(); M1GameplayTests.FinishNeedleDrop(flow);
            yield return null;
            yield return null;
            Assert.That(flow.Simulation.Time, Is.GreaterThan(0));
            int ended = 0;
            flow.RunEnded += _ => ended++;

            for (int cycle = 0; cycle < 2; cycle++)
            {
                double frozen = flow.Simulation.Time;
                shell.enabled = false;
                source.Sample = 100;
                flow.Advance(1);
                flow.StartRun();
                Assert.That(shell.StartPlaying(), Is.False);
                Assert.That(shell.Restart(), Is.False);
                Assert.That(shell.Retry(), Is.False);
                Assert.That(shell.State, Is.EqualTo(AdShellState.Playing));
                yield return Press(Key.R);
                Assert.That(flow.Simulation.Time, Is.EqualTo(frozen));
                Assert.That(flow.Clock.Time, Is.EqualTo(frozen));
                Assert.That(flow.State, Is.EqualTo(RunState.Playing));
                Assert.That(flow.Move(-1).Result, Is.EqualTo("inactive"));

                shell.enabled = true;
                yield return null;
                Assert.That(flow.Simulation.Time, Is.EqualTo(frozen), "Re-enabling discards the sample spanning disable.");
                source.Sample = .02;
                yield return null;
                Assert.That(flow.Simulation.Time, Is.EqualTo(frozen + .02).Within(1e-10));
                Assert.That(flow.Clock.Time, Is.EqualTo(flow.Simulation.Time));
            }

            flow.AutomaticTick = false;
            M1GameplayTests.DriveToEndWithoutInput(flow);
            Assert.That(ended, Is.EqualTo(1));
            Assert.That(shell.State, Is.EqualTo(AdShellState.FailDialog), "The enabled shell must still observe the end event.");
            shell.enabled = false;
            var endedSimulation = flow.Simulation;
            Assert.That(shell.Retry(), Is.False, "Disabled Retry must not replace the ended session or change shell state.");
            Assert.That(shell.State, Is.EqualTo(AdShellState.FailDialog));
            Assert.That(flow.Simulation, Is.SameAs(endedSimulation));
            Assert.That(flow.State, Is.EqualTo(RunState.Ended));
            shell.enabled = true;
            Assert.That(shell.Retry(), Is.True);
            Assert.That(shell.State, Is.EqualTo(AdShellState.Idle));
            Assert.That(flow.State, Is.EqualTo(RunState.Idle));
            Assert.That(ended, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator RecoveryRetainsItsRemainderWhileUnfocusedOrApplicationPaused()
        {
            foreach (string callback in new[] { "OnApplicationFocus", "OnApplicationPause" })
            {
                yield return LoadMain();
                shell.Advance(flow.Config.Shell.BootDuration);
                shell.AcceptLottery();
                bool pausedValue = callback == "OnApplicationPause";
                try
                {
                    InvokeApplicationCallback(callback, pausedValue);
                    shell.Advance(flow.Config.Shell.SkipCountdownDuration);
                    Assert.That(shell.Machine.CanSkip, Is.True,
                        "The background pause gate applies to recovery, not the ad countdown.");
                    InvokeApplicationCallback(callback, !pausedValue);
                    shell.StartPlaying(); M1GameplayTests.FinishNeedleDrop(flow);
                    flow.Advance(0.2);
                    shell.RequestSkip();
                    shell.Stay();
                    shell.Advance(flow.Config.Shell.StayResumeDelay / 3);
                    double remaining = shell.Machine.ResumeDelayRemaining;
                    double frozenTime = flow.Simulation.Time;
                    InvokeApplicationCallback(callback, pausedValue);
                    shell.Advance(60);
                    flow.Advance(60);
                    Assert.That(shell.Machine.ResumeDelayRemaining, Is.EqualTo(remaining), callback);
                    Assert.That(flow.Simulation.Time, Is.EqualTo(frozenTime));
                    Assert.That(flow.Move(-1).Result, Is.EqualTo("inactive"));
                    Assert.That(shell.Restart(), Is.False);
                    InvokeApplicationCallback(callback, !pausedValue);
                    shell.Advance(remaining - 0.01);
                    Assert.That(flow.IsPaused, Is.True, "Returning to the foreground must finish the remaining beat.");
                    shell.Advance(0.011);
                    Assert.That(flow.IsPaused, Is.False);
                    flow.Advance(0.02);
                    Assert.That(flow.Simulation.Time, Is.EqualTo(frozenTime + 0.02).Within(1e-10));
                }
                finally { InvokeApplicationCallback(callback, !pausedValue); }
            }
        }

        [UnityTest]
        public IEnumerator RecoveryWaitsUntilBothFocusAndApplicationPauseHaveCleared()
        {
            yield return LoadMain();
            shell.Advance(flow.Config.Shell.BootDuration);
            shell.AcceptLottery();
            shell.Advance(flow.Config.Shell.SkipCountdownDuration);
            shell.StartPlaying(); M1GameplayTests.FinishNeedleDrop(flow);
            shell.RequestSkip();
            shell.Stay();
            double remaining = shell.Machine.ResumeDelayRemaining;
            try
            {
                InvokeApplicationCallback("OnApplicationFocus", false);
                InvokeApplicationCallback("OnApplicationPause", true);
                InvokeApplicationCallback("OnApplicationFocus", true);
                shell.Advance(10);
                Assert.That(shell.Machine.ResumeDelayRemaining, Is.EqualTo(remaining));
                InvokeApplicationCallback("OnApplicationPause", false);
                shell.Advance(remaining);
                Assert.That(flow.IsPaused, Is.False);
            }
            finally
            {
                InvokeApplicationCallback("OnApplicationFocus", true);
                InvokeApplicationCallback("OnApplicationPause", false);
            }
        }

        [UnityTest]
        public IEnumerator ForegroundFirstUpdateDiscardsSuspendedRecoveryTimeButKeepsAdElapsedTime()
        {
            yield return LoadMain();
            shell.Advance(flow.Config.Shell.BootDuration);
            shell.AcceptLottery();
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var adSample = typeof(AdShell).GetField("lastRealTime", flags);
            var recoverySample = typeof(AdShell).GetField("lastRecoveryTime", flags);
            var update = typeof(AdShell).GetMethod("Update", flags);
            Assert.That(adSample, Is.Not.Null);
            Assert.That(recoverySample, Is.Not.Null);
            Assert.That(update, Is.Not.Null);
            try
            {
                shell.AutomaticTick = true;
                InvokeApplicationCallback("OnApplicationPause", true);
                double staleSample = Time.realtimeSinceStartupAsDouble - flow.Config.Shell.SkipCountdownDuration - 1;
                adSample.SetValue(shell, staleSample);
                recoverySample.SetValue(shell, staleSample);
                InvokeApplicationCallback("OnApplicationPause", false);
                Assert.That((double)adSample.GetValue(shell), Is.EqualTo(staleSample));
                update.Invoke(shell, null);
                Assert.That(shell.Machine.CanSkip, Is.True, "Resuming must retain the ad's suspended real time.");

                shell.StartPlaying(); M1GameplayTests.FinishNeedleDrop(flow);
                shell.RequestSkip();
                shell.Stay();
                shell.Advance(flow.Config.Shell.StayResumeDelay / 3);
                double remaining = shell.Machine.ResumeDelayRemaining;
                foreach (string callback in new[] { "OnApplicationFocus", "OnApplicationPause" })
                {
                    bool pausedValue = callback == "OnApplicationPause";
                    InvokeApplicationCallback(callback, pausedValue);
                    staleSample = Time.realtimeSinceStartupAsDouble - 10;
                    adSample.SetValue(shell, staleSample);
                    recoverySample.SetValue(shell, staleSample);
                    double beforeResume = Time.realtimeSinceStartupAsDouble;
                    InvokeApplicationCallback(callback, !pausedValue);
                    Assert.That((double)recoverySample.GetValue(shell), Is.GreaterThanOrEqualTo(beforeResume));
                    Assert.That((double)adSample.GetValue(shell), Is.EqualTo(staleSample));
                    update.Invoke(shell, null);
                    Assert.That(shell.Machine.ResumeDelayRemaining, Is.GreaterThan(remaining / 2),
                        "The background interval must not eat the held beat on the first foreground Update.");
                    Assert.That(flow.IsPaused, Is.True);
                    remaining = shell.Machine.ResumeDelayRemaining;
                }
            }
            finally
            {
                shell.AutomaticTick = false;
                InvokeApplicationCallback("OnApplicationFocus", true);
                InvokeApplicationCallback("OnApplicationPause", false);
            }
        }

        [UnityTest]
        public IEnumerator StayStartsRecoveryAtTheClickInsteadOfConsumingThePrecedingSlowFrame()
        {
            yield return LoadMain();
            shell.Advance(flow.Config.Shell.BootDuration);
            shell.AcceptLottery();
            shell.Advance(flow.Config.Shell.SkipCountdownDuration);
            shell.StartPlaying(); M1GameplayTests.FinishNeedleDrop(flow);
            flow.Advance(0.2);
            double frozenTime = flow.Simulation.Time;
            Assert.That(shell.RequestSkip(), Is.True);

            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var sampleField = typeof(AdShell).GetField("lastRealTime", flags);
            var update = typeof(AdShell).GetMethod("Update", flags);
            Assert.That(sampleField, Is.Not.Null);
            Assert.That(update, Is.Not.Null);
            try
            {
                shell.AutomaticTick = true;
                // Reproduce InputRouter's successful click before AdShell.Update in a slow frame,
                // without sleeping or depending on the machine's actual frame rate.
                double staleSample = Time.realtimeSinceStartupAsDouble - flow.Config.Shell.StayResumeDelay - 1;
                sampleField.SetValue(shell, staleSample);
                double beforeClick = Time.realtimeSinceStartupAsDouble;
                Assert.That(shell.Stay(), Is.True);
                Assert.That((double)sampleField.GetValue(shell), Is.GreaterThanOrEqualTo(beforeClick));
                update.Invoke(shell, null);
                Assert.That(shell.Machine.ResumeDelayRemaining, Is.GreaterThan(0),
                    "The elapsed time before Stay must not erase the newly started recovery beat.");
                flow.Advance(1);
                Assert.That(flow.IsPaused, Is.True);
                Assert.That(flow.Simulation.Time, Is.EqualTo(frozenTime));

                shell.AutomaticTick = false;
                sampleField.SetValue(shell, staleSample);
                Assert.That(shell.Stay(), Is.False);
                Assert.That((double)sampleField.GetValue(shell), Is.EqualTo(staleSample),
                    "An out-of-order Stay must not change the shell's clock sample.");
            }
            finally { shell.AutomaticTick = false; }
        }

        [UnityTest]
        public IEnumerator ShellPauseResetsAutomaticClockSampleAndNeverCatchesUpOnStay()
        {
            yield return LoadMain();
            shell.Advance(1);
            shell.AcceptLottery();
            shell.Advance(8);
            var source = new SampleTimeSource { Sample = 0.02 };
            flow.SetTimeSource(source);
            flow.AutomaticTick = true;
            shell.StartPlaying(); M1GameplayTests.FinishNeedleDrop(flow);
            yield return null;
            yield return null;
            Assert.That(flow.Simulation.Time, Is.GreaterThan(0));
            Assert.That(shell.RequestSkip(), Is.True);
            double pausedTime = flow.Simulation.Time;
            source.Sample = 100;
            for (int frame = 0; frame < 3; frame++) yield return null;
            Assert.That(flow.Simulation.Time, Is.EqualTo(pausedTime));
            Assert.That(flow.Clock.Time, Is.EqualTo(pausedTime));
            Assert.That(shell.Stay(), Is.True);
            shell.Advance(flow.Config.Shell.StayResumeDelay - 0.01);
            yield return null;
            Assert.That(flow.IsPaused, Is.True);
            Assert.That(flow.Simulation.Time, Is.EqualTo(pausedTime));
            shell.Advance(0.011);
            Assert.That(flow.IsPaused, Is.False);
            yield return null;
            Assert.That(flow.Simulation.Time, Is.EqualTo(pausedTime), "First resumed sample spans the modal and must be discarded.");
            source.Sample = 0.015;
            yield return null;
            Assert.That(flow.Simulation.Time, Is.EqualTo(pausedTime + 0.015).Within(1e-10));
            Assert.That(flow.Clock.Time, Is.EqualTo(flow.Simulation.Time));
            flow.AutomaticTick = false;
        }

        private IEnumerator LoadMain()
        {
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            BindMain();
            yield return null;
        }

        private void InvokeApplicationCallback(string name, bool value)
        {
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var flowCallback = typeof(GameFlow).GetMethod(name, flags);
            var shellCallback = typeof(AdShell).GetMethod(name, flags);
            Assert.That(flowCallback, Is.Not.Null, name);
            Assert.That(shellCallback, Is.Not.Null, name);
            flowCallback.Invoke(flow, new object[] { value });
            shellCallback.Invoke(shell, new object[] { value });
        }

        private static double RelativeLuminance(Color32 color)
        {
            double Linear(byte channel)
            {
                double srgb = channel / 255.0;
                return srgb <= 0.04045 ? srgb / 12.92 : Math.Pow((srgb + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Linear(color.r) + 0.7152 * Linear(color.g) + 0.0722 * Linear(color.b);
        }

        private void BindMain()
        {
            flow = Object.FindAnyObjectByType<GameFlow>();
            shell = Object.FindAnyObjectByType<AdShell>();
            Assert.That(flow, Is.Not.Null);
            Assert.That(shell, Is.Not.Null);
            flow.AutomaticTick = false;
            flow.SetApplicationFocus(true);
            shell.AutomaticTick = false;
        }

        private IEnumerator Press(Key key)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
            yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
        }

        private IEnumerator Click(RectTransform target)
        {
            Assert.That(target, Is.Not.Null);
            Assert.That(target.gameObject.activeInHierarchy, Is.True, "The tested control must actually be visible.");
            yield return Click(ScreenPoint(target));
        }

        private static Vector2 ScreenPoint(RectTransform target) =>
            RectTransformUtility.WorldToScreenPoint(Camera.main, target.TransformPoint(target.rect.center));

        private void QueueTouch(int id, UnityEngine.InputSystem.TouchPhase phase, Vector2 position) =>
            InputSystem.QueueStateEvent(touchscreen, new TouchState { touchId = id, phase = phase, position = position });

        private IEnumerator Click(Vector2 point)
        {
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point }.WithButton(MouseButton.Left));
            yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point });
            yield return null;
        }

        private Vector2 StagePosition(float fraction, float verticalFraction = 0.5f)
        {
            Rect rect = shell.Stage.rect;
            var local = new Vector3(Mathf.Lerp(rect.xMin, rect.xMax, fraction), Mathf.Lerp(rect.yMin, rect.yMax, verticalFraction));
            return RectTransformUtility.WorldToScreenPoint(Camera.main, shell.Stage.TransformPoint(local));
        }

        private void CompleteUsingAcceptedFixture()
        {
            string path = Path.Combine(Application.dataPath, "Offtrack", "Tests", "EditMode", "Fixtures", "bot_early.json");
            var fixture = JsonUtility.FromJson<BotFixture>(File.ReadAllText(path));
            Assert.That(fixture.inputs.Length, Is.EqualTo(14));
            int index = 0;
            for (int frame = 0; frame < 3000 && flow.State == RunState.Playing; frame++)
            {
                while (index < fixture.inputs.Length && fixture.inputs[index].frame == frame)
                {
                    Assert.That(flow.Move(fixture.inputs[index].dir).Result, Is.EqualTo("jump"));
                    index++;
                }
                flow.Advance(1.0 / 60);
            }
            Assert.That(index, Is.EqualTo(14));
            Assert.That(flow.Simulation.Result, Is.Not.Null);
            Assert.That(flow.Simulation.Result.Won, Is.True);
            Assert.That(flow.Simulation.Time, Is.EqualTo(36));
            Assert.That(flow.Simulation.Lives, Is.EqualTo(4));
        }

        private static IEnumerator WaitForScene(string sceneName)
        {
            for (int frame = 0; frame < 300 && SceneManager.GetActiveScene().name != sceneName; frame++) yield return null;
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(sceneName));
            yield return null;
        }

        [Serializable]
        private sealed class BotFixture { public BotInput[] inputs = Array.Empty<BotInput>(); }

        [Serializable]
        private sealed class BotInput { public int frame = 0; public int dir = 0; }

        private sealed class SampleTimeSource : IGameTimeSource
        {
            public double Sample;
            public double DeltaTime => Sample;
            public void Reset() { }
        }
    }
}
