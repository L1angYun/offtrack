using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
    public sealed class CutscenePresentationTests
    {
        [Test]
        public void FadeStepsUseNamedOpaqueShadesOfTheirOwnHue()
        {
            var strengths = new[] { 0d, .15, .4, .6, 1 };
            var families = new[] { CutsceneInk.Bone, CutsceneInk.Red, CutsceneInk.DarkRed };
            var expected = new[]
            {
                new[] { Palette.Ink, Palette.Bone15, Palette.Bone40, Palette.Bone60, Palette.Bone },
                new[] { Palette.Ink, Palette.Red15, Palette.Red40, Palette.Red60, Palette.Red },
                new[] { Palette.Ink, Palette.DarkRed15, Palette.DarkRed40, Palette.DarkRed60, Palette.DarkRed }
            };
            for (var hue = 0; hue < families.Length; hue++)
                for (var step = 0; step < strengths.Length; step++)
                    Assert.That(CutsceneGraphic.ResolveInk(families[hue], strengths[step]), Is.EqualTo(expected[hue][step]));
            foreach (CutsceneInk ink in System.Enum.GetValues(typeof(CutsceneInk)))
                for (var step = 0; step <= 100; step++)
                {
                    var color = CutsceneGraphic.ResolveInk(ink, step / 100d);
                    Assert.That(color.a, Is.EqualTo(255));
                    Assert.That(Palette.OpaqueColors.Contains(color), Is.True, $"{ink} at {step}% must use Palette.");
                }
            Assert.That(CutsceneGraphic.ResolveInk(CutsceneInk.DarkRed15), Is.EqualTo(Palette.DarkRed15));
        }

        [UnityTest]
        public IEnumerator TeacherContrastsWithTheBlackboardWithoutAnInkOutline()
        {
            yield return SceneManager.LoadSceneAsync("CutsceneA", LoadSceneMode.Single);
            var cutscene = Object.FindAnyObjectByType<CutsceneController>();
            cutscene.AutomaticTick = false;
            cutscene.Seek(4.5);
            yield return CaptureStrict(pixels =>
            {
                var board = pixels[(900 - 200) * 1600 + 800];
                var teacher = pixels[(900 - 525) * 1600 + 450];
                Assert.That(PaletteAssert.Rgb(board), Is.EqualTo(PaletteAssert.Rgb(Palette.DarkRed15)));
                Assert.That(PaletteAssert.Rgb(teacher), Is.EqualTo(PaletteAssert.Rgb(Palette.DarkRed)));
                Assert.That((Luminance(teacher) + .05) / (Luminance(board) + .05), Is.GreaterThanOrEqualTo(2));
            });
        }

        [Test]
        public void RotatedLimbsUseClockwiseAuthoredRotationBeforeTheCamera()
        {
            var vertices = MeshVertices(new CutsceneCommand
            {
                Primitive = CutscenePrimitive.RotatedRectangle, Ink = CutsceneInk.Bone,
                X = 300, Y = 200, Width = 100, Height = 20, Rotation = 90, Alpha = 1, Scale = 1
            }, 2, 20, 30);
            Assert.That(vertices.Length, Is.EqualTo(6));
            Assert.That(vertices[0].position.x, Is.EqualTo(740).Within(.001));
            Assert.That(vertices[0].position.y, Is.EqualTo(-350).Within(.001));
            Assert.That(vertices.Min(v => v.position.x), Is.EqualTo(700).Within(.001));
            Assert.That(vertices.Max(v => v.position.y), Is.EqualTo(-350).Within(.001));
            Assert.That(vertices.Min(v => v.position.y), Is.EqualTo(-550).Within(.001));
        }

        [TestCase(CutscenePrimitive.Rectangle)]
        [TestCase(CutscenePrimitive.Outline)]
        [TestCase(CutscenePrimitive.Circle)]
        [TestCase(CutscenePrimitive.Ring)]
        [TestCase(CutscenePrimitive.Line)]
        [TestCase(CutscenePrimitive.Label)]
        [TestCase(CutscenePrimitive.ThoughtBubble)]
        public void CameraMovesShapesAndClippedGlyphsWithIdenticalMath(CutscenePrimitive primitive)
        {
            var command = new CutsceneCommand
            {
                Primitive = primitive, Ink = CutsceneInk.Bone, X = 600, Y = 400,
                Width = 30, Height = 44, Stroke = 2, Text = "MMMM", FontSize = 24,
                Alpha = 1, Scale = 1, Align = CutsceneTextAlign.Left,
                HasClip = true, ClipX = 600, ClipY = 360, ClipWidth = 30, ClipHeight = 60
            };
            var original = MeshVertices(command);
            var transformed = MeshVertices(command, 1.1, 40, 25);
            Assert.That(original.Length, Is.GreaterThan(0));
            Assert.That(transformed.Length, Is.EqualTo(original.Length));
            for (var i = 0; i < original.Length; i++)
            {
                Assert.That(transformed[i].position.x, Is.EqualTo(original[i].position.x * 1.1 + 40).Within(.001));
                Assert.That(transformed[i].position.y, Is.EqualTo(original[i].position.y * 1.1 - 25).Within(.001));
                Assert.That(transformed[i].uv0, Is.EqualTo(original[i].uv0));
            }
        }

        [Test]
        public void ZoomedGeometryCannotExtendIntoTheLetterbox()
        {
            var vertices = MeshVertices(new CutsceneCommand
            {
                Primitive = CutscenePrimitive.RotatedRectangle, Ink = CutsceneInk.Bone,
                X = 1300, Y = 600, Width = 400, Height = 400, Rotation = 30, Alpha = 1, Scale = 1
            }, 1.15, -100, -50);
            Assert.That(vertices.Length, Is.GreaterThan(0));
            Assert.That(vertices.Any(v => Mathf.Abs(v.position.x - 1600) < .001), Is.True);
            foreach (var vertex in vertices)
            {
                Assert.That(vertex.position.x, Is.InRange(0, 1600));
                Assert.That(vertex.position.y, Is.InRange(-900, 0));
            }
        }

        [Test]
        public void ThoughtBubbleWidthUsesGlyphAdvancesInsteadOfCharacterCount()
        {
            var command = new CutsceneCommand
            {
                Primitive = CutscenePrimitive.ThoughtBubble, Ink = CutsceneInk.Bone,
                X = 800, Y = 450, Height = 44, Stroke = 2, Text = "iiii",
                FontSize = 24, Alpha = 1, Scale = 1
            };
            var narrow = MeshVertices(command);
            command.Text = "WWWW";
            var wide = MeshVertices(command);
            var narrowWidth = narrow.Max(v => v.position.x) - narrow.Min(v => v.position.x);
            var wideWidth = wide.Max(v => v.position.x) - wide.Min(v => v.position.x);
            Assert.That(wideWidth, Is.GreaterThan(narrowWidth * 1.3),
                "Equal character counts can need very different bubble widths.");
        }

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
        public IEnumerator OpeningCompletesAtSixteenSecondsWithoutASkipAndEntersBoot()
        {
            Time.timeScale = 1f;
            yield return SceneManager.LoadSceneAsync("CutsceneA", LoadSceneMode.Single);
            var cutscene = Object.FindAnyObjectByType<CutsceneController>();
            Assert.That(cutscene, Is.Not.Null);
            cutscene.AutomaticTick = false;
            cutscene.Seek(0);
            cutscene.Advance(15.75);
            yield return null;
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("CutsceneA"));
            Assert.That(cutscene.Time, Is.EqualTo(15.75));
            Assert.That(cutscene.Duration, Is.EqualTo(16));
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

        [UnityTest]
        public IEnumerator EveryStagedShotRendersOnlyTheStrictPalette()
        {
            var scenes = new[] { "CutsceneA", "CutsceneB" };
            var samples = new[]
            {
                new[] { .2, .4, .8, 2.8, 6.0, 8.8, 9.8, 10.8, 12.2, 13.7, 13.8, 14.5, 15.99 },
                new[] { .2, 1.2, 1.4, 2.4, 4.8, 6.7, 8.8, 10.2, 10.5, 10.7, 10.8, 12.0 }
            };
            for (var scene = 0; scene < scenes.Length; scene++)
            {
                yield return SceneManager.LoadSceneAsync(scenes[scene], LoadSceneMode.Single);
                var cutscene = Object.FindAnyObjectByType<CutsceneController>();
                cutscene.AutomaticTick = false;
                foreach (var time in samples[scene])
                {
                    cutscene.Seek(time);
                    yield return CaptureStrict(pixels => PaletteAssert.AssertExactPalette(pixels,
                        $"{scenes[scene]} t={time:0.00}"));
                }
            }
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator SeekingBackToTheSameTimeRecreatesTheSameRenderedPixels()
        {
            foreach (var scene in new[] { "CutsceneA", "CutsceneB" })
            {
                yield return SceneManager.LoadSceneAsync(scene, LoadSceneMode.Single);
                var cutscene = Object.FindAnyObjectByType<CutsceneController>();
                cutscene.AutomaticTick = false;
                foreach (var time in new[] { 4.8, 9.8 })
                {
                    Color32[] first = null;
                    cutscene.Seek(time);
                    yield return CaptureStrict(pixels => first = pixels);
                    cutscene.Seek(11.8);
                    yield return null;
                    cutscene.Seek(.2);
                    yield return null;
                    cutscene.Seek(time);
                    yield return CaptureStrict(pixels => CollectionAssert.AreEqual(first, pixels,
                        $"{scene} t={time}: intervening shots must not change text, pose, camera, or pixels."));
                }
            }
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator ReportShotShowsALargeCenteredGradeWithoutTheStudent()
        {
            yield return SceneManager.LoadSceneAsync("CutsceneB", LoadSceneMode.Single);
            var cutscene = Object.FindAnyObjectByType<CutsceneController>();
            cutscene.AutomaticTick = false;
            cutscene.Seek((cutscene.Settings.Timing.ReportInEnd + cutscene.Settings.Timing.ReportOutStart) / 2);
            Assert.That(cutscene.CurrentFrame.ReportVisible, Is.True);
            Assert.That(cutscene.CurrentFrame.StudentVisible, Is.False,
                "The report is a separate shot so it cannot compete with the bone-white student.");
            yield return CaptureStrict(pixels =>
            {
                PaletteAssert.AssertExactPalette(pixels, "Report close-up");
                var bonePixels = pixels.Count(p => PaletteAssert.Rgb(p) == PaletteAssert.Rgb(Palette.Bone));
                Assert.That((double)bonePixels / pixels.Length, Is.GreaterThan(.35),
                    "The report must read as a close-up, rather than a small overlay on the classroom.");
                var bounds = ColorBounds(pixels, Palette.Red);
                Assert.That(bounds.height, Is.GreaterThanOrEqualTo(120), "58 must be legible at close-up scale.");
                Assert.That(bounds.center.x, Is.EqualTo(800).Within(100));
                Assert.That(bounds.center.y, Is.EqualTo(450).Within(100));
            });
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator OpeningEndsOnACenteredRedScreenForTheMatchCut()
        {
            yield return SceneManager.LoadSceneAsync("CutsceneA", LoadSceneMode.Single);
            var cutscene = Object.FindAnyObjectByType<CutsceneController>();
            cutscene.AutomaticTick = false;
            cutscene.Seek(16);
            yield return CaptureStrict(pixels =>
            {
                var bounds = ColorBounds(pixels, Palette.Red);
                Assert.That(bounds.width, Is.GreaterThanOrEqualTo(320), "The phone screen must occupy the center of the frame, not remain a handheld detail.");
                Assert.That(bounds.height, Is.GreaterThanOrEqualTo(400));
                Assert.That(bounds.center.x / 1600d, Is.EqualTo(.5).Within(.002));
                // Pixel storage is bottom-up; the reference coordinate and boot design are top-down.
                Assert.That(1 - bounds.center.y / 900d, Is.EqualTo(758d / 1688).Within(.002));
                Assert.That(PaletteAssert.Rgb(pixels[450 * 1600 + 800]), Is.EqualTo(PaletteAssert.Rgb(Palette.Red)));
            });
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator ClosingShowsTheAdOnThePhoneThenContinuesIntoTheAd()
        {
            yield return SceneManager.LoadSceneAsync("CutsceneB", LoadSceneMode.Single);
            var cutscene = Object.FindAnyObjectByType<CutsceneController>();
            cutscene.AutomaticTick = false;
            cutscene.Seek(0);
            cutscene.Advance(12);
            Assert.That(cutscene.Duration, Is.EqualTo(12));
            Assert.That(cutscene.IsEnd, Is.True);
            Color32[] held = null;
            yield return CaptureStrict(pixels =>
            {
                held = pixels;
                PaletteAssert.AssertExactPalette(pixels, "Closing ad close-up");
                var red = ColorBounds(pixels, Palette.Red);
                Assert.That(red.width * red.height, Is.GreaterThan(5000), "The phone's screen shows the red music app icon.");
                Assert.That(red.center.x, Is.EqualTo(800).Within(40), "The ad sits in the middle of the close-up.");
            });
            // Held briefly, then the ad on the phone becomes the ad on screen (Main's boot).
            double hold = cutscene.Settings.ClosingContinueAfter;
            cutscene.Advance(hold * .5);
            yield return CaptureStrict(pixels => CollectionAssert.AreEqual(held, pixels));
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("CutsceneB"));
            cutscene.Advance(hold);
            for (var i = 0; i < 30 && SceneManager.GetActiveScene().name != "Main"; i++) yield return null;
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("Main"));
            Assert.That(Object.FindAnyObjectByType<AdShell>().State, Is.EqualTo(AdShellState.Boot), "The ad starts again.");
            LogAssert.NoUnexpectedReceived();
        }

        private static RectInt ColorBounds(Color32[] pixels, Color32 color)
        {
            int minX = 1600, minY = 900, maxX = -1, maxY = -1;
            var rgb = PaletteAssert.Rgb(color);
            for (var y = 0; y < 900; y++)
                for (var x = 0; x < 1600; x++)
                    if (PaletteAssert.Rgb(pixels[y * 1600 + x]) == rgb)
                    {
                        minX = Mathf.Min(minX, x); minY = Mathf.Min(minY, y);
                        maxX = Mathf.Max(maxX, x); maxY = Mathf.Max(maxY, y);
                    }
            Assert.That(maxX, Is.GreaterThanOrEqualTo(0), $"Expected rendered color {color}.");
            return new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }

        private static double Luminance(Color32 color)
        {
            double Linear(byte channel)
            {
                var value = channel / 255d;
                return value <= .04045 ? value / 12.92 : System.Math.Pow((value + .055) / 1.055, 2.4);
            }
            return .2126 * Linear(color.r) + .7152 * Linear(color.g) + .0722 * Linear(color.b);
        }

        private static UIVertex[] MeshVertices(CutsceneCommand command, double scale = 1, double x = 0, double y = 0)
        {
            var node = new GameObject("Cutscene mesh under test", typeof(RectTransform), typeof(CutsceneGraphic));
            try
            {
                var graphic = node.GetComponent<CutsceneGraphic>();
                graphic.SetCommand(command, 32, scale, x, y);
                using (var helper = new VertexHelper())
                {
                    typeof(CutsceneGraphic).GetMethod("OnPopulateMesh", BindingFlags.Instance | BindingFlags.NonPublic,
                        null, new[] { typeof(VertexHelper) }, null)
                        .Invoke(graphic, new object[] { helper });
                    var vertices = new List<UIVertex>();
                    helper.GetUIVertexStream(vertices);
                    return vertices.ToArray();
                }
            }
            finally
            {
                Object.DestroyImmediate(node);
            }
        }

        private static IEnumerator CaptureStrict(System.Action<Color32[]> inspect)
        {
            const string keyword = "OFFTRACK_STRICT_PALETTE";
            var camera = Camera.main;
            Assert.That(camera, Is.Not.Null);
            var originalTarget = camera.targetTexture;
            var originalActive = RenderTexture.active;
            var originalAspect = camera.aspect;
            var originalMsaa = camera.allowMSAA;
            var originalHdr = camera.allowHDR;
            var keywordEnabled = Shader.IsKeywordEnabled(keyword);
            var target = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            Texture2D image = null;
            try
            {
                target.Create();
                Shader.EnableKeyword(keyword);
                camera.allowMSAA = false;
                camera.allowHDR = false;
                camera.targetTexture = target;
                camera.aspect = 1600f / 900;
                Canvas.ForceUpdateCanvases();
                yield return null;
                Canvas.ForceUpdateCanvases();
                yield return null;
                image = new Texture2D(1600, 900, TextureFormat.RGB24, false);
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0, false);
                image.Apply(false, false);
                inspect(image.GetPixels32());
            }
            finally
            {
                if (!keywordEnabled) Shader.DisableKeyword(keyword);
                camera.targetTexture = originalTarget;
                camera.aspect = originalAspect;
                camera.allowMSAA = originalMsaa;
                camera.allowHDR = originalHdr;
                RenderTexture.active = originalActive;
                target.Release();
                Object.Destroy(target);
                if (image != null) Object.Destroy(image);
            }
        }
    }
}
