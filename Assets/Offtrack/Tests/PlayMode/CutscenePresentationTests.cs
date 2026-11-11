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
    public sealed class CutscenePresentationTests
    {
        [UnityTest]
        public IEnumerator EntireSixteenByNineFrameFitsAndPortraitLetterboxesRenderInk()
        {
            Time.timeScale = 1f;
            yield return SceneManager.LoadSceneAsync("CutsceneA", LoadSceneMode.Single);
            var cutscene = Object.FindAnyObjectByType<CutsceneController>();
            Assert.That(cutscene, Is.Not.Null);
            cutscene.AutomaticTick = false;
            cutscene.Seek(10.6);
            var camera = Camera.main;
            var frame = cutscene.Frame;
            var canvas = frame.GetComponentInParent<Canvas>();
            Assert.That(camera, Is.Not.Null);
            Assert.That(canvas.renderMode, Is.EqualTo(RenderMode.ScreenSpaceCamera));
            Assert.That(canvas.worldCamera, Is.EqualTo(camera));
            var sizes = new[] { new Vector2Int(1600, 900), new Vector2Int(1280, 720), new Vector2Int(585, 1266) };
            var originalScreen = new Vector2Int(Screen.width, Screen.height);
            var originalTarget = camera.targetTexture;
            var originalActive = RenderTexture.active;
            var originalAspect = camera.aspect;
            var corners = new Vector3[4];
            foreach (var size in sizes)
            {
                var target = new RenderTexture(size.x, size.y, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
                Texture2D pixels = null;
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
                    Assert.That(new Vector2Int(Screen.width, Screen.height), Is.EqualTo(originalScreen));
                    frame.GetWorldCorners(corners);
                    var viewport = corners.Select(camera.WorldToViewportPoint).ToArray();
                    string context = $"Cutscene target={size.x}x{size.y}, bottomLeft={viewport[0]}, topRight={viewport[2]}";
                    TestContext.WriteLine(context);
                    foreach (var point in viewport)
                    {
                        Assert.That(point.z, Is.GreaterThan(0f), context);
                        Assert.That(point.x, Is.InRange(-.0001f, 1.0001f), context);
                        Assert.That(point.y, Is.InRange(-.0001f, 1.0001f), context);
                    }
                    var center = camera.WorldToViewportPoint(frame.TransformPoint(frame.rect.center));
                    Assert.That(center.x, Is.EqualTo(.5f).Within(.0001f), context);
                    Assert.That(center.y, Is.EqualTo(.5f).Within(.0001f), context);
                    Assert.That(frame.lossyScale.x, Is.EqualTo(frame.lossyScale.y).Within(.000001f), context);
                    float scale = Mathf.Min(size.x / 1600f, size.y / 900f);
                    Assert.That(viewport[2].x - viewport[0].x, Is.EqualTo(1600f * scale / size.x).Within(.0001f), context);
                    Assert.That(viewport[2].y - viewport[0].y, Is.EqualTo(900f * scale / size.y).Within(.0001f), context);

                    // Read the actual rendered portrait bars, including their full width.
                    int lowerBarEnd = Mathf.FloorToInt(viewport[0].y * size.y) - 1;
                    int upperBarStart = Mathf.CeilToInt(viewport[2].y * size.y) + 1;
                    if (lowerBarEnd > 0)
                    {
                        pixels = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
                        RenderTexture.active = target;
                        pixels.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0, false);
                        pixels.Apply(false, false);
                        var data = pixels.GetPixels32();
                        var ink = Palette.Ink;
                        for (var y = 0; y < size.y; y++)
                        {
                            if (y >= lowerBarEnd && y < upperBarStart) continue;
                            for (var x = 0; x < size.x; x++)
                            {
                                var p = data[y * size.x + x];
                                if (p.r != ink.r || p.g != ink.g || p.b != ink.b)
                                    Assert.Fail($"Letterbox pixel ({x},{y}) is {p}, expected ink. {context}");
                            }
                        }
                    }
                }
                finally
                {
                    camera.targetTexture = originalTarget;
                    camera.aspect = originalAspect;
                    RenderTexture.active = originalActive;
                    target.Release();
                    Object.Destroy(target);
                    if (pixels != null) Object.Destroy(pixels);
                }
            }
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator AutomaticCutsceneDiscardsTheFrameSpanningStartupOrReenable()
        {
            Time.timeScale = 1f;
            yield return SceneManager.LoadSceneAsync("CutsceneA", LoadSceneMode.Single);
            var cutscene = Object.FindAnyObjectByType<CutsceneController>();
            Assert.That(cutscene, Is.Not.Null);
            try
            {
                for (int mode = 0; mode < 2; mode++)
                {
                    cutscene.AutomaticTick = false;
                    cutscene.Seek(0);
                    yield return null;
                    cutscene.AutomaticTick = true;
                    if (mode == 1)
                    {
                        // Consume the tick-toggle reset, then independently exercise OnEnable.
                        yield return null;
                        cutscene.enabled = false;
                        cutscene.enabled = true;
                    }

                    // Reproduce a real long frame, as produced by standalone splash/scene loading.
                    // This does not inject time or bypass the production Update path.
                    System.Threading.Thread.Sleep(150);
                    yield return null;
                    Assert.That(Time.unscaledDeltaTime, Is.GreaterThanOrEqualTo(.1f),
                        "The regression requires an actual long first sample.");
                    Assert.That(cutscene.Time, Is.Zero,
                        "The first visible frame must not include time spent loading or disabled.");
                    yield return null;
                    Assert.That(cutscene.Time, Is.GreaterThan(0));
                    Assert.That(cutscene.Time, Is.EqualTo((double)Time.unscaledDeltaTime).Within(1e-6),
                        "The following frame must advance exactly once using its current sample.");
                }
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                if (cutscene != null)
                {
                    cutscene.enabled = true;
                    cutscene.AutomaticTick = true;
                }
            }
        }

        [UnityTest]
        public IEnumerator OpeningCompletesAtFifteenSecondsWithoutASkipAndEntersBoot()
        {
            Time.timeScale = 1f;
            yield return SceneManager.LoadSceneAsync("CutsceneA", LoadSceneMode.Single);
            var cutscene = Object.FindAnyObjectByType<CutsceneController>();
            Assert.That(cutscene, Is.Not.Null);
            cutscene.AutomaticTick = false;
            cutscene.Seek(0);
            cutscene.Advance(14.75);
            yield return null;
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("CutsceneA"));
            Assert.That(cutscene.Time, Is.EqualTo(14.75));
            Assert.That(cutscene.CurrentFrame.Dim, Is.EqualTo(1));
            // Freeze Boot on scene load so editor scheduling delays do not skip the observed state.
            UnityEngine.Events.UnityAction<Scene, LoadSceneMode> observeMain = (scene, mode) =>
            {
                if (scene.name == "Main") Object.FindAnyObjectByType<AdShell>().AutomaticTick = false;
            };
            SceneManager.sceneLoaded += observeMain;
            try
            {
                cutscene.Advance(.25);
                for (var i = 0; i < 30 && SceneManager.GetActiveScene().name != "Main"; i++) yield return null;
            }
            finally
            {
                SceneManager.sceneLoaded -= observeMain;
            }
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("Main"));
            var shell = Object.FindAnyObjectByType<AdShell>();
            Assert.That(shell, Is.Not.Null);
            shell.AutomaticTick = false;
            Assert.That(shell.State, Is.EqualTo(AdShellState.Boot));
            Assert.That(Object.FindAnyObjectByType<GameFlow>().State, Is.EqualTo(RunState.Idle));
            LogAssert.NoUnexpectedReceived();
        }
    }
}
