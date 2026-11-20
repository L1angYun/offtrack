using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Offtrack.Runtime;
using Offtrack.Runtime.Presentation;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Offtrack.Tests.PlayMode
{
    // The title page: it waits forever, only the start button, Enter and Space leave it, it is
    // silent, and it shows a wordmark, one button and half a slowly turning record.
    // Every press below is an Input System state event that TitleController.Update reads itself,
    // and time passes on the engine clocks rather than through a stepping call.
    public sealed class V3TitleTests
    {
        private const int CaptureWidth = 780;
        private const int CaptureHeight = 1688;
        // RectTransform positions survive the world-space round trip to well under this.
        private const float Exact = .01f;
        // A hard-edged render lands within two pixels of the authored geometry.
        private const float Painted = 2f;

        private Keyboard keyboard;
        private Mouse mouse;
        private Touchscreen touchscreen;
        private InputSettings.BackgroundBehavior previousBackground;
#if UNITY_EDITOR
        private InputSettings.EditorInputBehaviorInPlayMode previousEditorRouting;
#endif
        private float previousCaptureDeltaTime;
        private TitleController title;
        private RectTransform frame;
        private RectTransform startButton;
        private RectTransform wordmark;
        private RectTransform record;

        [UnitySetUp]
        public IEnumerator PrepareTitle()
        {
            previousBackground = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
            previousEditorRouting = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            previousCaptureDeltaTime = Time.captureDeltaTime;
            Time.timeScale = 1;
            yield return SettleTransition();
            SceneTransition.AutomaticTick = true;
            keyboard = InputSystem.AddDevice<Keyboard>();
            mouse = InputSystem.AddDevice<Mouse>();
            yield return LoadTitle();
        }

        [UnityTearDown]
        public IEnumerator RestoreInputAndClock()
        {
            Time.captureDeltaTime = previousCaptureDeltaTime;
            Time.timeScale = 1;
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
            if (touchscreen != null && touchscreen.added) InputSystem.RemoveDevice(touchscreen);
            InputSystem.settings.backgroundBehavior = previousBackground;
#if UNITY_EDITOR
            InputSystem.settings.editorInputBehaviorInPlayMode = previousEditorRouting;
#endif
            yield return SettleTransition();
            SceneTransition.AutomaticTick = true;
            // Leave the one scene that never moves on by itself for whichever test runs next.
            if (SceneManager.GetActiveScene().name != OfftrackScenes.Title)
                yield return SceneManager.LoadSceneAsync(OfftrackScenes.Title, LoadSceneMode.Single);
        }

        [UnityTest]
        public IEnumerator EnterAndSpaceStartTheOpeningThroughTheInputSystemAndOtherKeysDoNot()
        {
            var starters = new[] { Key.Enter, Key.Space };
            for (int round = 0; round < starters.Length; round++)
            {
                if (round > 0) yield return LoadTitle();
                foreach (var ignored in new[] { Key.A, Key.D, Key.R, Key.LeftArrow, Key.RightArrow, Key.Escape, Key.Tab, Key.Backspace })
                {
                    yield return Hold(ignored);
                    Assert.That(keyboard[ignored].isPressed, Is.True, $"{ignored} must reach the keyboard device.");
                    Assert.That(Keyboard.current, Is.SameAs(keyboard), "The title must be reading the keyboard these events go to.");
                    AssertStillOnTitle($"{ignored} held");
                    yield return Hold();
                    AssertStillOnTitle($"{ignored} released");
                }

                yield return Hold(starters[round]);
                Assert.That(keyboard[starters[round]].isPressed, Is.True, $"{starters[round]} must reach the keyboard device.");
                Assert.That(SceneTransition.BlocksInput, Is.True,
                    $"{starters[round]} must start the transition in the frame Update reads it.");
                // The check the ignored keys passed does report this press.
                Assert.That(TitleFault(), Is.EqualTo("a scene transition started"), starters[round].ToString());
                Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(OfftrackScenes.Title));
                yield return Hold();
                yield return V3JourneyTests.FinishTransition(OfftrackScenes.CutsceneA);
                Assert.That(Object.FindAnyObjectByType<CutsceneController>(), Is.Not.Null, starters[round].ToString());
            }
        }

        [UnityTest]
        public IEnumerator TitleNeverLeavesDuringThirtyFiveSecondsWithoutInput()
        {
            // Longer than the 30 s attract loop this page must never grow back.
            const double horizon = 35;
            // A short timeout could also be counted on the wall clock, which no capture step moves:
            // stay long enough in real seconds to outlast a 5 s one as well.
            const double realHorizon = 6;
            int loads = 0;
            int switches = 0;
            UnityAction<Scene, LoadSceneMode> countLoad = (scene, mode) => loads++;
            UnityAction<Scene, Scene> countSwitch = (from, to) => switches++;
            SceneManager.sceneLoaded += countLoad;
            SceneManager.activeSceneChanged += countSwitch;
            Assert.That(SceneTransition.AutomaticTick, Is.True, "The transition layer must run by itself during the wait.");
            double scaledStart = Time.timeAsDouble;
            double unscaledStart = Time.unscaledTimeAsDouble;
            double realStart = Time.realtimeSinceStartupAsDouble;
            double deadline = realStart + 120;
            int frames = 0;
            // Thirty simulated frames a second. The loop measures the clocks instead of counting frames,
            // so it covers the full wait on each of them however the capture step drives them.
            Time.captureDeltaTime = 1f / 30;
            try
            {
                while (Time.timeAsDouble - scaledStart < horizon || Time.unscaledTimeAsDouble - unscaledStart < horizon
                    || Time.realtimeSinceStartupAsDouble - realStart < realHorizon)
                {
                    if (Time.realtimeSinceStartupAsDouble > deadline)
                        Assert.Fail($"The engine clocks stalled: {Time.timeAsDouble - scaledStart:0.00} s scaled and " +
                            $"{Time.unscaledTimeAsDouble - unscaledStart:0.00} s unscaled after {frames} frames.");
                    yield return null;
                    frames++;
                    string fault = TitleFault();
                    if (fault != null)
                        Assert.Fail($"Without any input {fault} after {frames} frames: {Time.timeAsDouble - scaledStart:0.00} s scaled, " +
                            $"{Time.unscaledTimeAsDouble - unscaledStart:0.00} s unscaled, " +
                            $"{Time.realtimeSinceStartupAsDouble - realStart:0.00} s real.");
                }
            }
            finally
            {
                Time.captureDeltaTime = previousCaptureDeltaTime;
                SceneManager.sceneLoaded -= countLoad;
                SceneManager.activeSceneChanged -= countSwitch;
            }

            TestContext.WriteLine($"Title waited {frames} frames: {Time.timeAsDouble - scaledStart:0.00} s scaled, " +
                $"{Time.unscaledTimeAsDouble - unscaledStart:0.00} s unscaled, {Time.realtimeSinceStartupAsDouble - realStart:0.00} s real.");
            Assert.That(Time.timeAsDouble - scaledStart, Is.GreaterThanOrEqualTo(35), "Scaled time must really have passed.");
            Assert.That(Time.unscaledTimeAsDouble - unscaledStart, Is.GreaterThanOrEqualTo(35), "Unscaled time must really have passed.");
            Assert.That(Time.realtimeSinceStartupAsDouble - realStart, Is.GreaterThanOrEqualTo(6), "Real time must have passed as well.");
            Assert.That(loads, Is.Zero, "No scene may load while the title waits.");
            Assert.That(switches, Is.Zero, "The active scene may not change while the title waits.");
            Assert.That(SceneTransition.AutomaticTick, Is.True);
            Assert.That(SceneTransition.Opacity, Is.Zero);
            AssertStillOnTitle("after the wait");
            Assert.That(Object.FindObjectsByType<TitleController>(FindObjectsInactive.Include, FindObjectsSortMode.None),
                Is.EqualTo(new[] { title }), "The same title page is still the only one.");
            AssertSilent("after the wait");

            // The same check does report a departure: after the long wait a real key still leaves at once.
            yield return Hold(Key.Enter);
            Assert.That(TitleFault(), Is.EqualTo("a scene transition started"));
            yield return Hold();
            yield return V3JourneyTests.FinishTransition(OfftrackScenes.CutsceneA);
        }

        [UnityTest]
        public IEnumerator OnlyTheStartButtonAcceptsMouseAndTouchPresses()
        {
            foreach (var probe in OutsideProbes())
            {
                yield return MouseDown(probe.Point);
                AssertStillOnTitle($"mouse pressed on {probe.Name}");
                yield return MouseUp(probe.Point);
                AssertStillOnTitle($"mouse released on {probe.Name}");
            }
            Vector2 centre = ScreenPoint(InFrame(startButton).center);
            yield return MouseDown(centre);
            Assert.That(SceneTransition.BlocksInput, Is.True, "A mouse press on the start button must start the transition.");
            // The check every other place passed does report this press.
            Assert.That(TitleFault(), Is.EqualTo("a scene transition started"));
            yield return MouseUp(centre);
            yield return V3JourneyTests.FinishTransition(OfftrackScenes.CutsceneA);

            yield return LoadTitle();
            touchscreen = InputSystem.AddDevice<Touchscreen>();
            yield return null;
            int finger = 0;
            foreach (var probe in OutsideProbes())
            {
                finger++;
                yield return TouchDown(finger, probe.Point);
                AssertStillOnTitle($"touch began on {probe.Name}");
                yield return TouchUp(finger, probe.Point);
                AssertStillOnTitle($"touch ended on {probe.Name}");
            }
            // A corner of the button, so the whole rectangle is the target and not only its middle.
            Rect button = InFrame(startButton);
            Vector2 corner = ScreenPoint(new Vector2(button.xMax - 8, button.yMax - 8));
            finger++;
            yield return TouchDown(finger, corner);
            Assert.That(SceneTransition.BlocksInput, Is.True, "A touch on the start button must start the transition.");
            Assert.That(TitleFault(), Is.EqualTo("a scene transition started"));
            yield return TouchUp(finger, corner);
            yield return V3JourneyTests.FinishTransition(OfftrackScenes.CutsceneA);
        }

        [UnityTest]
        public IEnumerator WordmarkButtonAndHalfRecordHoldTheTitleLayout()
        {
            // Reference pixels with the origin at the frame centre: x -390..390, y -844..844.
            Assert.That(RectFault(frame.rect, -390, -844, 390, 844, Exact), Is.Null, "ReferenceFrame");

            Rect word = InFrame(wordmark);
            Assert.That(RectFault(word, -340, 723, -60, 763, Exact), Is.Null,
                "The wordmark box sits 50 px from the left edge and 81 px from the top.");
            var wordText = wordmark.GetComponent<Text>();
            Assert.That(wordText.text, Is.EqualTo("OFFTRACK"));
            Assert.That(wordText.alignment, Is.EqualTo(TextAnchor.MiddleLeft));
            Assert.That(wordText.fontSize, Is.EqualTo(24));
            Assert.That((Color32)wordText.color, Is.EqualTo(Palette.Bone));
            var tracking = wordmark.GetComponent<LetterSpacing>();
            Assert.That(tracking, Is.Not.Null);
            Assert.That(tracking.isActiveAndEnabled, Is.True);
            Assert.That(tracking.Spacing, Is.EqualTo(5f));

            Rect button = InFrame(startButton);
            Assert.That(RectFault(button, -311, -188, -87, -122, Exact), Is.Null,
                "The start button sits left of centre and 155 px below the middle line.");
            Assert.That(RectFault(button, -311, 122, -87, 188, Exact), Is.Not.Null,
                "The comparison must reject a button mirrored above the middle line.");
            Assert.That(startButton.GetComponentInChildren<Text>().text, Is.EqualTo("开始游戏"));

            Vector2 disc = frame.InverseTransformPoint(record.position);
            Assert.That(disc.x, Is.EqualTo(390f).Within(Exact), "The record centre lies on the frame's right edge.");
            Assert.That(disc.y, Is.EqualTo(0f).Within(Exact));
            Assert.That(record.rect.size, Is.EqualTo(new Vector2(828, 828)));
            Assert.That(record.localScale, Is.EqualTo(Vector3.one));

            // The bounds reader itself: one known patch, and a colour that is not there.
            var patch = new Color32[CaptureWidth * CaptureHeight];
            for (int y = 200; y < 205; y++)
                for (int x = 100; x < 110; x++)
                    patch[y * CaptureWidth + x] = Palette.Red;
            Assert.That(RectFault(PaintedBounds(patch, Palette.Red), -290, -644, -280, -639, 0), Is.Null);
            Assert.Throws<AssertionException>(() => PaintedBounds(patch, Palette.Bone));

            // What the journey window shows: the frame fills it, so the frame's right edge is the screen's.
            Rect paintedButton = default;
            Rect label = default;
            Rect grooves = default;
            Rect letters = default;
            yield return CaptureJourneyFrame(pixels =>
            {
                // The red mark on the record label turns with it; keep to the columns left of the label.
                paintedButton = PaintedBounds(pixels, Palette.Red, 0, 600);
                label = PaintedBounds(pixels, Palette.DarkRed);
                grooves = PaintedBounds(pixels, Palette.DarkRed40);
                letters = PaintedBounds(pixels, Palette.Bone);
            });
            Assert.That(RectFault(paintedButton, -311, -188, -87, -122, Painted), Is.Null, "Painted start button");
            Assert.That(RectFault(label, 286, -104, 390, 104, Painted), Is.Null,
                "Only the left half of the 208 px label shows, cut by the right screen edge.");
            Assert.That(RectFault(grooves, -24, -414, 390, 414, Painted), Is.Null,
                "Only the left half of the 828 px record shows, cut by the right screen edge.");
            Assert.That(letters.xMin, Is.InRange(-342f, -330f), "The wordmark starts at the left of its box.");
            Assert.That(letters.xMax, Is.LessThanOrEqualTo(-58f), "The wordmark stays in the top-left corner.");
            Assert.That(letters.yMin, Is.GreaterThanOrEqualTo(721f));
            Assert.That(letters.yMax, Is.LessThanOrEqualTo(765f));

            // Tracking is what the eight letters gain over their natural width: seven gaps of 5 px.
            var textGenerator = wordText.cachedTextGenerator;
            Assert.That(textGenerator.Populate(wordText.text, wordText.GetGenerationSettings(wordmark.rect.size)), Is.True);
            using var mesh = new VertexHelper();
            var glyphs = textGenerator.verts;
            for (int index = 0; index < 8; index++)
            {
                var quad = new UIVertex[4];
                for (int corner = 0; corner < 4; corner++) quad[corner] = glyphs[4 * index + corner];
                mesh.AddUIVertexQuad(quad);
            }
            tracking.ModifyMesh(mesh);
            var vertex = new UIVertex();
            mesh.PopulateUIVertex(ref vertex, 0);
            Assert.That(vertex.position.x, Is.EqualTo(glyphs[0].position.x).Within(.01));
            mesh.PopulateUIVertex(ref vertex, 28);
            Assert.That(vertex.position.x - glyphs[28].position.x, Is.EqualTo(35f).Within(1f));
        }

        [UnityTest]
        public IEnumerator RecordTurnsClockwiseAtNineDegreesPerSecond()
        {
            var mark = Child(record, "LabelMark");
            Vector2 Arm() => RectTransformUtility.WorldToScreenPoint(Camera.main, mark.position)
                - RectTransformUtility.WorldToScreenPoint(Camera.main, record.position);

            Vector2 startArm = Arm();
            float previous = record.localEulerAngles.z;
            double elapsed = 0;
            double turned = 0;
            double deadline = Time.realtimeSinceStartupAsDouble + 30;
            Time.captureDeltaTime = 1f / 30;
            try
            {
                while (elapsed < 2)
                {
                    if (Time.realtimeSinceStartupAsDouble > deadline)
                        Assert.Fail($"Unscaled time stalled at {elapsed:0.000} s.");
                    yield return null;
                    float angle = record.localEulerAngles.z;
                    // Each frame turns far less than half a circle, so the short way round is the way it went.
                    turned += Mathf.DeltaAngle(previous, angle);
                    elapsed += Time.unscaledDeltaTime;
                    previous = angle;
                }
            }
            finally
            {
                Time.captureDeltaTime = previousCaptureDeltaTime;
            }

            Assert.That(elapsed, Is.LessThan(15), "The sample must stay well inside half a turn.");
            Assert.That(turned, Is.EqualTo(-9 * elapsed).Within(.05),
                $"The record's z angle must fall by 9 degrees for every second ({elapsed:0.000} s sampled).");
            // On screen, with y up, a negative signed angle is a clockwise sweep: twelve o'clock to three o'clock.
            Assert.That(Vector2.SignedAngle(Vector2.up, Vector2.right), Is.EqualTo(-90f).Within(.001f));
            Assert.That(Vector2.SignedAngle(Vector2.up, Vector2.left), Is.EqualTo(90f).Within(.001f), "The other way round reads positive.");
            Assert.That(Vector2.SignedAngle(startArm, Arm()), Is.EqualTo(-9 * elapsed).Within(.5),
                "The label mark must sweep clockwise around the record centre on screen.");
            Vector2 disc = frame.InverseTransformPoint(record.position);
            Assert.That(disc.x, Is.EqualTo(390f).Within(Exact), "Turning must not move the record off the right edge.");
            Assert.That(disc.y, Is.EqualTo(0f).Within(Exact));
        }

        [UnityTest]
        public IEnumerator TitleIsSilentOnFirstLoadAndAfterTheEnding()
        {
            AssertSilent("first load");

            // The check itself: one looping source in the title scene must be reported both ways.
            var probe = new GameObject("AudibleProbe").AddComponent<AudioSource>();
            var tone = AudioClip.Create("AudibleProbe", 4410, 1, 44100, false);
            probe.clip = tone;
            probe.loop = true;
            probe.Play();
            Assert.That(SoundingSources(), Is.EqualTo(new[] { "AudibleProbe" }));
            Assert.That(TitleSources(), Is.EqualTo(new[] { "AudibleProbe" }));
            Object.DestroyImmediate(probe.gameObject);
            Object.DestroyImmediate(tone);
            AssertSilent("probe removed");

            // Come back the way a player does: the ending hands over through the real transition.
            yield return SceneManager.LoadSceneAsync(OfftrackScenes.CutsceneB, LoadSceneMode.Single);
            var closing = Object.FindAnyObjectByType<CutsceneController>();
            Assert.That(closing, Is.Not.Null);
            closing.AutomaticTick = false;
            closing.Seek(closing.Duration - .001);
            closing.Advance(.0011);
            yield return V3JourneyTests.FinishTransition(OfftrackScenes.Title);
            for (int waited = 0; waited < 30; waited++)
            {
                AssertSilent($"{waited} frames after the ending");
                yield return null;
            }
        }

        private IEnumerator LoadTitle()
        {
            yield return SceneManager.LoadSceneAsync(OfftrackScenes.Title, LoadSceneMode.Single);
            Canvas.ForceUpdateCanvases();
            yield return null;
            Canvas.ForceUpdateCanvases();
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(OfftrackScenes.Title));
            title = Object.FindAnyObjectByType<TitleController>();
            Assert.That(title, Is.Not.Null);
            frame = Child(title.transform, "ReferenceFrame");
            startButton = Child(frame, "StartButton");
            wordmark = Child(frame, "Wordmark");
            record = Child(frame, "TitleRecord");
            Assert.That(SceneTransition.BlocksInput, Is.False, "The title must be up with no transition in flight.");
        }

        private static IEnumerator SettleTransition()
        {
            for (int guard = 0; SceneTransition.BlocksInput && guard < 600; guard++)
            {
                SceneTransition.Advance(.4);
                yield return null;
            }
        }

        private static RectTransform Child(Transform parent, string name)
        {
            var child = parent.Find(name) as RectTransform;
            Assert.That(child, Is.Not.Null, $"{parent.name}/{name} is missing from the title scene.");
            return child;
        }

        // Null while the page is still simply waiting; otherwise what took it away.
        private string TitleFault()
        {
            if (SceneTransition.BlocksInput) return "a scene transition started";
            string scene = SceneManager.GetActiveScene().name;
            if (scene != OfftrackScenes.Title) return "the active scene became " + scene;
            if (title == null) return "the title page was destroyed";
            return null;
        }

        private void AssertStillOnTitle(string context) => Assert.That(TitleFault(), Is.Null, context);

        // An unrotated child's rectangle in the reference frame's own pixels.
        private Rect InFrame(RectTransform target)
        {
            var corners = new Vector3[4];
            target.GetWorldCorners(corners);
            Vector2 min = frame.InverseTransformPoint(corners[0]);
            Vector2 max = frame.InverseTransformPoint(corners[2]);
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private Vector2 ScreenPoint(Vector2 framePoint) =>
            RectTransformUtility.WorldToScreenPoint(Camera.main, frame.TransformPoint(framePoint));

        // Places on the visible screen that are not the start button, taken from the live layout.
        private List<(string Name, Vector2 Point)> OutsideProbes()
        {
            Rect button = InFrame(startButton);
            Rect word = InFrame(wordmark);
            Vector2 disc = frame.InverseTransformPoint(record.position);
            float radius = record.rect.width / 2;
            float labelRadius = Child(record, "Label").rect.width / 2;
            const float margin = 12;
            var places = new List<(string Name, Vector2 Point, bool Record, bool Word)>
            {
                ("the record's grooves", disc + Vector2.left * radius * .75f, true, false),
                ("the record's label", disc + Vector2.left * labelRadius * .5f, true, false),
                ("the wordmark's letters", new Vector2(Mathf.Lerp(word.xMin, word.xMax, .25f), word.center.y), false, true),
                ("the wordmark's box", word.center, false, true),
                ("blank ink below the button", new Vector2(button.center.x, (button.yMin + frame.rect.yMin) / 2), false, false),
                ("blank ink above the button", new Vector2(button.center.x, (button.yMax + word.yMin) / 2), false, false),
                ("just left of the button", new Vector2(button.xMin - margin, button.center.y), false, false),
                ("just right of the button", new Vector2(button.xMax + margin, button.center.y), false, false),
                ("just above the button", new Vector2(button.center.x, button.yMax + margin), false, false),
                ("just below the button", new Vector2(button.center.x, button.yMin - margin), false, false)
            };
            var screen = Camera.main.pixelRect;
            var probes = new List<(string Name, Vector2 Point)>();
            foreach (var place in places)
            {
                // Each place is what its name says in the live layout, so none can drift onto the button or off its element.
                bool onRecord = (place.Point - disc).magnitude < radius && place.Point.x < frame.rect.xMax;
                Assert.That(button.Contains(place.Point), Is.False, $"{place.Name} must lie off the start button.");
                Assert.That(onRecord, Is.EqualTo(place.Record), $"{place.Name}: on the visible half of the record.");
                Assert.That(word.Contains(place.Point), Is.EqualTo(place.Word), $"{place.Name}: on the wordmark.");
                Vector2 point = ScreenPoint(place.Point);
                Assert.That(screen.Contains(point), Is.True, $"{place.Name} must be on screen, was {point} in {screen}.");
                Assert.That(RectTransformUtility.RectangleContainsScreenPoint(startButton, point, Camera.main), Is.False,
                    $"{place.Name} must lie off the start button on screen.");
                probes.Add((place.Name, point));
            }
            // A window wider than the frame also has room right of it, where the other half of the record would be.
            Vector2 beyond = ScreenPoint(disc + Vector2.right * radius * .5f);
            if (screen.Contains(beyond)) probes.Add(("the far half of the record", beyond));
            return probes;
        }

        private IEnumerator Hold(params Key[] keys)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
            yield return null;
        }

        private IEnumerator MouseDown(Vector2 point)
        {
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point }.WithButton(MouseButton.Left));
            yield return null;
            Assert.That(mouse.leftButton.isPressed, Is.True, "The press must reach the mouse device.");
            Assert.That(Vector2.Distance(mouse.position.ReadValue(), point), Is.LessThan(.01f), "The press must land where it was aimed.");
        }

        private IEnumerator MouseUp(Vector2 point)
        {
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point });
            yield return null;
        }

        private IEnumerator TouchDown(int finger, Vector2 point)
        {
            InputSystem.QueueStateEvent(touchscreen, new TouchState
                { touchId = finger, phase = UnityEngine.InputSystem.TouchPhase.Began, position = point });
            yield return null;
            var down = touchscreen.touches.Where(touch => touch.press.isPressed).ToArray();
            Assert.That(down.Length, Is.EqualTo(1), "The touch must reach the touchscreen device.");
            Assert.That(Vector2.Distance(down[0].position.ReadValue(), point), Is.LessThan(.01f), "The touch must land where it was aimed.");
        }

        private IEnumerator TouchUp(int finger, Vector2 point)
        {
            InputSystem.QueueStateEvent(touchscreen, new TouchState
                { touchId = finger, phase = UnityEngine.InputSystem.TouchPhase.Ended, position = point });
            yield return null;
        }

        private static AudioSource[] LoadedSources() =>
            Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        private static string[] SoundingSources() =>
            LoadedSources().Where(source => source.isPlaying).Select(source => source.name).ToArray();

        private static string[] TitleSources() =>
            LoadedSources().Where(source => source.gameObject.scene == SceneManager.GetActiveScene())
                .Select(source => source.name).ToArray();

        private static void AssertSilent(string context)
        {
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(OfftrackScenes.Title), context);
            Assert.That(SoundingSources(), Is.Empty, context + ": nothing may be sounding on the title page.");
            Assert.That(TitleSources(), Is.Empty, context + ": the title scene owns no AudioSource.");
        }

        // Null when the rectangle matches; otherwise both rectangles for the failure message.
        private static string RectFault(Rect actual, float xMin, float yMin, float xMax, float yMax, float tolerance)
        {
            bool matches = Mathf.Abs(actual.xMin - xMin) <= tolerance && Mathf.Abs(actual.yMin - yMin) <= tolerance
                && Mathf.Abs(actual.xMax - xMax) <= tolerance && Mathf.Abs(actual.yMax - yMax) <= tolerance;
            return matches
                ? null
                : $"expected x {xMin}..{xMax}, y {yMin}..{yMax} within {tolerance}, was x {actual.xMin}..{actual.xMax}, y {actual.yMin}..{actual.yMax}";
        }

        // Bounding box of one exact palette colour in a journey-frame capture, in reference-frame pixels.
        private static Rect PaintedBounds(Color32[] pixels, Color32 color, int fromColumn = 0, int toColumn = CaptureWidth)
        {
            int rgb = PaletteAssert.Rgb(color);
            int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
            for (int y = 0; y < CaptureHeight; y++)
                for (int x = fromColumn; x < toColumn; x++)
                {
                    if (PaletteAssert.Rgb(pixels[y * CaptureWidth + x]) != rgb) continue;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            Assert.That(maxX, Is.GreaterThanOrEqualTo(0), $"Nothing is painted #{rgb:X6} in columns {fromColumn}..{toColumn}.");
            return Rect.MinMaxRect(minX - CaptureWidth / 2, minY - CaptureHeight / 2,
                maxX + 1 - CaptureWidth / 2, maxY + 1 - CaptureHeight / 2);
        }

        // Renders the title at the journey window's own 780 x 1688 with hard palette edges.
        private static IEnumerator CaptureJourneyFrame(System.Action<Color32[]> inspect)
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
            var target = new RenderTexture(CaptureWidth, CaptureHeight, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            Texture2D image = null;
            try
            {
                target.Create();
                Shader.EnableKeyword(keyword);
                camera.allowMSAA = false;
                camera.allowHDR = false;
                camera.targetTexture = target;
                camera.aspect = (float)CaptureWidth / CaptureHeight;
                Canvas.ForceUpdateCanvases();
                yield return null;
                Canvas.ForceUpdateCanvases();
                yield return null;
                image = new Texture2D(CaptureWidth, CaptureHeight, TextureFormat.RGB24, false);
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, CaptureWidth, CaptureHeight), 0, 0, false);
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
