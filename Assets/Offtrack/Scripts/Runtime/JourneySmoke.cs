#if OFFTRACK_PLAYER_SMOKE && UNITY_STANDALONE && !UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Offtrack.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Offtrack.Runtime
{
    /// <summary>
    /// Drives one journey of the exported smoke player and logs the evidence for it. Compiled only
    /// with OFFTRACK_PLAYER_SMOKE, which the smoke export alone defines; the release export refuses
    /// a player that still contains this class or its switches.
    /// </summary>
    internal sealed class JourneySmoke : MonoBehaviour
    {
        private const string Tag = "OFFTRACK smoke ";
        private const string ClaimSwitch = "--offtrack-smoke-claim";
        private const string AbandonSwitch = "--offtrack-smoke-abandon";
        // Starts from the first-launch client size, so the single journey request really resizes.
        private const string FreshWindowSwitch = "--offtrack-smoke-fresh-window";
        // Negative control: silences the listener, so the output probe has to fail the first level.
        private const string MuteSwitch = "--offtrack-smoke-mute";
        private const string TitleScreenshotVariable = "OFFTRACK_SMOKE_TITLE_PNG";
        private const string WindowRequestLine = "OFFTRACK window resize:";

        // PlayerSettings.defaultScreenWidth/Height, the client size of a first launch. No asset is
        // loaded this early; the run fails if the journey request reports a different default.
        private const int FreshWindowWidth = 585;
        private const int FreshWindowHeight = 1266;
        // Screen.SetResolution applies after the frame that requested it.
        private const int WindowSettleFrames = 10;
        private const int WindowDeadlineFrames = 600;
        private const float TitleHoldSeconds = .5f;
        private const double StepTimeoutSeconds = 45;
        private const double PlannerLead = .4;
        // One probe per frame over the most recent samples of each output channel.
        private const int OutputWindowSamples = 1024;
        // -34 dBFS. Side A leaves the mixer at roughly 0.2 RMS; its opening bars keep about 60 %
        // of the probe windows above this floor, a silent or cut-off level keeps none or few.
        private const float OutputFloorRms = .02f;
        private const float MinimumAudibleShare = .25f;

        private static readonly Queue<string> witnessed = new Queue<string>();
        // The first unhandled exception of the run, the journey's own included: it fails the run.
        private static string fault;
        private static bool failed;
        private static bool active;
        private static bool claim;
        private static bool freshWindow;
        private static bool mute;
        private static Vector2Int launchClient;
        private static bool requestSeen;
        private static Vector2Int requestBefore;
        private static Vector2Int requestDefault;
        private static Vector2Int requestTarget;

        private readonly float[] output = new float[OutputWindowSamples];
        private GameFlow probed;
        private double squares;
        private long samples;
        private int windows;
        private int audibleWindows;
        private float peak;
        private float lowestListenerVolume;
        private bool listenerPaused;
        private int outputChannels;
        private int levelsHeard;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
        private static void Prepare()
        {
            var arguments = Environment.GetCommandLineArgs();
            claim = Array.IndexOf(arguments, ClaimSwitch) >= 0;
            active = claim || Array.IndexOf(arguments, AbandonSwitch) >= 0;
            if (!active) return;
            freshWindow = Array.IndexOf(arguments, FreshWindowSwitch) >= 0;
            mute = Array.IndexOf(arguments, MuteSwitch) >= 0;
            Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
            // Subscribed before the title scene wakes, so the journey window request is witnessed.
            Application.logMessageReceived += Witness;
            launchClient = Client();
            Log($"start route={(claim ? "claim" : "abandon")} freshWindow={freshWindow} mute={mute} " +
                $"launchClient={Size(launchClient)} mode={Screen.fullScreenMode}");
            if (!freshWindow) return;
            // Takes effect while the splash screen runs, before the title scene makes its request.
            Screen.SetResolution(FreshWindowWidth, FreshWindowHeight, FullScreenMode.Windowed);
            Log($"fresh window reset {Size(launchClient)} -> {FreshWindowWidth}x{FreshWindowHeight}");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Launch()
        {
            if (!active) return;
            var host = new GameObject(nameof(JourneySmoke));
            DontDestroyOnLoad(host);
            var smoke = host.AddComponent<JourneySmoke>();
            smoke.StartCoroutine(smoke.Run(claim));
        }

        private IEnumerator Run(bool win)
        {
            Application.runInBackground = true;
            SceneManager.sceneLoaded += TraceScene;
            TraceScene(SceneManager.GetActiveScene(), LoadSceneMode.Single);
            yield return SettleWindow();
            yield return new WaitForSecondsRealtime(TitleHoldSeconds);
            var screenshot = Environment.GetEnvironmentVariable(TitleScreenshotVariable);
            if (!string.IsNullOrEmpty(screenshot))
            {
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(screenshot);
                Log("title screenshot requested");
                yield return null;
            }
            var title = FindAnyObjectByType<TitleController>();
            var button = title.GetComponentInChildren<Canvas>().transform.Find("ReferenceFrame/StartButton");
            if (!title.HandlePointer(ScreenPoint((RectTransform)button))) yield return Fail("title input");
            yield return WaitUntil(() => SceneManager.GetActiveScene().name == OfftrackScenes.Main && !SceneTransition.BlocksInput, "Main");
            var shell = FindAnyObjectByType<AdShell>();
            var flow = shell.Flow;
            yield return WaitUntil(() => shell.State == AdShellState.Lottery && shell.Dialog.CanPress, "lottery");
            shell.ActivatePrimary();
            // Giving up costs one level at a time, so the abandon route also passes all three.
            for (int level = 1; level <= 3; level++)
            {
                int number = level;
                yield return WaitUntil(() => shell.State == AdShellState.Guide && shell.Level == number &&
                    shell.Dialog.CanPress && !SceneTransition.BlocksInput, "guide " + level);
                Log($"guide L{level}");
                Arm(flow);
                shell.ActivatePrimary();
                yield return WaitUntil(() => flow.Phase == MachinePhase.Running, "needle down");
                Log($"L{level} running");
                if (win)
                {
                    var result = ChartPlanner.Simulate(flow.Config.ToRules(), flow.Chart, PlannerLead, out var plan);
                    if (!result.Won) yield return Fail("no winning plan");
                    foreach (var press in plan)
                    {
                        double time = press.Frame * ChartPlanner.Step;
                        double deadline = Time.realtimeSinceStartupAsDouble + StepTimeoutSeconds;
                        while (flow.RunTime < time && flow.State == RunState.Playing)
                        {
                            if (Time.realtimeSinceStartupAsDouble > deadline)
                                yield return Fail(Text($"timeout L{level} press at {time:F3}: run time {flow.RunTime:F3} ") +
                                    $"paused={flow.IsPaused} focused={Application.isFocused}");
                            yield return null;
                        }
                        var move = flow.Move(press.Direction);
                        if (move.Result != "jump") yield return Fail(Text($"L{level} {time:F3}: {move.Result}"));
                    }
                }
                var outcome = win ? AdShellState.Fragment : AdShellState.FailDialog;
                yield return WaitUntil(() => shell.State == outcome && shell.Dialog.CanPress, () => Text(
                    $"outcome L{number}: {shell.State}/{flow.Phase} time={flow.RunTime:R} audio={flow.FollowingMusic} paused={flow.IsPaused}"));
                Log($"L{level} {shell.State}, lives={flow.Simulation.Lives}");
                yield return ReportOutput(level);
                if (win) shell.ActivatePrimary();
                else
                {
                    shell.HandlePointer(ScreenPoint(shell.Dialog.SecondaryButton));
                    yield return WaitUntil(() => shell.State == AdShellState.ConfirmSkip && shell.Dialog.CanPress, "abandon confirmation");
                    yield return new WaitForSecondsRealtime((float)flow.Config.Shell.DialogDebounceDuration);
                    shell.HandlePointer(ScreenPoint(shell.Dialog.SecondaryButton));
                }
            }
            yield return WaitUntil(() => SceneManager.GetActiveScene().name == OfftrackScenes.Title && !SceneTransition.BlocksInput, "return to title");
            if (StandaloneWindow.RequestCount != 1) yield return Fail("window request count " + StandaloneWindow.RequestCount);
            if (Client() != requestTarget)
                yield return Fail($"window client {Size(Client())} at the end, target {Size(requestTarget)}");
            if (fault != null || failed) yield return Fail("exception " + fault);
            Log($"PASS {(win ? "claim" : "abandon")} windowRequests={StandaloneWindow.RequestCount} " +
                $"resized={requestBefore != requestTarget} freshWindow={freshWindow} client={Size(Client())} " +
                $"levelsHeard={levelsHeard}");
            Application.Quit(0);
        }

        // Reads the client size the journey request started from and the size it settled on.
        private IEnumerator SettleWindow()
        {
            int waited = 0;
            while (waited < WindowSettleFrames
                || requestSeen && Client() != requestTarget && waited < WindowDeadlineFrames)
            {
                waited++;
                yield return null;
            }
            var after = Client();
            Log($"window launch={Size(launchClient)} before={Size(requestBefore)} default={Size(requestDefault)} " +
                $"target={Size(requestTarget)} after={Size(after)} waitedFrames={waited} " +
                $"resized={requestBefore != after} freshWindow={freshWindow} requests={StandaloneWindow.RequestCount}");
            if (!requestSeen) yield return Fail("window request line not seen: " + WindowRequestLine);
            if (after != requestTarget)
                yield return Fail($"window client {Size(after)} after {waited} frames, target {Size(requestTarget)}");
            var fresh = new Vector2Int(FreshWindowWidth, FreshWindowHeight);
            if (freshWindow && (requestBefore != fresh || requestDefault != fresh || requestBefore == requestTarget))
                yield return Fail($"fresh window not covered: before={Size(requestBefore)} default={Size(requestDefault)} " +
                    $"target={Size(requestTarget)}, expected a resize from {Size(fresh)}");
        }

        private void Arm(GameFlow flow)
        {
            probed = flow;
            squares = 0;
            samples = 0;
            windows = audibleWindows = 0;
            peak = 0;
            lowestListenerVolume = float.PositiveInfinity;
            listenerPaused = false;
        }

        private void Update()
        {
            Flush();
            // A journey step that threw has stopped for good: end the run instead of waiting on it.
            if (fault != null && !failed) StartCoroutine(Fail("exception " + fault));
        }

        // After every Update of the frame, so the listener state is the one this frame is mixed with.
        private void LateUpdate()
        {
            if (probed == null) return;
            if (mute) AudioListener.volume = 0;
            if (probed.Phase != MachinePhase.Running || probed.IsPaused) return;
            outputChannels = AudioSettings.speakerMode == AudioSpeakerMode.Mono ? 1 : 2;
            double window = 0;
            for (int channel = 0; channel < outputChannels; channel++)
            {
                AudioListener.GetOutputData(output, channel);
                foreach (float value in output)
                {
                    window += (double)value * value;
                    peak = Mathf.Max(peak, Mathf.Abs(value));
                }
            }
            int count = outputChannels * output.Length;
            squares += window;
            samples += count;
            windows++;
            if (Math.Sqrt(window / count) >= OutputFloorRms) audibleWindows++;
            lowestListenerVolume = Mathf.Min(lowestListenerVolume, AudioListener.volume);
            listenerPaused |= AudioListener.pause;
        }

        // What the listener's output carried while the level was Running.
        private IEnumerator ReportOutput(int level)
        {
            probed = null;
            double rms = samples == 0 ? 0 : Math.Sqrt(squares / samples);
            double share = windows == 0 ? 0 : (double)audibleWindows / windows;
            float volume = windows == 0 ? 0 : lowestListenerVolume;
            Log(Text($"L{level} output rms={rms:F4} peak={peak:F4} windows={windows} audible={audibleWindows} share={share:F2} ") +
                Text($"listenerVolume={volume:F2} listenerPaused={listenerPaused} floor={OutputFloorRms:F4} minimumShare={MinimumAudibleShare:F2} ") +
                $"channels={outputChannels} windowSamples={OutputWindowSamples} outputRate={AudioSettings.outputSampleRate}");
            if (windows == 0) yield return Fail($"L{level} output not sampled while Running");
            if (rms < OutputFloorRms) yield return Fail(Text($"L{level} output rms {rms:F4} below {OutputFloorRms:F4}"));
            if (share < MinimumAudibleShare)
                yield return Fail(Text($"L{level} output audible share {share:F2} below {MinimumAudibleShare:F2}"));
            if (listenerPaused || volume <= 0) yield return Fail($"L{level} listener muted");
            levelsHeard++;
        }

        private IEnumerator WaitUntil(Func<bool> condition, string label) => WaitUntil(condition, () => label);

        private IEnumerator WaitUntil(Func<bool> condition, Func<string> label)
        {
            double deadline = Time.realtimeSinceStartupAsDouble + StepTimeoutSeconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartupAsDouble > deadline) yield return Fail("timeout " + label());
                yield return null;
            }
        }

        // Other OFFTRACK lines carry no clock of their own: repeat each with the moment it was logged.
        private static void Witness(string message, string stackTrace, LogType type)
        {
            if (message == null || message.StartsWith(Tag, StringComparison.Ordinal)) return;
            if (type == LogType.Exception)
            {
                if (fault == null) fault = message;
                return;
            }
            if (!message.StartsWith("OFFTRACK ", StringComparison.Ordinal)
                && !message.StartsWith("[OFFTRACK]", StringComparison.Ordinal)) return;
            if (message.StartsWith(WindowRequestLine, StringComparison.Ordinal))
                requestSeen = ReadSize(message, "before", out requestBefore)
                    & ReadSize(message, "requested", out requestDefault)
                    & ReadSize(message, "target", out requestTarget);
            // Queued, not logged from inside the log callback.
            witnessed.Enqueue($"saw \"{message}\" {Stamp()}");
        }

        private static bool ReadSize(string message, string key, out Vector2Int size)
        {
            var match = Regex.Match(message, @"\b" + key + @"=(\d+)x(\d+)\b");
            size = match.Success
                ? new Vector2Int(int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value))
                : default;
            return match.Success;
        }

        private static void Flush()
        {
            while (witnessed.Count > 0) Debug.Log(Tag + witnessed.Dequeue());
        }

        private static void Log(string message)
        {
            Flush();
            Debug.Log(Tag + message + " " + Stamp());
        }

        // Application.Quit ends the process when this frame completes: a failed step never resumes its caller.
        private static IEnumerator Fail(string label)
        {
            // Only the first failure is reported and decides the exit code.
            if (!failed)
            {
                failed = true;
                Flush();
                Debug.LogError(Tag + "FAIL " + label + " " + Stamp());
                Application.Quit(1);
            }
            while (true) yield return null;
        }

        private static string Stamp() =>
            Text($"[t={Time.realtimeSinceStartup:F3} frame={Time.frameCount} dsp={AudioSettings.dspTime:F3}]");
        private static string Text(FormattableString value) => FormattableString.Invariant(value);
        private static Vector2Int Client() => new Vector2Int(Screen.width, Screen.height);
        private static string Size(Vector2Int size) => size.x + "x" + size.y;
        private static void TraceScene(Scene scene, LoadSceneMode mode) => Log("scene " + scene.name);
        private static Vector2 ScreenPoint(RectTransform button) => RectTransformUtility.WorldToScreenPoint(Camera.main,
            button.TransformPoint(button.rect.center));
    }
}
#endif
