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
    // The three portrait cutscenes as the player sees them. Pixel checks read the scene's own camera
    // while CutsceneController plays the saved GameConfig. Positions are reference pixels measured
    // from the top-left corner, as in the draw commands; captured rows are stored bottom-up.
    public sealed class CutscenePresentationTests
    {
        private const int Width = PortraitScore.Width, Height = PortraitScore.Height;
        // Swatches written out rather than read from Palette, so a changed production color fails here.
        private const int InkRgb = 0x0B0707, RedRgb = 0xEC4141, BoneRgb = 0xF0E6D6, DarkRedRgb = 0x8C3A3A, DarkRed15Rgb = 0x1E0F0F;
        private const int Shadow30Rgb = 0x3F1C1C, Shadow15Rgb = 0x2F1515, Shadow08Rgb = 0x271212;
        // The shades a fading element passes through, dimmest first: 15 %, 40 %, 60 %, then full strength.
        private static readonly int[] BoneSteps = { 0x2D2826, 0x67605A, 0x948D83, BoneRgb };
        private static readonly int[] RedSteps = { 0x2D1010, 0x651E1E, 0x922A2A, RedRgb };
        // In the room scenes the wall ends at the floor line, 1530 px down the frame.
        private const int WallBottom = 1520;
        // Rows that cross only the head of a silhouette passing at mid-screen.
        private const int HeadBandTop = 985, HeadBandBottom = 1045;

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
            // The passing shadows are three fixed swatches; visibility never walks them along a hue.
            foreach (var strength in new[] { .15, .4, .6, 1 })
            {
                Assert.That(PaletteAssert.Rgb(CutsceneGraphic.ResolveInk(CutsceneInk.Shadow30, strength)), Is.EqualTo(Shadow30Rgb));
                Assert.That(PaletteAssert.Rgb(CutsceneGraphic.ResolveInk(CutsceneInk.Shadow15, strength)), Is.EqualTo(Shadow15Rgb));
                Assert.That(PaletteAssert.Rgb(CutsceneGraphic.ResolveInk(CutsceneInk.Shadow08, strength)), Is.EqualTo(Shadow08Rgb));
            }
        }

        [UnityTest]
        public IEnumerator TeacherContrastsWithTheBlackboardWithoutAnInkOutline()
        {
            yield return SceneManager.LoadSceneAsync("CutsceneA", LoadSceneMode.Single);
            var cutscene = Object.FindAnyObjectByType<CutsceneController>();
            cutscene.AutomaticTick = false;
            cutscene.Seek(4.5);
            var commands = new List<CutsceneCommand>();
            CutsceneTimeline.DrawOpeningPortrait(4.5, cutscene.Settings.Timing, cutscene.Settings.Portrait, commands, out _);
            var head = commands.Last(command => command.SubjectId == 5 && command.Primitive == CutscenePrimitive.Circle);
            // The board shot holds the teacher's whole head, with bare board on its right.
            Assert.That(head.X - head.Width, Is.GreaterThan(0));
            Assert.That(head.X + head.Width + 2, Is.LessThan(Width));
            Assert.That(head.Y, Is.InRange(head.Width, Height - head.Width));
            yield return CaptureStrict(pixels =>
            {
                var teacher = At(pixels, (int)head.X, (int)head.Y);
                // Two pixels past the head's edge: an ink outline would sit exactly here.
                var board = At(pixels, (int)(head.X + head.Width + 2), (int)head.Y);
                Assert.That(PaletteAssert.Rgb(board), Is.EqualTo(DarkRed15Rgb));
                Assert.That(PaletteAssert.Rgb(teacher), Is.EqualTo(DarkRedRgb));
                Assert.That((Luminance(teacher) + .05) / (Luminance(board) + .05), Is.GreaterThanOrEqualTo(2));
            });
        }

        [Test]
        public void RotatedLimbsUseClockwiseAuthoredRotation()
        {
            // A 100 x 20 bar centered on (350, 210), turned a quarter clockwise: it stands 20 x 100,
            // and the corner authored at the top right ends up at the bottom right.
            var vertices = MeshVertices(new CutsceneCommand
            {
                Primitive = CutscenePrimitive.RotatedRectangle, Ink = CutsceneInk.Bone,
                X = 300, Y = 200, Width = 100, Height = 20, Rotation = 90, Alpha = 1, Scale = 1
            });
            Assert.That(vertices.Length, Is.EqualTo(6));
            Assert.That(vertices[0].position.x, Is.EqualTo(360).Within(.001));
            Assert.That(vertices[0].position.y, Is.EqualTo(-160).Within(.001));
            Assert.That(vertices[1].position.x, Is.EqualTo(360).Within(.001));
            Assert.That(vertices[1].position.y, Is.EqualTo(-260).Within(.001));
            AssertBounds(vertices, 340, 360, -260, -160, "quarter-turned bar");
        }

        [Test]
        public void EveryPrimitiveMeshesAtItsAuthoredPortraitCoordinates()
        {
            CutsceneCommand Shape(CutscenePrimitive primitive, double x, double y, double width, double height, double stroke = 0) =>
                new CutsceneCommand
                {
                    Primitive = primitive, Ink = CutsceneInk.Bone, X = x, Y = y, Width = width, Height = height,
                    Stroke = stroke, Alpha = 1, Scale = 1
                };
            // Mesh space is the reference frame with y pointing up from its top edge.
            AssertBounds(MeshVertices(Shape(CutscenePrimitive.Rectangle, 600, 400, 30, 44)), 600, 630, -444, -400, "rectangle");
            var outline = MeshVertices(Shape(CutscenePrimitive.Outline, 600, 400, 30, 44, 2));
            Assert.That(outline.Length, Is.EqualTo(24), "an outline is four bars");
            AssertBounds(outline, 600, 630, -444, -400, "outline");
            // Circle and ring: Width is the radius, and a ring's stroke straddles it.
            AssertBounds(MeshVertices(Shape(CutscenePrimitive.Circle, 600, 400, 30, 0)), 570, 630, -430, -370, "circle");
            AssertBounds(MeshVertices(Shape(CutscenePrimitive.Ring, 600, 400, 30, 0, 2)), 569, 631, -431, -369, "ring");
            // A line runs from (X, Y) to (Width, Height).
            AssertBounds(MeshVertices(Shape(CutscenePrimitive.Line, 600, 400, 700, 400, 4)), 600, 700, -402, -398, "line");

            var label = new CutsceneCommand
            {
                Primitive = CutscenePrimitive.Label, Ink = CutsceneInk.Bone, X = 600, Y = 400, Text = "MMMM", FontSize = 24,
                Alpha = 1, Scale = 1, Align = CutsceneTextAlign.Left
            };
            var whole = MeshVertices(label);
            Assert.That(whole.Length, Is.EqualTo(24), "one quad per glyph");
            Assert.That(whole.Min(v => v.position.x), Is.InRange(598, 610), "left-aligned type starts at X");
            Assert.That(whole.Max(v => v.position.x), Is.GreaterThan(650));
            label.HasClip = true; label.ClipX = 600; label.ClipY = 360; label.ClipWidth = 30; label.ClipHeight = 60;
            var clipped = MeshVertices(label);
            Assert.That(clipped.Length, Is.GreaterThan(0));
            Assert.That(clipped.Max(v => v.position.x), Is.EqualTo(630).Within(.001), "the reveal clip cuts through the row");
            foreach (var vertex in clipped)
            {
                Assert.That(vertex.position.x, Is.InRange(600, 630.001f));
                Assert.That(vertex.position.y, Is.InRange(-420.001f, -360));
            }

            // A thought bubble is centered on X, with its middle 8 px above the text baseline at Y.
            var bubble = MeshVertices(new CutsceneCommand
            {
                Primitive = CutscenePrimitive.ThoughtBubble, Ink = CutsceneInk.Bone, X = 390, Y = 450, Height = 44, Stroke = 2,
                Text = "MMMM", FontSize = 24, Alpha = 1, Scale = 1
            });
            Assert.That(bubble.Length, Is.EqualTo(24));
            Assert.That((bubble.Min(v => v.position.x) + bubble.Max(v => v.position.x)) / 2, Is.EqualTo(390).Within(.01));
            Assert.That(bubble.Max(v => v.position.x) - bubble.Min(v => v.position.x), Is.GreaterThan(32 + 60));
            Assert.That(bubble.Max(v => v.position.y), Is.EqualTo(-420).Within(.001));
            Assert.That(bubble.Min(v => v.position.y), Is.EqualTo(-464).Within(.001));
        }

        [Test]
        public void OversizedGeometryIsClippedToThePortraitFrame()
        {
            // As a close-up projects them: a limb leaving through the right and bottom edges...
            var limb = MeshVertices(new CutsceneCommand
            {
                Primitive = CutscenePrimitive.RotatedRectangle, Ink = CutsceneInk.Bone,
                X = 610, Y = 1100, Width = 400, Height = 600, Rotation = 30, Alpha = 1, Scale = 1
            });
            Assert.That(limb.Length, Is.GreaterThan(0));
            Assert.That(limb.Any(v => Mathf.Abs(v.position.x - Width) < .001), Is.True);
            Assert.That(limb.Any(v => Mathf.Abs(v.position.y + Height) < .001), Is.True);
            foreach (var vertex in limb)
            {
                Assert.That(vertex.position.x, Is.InRange(0, Width + .001f));
                Assert.That(vertex.position.y, Is.InRange(-Height - .001f, 0));
            }
            // ...and a phone screen pushed past all four edges, which must come out as exactly the frame.
            AssertBounds(MeshVertices(new CutsceneCommand
            {
                Primitive = CutscenePrimitive.Rectangle, Ink = CutsceneInk.Red, X = -88, Y = -17, Width = 956, Height = 1722, Alpha = 1, Scale = 1
            }), 0, 780, -1688, 0, "frame-filling screen");
        }

        [Test]
        public void ThoughtBubbleWidthUsesGlyphAdvancesInsteadOfCharacterCount()
        {
            var command = new CutsceneCommand
            {
                Primitive = CutscenePrimitive.ThoughtBubble, Ink = CutsceneInk.Bone,
                X = 390, Y = 450, Height = 44, Stroke = 2, Text = "iiii",
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

        // A render target, then the bars the centered 780 : 1688 frame must leave beside it (Side) and
        // above and below it (Cap), and how many whole pixels those bars hold.
        private static readonly (int Wide, int High, float Side, float Cap, int BarPixels)[] TargetSizes =
        {
            (780, 1688, 0f, 0f, 0), (388, 840, 0f, .164f, 0), (585, 1266, 0f, 0f, 0), (600, 1300, 0f, .769f, 0),
            (780, 1920, 0f, 116f, 180960), (540, 1400, 0f, 115.692f, 124200),
            (900, 1688, 60f, 0f, 202560), (720, 1200, 82.749f, 0f, 196800)
        };

        // Whole pixels outside the frame, and how many of them are anything but bare ink.
        private static (int Bar, int Painted) Letterbox(Color32[] pixels, int wide, int high, float side, float cap)
        {
            int columns = Mathf.FloorToInt(side), rows = Mathf.FloorToInt(cap), bar = 0, painted = 0;
            for (var y = 0; y < high; y++)
                for (var x = 0; x < wide; x++)
                {
                    if (x >= columns && x < wide - columns && y >= rows && y < high - rows) continue;
                    bar++;
                    if (PaletteAssert.Rgb(pixels[y * wide + x]) != InkRgb) painted++;
                }
            return (bar, painted);
        }

        // Pixels lying wholly inside the frame that are not the given color.
        private static int OffColorInsideFrame(Color32[] pixels, int wide, int high, float side, float cap, int rgb)
        {
            int columns = Mathf.CeilToInt(side), rows = Mathf.CeilToInt(cap), off = 0;
            for (var y = rows; y < high - rows; y++)
                for (var x = columns; x < wide - columns; x++)
                    if (PaletteAssert.Rgb(pixels[y * wide + x]) != rgb) off++;
            return off;
        }

        [UnityTest]
        public IEnumerator EntirePortraitFrameFitsUniformlyAtEveryTargetSize()
        {
            Time.timeScale = 1f;
            yield return SceneManager.LoadSceneAsync("CutsceneA", LoadSceneMode.Single);
            var cutscene = Object.FindAnyObjectByType<CutsceneController>();
            Assert.That(cutscene, Is.Not.Null);
            cutscene.AutomaticTick = false;
            var camera = Camera.main;
            var frame = cutscene.Frame;
            var canvas = frame.GetComponentInParent<Canvas>();
            Assert.That(camera, Is.Not.Null);
            Assert.That(canvas.renderMode, Is.EqualTo(RenderMode.ScreenSpaceCamera));
            Assert.That(canvas.worldCamera, Is.EqualTo(camera));

            // Negative controls for the two pixel counters: a 20 x 30 image with 3.5 px side bars.
            var control = Enumerable.Repeat(Swatch(InkRgb), 20 * 30).ToArray();
            for (var y = 0; y < 30; y++)
                for (var x = 3; x < 17; x++) control[y * 20 + x] = Swatch(RedRgb);
            Assert.That(Letterbox(control, 20, 30, 3.5f, 0), Is.EqualTo((180, 0)));
            Assert.That(OffColorInsideFrame(control, 20, 30, 3.5f, 0, RedRgb), Is.Zero);
            control[5 * 20 + 2] = Swatch(RedRgb);
            Assert.That(Letterbox(control, 20, 30, 3.5f, 0), Is.EqualTo((180, 1)), "A leak into the bar must be counted.");
            control[5 * 20 + 10] = Swatch(InkRgb);
            Assert.That(OffColorInsideFrame(control, 20, 30, 3.5f, 0, RedRgb), Is.EqualTo(1), "A hole in the frame must be counted.");

            var originalScreen = new Vector2Int(Screen.width, Screen.height);
            var corners = new Vector3[4];
            foreach (var size in TargetSizes)
                // The desk shot, then the last frame, where the phone's red screen overfills the frame.
                foreach (var time in new[] { 10.6, 16.0 })
                {
                    cutscene.Seek(time);
                    yield return CaptureStrict(size.Wide, size.High, pixels =>
                    {
                        Assert.That(new Vector2Int(Screen.width, Screen.height), Is.EqualTo(originalScreen));
                        frame.GetWorldCorners(corners);
                        var viewport = corners.Select(camera.WorldToViewportPoint).ToArray();
                        string context = $"Cutscene target={size.Wide}x{size.High} t={time}, bottomLeft={viewport[0]}, topRight={viewport[2]}";
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
                        Assert.That(viewport[0].x * size.Wide, Is.EqualTo(size.Side).Within(.02f), context);
                        Assert.That((1 - viewport[2].x) * size.Wide, Is.EqualTo(size.Side).Within(.02f), context);
                        Assert.That(viewport[0].y * size.High, Is.EqualTo(size.Cap).Within(.02f), context);
                        Assert.That((1 - viewport[2].y) * size.High, Is.EqualTo(size.Cap).Within(.02f), context);

                        // The rendered bars themselves, over their full width and height.
                        var bars = Letterbox(pixels, size.Wide, size.High, size.Side, size.Cap);
                        Assert.That(bars.Bar, Is.EqualTo(size.BarPixels), context);
                        Assert.That(bars.Painted, Is.Zero, "No cutscene pixel may reach the letterbox. " + context);
                        if (time == 16.0)
                            Assert.That(OffColorInsideFrame(pixels, size.Wide, size.High, size.Side, size.Cap, RedRgb), Is.Zero,
                                "The red screen covers the frame edge to edge. " + context);
                        else
                            Assert.That(pixels.Count(pixel => PaletteAssert.Rgb(pixel) != InkRgb), Is.GreaterThan(pixels.Length / 10),
                                "The desk shot is on screen. " + context);
                    });
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
                yield return V3JourneyTests.FinishTransition(OfftrackScenes.Main);
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
        public IEnumerator OpeningMontageAndClosingRunForExactlySixteenSixAndThirteenSeconds()
        {
            Time.timeScale = 1f;
            var scenes = new (string Scene, double Seconds, string Next)[]
            {
                (OfftrackScenes.CutsceneA, 16, OfftrackScenes.Main),
                (OfftrackScenes.CutsceneC, 6, OfftrackScenes.CutsceneB),
                (OfftrackScenes.CutsceneB, 13, OfftrackScenes.Title)
            };
            foreach (var scene in scenes)
            {
                yield return SceneManager.LoadSceneAsync(scene.Scene, LoadSceneMode.Single);
                var cutscene = Object.FindAnyObjectByType<CutsceneController>();
                Assert.That(cutscene, Is.Not.Null);
                cutscene.AutomaticTick = false;
                Assert.That(cutscene.Duration, Is.EqualTo(scene.Seconds), scene.Scene);
                cutscene.Seek(0);
                // One millisecond short of the authored length the scene is still playing...
                cutscene.Advance(scene.Seconds - .001);
                yield return null;
                Assert.That(cutscene.Time, Is.EqualTo(scene.Seconds - .001).Within(1e-9), scene.Scene);
                Assert.That(cutscene.IsEnd, Is.False, scene.Scene);
                Assert.That(SceneTransition.BlocksInput, Is.False, scene.Scene + " must not leave early.");
                Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(scene.Scene));
                // ...and the clock crossing it is what ends the scene.
                cutscene.Advance(.002);
                Assert.That(cutscene.Time, Is.EqualTo(scene.Seconds), scene.Scene);
                Assert.That(SceneTransition.BlocksInput, Is.True, scene.Scene + " must leave when its time is up.");
                yield return V3JourneyTests.FinishTransition(scene.Next);
            }
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator EveryStagedShotRendersOnlyTheStrictPalette()
        {
            yield return SceneManager.LoadSceneAsync(OfftrackScenes.Title, LoadSceneMode.Single);
            yield return CaptureStrict(pixels => Assert.That(PaletteAssert.AssertExactPalette(pixels,
                "Title", allowDanger: false).Count, Is.GreaterThan(1)));
            // Every shot, every camera move at its start, middle and end, every fade step, and the
            // last hundredth of a second of each scene.
            var shots = new (string Scene, double[] Times)[]
            {
                (OfftrackScenes.CutsceneA, new[]
                {
                    .2, .4, .8, 1.5, 2.8, 4.5, 6.0, 6.02, 6.05, 6.1, 6.35, 8.0, 8.8, 9.8, 10.8, 11.5, 12.2, 12.99,
                    13.0, 13.35, 13.7, 13.8, 14.5, 15.99
                }),
                (OfftrackScenes.CutsceneC, new[] { 0, 1.0, 1.97, 2.0, 3.04, 4.0, 4.45, 5.0, 5.5, 5.8, 5.99 }),
                (OfftrackScenes.CutsceneB, new[]
                {
                    .05, .2, 1.2, 1.4, 2.4, 4.8, 4.95, 5.2, 6.7, 7.65, 8.0, 8.8, 9.85, 10.0, 10.15, 10.2, 10.3, 10.5, 10.7, 10.8,
                    10.9, 12.0, 12.5, 12.99
                })
            };
            foreach (var shot in shots)
            {
                yield return SceneManager.LoadSceneAsync(shot.Scene, LoadSceneMode.Single);
                var cutscene = Object.FindAnyObjectByType<CutsceneController>();
                cutscene.AutomaticTick = false;
                foreach (var time in shot.Times)
                {
                    cutscene.Seek(time);
                    Assert.That(cutscene.Time, Is.EqualTo(time), $"{shot.Scene} is still running at {time}s.");
                    yield return CaptureStrict(pixels =>
                    {
                        var colors = PaletteAssert.AssertExactPalette(pixels, $"{shot.Scene} t={time:0.00}", allowDanger: false);
                        if (shot.Scene == OfftrackScenes.CutsceneA) return;
                        // The room is on screen in every frame of the montage and the closing.
                        Assert.That(colors.Count, Is.GreaterThan(1), $"{shot.Scene} t={time:0.00}");
                        Assert.That(colors.Contains(DarkRed15Rgb), Is.True, $"{shot.Scene} t={time:0.00} shows the wall.");
                    });
                }
            }
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void StrictStoryPaletteRejectsBothDangerShadesAndTheirDimmedComposites()
        {
            foreach (var color in new[] { Palette.Danger, Palette.Danger60, Palette.Dimmed(Palette.Danger), Palette.Dimmed(Palette.Danger60) })
                Assert.Throws<AssertionException>(() => PaletteAssert.AssertExactPalette(new[] { Palette.Ink, color }, "negative control", allowDanger: false));
            // A live blend between two swatches is no swatch at all.
            Assert.Throws<AssertionException>(() => PaletteAssert.AssertExactPalette(
                new[] { Palette.Ink, new Color32(0x7E, 0x77, 0x6F, 255) }, "negative control", allowDanger: false));
        }

        [UnityTest]
        public IEnumerator SeekingBackToTheSameTimeRecreatesTheSameRenderedPixels()
        {
            var shots = new (string Scene, double[] Times)[]
            {
                (OfftrackScenes.CutsceneA, new[] { 4.8, 9.8, 13.35 }),
                (OfftrackScenes.CutsceneC, new[] { 1.97, 4.45 }),
                (OfftrackScenes.CutsceneB, new[] { 4.8, 9.85, 10.5 })
            };
            foreach (var shot in shots)
            {
                yield return SceneManager.LoadSceneAsync(shot.Scene, LoadSceneMode.Single);
                var cutscene = Object.FindAnyObjectByType<CutsceneController>();
                cutscene.AutomaticTick = false;
                foreach (var time in shot.Times)
                {
                    Color32[] first = null;
                    cutscene.Seek(time);
                    yield return CaptureStrict(pixels => first = pixels);
                    cutscene.Seek(cutscene.Duration - .2);
                    yield return null;
                    cutscene.Seek(.2);
                    yield return null;
                    cutscene.Seek(time);
                    yield return CaptureStrict(pixels => CollectionAssert.AreEqual(first, pixels,
                        $"{shot.Scene} t={time}: intervening shots must not change text, pose, camera, or pixels."));
                }
            }
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator GradeIsLargeCenteredAndPinnedToItsPageWhileTheCameraMoves()
        {
            yield return SceneManager.LoadSceneAsync("CutsceneB", LoadSceneMode.Single);
            var cutscene = Object.FindAnyObjectByType<CutsceneController>();
            cutscene.AutomaticTick = false;
            var offsetsX = new List<double>();
            var offsetsY = new List<double>();
            var sizes = new List<double>();
            // Mid push-in, the held close-up twice, then the middle and the tail of the pull-back.
            foreach (var time in new[] { 7.8, 8.0, 9.0, 9.85, 10.0 })
            {
                cutscene.Seek(time);
                yield return CaptureStrict(pixels =>
                {
                    PaletteAssert.AssertExactPalette(pixels, $"Grade page t={time:0.00}", allowDanger: false);
                    // Until the phone lights, the sheet is the only bone shape and "58" the only red one.
                    var page = ColorBounds(pixels, BoneRgb);
                    var grade = ColorBounds(pixels, RedRgb);
                    string context = $"t={time}: page={page}, grade={grade}";
                    TestContext.WriteLine(context);
                    Assert.That(page.xMin, Is.GreaterThan(0), context);
                    Assert.That(page.xMax, Is.LessThan(Width), context);
                    Assert.That(page.yMin, Is.GreaterThan(0), context);
                    Assert.That(page.yMax, Is.LessThan(Height), context);
                    Assert.That((double)page.width / page.height, Is.EqualTo(148d / 189).Within(.01), "A 148 : 189 sheet. " + context);
                    Assert.That(grade.xMin, Is.GreaterThan(page.xMin), context);
                    Assert.That(grade.xMax, Is.LessThan(page.xMax), context);
                    offsetsX.Add((grade.center.x - page.center.x) / page.width);
                    offsetsY.Add((page.center.y - grade.center.y) / page.height);
                    sizes.Add((double)grade.height / page.height);
                    if (time != 8.0 && time != 9.0) return;
                    // The close-up: the sheet at 4.34x sits dead center and reads as the whole shot.
                    Assert.That(page.width, Is.InRange(640, 644), context);
                    Assert.That(page.height, Is.InRange(818, 822), context);
                    Assert.That(page.center.x, Is.EqualTo(390).Within(2), context);
                    Assert.That(page.center.y, Is.EqualTo(844).Within(2), context);
                    Assert.That((double)pixels.Count(p => PaletteAssert.Rgb(p) == BoneRgb) / pixels.Length, Is.GreaterThan(.35),
                        "The report must read as a close-up, rather than a small overlay on the room.");
                    Assert.That(grade.height, Is.GreaterThanOrEqualTo(120), "58 must be legible at close-up scale.");
                });
            }
            // "58" keeps its place and its size on the sheet at every zoom: type and shapes share one camera.
            Assert.That(offsetsX, Is.All.EqualTo(0).Within(.03), "58 is centered on the sheet.");
            Assert.That(offsetsY, Is.All.EqualTo(.092).Within(.03), "58 sits just below the middle of the sheet.");
            Assert.That(sizes, Is.All.EqualTo(.204).Within(.03), "58 is a fifth of the sheet's height.");
            Assert.That(offsetsX.Max() - offsetsX.Min(), Is.LessThan(.012));
            Assert.That(offsetsY.Max() - offsetsY.Min(), Is.LessThan(.012));
            Assert.That(sizes.Max() - sizes.Min(), Is.LessThan(.012));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator OpeningPhoneLightsInPlaceThenFillsThePortraitFrameBeforeFade()
        {
            yield return SceneManager.LoadSceneAsync("CutsceneA", LoadSceneMode.Single);
            var cutscene = Object.FindAnyObjectByType<CutsceneController>();
            cutscene.AutomaticTick = false;
            cutscene.Seek(12.99);
            yield return CaptureStrict(pixels => Assert.That(pixels.Count(p => PaletteAssert.Rgb(p) == RedRgb), Is.Zero,
                "Nothing is red before the phone lights at 13 s."));
            cutscene.Seek(13);
            yield return CaptureStrict(pixels =>
            {
                // A 40 x 72 screen at the desk shot's 2.7x, still in the student's raised hand.
                var bounds = ColorBounds(pixels, RedRgb);
                Assert.That(bounds.width, Is.InRange(106, 110));
                Assert.That(bounds.height, Is.InRange(192, 197));
                Assert.That(bounds.center.x, Is.GreaterThan(540), "The screen lights where it is held, right of center.");
            });
            cutscene.Seek(16);
            yield return CaptureStrict(pixels =>
            {
                var bounds = ColorBounds(pixels, RedRgb);
                Assert.That(bounds.width, Is.EqualTo(780));
                Assert.That(bounds.height, Is.EqualTo(1688));
                Assert.That(pixels.Count(p => PaletteAssert.Rgb(p) != RedRgb), Is.Zero, "Every pixel of the last frame is the red screen.");
            });
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator ClosingHoldsTheAdOnThePhoneUntilThirteenSecondsThenReturnsToTitle()
        {
            yield return SceneManager.LoadSceneAsync("CutsceneB", LoadSceneMode.Single);
            var cutscene = Object.FindAnyObjectByType<CutsceneController>();
            cutscene.AutomaticTick = false;
            Assert.That(cutscene.Duration, Is.EqualTo(13));
            // The push into the phone ends at 10.9 s.
            cutscene.Seek(11);
            Color32[] held = null;
            yield return CaptureStrict(pixels =>
            {
                held = pixels;
                PaletteAssert.AssertExactPalette(pixels, "Closing ad close-up", allowDanger: false);
                // The app icon is a 14 px disc on the phone, seen at 17x: 238 px across, centered
                // left to right and 153 px above the middle of the frame.
                var red = ColorBounds(pixels, RedRgb);
                Assert.That(red.width, Is.InRange(236, 240));
                Assert.That(red.height, Is.InRange(236, 240));
                Assert.That(red.center.x, Is.EqualTo(390).Within(2));
                Assert.That(red.center.y, Is.EqualTo(997).Within(2));
                Assert.That(PaletteAssert.Rgb(At(pixels, 390, 580)), Is.EqualTo(RedRgb));
                Assert.That(PaletteAssert.Rgb(At(pixels, 390, 1200)), Is.EqualTo(InkRgb), "Below the icon the phone's screen is dark.");
                Assert.That(pixels.Count(p => PaletteAssert.Rgb(p) == BoneRgb), Is.GreaterThan(1000), "The note and the app name are bone.");
            });
            // The scene's own clock runs on to the last millisecond: nothing may move, dim or fade.
            for (var step = 0; step < 4; step++) cutscene.Advance(.49975);
            Assert.That(cutscene.Time, Is.EqualTo(12.999).Within(1e-9));
            Assert.That(cutscene.IsEnd, Is.False);
            yield return CaptureStrict(pixels => CollectionAssert.AreEqual(held, pixels, "The ad holds still from 11 s to the end."));
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("CutsceneB"));
            Assert.That(SceneTransition.BlocksInput, Is.False);
            cutscene.Advance(.0011);
            Assert.That(cutscene.IsEnd, Is.True);
            yield return V3JourneyTests.FinishTransition(OfftrackScenes.Title);
            Assert.That(Object.FindAnyObjectByType<TitleController>(), Is.Not.Null);
            LogAssert.NoUnexpectedReceived();
        }

        // What is wrong with the passing shadows in a strict montage capture taken at the middle of a
        // pass: a layer that does not show against the wall, or dimmer layers that do not trail.
        private static List<string> ShadowLayerProblems(Color32[] pixels, bool leftToRight)
        {
            var swatches = new[] { Shadow30Rgb, Shadow15Rgb, Shadow08Rgb };
            var names = new[] { "Shadow30", "Shadow15", "Shadow08" };
            var minimum = new[] { 10000, 1000, 1000 };
            var count = new int[3];
            var bandCount = new int[3];
            var bandSum = new double[3];
            int wall = 0;
            for (var top = 0; top < WallBottom; top++)
                for (var x = 0; x < Width; x++)
                {
                    int rgb = PaletteAssert.Rgb(pixels[(Height - 1 - top) * Width + x]);
                    if (rgb == DarkRed15Rgb) { wall++; continue; }
                    int layer = System.Array.IndexOf(swatches, rgb);
                    if (layer < 0) continue;
                    count[layer]++;
                    if (top < HeadBandTop || top >= HeadBandBottom) continue;
                    bandCount[layer]++;
                    bandSum[layer] += x;
                }
            var problems = new List<string>();
            if (wall < WallBottom * Width / 2) problems.Add($"wall: only {wall} px of the wall area are DarkRed15");
            for (var layer = 0; layer < 3; layer++)
                if (count[layer] < minimum[layer])
                    problems.Add($"{names[layer]}: {count[layer]} px against the wall, expected at least {minimum[layer]}");
            // Beside the leading head each dimmer layer shows as a sliver four pixels wide on every row.
            int rows = HeadBandBottom - HeadBandTop;
            for (var layer = 1; layer < 3; layer++)
                if (bandCount[layer] < rows * 4 * 5 / 6 || bandCount[layer] > rows * 4 * 7 / 6)
                    problems.Add($"{names[layer]}: sliver of {bandCount[layer]} px beside the head, expected about {rows * 4}");
            if (bandCount.All(layer => layer > 0))
            {
                double direction = leftToRight ? 1 : -1;
                double lead = bandSum[0] / bandCount[0], second = bandSum[1] / bandCount[1], third = bandSum[2] / bandCount[2];
                if ((lead - second) * direction < 20) problems.Add($"Shadow15 does not trail the leading layer ({second:0.0} against {lead:0.0})");
                if (System.Math.Abs((second - third) * direction - 4) > 1)
                    problems.Add($"Shadow08 does not trail Shadow15 by four pixels ({third:0.0} against {second:0.0})");
            }
            return problems;
        }

        [UnityTest]
        public IEnumerator MontageShadowLayersAllShowAgainstTheWallAndTrailTheLeadingOne()
        {
            yield return SceneManager.LoadSceneAsync(OfftrackScenes.CutsceneC, LoadSceneMode.Single);
            var cutscene = Object.FindAnyObjectByType<CutsceneController>();
            cutscene.AutomaticTick = false;
            // The middle of the first pass, left to right, and of the second, right to left.
            foreach (var (time, leftToRight) in new[] { (1.97, true), (3.04, false) })
            {
                cutscene.Seek(time);
                yield return CaptureStrict(pixels =>
                {
                    PaletteAssert.AssertExactPalette(pixels, $"Montage pass t={time:0.00}", allowDanger: false);
                    Assert.That(ShadowLayerProblems(pixels, leftToRight), Is.Empty, $"t={time:0.00}");

                    // Negative control: the first delivery's swatches, one equal to the wall and one darker than it.
                    var vanished = (Color32[])pixels.Clone();
                    for (var index = 0; index < vanished.Length; index++)
                    {
                        int rgb = PaletteAssert.Rgb(vanished[index]);
                        if (rgb == Shadow15Rgb) vanished[index] = Swatch(DarkRed15Rgb);
                        else if (rgb == Shadow08Rgb) vanished[index] = Swatch(0x150B0B);
                    }
                    var problems = ShadowLayerProblems(vanished, leftToRight);
                    Assert.That(problems.Any(problem => problem.StartsWith("Shadow15: 0 px")), Is.True, string.Join("; ", problems));
                    Assert.That(problems.Any(problem => problem.StartsWith("Shadow08: 0 px")), Is.True, string.Join("; ", problems));
                    // Negative control: read as a pass in the other direction, the dim layers would be leading.
                    Assert.That(ShadowLayerProblems(pixels, !leftToRight).Any(problem => problem.Contains("does not trail")), Is.True);
                });
            }
            // Before the first pass and after the last, none of the three swatches is on screen (they
            // belong to the silhouettes alone) and the wall behind them is plain DarkRed15.
            foreach (var time in new[] { 0, 5.8 })
            {
                cutscene.Seek(time);
                yield return CaptureStrict(pixels =>
                {
                    foreach (var swatch in new[] { Shadow30Rgb, Shadow15Rgb, Shadow08Rgb })
                        Assert.That(pixels.Count(p => PaletteAssert.Rgb(p) == swatch), Is.Zero, $"t={time}: #{swatch:X6}");
                    foreach (var (x, top) in new[] { (20, 20), (760, 20), (20, 800), (760, 800) })
                        Assert.That(PaletteAssert.Rgb(At(pixels, x, top)), Is.EqualTo(DarkRed15Rgb), $"t={time}: wall at ({x}, {top})");
                    int wall = 0;
                    for (var top = 0; top < WallBottom; top++)
                        for (var x = 0; x < Width; x++)
                            if (PaletteAssert.Rgb(At(pixels, x, top)) == DarkRed15Rgb) wall++;
                    Assert.That(wall, Is.GreaterThan(WallBottom * Width * 6 / 10), $"t={time}: most of the wall is bare.");
                });
            }
            LogAssert.NoUnexpectedReceived();
        }

        // The tallest run of rows holding nothing but bare ink, as a share of the image height.
        private static double LongestInkBand(Color32[] pixels)
        {
            int longest = 0, run = 0;
            for (var y = 0; y < Height; y++)
            {
                bool bare = true;
                for (var x = 0; x < Width && bare; x++) bare = PaletteAssert.Rgb(pixels[y * Width + x]) == InkRgb;
                run = bare ? run + 1 : 0;
                if (run > longest) longest = run;
            }
            return (double)longest / Height;
        }

        // A copy with the rows from fromTop up to (not including) toTop painted over with ink.
        private static Color32[] Blanked(Color32[] pixels, int fromTop, int toTop)
        {
            var copy = (Color32[])pixels.Clone();
            for (var top = fromTop; top < toTop; top++)
                for (var x = 0; x < Width; x++) copy[(Height - 1 - top) * Width + x] = Swatch(InkRgb);
            return copy;
        }

        [UnityTest]
        public IEnumerator EveryShotFillsThePortraitFrameWithoutABandOfBareInk()
        {
            // Opening: the board, the walk-in, the teacher writing, then the desk with no bubble up
            // (8 s, 10 s), as the phone comes out (11.6 s) and just before it lights. Rooms: wall to floor.
            var shots = new (string Scene, double Limit, double[] Times)[]
            {
                (OfftrackScenes.CutsceneA, .30, new[] { .4, 1.5, 4.5, 8.0, 10.0, 11.6, 12.5 }),
                (OfftrackScenes.CutsceneC, .15, new[] { 0, 3.0, 5.8 }),
                (OfftrackScenes.CutsceneB, .15, new[] { .8, 6.0 })
            };
            Color32[] desk = null, room = null;
            foreach (var shot in shots)
            {
                yield return SceneManager.LoadSceneAsync(shot.Scene, LoadSceneMode.Single);
                var cutscene = Object.FindAnyObjectByType<CutsceneController>();
                cutscene.AutomaticTick = false;
                foreach (var time in shot.Times)
                {
                    cutscene.Seek(time);
                    yield return CaptureStrict(pixels =>
                    {
                        double band = LongestInkBand(pixels);
                        TestContext.WriteLine($"{shot.Scene} t={time:0.00}: longest bare-ink band {band:P1}");
                        Assert.That(band, Is.LessThan(shot.Limit), $"{shot.Scene} t={time:0.00}");
                        if (shot.Scene == OfftrackScenes.CutsceneA && time == 8.0) desk = pixels;
                        if (shot.Scene == OfftrackScenes.CutsceneB) room = pixels;
                    });
                }
            }
            // Negative controls: the desk shot with its top 600 rows empty, as when one camera height
            // served every shot, and a room whose picture stops 388 rows short of the bottom.
            Assert.That(LongestInkBand(Blanked(desk, 0, 600)), Is.GreaterThan(.30));
            Assert.That(LongestInkBand(Blanked(room, 1300, Height)), Is.GreaterThan(.15));
            LogAssert.NoUnexpectedReceived();
        }

        // What is wrong with a fading element in a strict capture. Pixels of each shade of its hue are
        // counted in the rows above `bottom`, where nothing else uses that hue: only the expected
        // step may be on screen, and it must be there. Step -1 means the element is not drawn at all.
        private static List<string> FadeStepProblems(Color32[] pixels, int[] hue, int bottom, int step, int floor)
        {
            var count = new int[hue.Length];
            for (var top = 0; top < bottom; top++)
                for (var x = 0; x < Width; x++)
                {
                    // The skip button in the top-right corner is the controller's UI, not a fading element.
                    if (top < 120 && x >= 600) continue;
                    int shade = System.Array.IndexOf(hue, PaletteAssert.Rgb(pixels[(Height - 1 - top) * Width + x]));
                    if (shade >= 0) count[shade]++;
                }
            var problems = new List<string>();
            for (var shade = 0; shade < hue.Length; shade++)
                if (shade == step)
                {
                    if (count[shade] < floor) problems.Add($"step {shade}: {count[shade]} px of #{hue[shade]:X6}, expected at least {floor}");
                }
                else if (count[shade] > 0) problems.Add($"step {shade}: {count[shade]} px of #{hue[shade]:X6}, expected none");
            return problems;
        }

        [UnityTest]
        public IEnumerator FadingElementsShowEachNamedStepOnScreenInsteadOfPopping()
        {
            // Sample times sit inside each step of the authored curves: entries ease out over .45 s
            // (caption, grade) or .4 s (bubble), exits ease in over the last .3 s. Steps 0 to 2 are
            // the 15, 40 and 60 % shades, 3 is full strength, -1 is not drawn.
            var fades = new (string Scene, string What, int[] Hue, int Bottom, int Floor, (double Time, int Step)[] Samples)[]
            {
                // The caption is the only bone shape in the room until the page lands at 5 s.
                (OfftrackScenes.CutsceneB, "caption", BoneSteps, Height, 2000, new[]
                {
                    (0d, -1), (.03, 0), (.07, 1), (.14, 2), (.3, 3), (4.8, 3), (4.87, 2), (4.935, 1), (4.972, 0), (4.995, -1)
                }),
                // "58" is printed in during the push, and dims one step just before the phone takes the red.
                (OfftrackScenes.CutsceneB, "grade", RedSteps, Height, 800, new[]
                {
                    (7.5, -1), (7.58, 0), (7.62, 1), (7.69, 2), (7.9, 3), (10.05, 3), (10.15, 2)
                }),
                // The second thought bubble. The student under it stays full bone, so only the rows
                // above the head are read: there the bubble's box and text are the one bone shape.
                (OfftrackScenes.CutsceneA, "bubble", BoneSteps, 760, 5000, new[]
                {
                    (8d, -1), (8.025, 0), (8.06, 1), (8.12, 2), (8.3, 3), (9.8, 3), (9.87, 2), (9.935, 1), (9.972, 0), (9.995, -1)
                })
            };
            foreach (var fade in fades)
            {
                yield return SceneManager.LoadSceneAsync(fade.Scene, LoadSceneMode.Single);
                var cutscene = Object.FindAnyObjectByType<CutsceneController>();
                cutscene.AutomaticTick = false;
                foreach (var (time, step) in fade.Samples)
                {
                    cutscene.Seek(time);
                    yield return CaptureStrict(pixels =>
                    {
                        string context = $"{fade.What} t={time:0.000}";
                        PaletteAssert.AssertExactPalette(pixels, context, allowDanger: false);
                        Assert.That(FadeStepProblems(pixels, fade.Hue, fade.Bottom, step, fade.Floor), Is.Empty, context);
                        // Negative control: read as a pop (full strength where a dim step or nothing
                        // is due, a dim step where full strength is due), the same capture is rejected.
                        Assert.That(FadeStepProblems(pixels, fade.Hue, fade.Bottom, step == 3 ? 2 : 3, fade.Floor), Is.Not.Empty, context);
                    });
                }
            }
            LogAssert.NoUnexpectedReceived();
        }

        private static Color32 Swatch(int rgb) => new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);

        private static Color32 At(Color32[] pixels, int x, int top) => pixels[(Height - 1 - top) * Width + x];

        // Bounds in captured rows, which run bottom-up: center.y is measured from the bottom edge.
        private static RectInt ColorBounds(Color32[] pixels, int rgb)
        {
            int minX = Width, minY = Height, maxX = -1, maxY = -1;
            for (var y = 0; y < Height; y++)
                for (var x = 0; x < Width; x++)
                    if (PaletteAssert.Rgb(pixels[y * Width + x]) == rgb)
                    {
                        minX = Mathf.Min(minX, x); minY = Mathf.Min(minY, y);
                        maxX = Mathf.Max(maxX, x); maxY = Mathf.Max(maxY, y);
                    }
            Assert.That(maxX, Is.GreaterThanOrEqualTo(0), $"Expected rendered color #{rgb:X6}.");
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

        private static void AssertBounds(UIVertex[] vertices, float left, float right, float bottom, float top, string label)
        {
            Assert.That(vertices.Length, Is.GreaterThan(0), label);
            Assert.That(vertices.Min(v => v.position.x), Is.EqualTo(left).Within(.001), label + " left");
            Assert.That(vertices.Max(v => v.position.x), Is.EqualTo(right).Within(.001), label + " right");
            Assert.That(vertices.Min(v => v.position.y), Is.EqualTo(bottom).Within(.001), label + " bottom");
            Assert.That(vertices.Max(v => v.position.y), Is.EqualTo(top).Within(.001), label + " top");
        }

        // The mesh a command produces exactly as the controller submits it: already projected, and
        // clipped to the reference frame.
        private static UIVertex[] MeshVertices(CutsceneCommand command)
        {
            var node = new GameObject("Cutscene mesh under test", typeof(RectTransform), typeof(CutsceneGraphic));
            try
            {
                var graphic = node.GetComponent<CutsceneGraphic>();
                graphic.SetCommand(command, 32, viewport: new Vector2(Width, Height));
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

        private static IEnumerator CaptureStrict(System.Action<Color32[]> inspect) => CaptureStrict(Width, Height, inspect);

        // Renders the scene's camera into a target of the given size with hard glyph edges and no
        // anti-aliasing, so every pixel is one fill color, and hands over the rows bottom-up.
        private static IEnumerator CaptureStrict(int wide, int high, System.Action<Color32[]> inspect)
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
            var target = new RenderTexture(wide, high, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            Texture2D image = null;
            try
            {
                target.Create();
                Shader.EnableKeyword(keyword);
                camera.allowMSAA = false;
                camera.allowHDR = false;
                camera.targetTexture = target;
                camera.aspect = (float)wide / high;
                Canvas.ForceUpdateCanvases();
                yield return null;
                Canvas.ForceUpdateCanvases();
                yield return null;
                Canvas.ForceUpdateCanvases();
                image = new Texture2D(wide, high, TextureFormat.RGB24, false);
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, wide, high), 0, 0, false);
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
