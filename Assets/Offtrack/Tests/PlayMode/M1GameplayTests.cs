using System.Collections;
using System.Collections.Generic;
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

namespace Offtrack.Tests.PlayMode
{
    public sealed class M1GameplayTests
    {
        private GameFlow flow;
        private AdShell shell;
        private Keyboard keyboard;
        private Mouse mouse;
        private Touchscreen touchscreen;
        private InputSettings.BackgroundBehavior previousBackgroundBehavior;
#if UNITY_EDITOR
        private InputSettings.EditorInputBehaviorInPlayMode previousEditorInputBehavior;
#endif

        [UnitySetUp]
        public IEnumerator LoadMain()
        {
            // A batch editor has no focused Game View. Route synthetic device events to the player,
            // while GameFlow's explicit focus and time-scale pause rules remain under test.
            previousBackgroundBehavior = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
            previousEditorInputBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            Time.timeScale = 1f;
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            flow = Object.FindAnyObjectByType<GameFlow>();
            Assert.That(flow, Is.Not.Null, "Main must contain its configured GameFlow.");
            flow.AutomaticTick = false;
            flow.SetApplicationFocus(true);
            shell = Object.FindAnyObjectByType<AdShell>();
            Assert.That(shell, Is.Not.Null, "Main must contain its configured advertising shell.");
            shell.AutomaticTick = false;
            shell.Advance(flow.Config.Shell.BootDuration);
            Assert.That(shell.AcceptLottery(), Is.True);
            Assert.That(shell.State, Is.EqualTo(AdShellState.Idle));
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator RestoreInputAndTime()
        {
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
            if (touchscreen != null && touchscreen.added) InputSystem.RemoveDevice(touchscreen);
            keyboard = null;
            mouse = null;
            touchscreen = null;
            InputSystem.settings.backgroundBehavior = previousBackgroundBehavior;
#if UNITY_EDITOR
            InputSystem.settings.editorInputBehaviorInPlayMode = previousEditorInputBehavior;
#endif
            Time.timeScale = 1f;
            if (flow != null) flow.SetApplicationFocus(true);
            LogAssert.NoUnexpectedReceived();
            yield return null;
        }

        [UnityTest]
        public IEnumerator MainBeginsIdleAndDiscRemainsStationary()
        {
            var disc = Object.FindAnyObjectByType<DiscController>();
            Assert.That(disc, Is.Not.Null);
            Assert.That(flow.State, Is.EqualTo(RunState.Idle));
            double startTime = flow.Simulation.Time;
            float startRotation = disc.RotationDegrees;

            flow.Advance(0.04);
            yield return null;
            flow.Advance(0.04);
            yield return null;

            Assert.That(flow.State, Is.EqualTo(RunState.Idle));
            Assert.That(flow.Simulation.Time, Is.EqualTo(startTime));
            Assert.That(disc.RotationDegrees, Is.EqualTo(startRotation));
            // V2-2: the approved placeholder sound exists, but an idle machine plays nothing.
            Assert.That(Object.FindAnyObjectByType<GameAudio>(), Is.Not.Null);
            Assert.That(Object.FindObjectsByType<AudioSource>().Any(source => source.isPlaying), Is.False,
                "An idle record must stay silent.");
        }

        [UnityTest]
        public IEnumerator ReferenceFrameFitsAndStaysCenteredAtPortraitAndLandscapeCameraSizes()
        {
            var camera = Camera.main;
            var canvas = Object.FindAnyObjectByType<Canvas>();
            Assert.That(camera, Is.Not.Null);
            Assert.That(canvas, Is.Not.Null);
            Assert.That(canvas.renderMode, Is.EqualTo(RenderMode.ScreenSpaceCamera));
            Assert.That(canvas.worldCamera, Is.EqualTo(camera));
            var frame = canvas.transform.Find("ReferenceFrame") as RectTransform;
            Assert.That(frame, Is.Not.Null);
            var sizes = new[] { new Vector2Int(780, 1688), new Vector2Int(585, 1266), new Vector2Int(1920, 1080) };
            var screenBefore = new Vector2Int(Screen.width, Screen.height);
            RenderTexture previousTarget = camera.targetTexture;
            float previousAspect = camera.aspect;
            var worldCorners = new Vector3[4];

            foreach (Vector2Int size in sizes)
            {
                var target = new RenderTexture(size.x, size.y, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
                try
                {
                    target.Create();
                    camera.targetTexture = target;
                    camera.aspect = (float)size.x / size.y;
                    Canvas.ForceUpdateCanvases();
                    yield return null;
                    Canvas.ForceUpdateCanvases();
                    yield return null;
                    Canvas.ForceUpdateCanvases();

                    Assert.That(new Vector2Int(Screen.width, Screen.height), Is.EqualTo(screenBefore),
                        "The test changes only camera output size, so adaptation must use the render target.");
                    frame.GetWorldCorners(worldCorners);
                    var viewportCorners = worldCorners.Select(camera.WorldToViewportPoint).ToArray();
                    string context = $"target={size.x}x{size.y}, screen={Screen.width}x{Screen.height}, " +
                        $"bottomLeft={viewportCorners[0]}, topRight={viewportCorners[2]}, scale={frame.lossyScale}";
                    TestContext.WriteLine(context);
                    foreach (Vector3 point in viewportCorners)
                    {
                        Assert.That(point.z, Is.GreaterThan(0f), context);
                        Assert.That(point.x, Is.InRange(-0.0001f, 1.0001f), "Reference frame is horizontally cropped. " + context);
                        Assert.That(point.y, Is.InRange(-0.0001f, 1.0001f), "Reference frame is vertically cropped. " + context);
                    }
                    Vector3 center = camera.WorldToViewportPoint(frame.TransformPoint(frame.rect.center));
                    Assert.That(center.x, Is.EqualTo(0.5f).Within(0.0001f), context);
                    Assert.That(center.y, Is.EqualTo(0.5f).Within(0.0001f), context);
                    Assert.That(frame.lossyScale.x, Is.EqualTo(frame.lossyScale.y).Within(0.000001f),
                        "Reference frame must scale uniformly. " + context);

                    // Independently derive the maximal contained rectangle from the specified 780x1688 frame.
                    float fitScale = Mathf.Min(size.x / 780f, size.y / 1688f);
                    float expectedWidth = 780f * fitScale / size.x;
                    float expectedHeight = 1688f * fitScale / size.y;
                    Assert.That(viewportCorners[2].x - viewportCorners[0].x,
                        Is.EqualTo(expectedWidth).Within(0.0001f), "Frame must occupy the fitted viewport width. " + context);
                    Assert.That(viewportCorners[2].y - viewportCorners[0].y,
                        Is.EqualTo(expectedHeight).Within(0.0001f), "Frame must occupy the fitted viewport height. " + context);
                }
                finally
                {
                    camera.targetTexture = previousTarget;
                    camera.aspect = previousAspect;
                    target.Release();
                    Object.Destroy(target);
                }
            }
        }

        [UnityTest]
        public IEnumerator HudOnlyRewritesTimerWhenTheDisplayedTenthChanges()
        {
            var hud = Object.FindAnyObjectByType<HudView>();
            Assert.That(hud, Is.Not.Null);
            flow.StartRun();
            Assert.That(hud.TimerText, Is.EqualTo("00.0 / 30s"));
            int initialWrites = hud.TimerWriteCount;
            flow.Advance(0.01);
            flow.Advance(0.01);
            flow.Advance(0.01);
            yield return null;
            Assert.That(hud.TimerText, Is.EqualTo("00.0 / 30s"));
            Assert.That(hud.TimerWriteCount, Is.EqualTo(initialWrites), "Identical text must not rebuild the UI every frame.");

            StepFor(flow, 0.10);
            yield return null;
            Assert.That(hud.TimerText, Is.EqualTo("00.1 / 30s"));
            Assert.That(hud.TimerWriteCount, Is.EqualTo(initialWrites + 1));
        }

        [UnityTest]
        public IEnumerator PlayingRotatesClockwiseAndReusesTheSameThirtyTwoScratchObjects()
        {
            var disc = Object.FindAnyObjectByType<DiscController>();
            var pool = Object.FindAnyObjectByType<ScratchPool>();
            Assert.That(pool, Is.Not.Null);
            Assert.That(pool.Capacity, Is.EqualTo(32));
            Assert.That(pool.Items.Count, Is.EqualTo(32));
            var originalObjects = PoolObjects(pool);
            int starts = 0;
            flow.RunStarted += () => starts++;

            flow.StartRun();
            float startRotation = disc.RotationDegrees;
            StepFor(flow, 1.0);
            yield return null;

            Assert.That(starts, Is.EqualTo(1));
            Assert.That(flow.State, Is.EqualTo(RunState.Playing));
            Assert.That(flow.Simulation.Time, Is.EqualTo(1.0).Within(1e-10));
            Assert.That(disc.RotationDegrees, Is.LessThan(startRotation), "Clockwise is negative Z.");
            double degreesPerSecond = 360.0 / (flow.Config.BeatsPerRevolution * 60.0 / flow.Config.Bpm);
            Assert.That(degreesPerSecond, Is.EqualTo(48.0).Within(1e-9), "128 BPM / 16 beats gives 48 degrees per second.");
            Assert.That(Mathf.DeltaAngle(disc.transform.eulerAngles.z, (float)(-degreesPerSecond * flow.Simulation.Time)),
                Is.EqualTo(0f).Within(0.001f));
            Assert.That(pool.ActiveCount, Is.GreaterThanOrEqualTo(2));
            CollectionAssert.AreEquivalent(originalObjects, PoolObjects(pool));

            ExpectNoInputDamageLogs(flow, 3);
            StepFor(flow, 12.0);
            yield return null;
            Assert.That(pool.Capacity, Is.EqualTo(32));
            CollectionAssert.AreEquivalent(originalObjects, PoolObjects(pool), "Pool recycle must preserve every object.");
            Assert.That(pool.Items.All(item => item != null), Is.True);
        }

        [UnityTest]
        public IEnumerator ReenabledScratchPoolRetiresObjectsItMissedWhileDisabled()
        {
            var pool = Object.FindAnyObjectByType<ScratchPool>();
            var originalObjects = PoolObjects(pool);
            flow.StartRun();
            StepFor(flow, 2.0);
            // V2 reveals scratches from the far side, three-quarters of a revolution ahead; the
            // expected count follows the scene's own chart (M4: side A).
            int visible = VisibleNotes(flow);
            Assert.That(visible, Is.GreaterThan(1));
            Assert.That(pool.ActiveCount, Is.EqualTo(visible));
            pool.enabled = false;
            try
            {
                // This large explicit step is solely a presentation probe late in the side (28 s,
                // still playing). It intentionally does not claim to validate gameplay survival.
                flow.Advance(26.0);
                Assert.That(flow.State, Is.EqualTo(RunState.Playing));
                Assert.That(pool.Items.Count(item => item.activeSelf), Is.EqualTo(visible),
                    "Disabled views must miss refreshes so this test reproduces stale visual slots.");
                int expected = VisibleNotes(flow);
                Assert.That(expected, Is.GreaterThan(0), "The rebuild half must have notes to show.");
                Assert.That(expected, Is.LessThan(visible));
                pool.enabled = true;
                yield return null;
                Assert.That(pool.ActiveCount, Is.EqualTo(expected));
                Assert.That(pool.Items.Count(item => item.activeSelf), Is.EqualTo(expected),
                    "Re-enabling must hide unassigned old slots before rebuilding visible notes.");
                CollectionAssert.AreEquivalent(originalObjects, PoolObjects(pool));
            }
            finally { pool.enabled = true; }
        }

        [UnityTest]
        public IEnumerator JumpKeepsOriginLaneUntilLandingAndRejectsAirborneAndCooldownMoves()
        {
            flow.StartRun();
            Assert.That(flow.Simulation.Lane, Is.EqualTo(1));
            flow.Move(-1);
            Assert.That(flow.Simulation.Jumps, Is.EqualTo(1));
            Assert.That(flow.Simulation.Jump, Is.Not.Null);
            Assert.That(flow.Simulation.Lane, Is.EqualTo(1));

            flow.Move(1);
            Assert.That(flow.Simulation.Jumps, Is.EqualTo(1), "A move while airborne must be rejected.");
            StepFor(flow, 0.20);
            Assert.That(flow.Simulation.Lane, Is.EqualTo(1));
            flow.Advance(0.021);
            yield return null;
            Assert.That(flow.Simulation.Jump, Is.Null);
            Assert.That(flow.Simulation.Lane, Is.EqualTo(0));
            Assert.That(flow.Simulation.LastLanding, Is.EqualTo(0.22).Within(1e-10));

            flow.Move(1);
            Assert.That(flow.Simulation.Jumps, Is.EqualTo(1), "Landing must retain its settle cooldown.");
            StepFor(flow, 0.36);
            flow.Move(1);
            Assert.That(flow.Simulation.Jumps, Is.EqualTo(2));
            Assert.That(flow.Simulation.Jump, Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator FailureEmitsOneEndEventAndRestartResetsTheWholeRun()
        {
            int starts = 0;
            int endings = 0;
            RunResult ended = default;
            flow.RunStarted += () => starts++;
            flow.RunEnded += result => { endings++; ended = result; };
            var pool = Object.FindAnyObjectByType<ScratchPool>();
            var originalObjects = PoolObjects(pool);
            flow.StartRun();
            DriveToEndWithoutInput(flow);
            yield return null;

            Assert.That(flow.State, Is.EqualTo(RunState.Ended));
            Assert.That(endings, Is.EqualTo(1));
            Assert.That(ended.Won, Is.False);
            Assert.That(flow.Simulation.Lives, Is.Zero);
            double endedTime = flow.Simulation.Time;
            flow.Advance(0.04);
            Assert.That(flow.Simulation.Time, Is.EqualTo(endedTime));
            Assert.That(endings, Is.EqualTo(1));

            Assert.That(shell.Retry(), Is.True);
            Assert.That(flow.State, Is.EqualTo(RunState.Idle), "Retry returns to the stationary idle record.");
            Assert.That(shell.StartPlaying(), Is.True);
            yield return null;
            Assert.That(starts, Is.EqualTo(2));
            Assert.That(endings, Is.EqualTo(1));
            Assert.That(flow.State, Is.EqualTo(RunState.Playing));
            Assert.That(flow.Simulation.Time, Is.Zero);
            Assert.That(flow.Simulation.Lane, Is.EqualTo(1));
            Assert.That(flow.Simulation.Lives, Is.EqualTo(4));
            Assert.That(flow.Simulation.Jumps, Is.Zero);
            Assert.That(flow.Simulation.DamageCount, Is.Zero);
            Assert.That(flow.Simulation.ReadyAt, Is.Zero);
            Assert.That(flow.Simulation.InvincibleUntil, Is.Zero);
            Assert.That(flow.Simulation.LastLanding, Is.EqualTo(-100));
            Assert.That(flow.Simulation.Jump, Is.Null);
            Assert.That(flow.Simulation.Result, Is.Null);
            Assert.That(flow.Simulation.Notes.All(note => !note.Hit && !note.Passed), Is.True,
                "A restart must clear the judgment state of every chart note.");
            CollectionAssert.AreEquivalent(originalObjects, PoolObjects(pool));
            // V2-2: the platter spins up during the needle drop and is at angle zero at judge time zero.
            FinishNeedleDrop(flow);
            Assert.That(flow.Simulation.Time, Is.Zero);
            Assert.That(Object.FindAnyObjectByType<DiscController>().RotationDegrees, Is.Zero);
        }

        [UnityTest]
        public IEnumerator FatalHitKeepsTheRedNeedleTipWhileTheJudgeClockIsFrozen()
        {
            var tonearm = Object.FindAnyObjectByType<TonearmView>();
            Assert.That(tonearm, Is.Not.Null);
            flow.StartRun();
            DriveToEndWithoutInput(flow, finishStop: false);
            yield return null;

            Assert.That(flow.Simulation.Result.Won, Is.False);
            Assert.That(tonearm.TipVisible, Is.True, "The stop holds the fatal pose before lifting.");
            Assert.That(flow.Simulation.InvincibleUntil, Is.GreaterThan(flow.Simulation.Time),
                "The fatal hit leaves an unexpired immunity timestamp while simulation time is frozen.");
            // V2-2 (review #3e): the fatal hit is the one GDD 5.3 tip change; it stays red rather than
            // snapping back to bone while the machine stops.
            Assert.That((Color32)tonearm.TipColor, Is.EqualTo(Palette.Red),
                "The fatal hit keeps the red tip.");
            double endedTime = flow.Simulation.Time;
            for (int frame = 0; frame < 3; frame++) yield return null;
            flow.Advance(flow.StopDuration * flow.Config.View.StopHoldShare * .5);
            Assert.That(flow.Simulation.Time, Is.EqualTo(endedTime));
            Assert.That(tonearm.TipVisible, Is.True);
            Assert.That((Color32)tonearm.TipColor, Is.EqualTo(Palette.Red));
            FinishMachineStop(flow);
            Assert.That(tonearm.TipVisible, Is.False, "Lifted and parked after the stop.");
        }

        [UnityTest]
        public IEnumerator AcceptedBotInputCompletesTheActualSceneWithAllFourLivesAndOneWinEvent()
        {
            // M4: the plan is computed for the scene's own (side-A) chart and simulated with the same steps.
            var presses = ChartBot.WinningPresses(flow);
            int endings = 0;
            RunResult result = null;
            flow.RunEnded += value => { endings++; result = value; };
            flow.StartRun();
            int inputIndex = ChartBot.Play(flow, presses);
            yield return null;

            Assert.That(flow.State, Is.EqualTo(RunState.Ended));
            Assert.That(inputIndex, Is.EqualTo(presses.Length));
            Assert.That(endings, Is.EqualTo(1));
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Won, Is.True);
            Assert.That(result.Time, Is.EqualTo(flow.Config.Duration));
            Assert.That(result.Lives, Is.EqualTo(4));
            Assert.That(result.Jumps, Is.EqualTo(presses.Length));
            Assert.That(result.DamageCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator FocusLossAndTimeScalePauseFreezeTimeInputAndRotationWithoutCatchUp()
        {
            flow.StartRun();
            flow.Advance(0.02);
            var disc = Object.FindAnyObjectByType<DiscController>();
            double frozenTime = flow.Simulation.Time;
            float frozenAngle = disc.RotationDegrees;
            flow.SetApplicationFocus(false);
            flow.Move(-1);
            flow.Advance(0.04);
            yield return null;
            Assert.That(flow.IsPaused, Is.True);
            Assert.That(flow.Simulation.Time, Is.EqualTo(frozenTime));
            Assert.That(flow.Simulation.Jumps, Is.Zero);
            Assert.That(disc.RotationDegrees, Is.EqualTo(frozenAngle));

            flow.SetApplicationFocus(true);
            flow.Advance(0.02);
            Assert.That(flow.Simulation.Time, Is.EqualTo(frozenTime + 0.02).Within(1e-10));
            Time.timeScale = 0f;
            flow.Move(1);
            flow.Advance(0.04);
            yield return null;
            Assert.That(flow.IsPaused, Is.True);
            Assert.That(flow.Simulation.Time, Is.EqualTo(frozenTime + 0.02).Within(1e-10));
            Assert.That(flow.Simulation.Jumps, Is.Zero);
            Time.timeScale = 1f;
            flow.Advance(0.02);
            Assert.That(flow.Simulation.Time, Is.EqualTo(frozenTime + 0.04).Within(1e-10));
        }

        [UnityTest]
        public IEnumerator KeyboardStartsMovesOnPressAndDoesNotRepeatAHeldDirection()
        {
            keyboard = InputSystem.AddDevice<Keyboard>();
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Space));
            yield return null;
            Assert.That(flow.State, Is.EqualTo(RunState.Playing), "Space must start within one frame. " + InputDiagnostics());
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
            FinishNeedleDrop(flow);

            flow.Move(-1);
            StepFor(flow, 0.60);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.D));
            yield return null;
            Assert.That(flow.Simulation.Jumps, Is.EqualTo(2), "D must start the next jump in its press frame.");
            StepFor(flow, 0.60);
            yield return null;
            yield return null;
            Assert.That(flow.Simulation.Lane, Is.EqualTo(1));
            Assert.That(flow.Simulation.Jumps, Is.EqualTo(2), "Holding D beyond cooldown must not repeat.");

            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.LeftArrow));
            yield return null;
            Assert.That(flow.Simulation.Jumps, Is.EqualTo(3));
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.R));
            yield return null;
            Assert.That(flow.State, Is.EqualTo(RunState.Playing));
            Assert.That(flow.Simulation.Time, Is.Zero, "R must restart a running game.");
            Assert.That(flow.Simulation.Jumps, Is.Zero);
        }

        [UnityTest]
        public IEnumerator AutomaticLoopHandlesInputBeforeItsClockStepAndDiscardsPausedIntervals()
        {
            var source = new ControlledTimeSource { Sample = 0.02 };
            flow.SetTimeSource(source);
            flow.AutomaticTick = true;
            flow.StartRun();
            try
            {
                yield return null;
                Assert.That(flow.Simulation.Time, Is.Zero, "The first automatic sample must be warmed and discarded.");
                yield return null;
                Assert.That(flow.Simulation.Time, Is.EqualTo(0.02).Within(1e-10));

                double beforePress = flow.Simulation.Time;
                keyboard = InputSystem.AddDevice<Keyboard>();
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.A));
                yield return null;
                Assert.That(flow.Simulation.Jumps, Is.EqualTo(1), InputDiagnostics());
                Assert.That(flow.Simulation.Jump, Is.Not.Null);
                Assert.That(flow.Simulation.Jump.From, Is.EqualTo(1));
                Assert.That(flow.Simulation.Jump.To, Is.EqualTo(0));
                Assert.That(flow.Simulation.Jump.Start, Is.EqualTo(beforePress).Within(1e-10),
                    "Input must use the previous frame's time before GameFlow.Update steps its clock.");
                Assert.That(flow.Simulation.Time, Is.EqualTo(beforePress + 0.02).Within(1e-10),
                    "The same automatic frame must then advance by the controlled sample.");
                Assert.That(flow.Clock.Time, Is.EqualTo(flow.Simulation.Time).Within(1e-10));

                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                double frozen = flow.Simulation.Time;
                source.Sample = 10.0;
                flow.SetApplicationFocus(false);
                for (int frame = 0; frame < 3; frame++) yield return null;
                Assert.That(flow.Simulation.Time, Is.EqualTo(frozen));
                Assert.That(flow.Clock.Time, Is.EqualTo(frozen));

                flow.SetApplicationFocus(true);
                yield return null;
                Assert.That(flow.Simulation.Time, Is.EqualTo(frozen), "Resume must discard the sample spanning focus loss.");
                source.Sample = 0.02;
                yield return null;
                Assert.That(flow.Simulation.Time, Is.EqualTo(frozen + 0.02).Within(1e-10));

                frozen = flow.Simulation.Time;
                Time.timeScale = 0f;
                source.Sample = 10.0;
                yield return null;
                yield return null;
                Assert.That(flow.Simulation.Time, Is.EqualTo(frozen));
                Assert.That(flow.Clock.Time, Is.EqualTo(frozen));
                Time.timeScale = 1f;
                yield return null;
                Assert.That(flow.Simulation.Time, Is.EqualTo(frozen), "Time-scale resume must also discard its stale sample.");
                source.Sample = 0.015;
                yield return null;
                Assert.That(flow.Simulation.Time, Is.EqualTo(frozen + 0.015).Within(1e-10));
                Assert.That(flow.Clock.Time, Is.EqualTo(flow.Simulation.Time).Within(1e-10));
            }
            finally
            {
                flow.AutomaticTick = false;
                Time.timeScale = 1f;
                flow.SetApplicationFocus(true);
            }
        }

        [UnityTest]
        public IEnumerator MouseStartsOnlyInsideThePlayButtonThenMapsStageHalvesToAdjacentTracks()
        {
            mouse = InputSystem.AddDevice<Mouse>();
            InputSystem.QueueStateEvent(mouse, new MouseState { position = new Vector2(4f, 4f) }.WithButton(MouseButton.Left));
            yield return null;
            Assert.That(flow.State, Is.EqualTo(RunState.Idle), "Clicking outside Play must leave Idle intact.");
            InputSystem.QueueStateEvent(mouse, new MouseState());
            yield return null;

            var hud = Object.FindAnyObjectByType<HudView>();
            Assert.That(hud.StartButton, Is.Not.Null);
            Vector2 buttonPoint = RectTransformUtility.WorldToScreenPoint(Camera.main, hud.StartButton.position);
            InputSystem.QueueStateEvent(mouse, new MouseState { position = buttonPoint }.WithButton(MouseButton.Left));
            yield return null;
            Assert.That(flow.State, Is.EqualTo(RunState.Playing), InputDiagnostics());
            Assert.That(flow.Simulation.Jumps, Is.Zero, "The starting click must not also move the needle.");
            InputSystem.QueueStateEvent(mouse, new MouseState());
            yield return null;
            FinishNeedleDrop(flow);

            InputSystem.QueueStateEvent(mouse, new MouseState { position = StagePosition(0.25f) }.WithButton(MouseButton.Left));
            yield return null;
            Assert.That(flow.Simulation.Jumps, Is.EqualTo(1));
            StepFor(flow, 0.60);
            Assert.That(flow.Simulation.Lane, Is.EqualTo(0));
            InputSystem.QueueStateEvent(mouse, new MouseState());
            yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = StagePosition(0.75f) }.WithButton(MouseButton.Left));
            yield return null;
            Assert.That(flow.Simulation.Jumps, Is.EqualTo(2));
            StepFor(flow, 0.60);
            Assert.That(flow.Simulation.Lane, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator EndedReplayButtonReturnsToIdleThenPlayStartsFreshWithoutAlsoMovingTheNeedle()
        {
            int starts = 0;
            flow.RunStarted += () => starts++;
            flow.StartRun();
            DriveToEndWithoutInput(flow);
            yield return null;
            Assert.That(flow.State, Is.EqualTo(RunState.Ended));
            Assert.That(shell.State, Is.EqualTo(AdShellState.FailDialog));
            var hud = Object.FindAnyObjectByType<HudView>();
            Assert.That(shell.Dialog.PrimaryButton.gameObject.activeInHierarchy, Is.True);
            Vector2 buttonPoint = RectTransformUtility.WorldToScreenPoint(Camera.main, shell.Dialog.PrimaryButton.position);
            mouse = InputSystem.AddDevice<Mouse>();
            InputSystem.QueueStateEvent(mouse, new MouseState { position = buttonPoint }.WithButton(MouseButton.Left));
            yield return null;

            Assert.That(starts, Is.EqualTo(1), "The retry button must not start a run automatically.");
            Assert.That(shell.State, Is.EqualTo(AdShellState.Idle));
            Assert.That(flow.State, Is.EqualTo(RunState.Idle));
            Assert.That(flow.Simulation.Jumps, Is.Zero);
            Assert.That(hud.StartButton.gameObject.activeInHierarchy, Is.True);
            InputSystem.QueueStateEvent(mouse, new MouseState());
            yield return null;
            buttonPoint = RectTransformUtility.WorldToScreenPoint(Camera.main, hud.StartButton.position);
            InputSystem.QueueStateEvent(mouse, new MouseState { position = buttonPoint }.WithButton(MouseButton.Left));
            yield return null;

            Assert.That(starts, Is.EqualTo(2), InputDiagnostics());
            Assert.That(flow.State, Is.EqualTo(RunState.Playing));
            Assert.That(flow.Simulation.Time, Is.Zero);
            Assert.That(flow.Simulation.Lives, Is.EqualTo(4));
            Assert.That(flow.Simulation.Jumps, Is.Zero, "The replay press must be consumed by start.");
            Assert.That(flow.Simulation.Jump, Is.Null);
        }

        [UnityTest]
        public IEnumerator TouchBeginningsMapBothStageHalvesAndAHeldFingerDoesNotRepeat()
        {
            touchscreen = InputSystem.AddDevice<Touchscreen>();
            flow.StartRun();
            flow.Move(-1);
            StepFor(flow, 0.60);
            Assert.That(flow.Simulation.Lane, Is.EqualTo(0));
            Vector2 right = StagePosition(0.75f);
            QueueTouch(1, UnityEngine.InputSystem.TouchPhase.Began, right);
            yield return null;
            Assert.That(flow.Simulation.Jumps, Is.EqualTo(2), "A right touch must move inward in its press frame. " + InputDiagnostics());
            StepFor(flow, 0.60);
            QueueTouch(1, UnityEngine.InputSystem.TouchPhase.Moved, right);
            yield return null;
            yield return null;
            Assert.That(flow.Simulation.Jumps, Is.EqualTo(2), "Holding a finger through cooldown must not start another jump.");
            Assert.That(flow.Simulation.Lane, Is.EqualTo(1));

            QueueTouch(1, UnityEngine.InputSystem.TouchPhase.Ended, right);
            yield return null;
            Vector2 left = StagePosition(0.25f);
            QueueTouch(2, UnityEngine.InputSystem.TouchPhase.Began, left);
            yield return null;
            Assert.That(flow.Simulation.Jumps, Is.EqualTo(3), "A left touch must move outward.");
            StepFor(flow, 0.60);
            Assert.That(flow.Simulation.Lane, Is.EqualTo(0));
            QueueTouch(2, UnityEngine.InputSystem.TouchPhase.Ended, left);
            yield return null;
        }

        private Vector2 StagePosition(float horizontalFraction)
        {
            Assert.That(shell.Stage, Is.Not.Null);
            Rect rect = shell.Stage.rect;
            var local = new Vector3(Mathf.Lerp(rect.xMin, rect.xMax, horizontalFraction), rect.center.y);
            return RectTransformUtility.WorldToScreenPoint(Camera.main, shell.Stage.TransformPoint(local));
        }

        private void QueueTouch(int id, UnityEngine.InputSystem.TouchPhase phase, Vector2 position)
        {
            InputSystem.QueueStateEvent(touchscreen, new TouchState { touchId = id, phase = phase, position = position });
        }

        private string InputDiagnostics()
        {
            string state = $"frame={Time.frameCount}, appFocused={Application.isFocused}, flowPaused={flow.IsPaused}, " +
                $"updateMode={InputSystem.settings.updateMode}, background={InputSystem.settings.backgroundBehavior}";
#if UNITY_EDITOR
            state += $", editorRouting={InputSystem.settings.editorInputBehaviorInPlayMode}";
#endif
            if (keyboard != null)
                state += $", keyboardEnabled={keyboard.enabled}, currentKeyboard={Keyboard.current == keyboard}, " +
                    $"spacePressed={keyboard.spaceKey.isPressed}/{keyboard.spaceKey.wasPressedThisFrame}, " +
                    $"aPressed={keyboard.aKey.isPressed}/{keyboard.aKey.wasPressedThisFrame}";
            if (mouse != null)
                state += $", mouseEnabled={mouse.enabled}, currentMouse={Mouse.current == mouse}, " +
                    $"mousePressed={mouse.leftButton.isPressed}/{mouse.leftButton.wasPressedThisFrame}, position={mouse.position.ReadValue()}";
            if (touchscreen != null)
                state += $", touchEnabled={touchscreen.enabled}, currentTouch={Touchscreen.current == touchscreen}, " +
                    $"touchPressed={touchscreen.primaryTouch.press.isPressed}/{touchscreen.primaryTouch.press.wasPressedThisFrame}";
            return state;
        }

        internal static void StepFor(GameFlow target, double seconds)
        {
            double remaining = seconds;
            while (remaining > 1e-12)
            {
                double dt = System.Math.Min(0.02, remaining);
                target.Advance(dt);
                remaining -= dt;
            }
        }

        internal static void DriveToEndWithoutInput(GameFlow target) => DriveToEndWithoutInput(target, true);

        internal static void DriveToEndWithoutInput(GameFlow target, bool finishStop)
        {
            ExpectNoInputDamageLogs(target, 4);
            FinishNeedleDrop(target);
            for (int frame = 0; frame < 3000 && target.State == RunState.Playing; frame++) target.Advance(1.0 / 60.0);
            Assert.That(target.State, Is.EqualTo(RunState.Ended), "The run must terminate within its configured duration.");
            // V2-2: the failure dialog appears once the machine has stopped (same stop as a win).
            if (finishStop) FinishMachineStop(target);
        }

        /// <summary>V2-2: a shell start drops the needle first; judging begins at time zero after it.</summary>
        internal static void FinishNeedleDrop(GameFlow target)
        {
            if (target.Phase != MachinePhase.Starting) return;
            target.Advance(target.PreparationLength);
            Assert.That(target.Phase, Is.EqualTo(MachinePhase.Running));
            Assert.That(target.Simulation.Time, Is.Zero);
        }

        internal static void FinishMachineStop(GameFlow target)
        {
            if (target.Phase != MachinePhase.Stopping) return;
            target.Advance(target.StopDuration);
            Assert.That(target.Phase, Is.EqualTo(MachinePhase.Stopped));
        }

        // M4: the expected hits come from an independent simulation of the scene's own chart.
        private static void ExpectNoInputDamageLogs(GameFlow target, int count)
        {
            var damage = ChartBot.NoInputDamage(target);
            Assert.That(damage.Count, Is.GreaterThanOrEqualTo(count), "A record left alone must wear the needle out.");
            for (int index = 0; index < count; index++)
                LogAssert.Expect(LogType.Log, $"[OFFTRACK] scratch={damage[index].ScratchId} lane={damage[index].Lane} kind={damage[index].Kind} lives={damage[index].LivesAfter}");
        }

        private static int VisibleNotes(GameFlow target)
        {
            if (target.State == RunState.Idle) return 0;
            double ahead = ScratchPool.LookAhead(target.Config);
            return target.Simulation.Notes.Count(note => target.Simulation.Time - note.Time >= -ahead
                && target.Simulation.Time - note.Time <= target.Config.ScratchRetireDelay);
        }

        private static HashSet<GameObject> PoolObjects(ScratchPool pool)
        {
            var objects = new HashSet<GameObject>(pool.Items);
            Assert.That(objects.Count, Is.EqualTo(32), "The pool must hold 32 distinct live instances.");
            Assert.That(objects.All(item => item != null), Is.True);
            return objects;
        }

        [System.Serializable]
        private sealed class BotFixture
        {
            public BotInput[] inputs = System.Array.Empty<BotInput>();
        }

        [System.Serializable]
        private sealed class BotInput
        {
            public int frame = 0;
            public int dir = 0;
        }

        private sealed class ControlledTimeSource : IGameTimeSource
        {
            public double Sample;
            public double DeltaTime => Sample;
            public void Reset() { }
        }
    }
}
