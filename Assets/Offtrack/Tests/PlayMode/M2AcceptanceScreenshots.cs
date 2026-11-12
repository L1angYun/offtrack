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
        public IEnumerator CaptureSevenMainStatesWithOnlyTheFourPaletteColors()
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
            Assert.That(shell.StartPlaying(), Is.True);
            M1GameplayTests.StepFor(flow, 1.0);
            yield return CaptureMain("playing.png");

            Assert.That(shell.RequestSkip(), Is.True);
            Assert.That(shell.State, Is.EqualTo(AdShellState.ConfirmSkip));
            Assert.That(flow.IsPaused, Is.True);
            yield return CaptureMain("confirm_skip.png");

            Assert.That(shell.Stay(), Is.True);
            M1GameplayTests.DriveToEndWithoutInput(flow);
            Assert.That(shell.State, Is.EqualTo(AdShellState.FailDialog));
            yield return CaptureMain("fail_dialog.png");

            Assert.That(shell.Retry(), Is.True);
            Assert.That(shell.StartPlaying(), Is.True);
            CompleteUsingAcceptedFixture(flow);
            Assert.That(shell.State, Is.EqualTo(AdShellState.Complete));
            yield return CaptureMain("complete.png");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator CaptureSixDeterministicCutsceneFrames()
        {
            Time.timeScale = 1f;
            yield return SceneManager.LoadSceneAsync("CutsceneA", LoadSceneMode.Single);
            var cutscene = Object.FindAnyObjectByType<CutsceneController>();
            Assert.That(cutscene, Is.Not.Null);
            cutscene.AutomaticTick = false;
            foreach (double time in new[] { 6.0, 10.6, 12.8 })
            {
                cutscene.Seek(time);
                Assert.That(cutscene.Time, Is.EqualTo(time));
                yield return Capture("cutscene_a_" + time.ToString("00.0", System.Globalization.CultureInfo.InvariantCulture) + ".png", 1600, 900, false);
            }

            yield return SceneManager.LoadSceneAsync("CutsceneB", LoadSceneMode.Single);
            cutscene = Object.FindAnyObjectByType<CutsceneController>();
            Assert.That(cutscene, Is.Not.Null);
            cutscene.AutomaticTick = false;
            foreach (double time in new[] { 1.0, 7.0, 8.5 })
            {
                cutscene.Seek(time);
                Assert.That(cutscene.Time, Is.EqualTo(time));
                yield return Capture("cutscene_b_" + time.ToString("00.0", System.Globalization.CultureInfo.InvariantCulture) + ".png", 1600, 900, false);
            }
            LogAssert.NoUnexpectedReceived();
        }

        private const string StrictKeyword = "OFFTRACK_STRICT_PALETTE";

        // Each Main state is rendered twice per size: a strict pass with hard edges and no MSAA, which
        // must contain only the four exact palette colors (no fifth color, glow, dim or gradient can
        // pass), and the saved evidence pass with the blended edges and MSAA that players see.
        // Sizes: the reference, and the window a 1920x1080 / 150% laptop gets (388x840 client).
        private static IEnumerator CaptureMain(string filename)
        {
            string small = Path.GetFileNameWithoutExtension(filename) + "_1080p.png";
            yield return Render(filename, 780, 1688, strict: true);
            yield return Render(filename, 780, 1688, strict: false, mainScene: true);
            yield return Render(small, 388, 840, strict: true);
            yield return Render(small, 388, 840, strict: false, mainScene: true);
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
            string fixturePath = Path.Combine(Application.dataPath, "Offtrack", "Tests", "EditMode", "Fixtures", "bot_early.json");
            var fixture = JsonUtility.FromJson<BotFixture>(File.ReadAllText(fixturePath));
            Assert.That(fixture.inputs.Length, Is.EqualTo(14));
            int inputIndex = 0;
            for (int frame = 0; frame < 3000 && flow.State == RunState.Playing; frame++)
            {
                while (inputIndex < fixture.inputs.Length && fixture.inputs[inputIndex].frame == frame)
                {
                    Assert.That(flow.Move(fixture.inputs[inputIndex].dir).Result, Is.EqualTo("jump"));
                    inputIndex++;
                }
                flow.Advance(1.0 / 60.0);
            }
            Assert.That(flow.Simulation.Result, Is.Not.Null);
            Assert.That(flow.Simulation.Result.Won, Is.True);
            Assert.That(flow.Simulation.Time, Is.EqualTo(36.0));
            Assert.That(flow.Simulation.Lives, Is.EqualTo(4));
            Assert.That(inputIndex, Is.EqualTo(14));
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
