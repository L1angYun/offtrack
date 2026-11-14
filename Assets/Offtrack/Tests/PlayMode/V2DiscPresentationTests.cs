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
        public void CompleteCopyIsEntirelyBelowTheMachineAndClearOfHudAndTicks()
        {
            var frame = Object.FindAnyObjectByType<Canvas>().transform.Find("ReferenceFrame");
            var stage = (RectTransform)frame.Find("GameStage");
            var title = (RectTransform)frame.Find("Result/ResultTitle");
            var body = (RectTransform)frame.Find("Result/ResultBody");
            var stageBounds = BoundsIn(stage, frame);
            var titleBounds = BoundsIn(title, frame);
            var bodyBounds = BoundsIn(body, frame);
            Assert.That(titleBounds.Overlaps(bodyBounds), Is.False);
            var ticks = stage.GetComponentsInChildren<FlatShape>(true).Where(shape => shape.name.StartsWith("ScaleTick_"));
            foreach (var copy in new[] { titleBounds, bodyBounds })
            {
                Assert.That(copy.yMax, Is.LessThan(stageBounds.yMin), "The entire completion block belongs outside the plinth.");
                Assert.That(copy.yMin, Is.GreaterThan(-flow.Config.View.ReferenceResolution.y / 2));
                foreach (var tick in ticks) Assert.That(copy.Overlaps(BoundsIn(tick.rectTransform, frame)), Is.False);
                foreach (var name in new[] { "Timer", "Progress", "ProgressTrack" })
                    Assert.That(copy.Overlaps(BoundsIn((RectTransform)frame.Find(name), frame)), Is.False, name);
                for (int life = 0; life < flow.Config.Lives; life++)
                    Assert.That(copy.Overlaps(BoundsIn((RectTransform)frame.Find("Life_" + life), frame)), Is.False);
            }
            foreach (var tick in ticks)
                Assert.That(tick.rectTransform.rect.width, Is.GreaterThanOrEqualTo(2), tick.name);
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
            var control = Object.Instantiate(original);
            var chart = new TextAsset(ChartService.ToJson(flow.Chart));
            var first = flow.Chart[0];
            var window = first.Time < original.WindowSwitchTime ? original.EarlyWindow : original.LateWindow;
            double captureTime = first.Time - window / 2;
            // The reference reaches exactly the same timestamp/lane with a narrower judging
            // window, so the only visual variable under test is the real damage outcome.
            // Both runs are before note.Time, where the highlight/retirement rules agree.
            if (first.Time < original.WindowSwitchTime) control.EarlyWindow = window / 4;
            else control.LateWindow = window / 4;
            var observed = new List<DamageEvent>();
            flow.DamageTaken += observed.Add;
            try
            {
                flow.Configure(control, chart);
                flow.StartRun();
                flow.Advance(captureTime);
                Assert.That(observed, Is.Empty);
                Assert.That(flow.Simulation.Lives, Is.EqualTo(original.Lives));
                Assert.That(flow.Simulation.Notes[0].Hit, Is.False);
                Assert.That(pool.HighlightedNoteId, Is.EqualTo(0));
                var before = SnapshotRecord();
                int laneBefore = Object.FindAnyObjectByType<TrackIndicatorView>().HighlightedLane;

                flow.Configure(original, chart);
                flow.StartRun();
                flow.Advance(captureTime);
                Assert.That(observed.Count, Is.EqualTo(1), "This must exercise a real Judge/GameSimulation hit.");
                Assert.That(observed[0].ScratchId, Is.EqualTo(0));
                Assert.That(flow.Simulation.Notes[0].Hit, Is.True);
                Assert.That(flow.Simulation.Lives, Is.EqualTo(original.Lives - 1));
                Assert.That(flow.Simulation.Time, Is.EqualTo(captureTime));
                Assert.That(pool.HighlightedNoteId, Is.EqualTo(0));
                Assert.That(Object.FindAnyObjectByType<TrackIndicatorView>().HighlightedLane, Is.EqualTo(laneBefore));
                var after = SnapshotRecord();
                Assert.That(after.Keys, Is.EquivalentTo(before.Keys));
                foreach (var key in before.Keys)
                {
                    Assert.That(after[key].Color, Is.EqualTo(before[key].Color), key + " color");
                    Assert.That(after[key].Active, Is.EqualTo(before[key].Active), key + " visibility");
                    for (int corner = 0; corner < 4; corner++)
                        Assert.That((after[key].Corners[corner] - before[key].Corners[corner]).sqrMagnitude,
                            Is.LessThan(1e-8f), key + " geometry");
                }
            }
            finally
            {
                flow.DamageTaken -= observed.Add;
                flow.Configure(original, chart);
                Object.Destroy(control);
                Object.Destroy(chart);
            }
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
            flow.StartRun();
            var born = pool.Items.Where(item => item.activeSelf).Select(item => item.GetComponent<ScratchVisual>()).ToArray();
            Assert.That(born.Length, Is.GreaterThanOrEqualTo(1));
            Assert.That(born.All(item => item.Growth == 0), Is.True);
            flow.Advance(flow.Config.View.ScratchGrowthSeconds / 2);
            foreach (var item in born)
            {
                Assert.That(item.Growth, Is.EqualTo(.5f).Within(.001f));
                Assert.That((Color32)item.transform.Find("InkNotch").GetComponent<FlatShape>().color, Is.EqualTo(Palette.Ink));
                Assert.That(item.transform.Find("Cross").gameObject.activeSelf, Is.EqualTo(item.Variant == 1));
                Assert.That(item.transform.Find("Second").gameObject.activeSelf, Is.EqualTo(item.Variant == 2));
            }
            flow.Advance(flow.Config.View.ScratchGrowthSeconds / 2);
            Assert.That(born.All(item => Mathf.Approximately(item.Growth, 1)), Is.True);
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
            flow.StartRun();
            for (var frame = 0; frame < 3000 && flow.State == RunState.Playing; frame++)
            {
                while (inputIndex < presses.Length && presses[inputIndex].Frame == frame)
                    flow.Move(presses[inputIndex++].Direction);
                flow.Advance(1.0 / 60);
                var active = pool.Items.Where(item => item.activeSelf).Select(item => item.GetComponent<ScratchVisual>()).ToArray();
                var red = active.Where(item => ((Color32)item.transform.Find("Cut").GetComponent<FlatShape>().color).Equals(Palette.Red)).ToArray();
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
