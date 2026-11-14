using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Offtrack.Core;
using Offtrack.Runtime;
using Offtrack.Runtime.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Offtrack.Tests.PlayMode
{
    public sealed class M2AcceptanceScreenshots
    {
        private static string EvidenceDirectory => Array.IndexOf(Environment.GetCommandLineArgs(),
            "--offtrack-update-acceptance") >= 0
            ? Path.GetFullPath(Path.Combine(Application.dataPath, "..", "docs", "acceptance", "m2", "evidence"))
            : Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "M1-M2-review-evidence", "m2"));

        // Keep graphics enabled. These PNGs come from actual scene cameras and UI rendering.
        // Routine verification must not replace acceptance evidence. Pass --offtrack-update-acceptance
        // explicitly only when a reviewed visual change requires refreshed M2 screenshots.
        [UnityTest]
        public IEnumerator CaptureMainStatesWithOnlyThePaletteWhitelist()
        {
            Time.timeScale = 1f;
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            var shell = Object.FindAnyObjectByType<AdShell>();
            var flow = Object.FindAnyObjectByType<GameFlow>();
            Assert.That(shell, Is.Not.Null);
            Assert.That(flow, Is.Not.Null);
            shell.AutomaticTick = false;
            flow.AutomaticTick = false;
            flow.SetApplicationFocus(true);
            Assert.That(shell.State, Is.EqualTo(AdShellState.Boot));
            shell.Advance(Math.Max(0, flow.Config.Shell.BootDuration * (0.5 - shell.Machine.BootProgress)));
            yield return CaptureMain("boot.png");

            shell.Advance(flow.Config.Shell.BootDuration);
            Assert.That(shell.State, Is.EqualTo(AdShellState.Lottery));
            yield return CaptureMain("lottery.png");

            Assert.That(shell.AcceptLottery(), Is.True);
            Assert.That(shell.Machine.SkipSecondsRemaining, Is.GreaterThan(0), "Idle evidence must show a countdown.");
            Assert.That(flow.State, Is.EqualTo(RunState.Idle));
            yield return CaptureMain("idle.png");

            shell.Advance(flow.Config.Shell.SkipCountdownDuration);
            Assert.That(shell.Machine.CanSkip, Is.True, "Playing evidence must show an available skip button.");
            Assert.That(shell.StartPlaying(), Is.True); M1GameplayTests.FinishNeedleDrop(flow);
            M1GameplayTests.StepFor(flow, 1.0);
            yield return CaptureMain("playing.png");

            // Later in the same run, explicitly include the near scratch and its fixed top arc.
            // Shortly before the first scratch (side A: beat 4), inside its two-beat focus and
            // before any hit, so the frame shows a clean run and the no-input ending stays intact.
            M1GameplayTests.StepFor(flow, flow.Chart[0].Time - .4 - flow.Simulation.Time);
            Assert.That(Object.FindAnyObjectByType<ScratchPool>().HighlightedNoteId, Is.EqualTo(0));
            Assert.That(flow.Simulation.DamageCount, Is.Zero);
            yield return CaptureMain("playing_next.png");

            Assert.That(shell.RequestSkip(), Is.True);
            Assert.That(shell.State, Is.EqualTo(AdShellState.ConfirmSkip));
            Assert.That(flow.IsPaused, Is.True);
            yield return CaptureMain("confirm_skip.png");

            Assert.That(shell.Stay(), Is.True);
            shell.Advance(flow.Config.Shell.StayResumeDelay);
            M1GameplayTests.DriveToEndWithoutInput(flow);
            Assert.That(shell.State, Is.EqualTo(AdShellState.FailDialog));
            yield return CaptureMain("fail_dialog.png");

            Assert.That(shell.Retry(), Is.True);
            Assert.That(shell.StartPlaying(), Is.True); M1GameplayTests.FinishNeedleDrop(flow);
            CompleteUsingAcceptedFixture(flow);
            Assert.That(shell.State, Is.EqualTo(AdShellState.Complete));
            // The machine stop finishes inside Complete's 3 s: capture the resting pose.
            shell.Advance(flow.StopDuration);
            M1GameplayTests.FinishMachineStop(flow);
            Assert.That(shell.State, Is.EqualTo(AdShellState.Complete));
            Assert.That(Object.FindAnyObjectByType<TonearmView>().Swing, Is.Zero, "Back on the rest.");
            yield return CaptureMain("complete.png");
            LogAssert.NoUnexpectedReceived();
        }

        // V2-2 feel states: needle drop, airborne, settling, a hit's static notice, machine stop.
        [UnityTest]
        public IEnumerator CaptureV22FeelStatesWithOnlyThePaletteWhitelist()
        {
            Time.timeScale = 1f;
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            var shell = Object.FindAnyObjectByType<AdShell>();
            var flow = Object.FindAnyObjectByType<GameFlow>();
            shell.AutomaticTick = false;
            flow.AutomaticTick = false;
            flow.SetApplicationFocus(true);
            shell.Advance(flow.Config.Shell.BootDuration);
            Assert.That(shell.AcceptLottery(), Is.True);
            Assert.That(shell.StartPlaying(), Is.True);
            flow.Advance(flow.PreparationLength * .55);
            Assert.That(flow.Phase, Is.EqualTo(MachinePhase.Starting));
            yield return CaptureMain("v22_dropping.png");
            M1GameplayTests.FinishNeedleDrop(flow);
            M1GameplayTests.StepFor(flow, 1.0);
            Assert.That(flow.Move(-1).Result, Is.EqualTo("jump"));
            flow.Advance(.11);
            yield return CaptureMain("v22_airborne.png");
            flow.Advance(.15);
            Assert.That(flow.Simulation.Time, Is.LessThan(flow.Simulation.ReadyAt), "Settling: the cooldown bar is refilling.");
            yield return CaptureMain("v22_settling.png");
            int lives = flow.Simulation.Lives;
            for (int frame = 0; frame < 3000 && flow.Simulation.Lives == lives; frame++) flow.Advance(1.0 / 60);
            Assert.That(flow.Simulation.Lives, Is.LessThan(lives));
            flow.Advance(.3);
            yield return CaptureMain("v22_damage.png");
            for (int frame = 0; frame < 3000 && flow.State == RunState.Playing; frame++) flow.Advance(1.0 / 60);
            Assert.That(flow.Phase, Is.EqualTo(MachinePhase.Stopping));
            flow.Advance(flow.StopDuration * .5);
            yield return CaptureMain("v22_stopping.png");
        }

        // About 20 s at 60 fps of the actual Main camera, for the V2-2 report. Frames only (no
        // audio); encoded afterwards. Explicit: run it by name, it writes ~1200 PNGs under Logs.
        [UnityTest, Explicit("Writes the V2-2 recording frames; run by name.")]
        public IEnumerator RecordV22FeelSequence()
        {
            Time.timeScale = 1f;
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            var shell = Object.FindAnyObjectByType<AdShell>();
            var flow = Object.FindAnyObjectByType<GameFlow>();
            shell.AutomaticTick = false;
            flow.AutomaticTick = false;
            flow.SetApplicationFocus(true);
            string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "V2-2-recording"));
            if (Directory.Exists(directory)) foreach (var file in Directory.GetFiles(directory, "frame_*.png")) File.Delete(file);
            Directory.CreateDirectory(directory);
            var camera = Camera.main;
            const int width = 390, height = 844;
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
                { antiAliasing = Mathf.Max(1, QualitySettings.antiAliasing) };
            var pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
            var previous = camera.targetTexture;
            float previousAspect = camera.aspect;
            int frame = 0;
            const double dt = 1.0 / 60;
            // Scripted presses at judge time: jumps, one refused press during cooldown, then none.
            var presses = new List<KeyValuePair<double, int>>
            {
                new KeyValuePair<double, int>(.55, -1), new KeyValuePair<double, int>(1.55, 1),
                new KeyValuePair<double, int>(2.35, 1), new KeyValuePair<double, int>(2.5, -1),
                new KeyValuePair<double, int>(3.4, -1)
            };
            int next = 0;
            try
            {
                target.Create();
                camera.targetTexture = target;
                camera.aspect = (float)width / height;
                shell.Advance(flow.Config.Shell.BootDuration);
                shell.AcceptLottery();
                int idleFrames = 30, tail = 60;
                bool started = false;
                while (frame < 1500 && tail > 0)
                {
                    if (!started && idleFrames-- <= 0) { started = shell.StartPlaying(); }
                    else if (started)
                    {
                        while (next < presses.Count && flow.Phase == MachinePhase.Running
                            && flow.Simulation.Time >= presses[next].Key) flow.Move(presses[next++].Value);
                        flow.Advance(dt);
                    }
                    shell.Advance(dt);
                    if (flow.Phase == MachinePhase.Stopped) tail--;
                    Canvas.ForceUpdateCanvases();
                    yield return null;
                    RenderTexture.active = target;
                    pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                    pixels.Apply(false, false);
                    File.WriteAllBytes(Path.Combine(directory, "frame_" + frame.ToString("0000") + ".png"), pixels.EncodeToPNG());
                    frame++;
                }
                TestContext.WriteLine("V2-2 recording frames: " + frame + " in " + directory);
                Assert.That(shell.State, Is.EqualTo(AdShellState.FailDialog));
            }
            finally
            {
                camera.targetTexture = previous;
                camera.aspect = previousAspect;
                RenderTexture.active = null;
                target.Release();
                Object.Destroy(target);
                Object.Destroy(pixels);
            }
        }

        [UnityTest]
        public IEnumerator CaptureSixDeterministicCutsceneFrames()
        {
            Time.timeScale = 1f;
            yield return SceneManager.LoadSceneAsync("CutsceneA", LoadSceneMode.Single);
            var cutscene = Object.FindAnyObjectByType<CutsceneController>();
            Assert.That(cutscene, Is.Not.Null);
            cutscene.AutomaticTick = false;
            foreach (double time in new[] { 1.8, 4.5, 6.8, 8.8, 10.8, 12.8, 13.7, 13.8, 14.5, 16.0 })
            {
                cutscene.Seek(time);
                Assert.That(cutscene.Time, Is.EqualTo(time));
                yield return Capture("cutscene_a_" + time.ToString("00.0", System.Globalization.CultureInfo.InvariantCulture) + ".png", 1600, 900, false);
            }

            yield return SceneManager.LoadSceneAsync("CutsceneB", LoadSceneMode.Single);
            cutscene = Object.FindAnyObjectByType<CutsceneController>();
            Assert.That(cutscene, Is.Not.Null);
            cutscene.AutomaticTick = false;
            foreach (double time in new[] { 0.8, 2.6, 3.6, 4.8, 6.3, 8.4, 10.2, 12.0 })
            {
                cutscene.Seek(time);
                Assert.That(cutscene.Time, Is.EqualTo(time));
                yield return Capture("cutscene_b_" + time.ToString("00.0", System.Globalization.CultureInfo.InvariantCulture) + ".png", 1600, 900, false);
            }
            LogAssert.NoUnexpectedReceived();
        }

        private const string StrictKeyword = "OFFTRACK_STRICT_PALETTE";

        [UnityTest]
        public IEnumerator CaptureOpeningToBootMatchWithAlignedRedCenters()
        {
            Time.timeScale = 1f;
            yield return SceneManager.LoadSceneAsync("CutsceneA", LoadSceneMode.Single);
            var opening = Object.FindAnyObjectByType<CutsceneController>();
            opening.AutomaticTick = false;
            opening.Seek(opening.Duration);
            yield return Capture("cutscene_a_16.0.png", 1600, 900, false);

            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            var shell = Object.FindAnyObjectByType<AdShell>();
            var flow = Object.FindAnyObjectByType<GameFlow>();
            shell.AutomaticTick = false;
            flow.AutomaticTick = false;
            flow.SetApplicationFocus(true);
            shell.Advance(Math.Max(0, flow.Config.Shell.BootDuration * (.5 - shell.Machine.BootProgress)));
            yield return Render("boot.png", 780, 1688, strict: false, mainScene: true);

            var openingPixels = new Texture2D(2, 2, TextureFormat.RGB24, false);
            var bootPixels = new Texture2D(2, 2, TextureFormat.RGB24, false);
            Texture2D comparison = null;
            try
            {
                Assert.That(openingPixels.LoadImage(File.ReadAllBytes(Path.Combine(EvidenceDirectory, "cutscene_a_16.0.png"))), Is.True);
                Assert.That(bootPixels.LoadImage(File.ReadAllBytes(Path.Combine(EvidenceDirectory, "boot.png"))), Is.True);
                var screenCenter = RedCenter(openingPixels, 0);
                // The loading bar is below the middle; only the upper-half brand icon belongs to the match.
                var iconCenter = RedCenter(bootPixels, bootPixels.height / 2);
                Assert.That(screenCenter.x, Is.EqualTo(iconCenter.x).Within(.01f));
                Assert.That(screenCenter.y, Is.EqualTo(iconCenter.y).Within(.01f));
                TestContext.WriteLine($"Match red centers (normalized, bottom-origin): A={screenCenter}, Boot={iconCenter}");

                // Faithful screenshot comparison: equal displayed heights, original aspect ratios.
                const int height = 900, gap = 40;
                int bootWidth = Mathf.RoundToInt((float)bootPixels.width * height / bootPixels.height);
                int width = openingPixels.width + gap + bootWidth;
                comparison = new Texture2D(width, height, TextureFormat.RGB24, false);
                var data = new Color32[width * height];
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                    {
                        data[y * width + x] = x < openingPixels.width ? (Color32)openingPixels.GetPixel(x, y)
                            : x < openingPixels.width + gap ? Palette.Ink
                            : (Color32)bootPixels.GetPixelBilinear((x - openingPixels.width - gap + .5f) / bootWidth,
                                (y + .5f) / height);
                    }
                comparison.SetPixels32(data);
                comparison.Apply(false, false);
                File.WriteAllBytes(Path.Combine(EvidenceDirectory, "opening_boot_match.png"), comparison.EncodeToPNG());
            }
            finally
            {
                Object.Destroy(openingPixels); Object.Destroy(bootPixels);
                if (comparison != null) Object.Destroy(comparison);
            }
            LogAssert.NoUnexpectedReceived();
        }

        private static Vector2 RedCenter(Texture2D texture, int minimumY)
        {
            int minX = texture.width, minY = texture.height, maxX = -1, maxY = -1;
            var pixels = texture.GetPixels32();
            for (int y = minimumY; y < texture.height; y++)
                for (int x = 0; x < texture.width; x++)
                {
                    if (Rgb(pixels[y * texture.width + x]) != Rgb(Palette.Red)) continue;
                    minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                    minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                }
            Assert.That(maxX, Is.GreaterThan(minX), "A visible red screen/icon is required for the match.");
            return new Vector2((minX + maxX + 1f) / (2 * texture.width), (minY + maxY + 1f) / (2 * texture.height));
        }

        // Each Main state is rendered twice per size: a strict pass with hard edges and no MSAA, which
        // must contain only Palette's fixed whitelist, and the saved evidence pass with the blended
        // edges and MSAA that players see. Strict mode preserves the approved dimmer's actual alpha.
        // Sizes: reference portrait, full 1080p letterboxed output, and a 1080p / 150% laptop's
        // 388x840 client window. An additional 585px frame checks fine-groove readability.
        private static IEnumerator CaptureMain(string filename)
        {
            string small = Path.GetFileNameWithoutExtension(filename) + "_1080p.png";
            string window = Path.GetFileNameWithoutExtension(filename) + "_1080p_window.png";
            yield return Render(filename, 780, 1688, strict: true);
            yield return Render(filename, 780, 1688, strict: false, mainScene: true);
            yield return Render(small, 1920, 1080, strict: true);
            yield return Render(small, 1920, 1080, strict: false, mainScene: true);
            yield return Render(window, 388, 840, strict: true);
            yield return Render(window, 388, 840, strict: false, mainScene: true);
            if (filename == "playing_next.png")
            {
                yield return Render("playing_next_585px.png", 585, 1266, strict: true);
                yield return Render("playing_next_585px.png", 585, 1266, strict: false, mainScene: true);
            }
        }

        private static IEnumerator Capture(string filename, int width, int height, bool strictPalette)
        {
            Assert.That(strictPalette, Is.False, "Main states go through CaptureMain.");
            yield return Render(filename, width, height, strict: false);
        }

        private static IEnumerator Render(string filename, int width, int height, bool strict, bool mainScene = false)
        {
            var camera = Camera.main;
            var canvas = Object.FindAnyObjectByType<Canvas>();
            Assert.That(camera, Is.Not.Null);
            Assert.That(canvas, Is.Not.Null);
            Assert.That(canvas.renderMode, Is.EqualTo(RenderMode.ScreenSpaceCamera));
            Assert.That(canvas.worldCamera, Is.EqualTo(camera));
            Directory.CreateDirectory(EvidenceDirectory);
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                name = "M2 acceptance " + filename + (strict ? " strict" : ""),
                // Evidence uses the player's multisampling; the strict proof renders without it.
                antiAliasing = strict ? 1 : Mathf.Max(1, QualitySettings.antiAliasing),
                filterMode = FilterMode.Point
            };
            RenderTexture previousTarget = camera.targetTexture;
            RenderTexture previousActive = RenderTexture.active;
            float previousAspect = camera.aspect;
            Texture2D pixels = null;
            try
            {
                if (strict) Shader.EnableKeyword(StrictKeyword);
                target.Create();
                camera.targetTexture = target;
                camera.aspect = (float)width / height;
                if (strict) AssertVisibleChineseGlyphs();
                Canvas.ForceUpdateCanvases();
                yield return null;
                Canvas.ForceUpdateCanvases();
                yield return null;
                Assert.That(camera.targetTexture, Is.EqualTo(target));
                pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                pixels.Apply(false, false);
                if (strict)
                {
                    var actual = PaletteAssert.AssertExactPalette(pixels.GetPixels32(), filename + " (strict)");
                    Assert.That(actual.Contains(Rgb(Palette.Ink)), Is.True);
                    Assert.That(actual.Contains(Rgb(Palette.Bone)), Is.True);
                    var shell = Object.FindAnyObjectByType<AdShell>();
                    if (shell != null && shell.Machine.IsModalOpen)
                        Assert.That(actual.Contains(Rgb(Palette.Dimmed(Palette.Bone))), Is.True,
                            "Strict rendering must preserve actual full-phone dimming beneath the panel.");
                    yield break;
                }
                byte[] png = pixels.EncodeToPNG();
                Assert.That(png.Length, Is.GreaterThan(1000), "Evidence must contain a rendered frame.");
                string path = Path.Combine(EvidenceDirectory, filename);
                File.WriteAllBytes(path, png);
                if (mainScene) PaletteAssert.AssertWithinPaletteHull(pixels.GetPixels32(), filename);
                TestContext.WriteLine(path);
            }
            finally
            {
                if (strict) Shader.DisableKeyword(StrictKeyword);
                camera.targetTexture = previousTarget;
                camera.aspect = previousAspect;
                RenderTexture.active = previousActive;
                target.Release();
                Object.Destroy(target);
                if (pixels != null) Object.Destroy(pixels);
            }
        }

        private static void AssertVisibleChineseGlyphs()
        {
            foreach (Text text in Object.FindObjectsByType<Text>())
            {
                if (!text.isActiveAndEnabled || string.IsNullOrEmpty(text.text)) continue;
                Assert.That(text.font, Is.Not.Null, text.name);
                text.font.RequestCharactersInTexture(text.text, text.fontSize, text.fontStyle);
                foreach (char character in text.text)
                {
                    if (character < '\u3400' || character > '\u9fff') continue;
                    Assert.That(text.font.HasCharacter(character), Is.True,
                        $"{text.name}: active font {text.font.name} lacks Chinese character {character}.");
                }
            }
        }

        private static void CompleteUsingAcceptedFixture(GameFlow flow)
        {
            var presses = ChartBot.WinningPresses(flow);
            int inputIndex = ChartBot.Play(flow, presses);
            Assert.That(flow.Simulation.Result, Is.Not.Null);
            Assert.That(flow.Simulation.Result.Won, Is.True);
            Assert.That(flow.Simulation.Time, Is.EqualTo(flow.Config.Duration));
            Assert.That(flow.Simulation.Lives, Is.EqualTo(4));
            Assert.That(inputIndex, Is.EqualTo(presses.Length));
        }

        private static int Rgb(Color32 color) => (color.r << 16) | (color.g << 8) | color.b;

        [Serializable]
        private sealed class BotFixture
        {
            public BotInput[] inputs = Array.Empty<BotInput>();
        }

        [Serializable]
        private sealed class BotInput
        {
            public int frame = 0;
            public int dir = 0;
        }
    }
}
