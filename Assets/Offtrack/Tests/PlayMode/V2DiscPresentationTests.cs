using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    public sealed class V2DiscPresentationTests
    {
        private GameFlow flow;
        private ScratchPool pool;

        [UnitySetUp]
        public IEnumerator LoadMain()
        {
            Time.timeScale = 1;
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            flow = Object.FindAnyObjectByType<GameFlow>();
            flow.AutomaticTick = false;
            flow.SetApplicationFocus(true);
            var shell = Object.FindAnyObjectByType<AdShell>();
            shell.AutomaticTick = false;
            shell.Advance(flow.Config.Shell.BootDuration);
            Assert.That(shell.AcceptLottery(), Is.True);
            pool = Object.FindAnyObjectByType<ScratchPool>();
            yield return null;
        }

        [Test]
        public void RecordHasPaperPrintQuietGroovesAndUnbrokenTangentialContactMarks()
        {
            var view = flow.Config.View;
            var disc = Object.FindAnyObjectByType<DiscController>().transform;
            Assert.That((Color32)disc.Find("Label").GetComponent<FlatShape>().color, Is.EqualTo(Palette.Bone));
            Assert.That((Color32)disc.Find("Title").GetComponent<Text>().color, Is.EqualTo(Palette.DarkRed));
            Assert.That((Color32)disc.Find("Detail").GetComponent<Text>().color, Is.EqualTo(Palette.DarkRed));
            Assert.That(disc.Find("LabelStamp"), Is.Not.Null);
            Assert.That(disc.GetComponentsInChildren<FlatShape>().Count(shape => shape.name.StartsWith("Groove_")), Is.EqualTo(view.GrooveCount));
            Assert.That(disc.GetComponentsInChildren<FlatShape>().Count(shape => shape.name.StartsWith("EdgeNotch_")), Is.EqualTo(12));
            foreach (var shape in disc.GetComponentsInChildren<FlatShape>().Where(shape => shape.name.StartsWith("Groove_")))
            {
                Assert.That((Color32)shape.color, Is.EqualTo(Palette.DarkRed40));
                var radius = shape.rectTransform.sizeDelta.x / 2;
                foreach (var lane in view.LaneRadiusRatios)
                    Assert.That(Mathf.Abs(radius - lane * view.DiscRadius), Is.GreaterThanOrEqualTo(view.LaneClearance - .001f));
            }
            var allShapes = Object.FindObjectsByType<FlatShape>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Assert.That(allShapes.Any(shape => shape.name.StartsWith("ContactPlate_")), Is.False);
            for (var lane = 0; lane < flow.Config.LaneCount; lane++)
            {
                var contact = allShapes.Single(shape => shape.name == "Contact_" + lane);
                Assert.That(contact.rectTransform.sizeDelta.x, Is.GreaterThan(contact.rectTransform.sizeDelta.y));
                Assert.That((Color32)contact.color, Is.EqualTo(lane == flow.Config.StartLane ? Palette.Bone : Palette.DarkRed));
            }
        }

        [Test]
        public void LegacyIdleAndResultAreAbsentFromMain()
        {
            var frame = Object.FindAnyObjectByType<Canvas>().transform.Find("ReferenceFrame");
            Assert.That(frame.Find("Idle"), Is.Null);
            Assert.That(frame.Find("Result"), Is.Null);
        }

        [Test]
        public void HudBoxesAreDisjointAndTonearmStaysOnPlinthInEveryPose()
        {
            var frame = Object.FindAnyObjectByType<Canvas>().transform.Find("ReferenceFrame");
            var stage = (RectTransform)frame.Find("GameStage");
            var plinth = (RectTransform)stage.Find("Plinth");
            var needle = Object.FindAnyObjectByType<TonearmView>();
            var graphics = needle.GetComponentsInChildren<FlatShape>(true);
            var hud = frame.GetComponentsInChildren<Graphic>(true).Where(graphic => graphic.name.StartsWith("LaneDot_") ||
                graphic.name.StartsWith("Life_") || graphic.name == "TimerRemaining" || graphic.name == "DamageNotice").ToArray();
            Assert.That(hud.Length, Is.EqualTo(flow.Config.Lives + flow.Config.LaneCount + 2));
            for (int first = 0; first < hud.Length; first++)
                for (int second = first + 1; second < hud.Length; second++)
                    Assert.That(BoundsIn(hud[first].rectTransform, frame).Overlaps(BoundsIn(hud[second].rectTransform, frame)),
                        Is.False, hud[first].name + " / " + hud[second].name);
            var tray = BoundsIn(plinth, stage);
            void Measure()
            {
                foreach (var graphic in graphics.Where(graphic => graphic.gameObject.activeInHierarchy))
                {
                    var bounds = BoundsIn(graphic.rectTransform, stage);
                    Assert.That(bounds.xMin, Is.GreaterThanOrEqualTo(tray.xMin), graphic.name);
                    Assert.That(bounds.xMax, Is.LessThanOrEqualTo(tray.xMax), graphic.name);
                    Assert.That(bounds.yMin, Is.GreaterThanOrEqualTo(tray.yMin), graphic.name);
                    Assert.That(bounds.yMax, Is.LessThanOrEqualTo(tray.yMax), graphic.name);
                }
            }
            Measure();
            Assert.That(Object.FindAnyObjectByType<AdShell>().StartPlaying(), Is.True);
            var visited = new HashSet<int>();
            var plan = ChartBot.WinningPresses(flow);
            int press = 0, runFrame = 0;
            for (int frameIndex = 0; frameIndex < 4000 && flow.Phase != MachinePhase.Stopped; frameIndex++)
            {
                if (flow.Phase == MachinePhase.Running)
                {
                    visited.Add(flow.Simulation.Lane);
                    while (press < plan.Length && plan[press].Frame == runFrame) flow.Move(plan[press++].Direction);
                    runFrame++;
                }
                flow.Advance(ChartBot.Step);
                Measure();
            }
            Assert.That(flow.Phase, Is.EqualTo(MachinePhase.Stopped));
            Assert.That(visited.Count, Is.EqualTo(3));
        }

        [Test]
        public void HitOnlyMovesTheNeedleLocallyAndDebrisExpiresWithinAQuarterSecond()
        {
            ChartBot.StartImmediately(flow);
            var tonearm = Object.FindAnyObjectByType<TonearmView>();
            var frame = GameObject.Find("ReferenceFrame").transform;
            var head = tonearm.transform.Find("Head");
            Vector3 HeadPosition() => frame.InverseTransformPoint(head.position);
            var firstContact = flow.Simulation.Notes[0];
            double hitTime = firstContact.Time - JudgeRules.WindowFor(firstContact.Time, flow.Config.ToRules()) / 2;
            typeof(GameSimulation).GetProperty(nameof(GameSimulation.InvincibleUntil)).SetValue(flow.Simulation, hitTime + .3);
            flow.Advance(hitTime);
            Assert.That(flow.Simulation.DamageCount, Is.Zero);
            var unhitHead = HeadPosition();
            flow.Advance(.100001);
            var unhitHeadAfterRecoil = HeadPosition();
            flow.SelectLevel(1);
            ChartBot.StartImmediately(flow);
            var cameraPosition = Camera.main.transform.position;
            var cameraRotation = Camera.main.transform.rotation;
            var pieces = Object.FindObjectsByType<FlatShape>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(shape => shape.name.StartsWith("ContactDebris_")).ToArray();
            Assert.That(pieces.Length, Is.EqualTo(4));
            flow.Advance(hitTime);
            Assert.That(flow.Simulation.Lives, Is.EqualTo(flow.Config.Lives - 1));
            var displacement = HeadPosition() - unhitHead;
            Assert.That(displacement.x, Is.GreaterThanOrEqualTo(4));
            Assert.That(displacement.y, Is.EqualTo(0).Within(.5));
            Assert.That(tonearm.LocalFeedbackOffset.x * flow.Config.ShellView.RecordScale, Is.GreaterThanOrEqualTo(4));
            Assert.That(tonearm.LocalFeedbackOffset.y, Is.EqualTo(0).Within(.001));
            Assert.That((Color32)tonearm.TipColor, Is.EqualTo(Palette.Red));
            Assert.That(pieces.Count(piece => piece.gameObject.activeInHierarchy), Is.InRange(3, 4));
            Assert.That(pieces.All(piece => ((Color32)piece.color).Equals(Palette.Danger)), Is.True);
            var focus = pool.HighlightedNoteId;
            Assert.That(focus, Is.GreaterThanOrEqualTo(0));
            flow.Advance(.02);
            Assert.That(pool.HighlightedNoteId, Is.EqualTo(focus));
            flow.Advance(.080001);
            Assert.That(tonearm.LocalFeedbackOffset, Is.EqualTo(Vector2.zero));
            Assert.That(Vector3.Distance(HeadPosition(), unhitHeadAfterRecoil), Is.LessThan(.001));
            flow.Advance(.15);
            Assert.That(pieces.All(piece => !piece.gameObject.activeInHierarchy), Is.True);
            Assert.That(Camera.main.transform.position, Is.EqualTo(cameraPosition));
            Assert.That(Camera.main.transform.rotation, Is.EqualTo(cameraRotation));
        }

        [Test]
        public void DamageTakesPriorityOverLandingOnTheSameTrack()
        {
            var config = flow.Config;
            int targetLane = config.StartLane - 1;
            var chart = new TextAsset(ChartService.ToJson(new[] { new ChartNote(1, targetLane) }));
            flow.Configure(config, chart);
            ChartBot.StartImmediately(flow);
            var layer = GameObject.Find("RecordFeedback").transform;
            var ring = layer.Find("LaneFeedback_" + targetLane).GetComponent<FlatShape>();
            flow.Advance(1 - config.JumpDuration);
            Assert.That(flow.Move(-1).Result, Is.EqualTo("jump"));
            flow.Advance(config.JumpDuration + .001);
            Assert.That(flow.Simulation.LastLanding, Is.EqualTo(1).Within(.001));
            Assert.That(flow.Simulation.DamageCount, Is.EqualTo(1));
            Assert.That(ring.gameObject.activeSelf, Is.True);
            Assert.That((Color32)ring.color, Is.EqualTo(Palette.Red));
            Object.Destroy(chart);
        }

        [Test]
        public void EntireNumberBoundsAvoidEveryBladeVariantSweepAndRemainReadableInSmallWindow()
        {
            var disc = Object.FindAnyObjectByType<DiscController>().transform;
            var stage = disc.parent.parent;
            var view = flow.Config.View;
            // Measure the actual generated blade corners at their widest appearance. A rotating
            // corner reaches its maximum distance at a vertex, so this encloses every sweep angle.
            var scratch = pool.Items[0].GetComponent<ScratchVisual>();
            float maximumSweep = 0;
            for (int variant = 0; variant < 3; variant++)
            {
                scratch.Present(variant, true, 1, view);
                foreach (var graphic in scratch.GetComponentsInChildren<FlatShape>(true))
                {
                    if (!graphic.gameObject.activeSelf) continue;
                    foreach (var point in CornersIn(graphic.rectTransform, scratch.transform))
                        foreach (var laneRatio in view.LaneRadiusRatios)
                            maximumSweep = Mathf.Max(maximumSweep, new Vector2(view.DiscRadius * laneRatio + point.x, point.y).magnitude);
                }
            }
            for (int lane = 0; lane < flow.Config.LaneCount; lane++)
            {
                var number = disc.parent.Find("ContactNumber_" + lane).GetComponent<Text>();
                var corners = CornersIn(number.rectTransform, disc);
                Assert.That(MinimumRadius(corners), Is.GreaterThan(maximumSweep + view.ContactNumberDiscGap),
                    "The complete text rectangle, including its nearest edge, must stay outside every rotating blade.");
                Assert.That(MinimumRadius(corners), Is.GreaterThan(view.DiscRadius), "Labels also clear the printed disc rim.");
                var labelBounds = BoundsIn(number.rectTransform, stage);
                var stageRect = ((RectTransform)stage).rect;
                Assert.That(stageRect.Contains(labelBounds.min) && stageRect.Contains(labelBounds.max), Is.True, number.name);
                Assert.That(number.fontSize * flow.Config.ShellView.RecordScale * 388 / view.ReferenceResolution.x,
                    Is.GreaterThanOrEqualTo(11), "The 388px client window still needs readable track numbers.");
                Assert.That(number.preferredWidth, Is.LessThanOrEqualTo(number.rectTransform.rect.width));
            }
            pool.Refresh();
        }

        [Test]
        public void ContactMarksOccupyCleanInkBeyondTheTrackBandsAndClearTheNeedleHead()
        {
            var disc = Object.FindAnyObjectByType<DiscController>().transform;
            var group = disc.parent;
            var view = flow.Config.View;
            for (int lane = 0; lane < flow.Config.LaneCount; lane++)
            {
                var contact = (RectTransform)group.Find("Contact_" + lane);
                var corners = CornersIn(contact, disc);
                float laneRadius = view.DiscRadius * view.LaneRadiusRatios[lane];
                Assert.That(MinimumRadius(corners), Is.GreaterThan(laneRadius + view.LaneBandWidth / 2),
                    "The short mark must not merge with the lane or its backing band.");
                Assert.That(corners.Max(point => point.magnitude), Is.LessThan(laneRadius + view.LaneClearance - view.GrooveWidth),
                    "The mark must not merge with the first groove outside the channel.");
                Assert.That(contact.rect.height * flow.Config.ShellView.RecordScale * 388 / view.ReferenceResolution.x,
                    Is.GreaterThanOrEqualTo(1.8f));
            }
            var current = (RectTransform)group.Find("Contact_" + flow.Config.StartLane);
            var head = (RectTransform)group.Find("Tonearm/Head");
            Assert.That(BoundsIn(current, group).Overlaps(BoundsIn(head, group)), Is.False,
                "The existing cartridge must not hide the active short mark.");
        }

        [Test]
        public void RealDamageDoesNotChangeRecordPresentationComparedWithAnUnhitRunAtTheSameTime()
        {
            var original = flow.Config;
            var chart = new TextAsset(ChartService.ToJson(flow.Chart));
            var first = flow.Chart[0];
            var window = first.Time < original.WindowSwitchTime ? original.EarlyWindow : original.LateWindow;
            double captureTime = first.Time - window / 2;
            double beforeWindow = first.Time - window - .001;
            var observed = new List<DamageEvent>();
            flow.DamageTaken += observed.Add;
            try
            {
                flow.Configure(original, chart);
                ChartBot.StartImmediately(flow);
                flow.Advance(beforeWindow);
                Assert.That(flow.Move(first.Lane == 0 ? 1 : -1).Result, Is.EqualTo("jump"));
                flow.Advance(captureTime - beforeWindow);
                Assert.That(observed, Is.Empty);
                Assert.That(flow.Simulation.Lives, Is.EqualTo(original.Lives));
                Assert.That(flow.Simulation.Notes[0].Hit, Is.False);
                Assert.That(pool.HighlightedNoteId, Is.EqualTo(0));
                var before = SnapshotRecord();
                var surroundings = SnapshotSurroundings();
                var layer = GameObject.Find("RecordFeedback").transform;
                Assert.That(layer.Cast<Transform>().Any(child => child.gameObject.activeSelf), Is.False);
                int laneBefore = Object.FindAnyObjectByType<TrackIndicatorView>().HighlightedLane;
                var steps = new[] { .11, .10, .10 };
                var unhitFrames = new List<Dictionary<string, GraphicSnapshot>>();
                foreach (double step in steps)
                {
                    flow.Advance(step);
                    Assert.That(observed, Is.Empty);
                    unhitFrames.Add(SnapshotRecord());
                }

                flow.Configure(original, chart);
                ChartBot.StartImmediately(flow);
                flow.Advance(beforeWindow);
                flow.Advance(captureTime - beforeWindow);
                Assert.That(observed.Count, Is.EqualTo(1), "This must exercise a real Judge/GameSimulation hit.");
                Assert.That(observed[0].ScratchId, Is.EqualTo(0));
                Assert.That(flow.Simulation.Notes[0].Hit, Is.True);
                Assert.That(flow.Simulation.Lives, Is.EqualTo(original.Lives - 1));
                Assert.That(flow.Simulation.Time, Is.EqualTo(captureTime));
                Assert.That(pool.HighlightedNoteId, Is.EqualTo(0));
                Assert.That(Object.FindAnyObjectByType<TrackIndicatorView>().HighlightedLane, Is.EqualTo(laneBefore));
                var after = SnapshotRecord();
                Assert.That(SnapshotSurroundings(), Is.EqualTo(surroundings));
                Assert.That(layer.Cast<Transform>().Count(child => child.gameObject.activeSelf &&
                    child.name.StartsWith("ContactDebris_")), Is.InRange(3, 4));
                var flash = layer.Find("LaneFeedback_" + observed[0].Lane).GetComponent<FlatShape>();
                Assert.That(flash.gameObject.activeSelf, Is.True);
                Assert.That((Color32)flash.color, Is.EqualTo(Palette.Red));
                AssertSameRecord(before, after, ignoreLaneColors: false);
                var flashColors = new[] { Palette.Red60, Palette.Red40 };
                for (int frame = 0; frame < steps.Length; frame++)
                {
                    flow.Advance(steps[frame]);
                    if (frame < flashColors.Length)
                        Assert.That((Color32)flash.color, Is.EqualTo(flashColors[frame]));
                    else Assert.That(layer.Cast<Transform>().Any(child => child.gameObject.activeSelf), Is.False);
                    Assert.That(SnapshotSurroundings(), Is.EqualTo(surroundings));
                    AssertSameRecord(unhitFrames[frame], SnapshotRecord(), ignoreLaneColors: true);
                }
            }
            finally
            {
                flow.DamageTaken -= observed.Add;
                flow.Configure(original, chart);
                Object.Destroy(chart);
            }
        }

        private static void AssertSameRecord(Dictionary<string, GraphicSnapshot> expected,
            Dictionary<string, GraphicSnapshot> actual, bool ignoreLaneColors)
        {
            Assert.That(actual.Keys, Is.EquivalentTo(expected.Keys));
            foreach (var key in expected.Keys)
            {
                Assert.That(actual[key].Active, Is.EqualTo(expected[key].Active), key + " visibility");
                if (!expected[key].Active) continue;
                if (!ignoreLaneColors || !(key.StartsWith("Contact_") || key.StartsWith("ContactNumber_") || key.StartsWith("Disc/LaneBand_")))
                    Assert.That(actual[key].Color, Is.EqualTo(expected[key].Color), key + " color");
                for (int corner = 0; corner < 4; corner++)
                    Assert.That((actual[key].Corners[corner] - expected[key].Corners[corner]).sqrMagnitude,
                        Is.LessThan(1e-8f), key + " geometry");
            }
        }

        private static (Vector3, Quaternion, Vector3, Rect, Color)[] SnapshotSurroundings()
        {
            var camera = Object.FindAnyObjectByType<Camera>();
            var frame = (RectTransform)GameObject.Find("ReferenceFrame").transform;
            var stage = (RectTransform)frame.Find("GameStage");
            var plinth = (RectTransform)stage.Find("Plinth");
            var home = (RectTransform)frame.Find("HomeIndicator");
            return new[] { (camera.transform.position, camera.transform.rotation, camera.transform.lossyScale, camera.rect,
                    camera.backgroundColor),
                Capture(frame, Color.clear), Capture(stage, Color.clear), Capture(plinth, plinth.GetComponent<Graphic>().color),
                Capture(home, home.GetComponent<Graphic>().color) };

            static (Vector3, Quaternion, Vector3, Rect, Color) Capture(RectTransform rect, Color color) =>
                (rect.position, rect.rotation, rect.lossyScale, rect.rect, color);
        }

        private Dictionary<string, GraphicSnapshot> SnapshotRecord()
        {
            var disc = Object.FindAnyObjectByType<DiscController>().transform;
            var group = disc.parent;
            var result = new Dictionary<string, GraphicSnapshot>();
            foreach (Transform root in group)
            {
                if (root.name != "Disc" && !root.name.StartsWith("Contact_") && !root.name.StartsWith("ContactNumber_") &&
                    !root.name.StartsWith("ApproachArc_")) continue;
                foreach (var graphic in root.GetComponentsInChildren<Graphic>(true))
                {
                    string path = graphic.name;
                    for (var parent = graphic.transform.parent; parent != group; parent = parent.parent) path = parent.name + "/" + path;
                    result.Add(path, new GraphicSnapshot
                    {
                        Color = graphic.color, Active = graphic.gameObject.activeInHierarchy,
                        Corners = CornersIn(graphic.rectTransform, group)
                    });
                }
            }
            return result;
        }

        private sealed class GraphicSnapshot
        {
            public Color Color;
            public bool Active;
            public Vector2[] Corners;
        }

        private static Vector2[] CornersIn(RectTransform rect, Transform relativeTo)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return corners.Select(point => (Vector2)relativeTo.InverseTransformPoint(point)).ToArray();
        }

        private static Rect BoundsIn(RectTransform rect, Transform relativeTo)
        {
            var points = CornersIn(rect, relativeTo);
            return Rect.MinMaxRect(points.Min(point => point.x), points.Min(point => point.y),
                points.Max(point => point.x), points.Max(point => point.y));
        }

        private static float MinimumRadius(Vector2[] corners)
        {
            float minimum = float.MaxValue;
            for (int i = 0; i < corners.Length; i++)
            {
                var a = corners[i];
                var edge = corners[(i + 1) % corners.Length] - a;
                float t = Mathf.Clamp01(Vector2.Dot(-a, edge) / edge.sqrMagnitude);
                minimum = Mathf.Min(minimum, (a + edge * t).magnitude);
            }
            return minimum;
        }

        [Test]
        public void ScratchBirthGrowsFromZeroAndThreeVariantsKeepTheirInkIncision()
        {
            Assert.That(Object.FindAnyObjectByType<AdShell>().StartPlaying(), Is.True);
            var born = pool.Items.Where(item => item.activeSelf).Select(item => item.GetComponent<ScratchVisual>()).ToArray();
            Assert.That(born.Length, Is.GreaterThanOrEqualTo(1));
            Assert.That(born.All(item => item.Growth == 0), Is.True);
            flow.Advance(flow.Config.View.ScratchGrowthSeconds / 2);
            Assert.That(born.Any(item => Mathf.Approximately(item.Growth, .5f)), Is.True);
            foreach (var item in born.Where(item => item.Growth > 0))
            {
                Assert.That((Color32)item.transform.Find("InkNotch").GetComponent<FlatShape>().color, Is.EqualTo(Palette.Ink));
                Assert.That(item.transform.Find("Cross").gameObject.activeSelf, Is.EqualTo(item.Variant == 1));
                Assert.That(item.transform.Find("Second").gameObject.activeSelf, Is.EqualTo(item.Variant == 2));
            }
            flow.Advance(flow.Config.View.ScratchGrowthSeconds / 2);
            Assert.That(born.Any(item => Mathf.Approximately(item.Growth, 1)), Is.True);
            for (var variant = 0; variant < 3; variant++)
            {
                born[0].Present(variant, true, 1, flow.Config.View);
                Assert.That(born[0].transform.Find("Cross").gameObject.activeSelf, Is.EqualTo(variant == 1));
                Assert.That(born[0].transform.Find("Second").gameObject.activeSelf, Is.EqualTo(variant == 2));
                Assert.That((Color32)born[0].transform.Find("InkNotch").GetComponent<FlatShape>().color, Is.EqualTo(Palette.Ink));
            }
            Assert.That(ScratchPool.LookAhead(flow.Config), Is.LessThanOrEqualTo(
                flow.Config.BeatsPerRevolution * 60 / flow.Config.Bpm * .75 + 1e-6),
                "Later births must start at least a quarter turn away from the needle.");
        }

        [Test]
        public void EveryFrameHasAtMostOneMainRedScratchAndItsFixedArcMatchesTheSameTrack()
        {
            var presses = ChartBot.WinningPresses(flow);
            var indicator = Object.FindAnyObjectByType<TrackIndicatorView>();
            var arcs = Object.FindObjectsByType<FlatShape>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(shape => shape.name.StartsWith("ApproachArc_")).ToArray();
            var inputIndex = 0;
            var highlightedFrames = 0;
            var seenVariants = new bool[3];
            var seenLaterBirth = false;
            ChartBot.StartImmediately(flow);
            for (var frame = 0; frame < 3000 && flow.State == RunState.Playing; frame++)
            {
                while (inputIndex < presses.Length && presses[inputIndex].Frame == frame)
                    flow.Move(presses[inputIndex++].Direction);
                flow.Advance(1.0 / 60);
                var active = pool.Items.Where(item => item.activeSelf).Select(item => item.GetComponent<ScratchVisual>()).ToArray();
                var red = active.Where(item => ((Color32)item.transform.Find("Cut").GetComponent<FlatShape>().color).Equals(Palette.Danger)).ToArray();
                Assert.That(red.Length, Is.LessThanOrEqualTo(1), "frame " + frame);
                foreach (var item in active)
                {
                    seenVariants[item.Variant] = true;
                    Assert.That((Color32)item.transform.Find("InkNotch").GetComponent<FlatShape>().color, Is.EqualTo(Palette.Ink));
                    if (flow.Simulation.Time > 1 && item.Growth > 0 && item.Growth < 1) seenLaterBirth = true;
                }
                if (red.Length == 1)
                {
                    highlightedFrames++;
                    var note = flow.Simulation.Notes[pool.HighlightedNoteId];
                    Assert.That(note.Time - flow.Simulation.Time,
                        Is.LessThanOrEqualTo(flow.Config.View.ScratchHighlightBeats * 60 / flow.Config.Bpm));
                    Assert.That(indicator.HighlightedLane, Is.EqualTo(note.Lane));
                    Assert.That(arcs.Count(arc => arc.gameObject.activeSelf), Is.EqualTo(1));
                    Assert.That(arcs.Single(arc => arc.gameObject.activeSelf).name, Is.EqualTo("ApproachArc_" + note.Lane));
                }
                else Assert.That(arcs.Count(arc => arc.gameObject.activeSelf), Is.Zero);
            }
            Assert.That(flow.Simulation.Result.Won, Is.True);
            Assert.That(highlightedFrames, Is.GreaterThan(100));
            Assert.That(seenVariants.All(value => value), Is.True);
            Assert.That(seenLaterBirth, Is.True);
        }

        [System.Serializable] private sealed class BotFixture { public BotInput[] inputs = System.Array.Empty<BotInput>(); }
        [System.Serializable] private sealed class BotInput { public int frame; public int dir; }
    }
}
