using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Offtrack.Core;
using Offtrack.Runtime;
using Offtrack.Runtime.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Offtrack.Tests.PlayMode
{
    public sealed class V3Acceptance
    {
        private static readonly string Output = Path.GetFullPath("Logs/v3-evidence");
        private const double Step = ChartBot.Step;
        private int clientWidth;
        private int clientHeight;

        private void Initialize()
        {
            if (!Environment.GetCommandLineArgs().Contains("--offtrack-v3-capture"))
                Assert.Ignore("Use --offtrack-v3-capture to produce V3 acceptance media.");
            var client = Environment.GetEnvironmentVariable("OFFTRACK_V3_CLIENT").Split('x');
            clientWidth = int.Parse(client[0]);
            clientHeight = int.Parse(client[1]);
            Directory.CreateDirectory(Output);
            SceneTransition.AutomaticTick = false;
        }

        [TearDown]
        public void Restore() => SceneTransition.AutomaticTick = true;

        [UnityTest]
        public IEnumerator CaptureStills()
        {
            Initialize();
            yield return SceneManager.LoadSceneAsync(OfftrackScenes.Title);
            yield return Still("title");
            // One still per shot: board, walk-in past door and window, writing, three thoughts,
            // the phone before and during the push, the red screen.
            yield return Story(OfftrackScenes.CutsceneA, .5, 1, 1.8, 4.5, 6.8, 8.8, 10.8, 12.9, 13.3, 14.5);
            yield return Story(OfftrackScenes.CutsceneC, 0, 1.97, 2.5, 4.5, 5, 5.8);
            yield return Story(OfftrackScenes.CutsceneB, .8, 5.2, 6.3, 8.4, 10.5, 12);
            yield return SceneManager.LoadSceneAsync(OfftrackScenes.Main);
            var shell = Shell();
            var flow = shell.Flow;
            var rules = flow.Config.Shell;
            shell.Advance(rules.BootDuration / 2);
            yield return Still("loading");
            shell.Advance(rules.BootDuration);
            shell.Advance(rules.DialogEntranceDuration);
            yield return Still("lottery");
            Assert.That(shell.AcceptLottery(), Is.True);
            shell.Advance(rules.DialogEntranceDuration);
            yield return Still("guide-1");
            Assert.That(shell.StartPlaying(), Is.True);
            Tick(shell, flow.PreparationLength - .16);
            yield return Still("needle-drop");
            Tick(shell, .16);
            Tick(shell, ChartBot.NoInputDamage(flow)[0].Time + .05);
            yield return Still("hit-mid");
            for (int frame = 0; frame < 6000 && flow.State == RunState.Playing; frame++) Tick(shell, Step);
            Tick(shell, flow.StopDuration + Step);
            shell.Advance(rules.DialogEntranceDuration);
            yield return Still("fail");
            shell.Advance(rules.SkipCountdownDuration);
            Assert.That(shell.RequestSkip(), Is.True);
            shell.Advance(rules.DialogEntranceDuration);
            yield return Still("confirm-skip");

            yield return SceneManager.LoadSceneAsync(OfftrackScenes.Main);
            shell = Shell();
            flow = shell.Flow;
            shell.Advance(rules.BootDuration + rules.DialogEntranceDuration);
            Assert.That(shell.AcceptLottery(), Is.True);
            for (int level = 1; level <= 3; level++)
            {
                shell.Advance(rules.DialogEntranceDuration);
                if (level > 1) yield return Still("guide-" + level);
                Assert.That(shell.StartPlaying(), Is.True);
                Tick(shell, flow.PreparationLength);
                var presses = ChartBot.WinningPresses(flow);
                double beat = flow.Record.BeatSeconds;
                var shots = level == 1 ? new[] { ("level-1-mid", 32 * beat) }
                    : level == 2 ? new[] { ("level-2-mid", 32 * beat),
                        ("level-2-break", (flow.Record.Hazards.First(hazard => hazard.kind == HazardKind.Break && hazard.phase >= 32).phase - .5) * beat),
                        ("level-2-runner", (flow.Record.Hazards.First(hazard => hazard.kind == HazardKind.RunningScratch && hazard.phase > 32).phase - 2.5) * beat) }
                    : new[] { ("level-3-before-reverse", flow.Record.Reversals[1] * beat - .2),
                        ("level-3-after-reverse", flow.Record.Reversals[1] * beat + .2), ("level-3-mid", 32 * beat) };
                var ordered = shots.OrderBy(shot => shot.Item2).ToArray();
                int press = 0, shotIndex = 0;
                double landing = double.PositiveInfinity;
                void OnLanded(LandingEvent value) { if (double.IsPositiveInfinity(landing)) landing = flow.RunTime + .06; }
                flow.Landed += OnLanded;
                for (int frame = 0; flow.State == RunState.Playing; frame++)
                {
                    Move(flow, presses, frame, ref press);
                    Tick(shell, Step);
                    if (level == 1 && Math.Abs(flow.RunTime - landing) <= Step / 2)
                        yield return Still("jump-landing-mid");
                    while (shotIndex < ordered.Length && flow.RunTime >= ordered[shotIndex].Item2)
                        yield return Still(ordered[shotIndex++].Item1);
                }
                flow.Landed -= OnLanded;
                Assert.That(flow.Simulation.Result.Won, Is.True);
                Assert.That(shotIndex, Is.EqualTo(ordered.Length));
                Tick(shell, flow.StopDuration + Step);
                shell.Advance(rules.DialogEntranceDuration);
                Assert.That(shell.State, Is.EqualTo(AdShellState.Fragment));
                yield return Still("fragment-" + level);
                if (level < 3) Assert.That(shell.AcceptFragment(), Is.True);
            }
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator RecordJourney()
        {
            Initialize();
            var configuration = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "UserSettings", "OfftrackFfmpegPath.txt"));
            if (!File.Exists(configuration)) Assert.Ignore("No local OfftrackFfmpegPath.txt; stills are independent of the encoder.");
            string ffmpeg = File.ReadAllText(configuration).Trim();
            if (!File.Exists(ffmpeg)) Assert.Ignore("The local FFmpeg executable does not exist: " + ffmpeg);
            yield return SceneManager.LoadSceneAsync(OfftrackScenes.Title);
            using var journey = new Video(ffmpeg, "journey-30fps", 30);
            using var second = new Video(ffmpeg, "level-2-60fps", 60);
            using var third = new Video(ffmpeg, "level-3-60fps", 60);
            using var target = new Frame(390, 844, false);
            string previousScene = null;
            CutsceneController story = null;
            AdShell shell = null;
            bool returned = false;
            double sceneTime = 0, dialogTime = 0;
            var previousState = AdShellState.Boot;
            var presses = Array.Empty<ChartBot.Press>();
            int runFrame = -1, press = 0;
            for (int frame = 0; frame < 11000; frame++)
            {
                string scene = SceneManager.GetActiveScene().name;
                if (scene != previousScene)
                {
                    returned = scene == OfftrackScenes.Title && previousScene == OfftrackScenes.CutsceneB;
                    sceneTime = 0;
                    previousScene = scene;
                    story = Object.FindAnyObjectByType<CutsceneController>();
                    if (story != null) story.AutomaticTick = false;
                    shell = scene == OfftrackScenes.Main ? Shell() : null;
                    target.Bind();
                }

                sceneTime += Step;
                if (scene == OfftrackScenes.Title && sceneTime >= 1 && !SceneTransition.BlocksInput)
                {
                    if (returned) break;
                    SceneTransition.GoTo(OfftrackScenes.CutsceneA, new TransitionRules());
                }
                if (!SceneTransition.BlocksInput && story != null) story.Advance(Step);
                Video segment = null;
                if (shell != null)
                {
                    var flow = shell.Flow;
                    if (shell.State != previousState) { previousState = shell.State; dialogTime = 0; }
                    dialogTime += Step;
                    if (!SceneTransition.BlocksInput && dialogTime >= .8 &&
                        (shell.State == AdShellState.Lottery || shell.State == AdShellState.Guide || shell.State == AdShellState.Fragment))
                    {
                        shell.ActivatePrimary();
                        dialogTime = 0;
                    }
                    shell.Advance(Step);
                    bool judging = flow.State == RunState.Playing && flow.Phase == MachinePhase.Running;
                    if (judging)
                    {
                        if (runFrame < 0) { presses = ChartBot.WinningPresses(flow); press = 0; runFrame = 0; }
                        Move(flow, presses, runFrame++, ref press);
                    }
                    else runFrame = -1;
                    flow.Advance(Step);
                    if (shell.State == AdShellState.FailDialog) Assert.Fail("The recorded run lost level " + shell.Level);
                    if (shell.State == AdShellState.Playing || shell.State == AdShellState.Fragment)
                        segment = shell.Level == 2 ? second : shell.Level == 3 ? third : null;
                }
                SceneTransition.Advance(Step);
                if (segment != null || frame % 2 == 0)
                {
                    Canvas.ForceUpdateCanvases();
                    yield return null;
                    if (!target.Read()) continue;
                    if (frame % 2 == 0) journey.Write(target.Bytes);
                    segment?.Write(target.Bytes);
                }
                else yield return null;
            }
            Assert.That(returned, Is.True, "Journey must complete all three levels and return to Title.");
            Assert.That(second.Frames, Is.GreaterThan(1800));
            Assert.That(third.Frames, Is.GreaterThan(1800));
        }

        private static void Move(GameFlow flow, ChartBot.Press[] presses, int frame, ref int index)
        {
            while (index < presses.Length && presses[index].Frame == frame)
                Assert.That(flow.Move(presses[index++].Direction).Result, Is.EqualTo("jump"), "frame " + frame);
        }

        private static void Tick(AdShell shell, double time)
        {
            shell.Flow.Advance(time);
            shell.Advance(time);
        }

        private static AdShell Shell()
        {
            var shell = Object.FindAnyObjectByType<AdShell>();
            shell.AutomaticTick = false;
            shell.Flow.AutomaticTick = false;
            shell.Flow.SetApplicationFocus(true);
            return shell;
        }

        private IEnumerator Story(string scene, params double[] times)
        {
            yield return SceneManager.LoadSceneAsync(scene);
            var story = Object.FindAnyObjectByType<CutsceneController>();
            story.AutomaticTick = false;
            foreach (double time in times)
            {
                story.Seek(time);
                yield return Still(FormattableString.Invariant($"{scene.ToLowerInvariant()}-{time:00.0}"));
            }
        }

        private IEnumerator Still(string name)
        {
            if (name == "level-2-runner")
            {
                var pool = Object.FindAnyObjectByType<ScratchPool>();
                Assert.That(pool.Items.Any(item => item.transform.Find("RunnerTail")?.gameObject.activeInHierarchy == true), Is.True);
            }
            yield return Save(name, "reference", 780, 1688);
            yield return Save(name, "window", clientWidth, clientHeight);
            if (name is "hit-mid" or "jump-landing-mid" or "level-2-break" or "level-2-runner" or "level-3-before-reverse")
                yield return StrictRecordFrame(name, name is "hit-mid" or "level-2-break" or "level-2-runner");
        }

        internal static IEnumerator StrictRecordFrame(string name, bool requireDanger)
        {
            const string shaderKeyword = "OFFTRACK_STRICT_PALETTE";
            using var target = new Frame(780, 1688, false);
            Shader.EnableKeyword(shaderKeyword);
            try
            {
                target.Bind();
                Canvas.ForceUpdateCanvases();
                yield return null;
                yield return null;
                Assert.That(target.Read(), Is.True);
                var colors = PaletteAssert.AssertExactPalette(target.Pixels.GetPixels32(), name + " strict");
                Assert.That(colors.Count, Is.GreaterThan(4));
                if (requireDanger) Assert.That(colors.Contains(PaletteAssert.Rgb(Palette.Danger)), Is.True);
            }
            finally { Shader.DisableKeyword(shaderKeyword); }
        }

        internal static IEnumerator AntialiasedRecordFrame(string name)
        {
            using var target = new Frame(780, 1688, true);
            target.Bind();
            Canvas.ForceUpdateCanvases();
            yield return null;
            yield return null;
            Assert.That(target.Read(), Is.True);
            PaletteAssert.AssertWithinPaletteHull(target.Pixels.GetPixels32(), name);
        }

        private static IEnumerator Save(string name, string folder, int width, int height)
        {
            using var target = new Frame(width, height, true);
            target.Bind();
            Canvas.ForceUpdateCanvases();
            yield return null;
            yield return null;
            Assert.That(target.Read(), Is.True);
            string directory = Path.Combine(Output, folder);
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, name + ".png"), target.Pixels.EncodeToPNG());
        }

        private sealed class Frame : IDisposable
        {
            private readonly RenderTexture target;
            private Camera camera;
            private RenderTexture previous;
            private float aspect;
            internal readonly Texture2D Pixels;
            internal readonly byte[] Bytes;

            internal Frame(int width, int height, bool multisample)
            {
                target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
                { antiAliasing = multisample ? Mathf.Max(1, QualitySettings.antiAliasing) : 1 };
                target.Create();
                Pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
                Bytes = new byte[width * height * 3];
            }

            internal void Bind()
            {
                camera = Camera.main;
                Assert.That(camera, Is.Not.Null);
                previous = camera.targetTexture;
                aspect = camera.aspect;
                camera.targetTexture = target;
                camera.aspect = (float)target.width / target.height;
            }

            internal bool Read()
            {
                if (camera == null) return false;
                var active = RenderTexture.active;
                RenderTexture.active = target;
                Pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0, false);
                RenderTexture.active = active;
                Pixels.GetRawTextureData<byte>().CopyTo(Bytes);
                return true;
            }

            public void Dispose()
            {
                if (camera != null) { camera.targetTexture = previous; camera.aspect = aspect; }
                target.Release();
                Object.Destroy(target);
                Object.Destroy(Pixels);
            }
        }

        private sealed class Video : IDisposable
        {
            private readonly Process encoder;
            internal int Frames { get; private set; }

            internal Video(string ffmpeg, string name, int fps)
            {
                string path = Path.Combine(Output, name + ".mp4");
                encoder = Process.Start(new ProcessStartInfo(ffmpeg,
                    $"-hide_banner -loglevel error -y -f rawvideo -pix_fmt rgb24 -video_size 390x844 -framerate {fps} " +
                    $"-i pipe:0 -vf vflip -c:v libx264 -threads 2 -preset ultrafast -crf 20 -pix_fmt yuv420p {(char)34}{path}{(char)34}")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true });
            }

            internal void Write(byte[] bytes)
            {
                encoder.StandardInput.BaseStream.Write(bytes, 0, bytes.Length);
                Frames++;
            }

            public void Dispose()
            {
                encoder.StandardInput.Close();
                Assert.That(encoder.WaitForExit(30000), Is.True);
                Assert.That(encoder.ExitCode, Is.Zero);
                encoder.Dispose();
            }
        }
    }
}
