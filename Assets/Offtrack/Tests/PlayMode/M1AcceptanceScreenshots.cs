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

namespace Offtrack.Tests.PlayMode
{
    public sealed class M1AcceptanceScreenshots
    {
        private const int Width = 780;
        private const int Height = 1688;

        // Batch entry: -runTests -testPlatform PlayMode
        // -testFilter Offtrack.Tests.PlayMode.M1AcceptanceScreenshots.CaptureAcceptanceScreenshots
        // Keep graphics enabled: this test saves the real Main camera output, including its UI canvas.
        [UnityTest]
        public IEnumerator CaptureAcceptanceScreenshots()
        {
            Time.timeScale = 1f;
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            var flow = Object.FindAnyObjectByType<GameFlow>();
            Assert.That(flow, Is.Not.Null);
            flow.AutomaticTick = false;
            flow.SetApplicationFocus(true);
            var shell = Object.FindAnyObjectByType<AdShell>();
            Assert.That(shell, Is.Not.Null);
            shell.AutomaticTick = false;
            shell.Advance(flow.Config.Shell.BootDuration);
            Assert.That(shell.AcceptLottery(), Is.True);
            var camera = Camera.main;
            Assert.That(camera, Is.Not.Null);
            var canvas = Object.FindAnyObjectByType<Canvas>();
            Assert.That(canvas, Is.Not.Null);
            Assert.That(canvas.renderMode, Is.EqualTo(RenderMode.ScreenSpaceCamera),
                "Acceptance evidence must include HUD and Chinese UI in the camera render.");
            Assert.That(canvas.worldCamera, Is.EqualTo(camera));
            Assert.That(Object.FindObjectsByType<Text>().Length, Is.GreaterThan(0));

            // Retain the regression render checks without overwriting the read-only M1 evidence.
            string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "M1-M2-review-evidence", "m1"));
            Directory.CreateDirectory(directory);
            var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32)
            {
                name = "M1 acceptance camera target",
                antiAliasing = 1,
                filterMode = FilterMode.Point
            };
            RenderTexture previousTarget = camera.targetTexture;
            RenderTexture previousActive = RenderTexture.active;
            float previousAspect = camera.aspect;
            try
            {
                target.Create();
                camera.targetTexture = target;
                camera.aspect = (float)Width / Height;
                Assert.That(flow.State, Is.EqualTo(RunState.Idle));
                yield return CaptureRenderedFrame(camera, target, Path.Combine(directory, "idle.png"));

                flow.StartRun();
                M1GameplayTests.StepFor(flow, 1.0);
                Assert.That(flow.State, Is.EqualTo(RunState.Playing));
                Assert.That(flow.Simulation.Jump, Is.Null);
                Assert.That(Object.FindAnyObjectByType<ScratchPool>().ActiveCount, Is.GreaterThanOrEqualTo(2));
                yield return CaptureRenderedFrame(camera, target, Path.Combine(directory, "playing.png"));

                M1GameplayTests.DriveToEndWithoutInput(flow);
                Assert.That(flow.Simulation.Result.Won, Is.False);
                yield return CaptureRenderedFrame(camera, target, Path.Combine(directory, "ended_lost.png"));
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                camera.targetTexture = previousTarget;
                camera.aspect = previousAspect;
                RenderTexture.active = previousActive;
                target.Release();
                Object.Destroy(target);
                Time.timeScale = 1f;
            }
        }

        private static IEnumerator CaptureRenderedFrame(Camera camera, RenderTexture target, string path)
        {
            Canvas.ForceUpdateCanvases();
            // Two ordinary player-loop frames also work in batchmode and allow URP and ScreenSpaceCamera UI
            // to render normally. WaitForEndOfFrame is intentionally avoided because it can stall headless tests.
            yield return null;
            Canvas.ForceUpdateCanvases();
            yield return null;
            Assert.That(camera.targetTexture, Is.EqualTo(target));
            var pixels = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            RenderTexture beforeRead = RenderTexture.active;
            try
            {
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
                pixels.Apply(false, false);
                byte[] png = pixels.EncodeToPNG();
                Assert.That(png.Length, Is.GreaterThan(1000), "A rendered scene should not be an empty target.");
                File.WriteAllBytes(path, png);
                var uniqueColors = new HashSet<int>();
                foreach (Color32 pixel in pixels.GetPixels32())
                    uniqueColors.Add((pixel.r << 16) | (pixel.g << 8) | pixel.b);
                var allowedColors = new HashSet<int>
                {
                    Rgb(Palette.Ink), Rgb(Palette.Red), Rgb(Palette.Bone), Rgb(Palette.DarkRed)
                };
                CollectionAssert.IsSubsetOf(uniqueColors, allowedColors,
                    "Actual camera pixels must belong to the four configured palette colors.");
                Assert.That(uniqueColors.Contains(Rgb(Palette.Ink)), Is.True, "The scene background must be visible.");
                Assert.That(uniqueColors.Contains(Rgb(Palette.Bone)), Is.True, "The scene needle and lives must be visible.");
                Assert.That(uniqueColors.Contains(Rgb(Palette.DarkRed)), Is.True, "Scene geometry and HUD text must be visible.");
                TestContext.WriteLine("Rendered RGB color count: " + uniqueColors.Count);
                TestContext.WriteLine(path);
            }
            finally
            {
                RenderTexture.active = beforeRead;
                Object.Destroy(pixels);
            }
        }

        private static int Rgb(Color32 color) => (color.r << 16) | (color.g << 8) | color.b;
    }
}
