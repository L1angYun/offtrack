using System.Collections;
using System.Linq;
using Offtrack.Core;
using Offtrack.Runtime.Presentation;
using NUnit.Framework;
using Offtrack.Runtime;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Offtrack.Tests.PlayMode
{
    public sealed class V3JourneyTests
    {
        [UnityTest]
        public IEnumerator ARealHitDoesNotChangeTheVisibleHazardsInLevelsTwoAndThree()
        {
            for (int level = 2; level <= 3; level++)
            {
                yield return SceneManager.LoadSceneAsync(OfftrackScenes.Main);
                var shell = Object.FindAnyObjectByType<AdShell>();
                var flow = shell.Flow;
                flow.AutomaticTick = shell.AutomaticTick = false;
                flow.SetApplicationFocus(true);
                shell.Advance(flow.Config.Shell.BootDuration);
                Assert.That(shell.AcceptLottery(), Is.True);
                var pool = Object.FindAnyObjectByType<ScratchPool>();
                flow.SelectLevel(level);
                ChartBot.StartImmediately(flow);
                var first = flow.Simulation.Notes[0];
                double window = JudgeRules.WindowFor(first.Time, flow.Config.ToRules());
                double hitTime = first.Time - window / 2;
                var outside = shell.Stage.parent.GetComponentsInChildren<Graphic>(true)
                    .Where(graphic => graphic.name is "StatusTime" or "BrandName" or "BrandSubtitle" or "SkipButton" or "SkipCopy").ToArray();
                var bands = shell.Stage.GetComponentsInChildren<FlatShape>(true)
                    .Where(shape => shape.name.StartsWith("LaneBand_") || shape.name.StartsWith("ApproachArc_") || shape.name.StartsWith("Contact_")).ToArray();
                Assert.That(outside.Length, Is.EqualTo(5));
                Assert.That(bands.Length, Is.GreaterThanOrEqualTo(6));
                var frame = (RectTransform)shell.Stage.parent;
                var shellBefore = ShellPicture();
                var overlay = new GameObject("Unexpected full-screen layer", typeof(RectTransform), typeof(FlatShape));
                var overlayRect = (RectTransform)overlay.transform;
                overlayRect.SetParent(frame, false);
                overlayRect.anchorMin = Vector2.zero;
                overlayRect.anchorMax = Vector2.one;
                overlayRect.sizeDelta = Vector2.zero;
                Assert.That(ShellPicture().Item2, Is.Not.EqualTo(shellBefore.Item2));
                overlay.SetActive(false);
                Object.Destroy(overlay);

                var snapshots = new System.Collections.Generic.List<((Vector2, Quaternion, bool, float, Color32)[], (bool, Color32, string)[], ((bool, Color32, string)[], string[]))>();
                foreach (bool shielded in new[] { true, false })
                {
                    flow.SelectLevel(level);
                    ChartBot.StartImmediately(flow);
                    if (shielded) typeof(GameSimulation).GetProperty(nameof(GameSimulation.InvincibleUntil)).SetValue(flow.Simulation, hitTime + .4);
                    var offsets = new[] { 0.0, .11, .21, .31 };
                    for (int index = 0; index < offsets.Length; index++)
                    {
                        flow.Advance(hitTime + offsets[index] - flow.RunTime);
                        Assert.That(flow.Simulation.DamageCount, Is.EqualTo(shielded ? 0 : 1));
                        var snapshot = (Picture(), bands.Select(ReadGraphic).ToArray(), ShellPicture());
                        if (shielded) snapshots.Add(snapshot);
                        else { Assert.That(snapshot.Item1, Is.EqualTo(snapshots[index].Item1)); Assert.That(snapshot.Item2, Is.EqualTo(snapshots[index].Item2)); Assert.That(snapshot.Item3.Item1, Is.EqualTo(snapshots[index].Item3.Item1)); Assert.That(snapshot.Item3.Item2, Is.EqualTo(snapshots[index].Item3.Item2)); }
                    }
                }

                (bool, Color32, string) ReadGraphic(Graphic item) => (item.gameObject.activeInHierarchy, item.color, (item as Text)?.text);
                ((bool, Color32, string)[], string[]) ShellPicture() => (outside.Select(ReadGraphic).ToArray(), frame.GetComponentsInChildren<Graphic>()
                    .Where(graphic => !graphic.transform.IsChildOf(shell.Stage) && graphic.rectTransform.rect.size == frame.rect.size)
                    .Select(graphic => graphic.name).ToArray());

                (Vector2, Quaternion, bool, float, Color32)[] Picture() => pool.Items.Where(item => item.activeSelf)
                    .SelectMany(item => item.GetComponentsInChildren<FlatShape>().Select(shape =>
                        (((RectTransform)item.transform).anchoredPosition, item.transform.localRotation,
                            item.GetComponent<ScratchVisual>().Highlighted, item.GetComponent<ScratchVisual>().Growth,
                            (Color32)shape.color))).ToArray();
            }
        }

        [UnityTest]
        public IEnumerator RecordFocusAndApproachArcSurviveEveryJudgmentFrame()
        {
            for (int level = 2; level <= 3; level++)
            {
                yield return SceneManager.LoadSceneAsync(OfftrackScenes.Main);
                var shell = Object.FindAnyObjectByType<AdShell>();
                var flow = shell.Flow;
                flow.AutomaticTick = shell.AutomaticTick = false;
                flow.SetApplicationFocus(true);
                shell.Advance(flow.Config.Shell.BootDuration);
                Assert.That(shell.AcceptLottery(), Is.True);
                flow.SelectLevel(level);
                ChartBot.StartImmediately(flow);
                var pool = Object.FindAnyObjectByType<ScratchPool>();
                var indicator = Object.FindAnyObjectByType<TrackIndicatorView>();
                var notes = flow.Simulation.Notes;
                var shapes = Object.FindObjectsByType<FlatShape>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                var arcs = shapes.Where(shape => shape.name.StartsWith("ApproachArc_")).OrderBy(shape => shape.name).ToArray();
                var arrow = shapes.Single(shape => shape.name == "Arrow" && shape.transform.parent.name == "ReversalStamp").transform;
                Assert.That(arcs.Length, Is.EqualTo(3));
                var plan = ChartBot.WinningPresses(flow);
                var rule = flow.Config.ToRules();
                var horizon = 2 * flow.Record.BeatSeconds;
                var cues = new System.Collections.Generic.List<(bool Reverse, double Time)>();
                flow.ReversalCue += reverse => cues.Add((reverse, flow.Simulation.Time));
                var previous = new bool[flow.Record.Hazards.Count];
                var seen = new bool[previous.Length];
                var reversalFrames = new System.Collections.Generic.HashSet<int>();
                double previousTime = double.NaN, previousAngle = 0, previousVelocity = 0;
                int nextPress = 0, gapFrames = 0, reverseFrames = 0, freshTails = 0, settledRunners = 0, reentries = 0;
                for (int frame = 0; frame < 4000 && flow.State == RunState.Playing; frame++)
                {
                    while (nextPress < plan.Length && plan[nextPress].Frame == frame)
                        flow.Move(plan[nextPress++].Direction);
                    double time = flow.Simulation.Time;
                    int expected = -1;
                    for (int index = 0; index < notes.Count; index++)
                    {
                        var note = notes[index];
                        if (note.Time - time > horizon || time > note.EndTime + JudgeRules.WindowFor(note.EndTime, rule)) continue;
                        expected = index;
                        break;
                    }
                    int lane = expected < 0 ? -1 : notes[expected].Lane;
                    Assert.That(pool.HighlightedNoteId, Is.EqualTo(expected), $"L{level} {time:F3}s");
                    Assert.That(indicator.HighlightedLane, Is.EqualTo(lane));
                    for (int index = 0; index < arcs.Length; index++)
                    {
                        Assert.That(arcs[index].gameObject.activeSelf, Is.EqualTo(index == lane));
                        if (index == lane) Assert.That((Color32)arcs[index].color, Is.EqualTo(Palette.Danger));
                    }
                    double beat = time / flow.Record.BeatSeconds;
                    double literalBeat = time / .46875;
                    double phase = level == 2 ? literalBeat : literalBeat < 16 ? literalBeat : literalBeat < 24 ? 32 - literalBeat
                        : literalBeat < 56 ? literalBeat - 16 : 96 - literalBeat;
                    Assert.That(flow.DiscRotationDegrees, Is.EqualTo(-22.5 * phase).Within(.001));
                    if (level == 3 && time > previousTime)
                    {
                        double velocity = (flow.DiscRotationDegrees - previousAngle) / (time - previousTime);
                        if (velocity * previousVelocity < 0)
                        {
                            int reversal = new[] { 16, 24, 56 }.Single(point => System.Math.Abs(time - point * .46875) <= 2 * ChartBot.Step);
                            Assert.That(reversalFrames.Add(reversal), Is.True);
                        }
                        previousVelocity = velocity;
                    }
                    previousTime = time;
                    previousAngle = flow.DiscRotationDegrees;
                    int announcedReversals = flow.Record.Reversals.Count(reverse => beat + 2 >= reverse);
                    Assert.That(Mathf.Abs(Mathf.DeltaAngle(arrow.localEulerAngles.z, announcedReversals % 2 * 180)), Is.LessThan(.01f));
                    if (flow.Record.DirectionAt(beat) < 0) reverseFrames++;
                    foreach (var hazardView in pool.Items.Where(item => item.activeInHierarchy))
                    {
                        bool highlighted = hazardView.GetComponent<ScratchVisual>().Highlighted;
                        var expectedColor = highlighted ? new Color32(255, 216, 77, 255) : new Color32(157, 132, 49, 255);
                        foreach (var part in hazardView.GetComponentsInChildren<FlatShape>())
                        {
                            var color = part.name is "InkNotch" or "MissingLaneArc" ? Palette.Ink : expectedColor;
                            Assert.That((Color32)part.color, Is.EqualTo(color), part.name);
                        }
                    }
                    for (int id = 0; id < previous.Length; id++)
                    {
                        var hazard = flow.Record.Hazards[id];
                        bool visible = flow.Record.IsVisible(id, beat, flow.Level.LeftBeats, flow.Level.RightBeats);
                        bool returning = visible && seen[id] && !previous[id];
                        previous[id] = visible;
                        seen[id] |= visible;
                        if (!visible || (!returning && hazard.kind != HazardKind.RunningScratch)) continue;
                        double delta = hazard.phase - flow.Record.PhaseAt(beat);
                        float radial = (float)hazard.RadialLane(delta, 3, 2);
                        int inner = Mathf.FloorToInt(radial);
                        var view = flow.Config.View;
                        float radius = view.DiscRadius * Mathf.Lerp(view.LaneRadiusRatios[inner],
                            view.LaneRadiusRatios[Mathf.Min(inner + 1, 2)], radial - inner);
                        float rotation = (float)(hazard.phase * 22.5 + 90);
                        var item = pool.Items.Single(candidate => candidate.activeSelf &&
                            Mathf.Abs(Mathf.DeltaAngle(candidate.transform.localEulerAngles.z, rotation)) < .01f &&
                            Mathf.Abs(((RectTransform)candidate.transform).anchoredPosition.magnitude - radius) < .01f);
                        bool focused = expected >= 0 && notes[expected].HazardId == id;
                        var scratch = item.GetComponent<ScratchVisual>();
                        Assert.That(scratch.Highlighted, Is.EqualTo(focused));
                        if (returning)
                        {
                            Assert.That(scratch.Growth, Is.LessThan(.1f));
                            reentries++;
                        }
                        if (hazard.kind != HazardKind.RunningScratch) continue;
                        var tail = item.transform.Find("RunnerTail");
                        Assert.That(tail != null && tail.gameObject.activeSelf, Is.EqualTo(delta > 2));
                        if (delta > 2)
                        {
                            Assert.That((Color32)tail.GetComponent<FlatShape>().color, Is.EqualTo(focused ? Palette.Danger : Palette.Danger60));
                            freshTails++;
                        }
                        else settledRunners++;
                    }
                    if (expected >= 0 && notes[expected].EndTime > notes[expected].Time && time >= notes[expected].Time - JudgeRules.WindowFor(notes[expected].Time, rule))
                    {
                        var visual = pool.Items.Single(item => item.activeSelf && item.GetComponent<ScratchVisual>().Highlighted);
                        foreach (string cap in new[] { "BreakStart", "BreakEnd" })
                        {
                            var shape = visual.transform.Find(cap).GetComponent<FlatShape>();
                            Assert.That(shape.gameObject.activeInHierarchy, Is.True);
                            Assert.That((Color32)shape.color, Is.EqualTo(Palette.Danger));
                        }
                        gapFrames++;
                    }
                    flow.Advance(ChartBot.Step);
                }
                Assert.That(flow.Simulation.Result.Won, Is.True);
                if (level == 2)
                {
                    Assert.That(gapFrames, Is.GreaterThan(60));
                    Assert.That(freshTails, Is.Positive);
                    Assert.That(settledRunners, Is.Positive);
                    Assert.That(cues, Is.Empty);
                }
                else
                {
                    Assert.That(gapFrames, Is.Zero);
                    Assert.That(reverseFrames, Is.GreaterThan(60));
                    Assert.That(reversalFrames, Is.EquivalentTo(new[] { 16, 24, 56 }));
                    Assert.That(reentries, Is.Positive);
                    Assert.That(cues.Select(cue => cue.Reverse), Is.EqualTo(new[] { false, true, false, true, false, true }));
                    for (int index = 0; index < cues.Count; index++)
                    {
                        double beat = flow.Record.Reversals[index / 2] - (cues[index].Reverse ? 0 : 1);
                        double expectedTime = beat * flow.Record.BeatSeconds;
                        Assert.That(cues[index].Time, Is.InRange(expectedTime - 1e-8, expectedTime + ChartBot.Step));
                    }
                    double stoppedFrom = flow.DiscRotationDegrees;
                    double previousStep = double.PositiveInfinity;
                    for (int frame = 0; frame < 60; frame++)
                    {
                        flow.Advance(flow.StopDuration / 60);
                        double angle = flow.DiscRotationDegrees;
                        double change = angle - stoppedFrom;
                        Assert.That(change, Is.Positive);
                        Assert.That(change, Is.LessThan(previousStep));
                        stoppedFrom = angle;
                        previousStep = change;
                    }
                    flow.Advance(ChartBot.Step);
                    Assert.That(flow.DiscRotationDegrees, Is.EqualTo(stoppedFrom).Within(.00001));
                }
            }
        }

        [UnityTest]
        public IEnumerator MainReservesDangerForTheRecordHazards()
        {
            yield return SceneManager.LoadSceneAsync(OfftrackScenes.Main);
            var shell = Object.FindAnyObjectByType<AdShell>();
            shell.Flow.AutomaticTick = shell.AutomaticTick = false;
            void Check()
            {
                foreach (var graphic in Object.FindObjectsByType<Graphic>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (!PaletteAssert.IsDanger(graphic.color)) continue;
                    Assert.That(graphic.GetComponentInParent<ScratchVisual>(true) != null ||
                        graphic.name.StartsWith("ApproachArc_") || graphic.name.StartsWith("ContactDebris_"), Is.True, graphic.name);
                }
            }
            Check();
            shell.Advance(shell.Flow.Config.Shell.BootDuration);
            Check();
            Assert.That(shell.AcceptLottery(), Is.True);
            Check();
            ChartBot.StartImmediately(shell.Flow);
            Check();
        }

#if UNITY_EDITOR
        [SetUp]
        public void ResetWindowSample() => StandaloneWindow.SetTestWindowBackend(null, null);

        [TearDown]
        public void ReleaseWindowSample() => StandaloneWindow.SetTestWindowBackend(null, null);
#endif
        [UnityTest]
        public IEnumerator NineDialogsMatchSpecAndLeaveMeasuredRoomForEveryLine()
        {
            yield return SceneManager.LoadSceneAsync(OfftrackScenes.Main);
            var shell = Object.FindAnyObjectByType<AdShell>();
            shell.AutomaticTick = false;
            var dialog = shell.Dialog;
            var card = (RectTransform)dialog.transform;
            var labels = dialog.GetComponentsInChildren<Text>(true);
            var title = labels.Single(label => label.name.EndsWith("_Title"));
            var body = labels.Single(label => label.name.EndsWith("_Body"));
            var primary = dialog.PrimaryButton.GetComponentInChildren<Text>(true);
            var secondary = dialog.SecondaryButton.GetComponentInChildren<Text>(true);
            var cases = new[]
            {
                (AdShellState.Lottery, 1, "天降好运", "恭喜你被天降好运砸中了！！\n现在玩把小游戏，就能获得限免黑胶会员！！", "开一把", "", 3),
                (AdShellState.ConfirmSkip, 1, "你真的忍心", "放弃掉获得黑胶会员的机会吗？", "还是留下", "忍痛放弃", 1),
                (AdShellState.FailDialog, 1, "很可惜", "", "重试本关", "放弃", 0),
                (AdShellState.Guide, 1, "玩法引导", "跳动指针，别让音乐停下来。", "开始吧", "", 1),
                (AdShellState.Guide, 2, "玩法引导", "路会断，划痕会跑。看清楚再跳。", "开始吧", "", 1),
                (AdShellState.Guide, 3, "玩法引导", "唱片会掉头。别信你刚才看的那一边。", "开始吧", "", 2),
                (AdShellState.Fragment, 1, "", "恭喜你获得黑胶碎片 ×1\n还需要再集齐 2 个碎片，就能够获得黑胶会员了。", "前往下一关", "", 3),
                (AdShellState.Fragment, 2, "", "恭喜你获得黑胶碎片 ×1\n还需要再集齐 1 个碎片，就能够获得黑胶会员了。", "前往下一关", "", 3),
                (AdShellState.Fragment, 3, "", "恭喜你集齐碎片，获得黑胶会员！", "开心收下", "", 1)
            };
            foreach (var (state, level, heading, copy, action, alternate, lineCount) in cases)
            {
                string context = $"{state} level {level}";
                dialog.Show(state, level, null);
                dialog.Advance(1);
                Canvas.ForceUpdateCanvases();
                Assert.That(title.gameObject.activeSelf ? title.text : "", Is.EqualTo(heading), context);
                Assert.That(body.text.Replace('\u00a0', ' '), Is.EqualTo(copy), context);
                Assert.That(primary.text, Is.EqualTo(action), context);
                Assert.That(dialog.SecondaryButton.gameObject.activeSelf ? secondary.text : "", Is.EqualTo(alternate), context);
                Assert.That(string.Concat(heading, copy, action, alternate), Does.Not.Contain("7 小时"));
                Assert.That(card.rect.width, Is.EqualTo(608), context);
                var edges = card.GetComponentsInChildren<FlatShape>().Where(shape => shape.transform.parent.name.EndsWith("_Outline")).ToArray();
                Assert.That(edges.Length, Is.EqualTo(4));
                foreach (var edge in edges)
                {
                    Assert.That((Color32)edge.color, Is.EqualTo(new Color32(240, 230, 214, 255)), context);
                    Assert.That(Mathf.Min(edge.rectTransform.rect.width, edge.rectTransform.rect.height), Is.EqualTo(1), edge.name);
                }
                Assert.That(title.gameObject.activeSelf, Is.EqualTo(state != AdShellState.Fragment), context);
                Assert.That(title.fontSize, Is.EqualTo(20), context);
                Assert.That((Color32)title.color, Is.EqualTo(new Color32(140, 58, 58, 255)), context);
                Assert.That(title.GetComponent<LetterSpacing>().Spacing, Is.EqualTo(shell.Flow.Config.ShellView.DialogTitleTracking), context);
                var firstLine = title.gameObject.activeSelf ? title.rectTransform : body.rectTransform;
                Assert.That(card.rect.yMax - firstLine.anchoredPosition.y - firstLine.rect.yMax, Is.EqualTo(55).Within(.01), context);
                Assert.That(card.rect.width - body.rectTransform.rect.width, Is.EqualTo(110), context);
                Assert.That(body.fontSize, Is.EqualTo(28), context);
                Assert.That(primary.fontSize, Is.EqualTo(28), context);
                Assert.That(secondary.fontSize, Is.EqualTo(primary.fontSize), context);
                Assert.That(secondary.fontStyle, Is.EqualTo(primary.fontStyle), context);
                float buttonTop = dialog.PrimaryButton.anchoredPosition.y + dialog.PrimaryButton.rect.yMax;
                float buttonBottom = dialog.PrimaryButton.anchoredPosition.y + dialog.PrimaryButton.rect.yMin;
                Assert.That(buttonBottom - card.rect.yMin, Is.EqualTo(55).Within(.01), context);
                float bodyBottom = body.rectTransform.anchoredPosition.y + body.rectTransform.rect.yMin;
                Assert.That(bodyBottom - buttonTop, Is.GreaterThanOrEqualTo(50.39f), context);
                if (lineCount == 0) continue;
                var generator = new TextGenerator();
                generator.Populate(body.text, body.GetGenerationSettings(body.rectTransform.rect.size));
                int minimumLines = copy.Contains('\n') ? 2 : 1;
                Assert.That(generator.lineCount, Is.InRange(minimumLines, lineCount), context);
                for (int index = 1; index < generator.lineCount; index++)
                {
                    var line = generator.lines[index];
                    float advance = Mathf.Round(42 * body.pixelsPerUnit);
                    Assert.That(generator.lines[index - 1].topY - line.topY, Is.EqualTo(advance).Within(.5), context);
                    Assert.That(generator.lines[0].topY - line.topY, Is.EqualTo(index * advance).Within(.5), context);
                    Assert.That(body.text[line.startCharIdx], Is.Not.EqualTo('个').And.Not.EqualTo('\u00a0'), context);
                    Assert.That(body.text[line.startCharIdx] == '1' && body.text[line.startCharIdx - 1] == '×', Is.False, context);
                }
                foreach (var vertex in generator.verts.Take(generator.vertexCount - 4))
                {
                    Vector2 position = (Vector2)vertex.position / body.pixelsPerUnit + body.rectTransform.anchoredPosition;
                    Assert.That(card.rect.Contains(position), Is.True, context);
                    Assert.That(position.y, Is.GreaterThan(buttonTop), context);
                }
            }
        }

        [UnityTest]
        public IEnumerator FullPlaysUseThreeChartsAndRetryKeepsLevelAndFragments()
        {
            yield return SceneManager.LoadSceneAsync(OfftrackScenes.Main);
            var shell = Object.FindAnyObjectByType<AdShell>();
            var flow = shell.Flow;
            shell.AutomaticTick = false;
            flow.AutomaticTick = false;
            flow.SetApplicationFocus(true);
            shell.Advance(flow.Config.Shell.BootDuration);
            Assert.That(shell.AcceptLottery(), Is.True);
            for (int number = 1; number <= 3; number++)
            {
                Assert.That(shell.Level, Is.EqualTo(number));
                Assert.That(flow.Level, Is.SameAs(flow.Config.Levels[number - 1]));
                Assert.That(shell.State, Is.EqualTo(AdShellState.Guide));
                flow.StartRun();
                Assert.That(flow.Phase, Is.EqualTo(MachinePhase.Parked));
                Assert.That(shell.StartPlaying(), Is.True);
                flow.Advance(flow.PreparationLength);
                if (number == 2)
                {
                    while (flow.State == RunState.Playing) flow.Advance(ChartBot.Step);
                    flow.Advance(flow.StopDuration);
                    Assert.That(shell.State, Is.EqualTo(AdShellState.FailDialog));
                    Assert.That(shell.Machine.Fragments, Is.EqualTo(1));
                    shell.Advance(.31);
                    var beforeRetry = CaptureAudio();
                    shell.ActivatePrimary();
                    AssertSilent(beforeRetry);
                    M1GameplayTests.FinishRetry(flow);
                    M1GameplayTests.FinishNeedleDrop(flow);
                    Assert.That(shell.State, Is.EqualTo(AdShellState.Playing));
                    Assert.That(flow.Level, Is.SameAs(flow.Config.Levels[1]));
                }
                var plan = ChartBot.WinningPresses(flow);
                Assert.That(ChartBot.Play(flow, plan), Is.EqualTo(plan.Length));
                Assert.That(flow.Simulation.Result.DamageCount, Is.Zero);
                flow.Advance(flow.StopDuration);
                Assert.That(shell.State, Is.EqualTo(AdShellState.Fragment));
                shell.Advance(.31);
                Assert.That(shell.RequestSkip(), Is.False);
                var beforeFragment = CaptureAudio();
                SceneTransition.AutomaticTick = false;
                try
                {
                    shell.ActivatePrimary();
                    AssertSilent(beforeFragment);
                    SceneTransition.Advance(.1);
                    AssertSilent(beforeFragment);
                    Assert.That(SceneTransition.Opacity, Is.EqualTo(number < 3 ? .5 : .25).Within(.001));
                    Assert.That(shell.State, Is.EqualTo(AdShellState.Fragment));
                    if (number < 3)
                    {
                        SceneTransition.Advance(.10001);
                        Assert.That(shell.State, Is.EqualTo(AdShellState.Guide));
                        Assert.That(shell.Level, Is.EqualTo(number + 1));
                        Assert.That(SceneTransition.Opacity, Is.EqualTo(1));
                        SceneTransition.Advance(.09);
                        Assert.That(SceneTransition.Opacity, Is.EqualTo(.5 + Palette.DialogDim.a / 510.0).Within(.001));
                        SceneTransition.Advance(.09001);
                        Assert.That(shell.Dialog.DimOpacity, Is.EqualTo(Palette.DialogDim.a / 255f).Within(.001));
                    }
                    else
                    {
                        SceneTransition.Advance(.3);
                        Assert.That(shell.State, Is.EqualTo(AdShellState.Leaving));
                        Assert.That(shell.Fragments, Is.EqualTo(3));
                        yield return FinishTransition(OfftrackScenes.CutsceneC);
                    }
                }
                finally { SceneTransition.AutomaticTick = true; }
            }
        }

        [UnityTest]
        public IEnumerator LotteryGuideAndStartShareOneInkLayerAndDoNotAdvanceJudgeTime()
        {
            yield return SceneManager.LoadSceneAsync(OfftrackScenes.Main);
            var shell = Object.FindAnyObjectByType<AdShell>();
            var flow = shell.Flow;
            shell.AutomaticTick = false;
            flow.AutomaticTick = false;
            flow.SetApplicationFocus(true);
            var rules = flow.Config.Shell;
            SceneTransition.AutomaticTick = false;
            try
            {
                shell.Advance(rules.BootDuration);
                shell.Advance(rules.DialogEntranceDuration);
                var beforeLottery = CaptureAudio();
                shell.ActivatePrimary();
                AssertSilent(beforeLottery);
                SceneTransition.Advance(rules.DialogExitDuration / 2);
                AssertSilent(beforeLottery);
                Assert.That(SceneTransition.Opacity, Is.EqualTo(.5f).Within(.001f));
                Assert.That(shell.State, Is.EqualTo(AdShellState.Lottery));
                SceneTransition.Advance(rules.DialogExitDuration / 2);
                Assert.That(SceneTransition.Opacity, Is.EqualTo(1));
                Assert.That(shell.State, Is.EqualTo(AdShellState.Guide));
                Assert.That(shell.Dialog.DimOpacity, Is.Zero);
                Assert.That(Object.FindAnyObjectByType<HudView>().MeterVisible, Is.False);
                float dim = Palette.DialogDim.a / 255f;
                shell.Advance(rules.DialogEntranceDuration / 2);
                SceneTransition.Advance(rules.DialogEntranceDuration / 2);
                Assert.That(SceneTransition.Opacity, Is.EqualTo((1 + dim) / 2).Within(.001f));
                shell.Advance(rules.DialogEntranceDuration / 2);
                SceneTransition.Advance(rules.DialogEntranceDuration / 2);
                Assert.That(SceneTransition.BlocksInput, Is.False);
                Assert.That(shell.Dialog.DimOpacity, Is.EqualTo(dim).Within(.001f));
                AssertSilent(beforeLottery);
                shell.Advance(.11);
                shell.ActivatePrimary();
                Assert.That(SceneTransition.BlocksInput, Is.False, "The second press at 290 ms is rejected.");
                shell.Advance(.02);
                shell.ActivatePrimary();
                AssertSilent(beforeLottery);
                SceneTransition.Advance(rules.DialogExitDuration);
                Assert.That(SceneTransition.Opacity, Is.EqualTo(1));
                flow.Advance(1);
                Assert.That(flow.Simulation.Time, Is.Zero);
                Assert.That(flow.Phase, Is.EqualTo(MachinePhase.Parked));
                SceneTransition.Advance(rules.DialogExitDuration);
                Assert.That(flow.Phase, Is.EqualTo(MachinePhase.Starting));
                Assert.That(flow.Simulation.Time, Is.Zero);
            }
            finally { SceneTransition.AutomaticTick = true; }
        }

        private static (int Effects, AudioSource[] Playing) CaptureAudio() =>
            (Object.FindAnyObjectByType<GameAudio>().EffectsPlayed,
                Object.FindObjectsByType<AudioSource>().Where(source => source.isPlaying).ToArray());

        private static void AssertSilent((int Effects, AudioSource[] Playing) before)
        {
            var after = CaptureAudio();
            Assert.That(after.Effects, Is.EqualTo(before.Effects));
            Assert.That(after.Playing.Except(before.Playing), Is.Empty);
        }

        internal static IEnumerator FinishTransition(string expected)
        {
            bool original = SceneTransition.AutomaticTick;
            SceneTransition.AutomaticTick = false;
            try
            {
                double deadline = Time.realtimeSinceStartupAsDouble + 10;
                while (SceneTransition.BlocksInput && Time.realtimeSinceStartupAsDouble < deadline)
                {
                    SceneTransition.Advance(.4);
                    yield return null;
                }
                Assert.That(SceneTransition.BlocksInput, Is.False);
                Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(expected));
                Assert.That(AudioListener.volume, Is.EqualTo(1));
            }
            finally { SceneTransition.AutomaticTick = original; }
        }

        [UnityTest]
        public IEnumerator StartAndRetryHideHudUntilFourEntranceStepsAndRejectSkipDuringTransition()
        {
            yield return SceneManager.LoadSceneAsync(OfftrackScenes.Main);
            var shell = Object.FindAnyObjectByType<AdShell>();
            var flow = shell.Flow;
            var hud = flow.GetComponent<HudView>();
            var root = Object.FindAnyObjectByType<Canvas>().transform.Find("ReferenceFrame");
            var life = root.Find("Life_0").GetComponent<FlatShape>();
            var remaining = root.Find("TimerRemaining").GetComponent<Text>();
            var laneDot = root.GetComponentsInChildren<FlatShape>(true).Single(shape => shape.name == "LaneDot_" + flow.Config.StartLane);
            shell.AutomaticTick = flow.AutomaticTick = false;
            flow.SetApplicationFocus(true);
            SceneTransition.AutomaticTick = false;
            try
            {
                shell.Advance(flow.Config.Shell.BootDuration);
                Assert.That(shell.AcceptLottery(), Is.True);
                shell.Advance(flow.Config.Shell.SkipCountdownDuration);
                Assert.That(shell.Machine.CanSkip, Is.True);
                Assert.That(hud.MeterVisible, Is.False);
                Assert.That(remaining.gameObject.activeInHierarchy || life.gameObject.activeInHierarchy, Is.False);
                foreach (var failureRetry in new[] { false, true })
                {
                    shell.ActivatePrimary();
                    SceneTransition.Advance(.1);
                    Assert.That(SceneTransition.Opacity, Is.EqualTo(.5).Within(.001));
                    Assert.That(shell.RequestSkip(), Is.False);
                    SceneTransition.Advance(.100001);
                    Assert.That(SceneTransition.Opacity, Is.EqualTo(1));
                    shell.Advance(.31);
                    var stateBeforeSkip = shell.State;
                    var skipPoint = RectTransformUtility.WorldToScreenPoint(Camera.main, shell.SkipButton.position);
                    Assert.That(shell.HandlePointer(skipPoint), Is.True);
                    Assert.That(shell.State, Is.EqualTo(stateBeforeSkip));
                    Assert.That(shell.RequestSkip(), Is.False, "Skip stays blocked in black after debounce.");
                    AssertSkipPalette(shell, false);
                    Assert.That(hud.MeterVisible, Is.False);
                    Assert.That(remaining.gameObject.activeInHierarchy || life.gameObject.activeInHierarchy, Is.False);
                    SceneTransition.Advance(.1);
                    Assert.That(SceneTransition.Opacity, Is.EqualTo(.5).Within(.001));
                    Assert.That(hud.MeterVisible, Is.False);
                    SceneTransition.Advance(.101);
                    Assert.That(flow.Phase, Is.EqualTo(MachinePhase.Starting));
                    shell.Advance(.4);
                    Assert.That(shell.RequestSkip(), Is.False);
                    AssertSkipPalette(shell, false);
                    flow.Advance(.05);
                    var inactiveDot = root.GetComponentsInChildren<FlatShape>(true).First(shape => shape.name.StartsWith("LaneDot_") && shape != laneDot);
                    var activeSteps = new[] { new Color32(45, 40, 38, 255), new Color32(103, 96, 90, 255), new Color32(148, 141, 131, 255), new Color32(240, 230, 214, 255) };
                    var dim = new Color32(63, 27, 27, 255);
                    var inactiveSteps = new[] { new Color32(30, 15, 15, 255), dim, dim, dim };
                    for (int stage = 0; stage < activeSteps.Length; stage++)
                    {
                        var color = activeSteps[stage];
                        Assert.That(hud.MeterVisible, Is.True);
                        Assert.That((Color32)inactiveDot.color, Is.EqualTo(inactiveSteps[stage]));
                        Assert.That((Color32)life.color, Is.EqualTo(color));
                        Assert.That((Color32)laneDot.color, Is.EqualTo(color));
                        Assert.That((Color32)remaining.color, Is.EqualTo(color));
                        Assert.That(flow.Simulation.Time, Is.Zero);
                        flow.Advance(.1);
                    }
                    Assert.That(flow.Phase, Is.EqualTo(MachinePhase.Running));
                    shell.Advance(0);
                    AssertSkipPalette(shell, true);
                    if (failureRetry) break;
                    for (int frame = 0; frame < 1800 && flow.State == RunState.Playing; frame++) flow.Advance(ChartBot.Step);
                    Assert.That(flow.Phase, Is.EqualTo(MachinePhase.Stopping));
                    Assert.That(shell.RequestSkip(), Is.False);
                    flow.Advance(flow.StopDuration + .001);
                    shell.Advance(.3);
                    Assert.That(shell.State, Is.EqualTo(AdShellState.FailDialog));
                    Assert.That(hud.MeterVisible, Is.True);
                    var frozen = remaining.text;
                    double judge = flow.Simulation.Time;
                    shell.Advance(1.1);
                    flow.Advance(1.1);
                    Assert.That(flow.Simulation.Time, Is.EqualTo(judge));
                    Assert.That(remaining.text, Is.EqualTo(frozen));
                    Assert.That(life.gameObject.activeInHierarchy, Is.True);
                }
            }
            finally { SceneTransition.AutomaticTick = true; }
        }

        [UnityTest]
        public IEnumerator ActiveStayFadesForTwoTenthsWithoutBlackAndFreezesJudge()
        {
            yield return SceneManager.LoadSceneAsync(OfftrackScenes.Main);
            var shell = Object.FindAnyObjectByType<AdShell>();
            var flow = shell.Flow;
            flow.AutomaticTick = shell.AutomaticTick = false;
            flow.SetApplicationFocus(true);
            shell.Advance(flow.Config.Shell.BootDuration + flow.Config.Shell.SkipCountdownDuration);
            shell.Advance(flow.Config.Shell.SkipCountdownDuration);
            Assert.That(shell.RequestSkip(), Is.True);
            shell.Advance(.2);
            shell.ActivatePrimary();
            Assert.That(shell.State, Is.EqualTo(AdShellState.Lottery));
            Assert.That(shell.Dialog.DimOpacity, Is.EqualTo(Palette.DialogDim.a / 255f).Within(.001));
            Assert.That(SceneTransition.BlocksInput, Is.False);
            Assert.That(shell.AcceptLottery(), Is.True);
            ChartBot.StartImmediately(flow);
            shell.Advance(.31);
            flow.Advance(.27);
            double judge = flow.Simulation.Time;
            Assert.That(shell.RequestSkip(), Is.True);
            shell.Advance(.31);
            var beforeStay = CaptureAudio();
            shell.ActivatePrimary();
            shell.Advance(.1);
            AssertSilent(beforeStay);
            Assert.That(SceneTransition.Opacity, Is.Zero);
            Assert.That(shell.Dialog.DimOpacity, Is.EqualTo(Palette.DialogDim.a / 510f).Within(.001));
            Assert.That(shell.State, Is.EqualTo(AdShellState.ConfirmSkip));
            flow.Advance(.1);
            Assert.That(flow.Simulation.Time, Is.EqualTo(judge));
            shell.Advance(.09);
            Assert.That(shell.State, Is.EqualTo(AdShellState.ConfirmSkip));
            Assert.That(shell.Dialog.DimOpacity, Is.EqualTo(179 / 255f * .05f).Within(.002));
            shell.Advance(.010001);
            Assert.That(shell.State, Is.EqualTo(AdShellState.Playing));
            Assert.That(shell.Machine.ResumeDelayRemaining, Is.EqualTo(flow.Config.Shell.StayResumeDelay - .2).Within(.001));
            Assert.That(shell.Dialog.DimOpacity, Is.Zero);
        }

        private static void AssertSkipPalette(AdShell shell, bool ready)
        {
            Assert.That((Color32)shell.SkipButton.GetComponent<FlatShape>().color,
                Is.EqualTo(ready ? new Color32(236, 65, 65, 255) : new Color32(30, 15, 15, 255)));
            Assert.That((Color32)shell.SkipButton.GetComponentInChildren<Text>().color,
                Is.EqualTo(ready ? new Color32(240, 230, 214, 255) : new Color32(103, 96, 90, 255)));
        }

        [UnityTest]
        public IEnumerator DialogEntersWithoutScaleAndFlashesSilentlyForEightyMilliseconds()
        {
            yield return SceneManager.LoadSceneAsync(OfftrackScenes.Main);
            var shell = Object.FindAnyObjectByType<AdShell>();
            shell.AutomaticTick = shell.Flow.AutomaticTick = false;
            var audio = Object.FindAnyObjectByType<GameAudio>();
            int effects = audio.EffectsPlayed;
            var dialog = shell.Dialog;
            var card = (RectTransform)dialog.transform;
            var initialScale = card.localScale;
            shell.Advance(shell.Flow.Config.Shell.BootDuration);
            dialog.Show(AdShellState.Lottery, 1, null);
            var group = dialog.GetComponent<CanvasGroup>();
            float start = card.anchoredPosition.y;
            Assert.That(group.alpha, Is.Zero);
            dialog.Advance(.09f);
            Assert.That(group.alpha, Is.EqualTo(.875f).Within(.001));
            Assert.That(card.anchoredPosition.y - start, Is.EqualTo(10.5f).Within(.01));
            Assert.That(dialog.CanPress, Is.False);
            dialog.Advance(.09f);
            Assert.That(card.anchoredPosition.y - start, Is.EqualTo(12).Within(.01));
            Assert.That(group.alpha, Is.EqualTo(1));
            Assert.That(card.localScale, Is.EqualTo(initialScale));
            Assert.That(dialog.CanPress, Is.True);
            var fill = dialog.PrimaryButton.GetComponentInChildren<FlatShape>();
            Color restingColor = fill.color;
            var buttonSize = dialog.PrimaryButton.sizeDelta;
            var buttonScale = dialog.PrimaryButton.localScale;
            dialog.Press(dialog.PrimaryButton);
            dialog.Advance(.079f);
            Assert.That((Color32)fill.color, Is.EqualTo(new Color32(247, 102, 102, 255)));
            float Luminance(Color color) => Vector3.Dot(new Vector3(color.linear.r, color.linear.g, color.linear.b), new Vector3(.2126f, .7152f, .0722f));
            Assert.That(Luminance(fill.color), Is.GreaterThan(Luminance(restingColor)));
            Assert.That(dialog.PrimaryButton.sizeDelta, Is.EqualTo(buttonSize));
            Assert.That(dialog.PrimaryButton.localScale, Is.EqualTo(buttonScale));
            dialog.Advance(.0011f);
            Assert.That(fill.color, Is.EqualTo(restingColor));
            dialog.Show(AdShellState.ConfirmSkip, 1, null);
            dialog.Advance(.18f);
            var secondaryFill = dialog.SecondaryButton.GetComponentInChildren<FlatShape>();
            dialog.Press(dialog.SecondaryButton);
            dialog.Advance(.079f);
            Assert.That((Color32)secondaryFill.color, Is.EqualTo(new Color32(45, 16, 16, 255)));
            dialog.Advance(.0011f);
            Assert.That((Color32)secondaryFill.color, Is.EqualTo(new Color32(11, 7, 7, 255)));
            Assert.That(audio.EffectsPlayed, Is.EqualTo(effects));
            Assert.That(audio.GetComponentsInChildren<AudioSource>().Any(source => source.isPlaying), Is.False);
        }

        [UnityTest]
        public IEnumerator FailureAbandonDebouncesTheReplacementCardAndMovesOnToTheNextLevel()
        {
            yield return SceneManager.LoadSceneAsync(OfftrackScenes.Main);
            var shell = Object.FindAnyObjectByType<AdShell>();
            var flow = shell.Flow;
            flow.AutomaticTick = shell.AutomaticTick = false;
            flow.SetApplicationFocus(true);
            shell.Advance(flow.Config.Shell.BootDuration);
            Assert.That(shell.AcceptLottery(), Is.True);
            ChartBot.StartImmediately(flow);
            for (int frame = 0; frame < 1800 && flow.State == RunState.Playing; frame++) flow.Advance(ChartBot.Step);
            flow.Advance(flow.StopDuration + .01);
            shell.Advance(.31);
            Assert.That(shell.State, Is.EqualTo(AdShellState.FailDialog));
            float dim = Palette.DialogDim.a / 255f;
            Assert.That(shell.Dialog.DimOpacity, Is.EqualTo(dim).Within(.001));
            Vector2 AlternatePoint() => RectTransformUtility.WorldToScreenPoint(Camera.main,
                shell.Dialog.SecondaryButton.TransformPoint(shell.Dialog.SecondaryButton.rect.center));
            shell.HandlePointer(AlternatePoint());
            Assert.That(shell.State, Is.EqualTo(AdShellState.ConfirmSkip));
            Assert.That(shell.Dialog.DimOpacity, Is.EqualTo(dim).Within(.001));
            var group = shell.Dialog.GetComponent<CanvasGroup>();
            Assert.That(group.alpha, Is.Zero);
            shell.Advance(.09);
            Assert.That(group.alpha, Is.EqualTo(.875).Within(.001));
            shell.Advance(.16);
            Assert.That(group.alpha, Is.EqualTo(1));
            shell.HandlePointer(AlternatePoint());
            Assert.That(SceneTransition.BlocksInput, Is.False);
            shell.Advance(.06);
            shell.ActivatePrimary();
            Assert.That(shell.State, Is.EqualTo(AdShellState.FailDialog));
            Assert.That(shell.Dialog.DimOpacity, Is.EqualTo(dim).Within(.001));
            shell.Advance(.31);
            shell.HandlePointer(AlternatePoint());
            shell.Advance(.31);
            SceneTransition.AutomaticTick = false;
            try
            {
                shell.HandlePointer(AlternatePoint());
                Assert.That(SceneTransition.BlocksInput, Is.True);
                Assert.That(shell.State, Is.EqualTo(AdShellState.ConfirmSkip));
                SceneTransition.Advance(flow.Config.Shell.DialogExitDuration + .0001);
                Assert.That(shell.State, Is.EqualTo(AdShellState.Guide), "Giving up after a failure moves on to the next level.");
                Assert.That(shell.Level, Is.EqualTo(2));
                Assert.That(shell.Fragments, Is.Zero);
                SceneTransition.Advance(flow.Config.Shell.DialogEntranceDuration + .0001);
                Assert.That(SceneTransition.BlocksInput, Is.False);
                Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(OfftrackScenes.Main));
                yield return null;
            }
            finally { SceneTransition.AutomaticTick = true; }
        }

        [UnityTest]
        public IEnumerator ClosingStartsTheTitleHandoffInTheEndingFrameAt30And60Fps()
        {
            foreach (int fps in new[] { 30, 60 })
            {
                yield return SceneManager.LoadSceneAsync(OfftrackScenes.CutsceneB);
                var closing = Object.FindAnyObjectByType<CutsceneController>();
                closing.AutomaticTick = false;
                SceneTransition.AutomaticTick = false;
                try
                {
                    closing.Seek(closing.Duration - .001);
                    closing.Advance(.0011);
                    Assert.That(SceneTransition.BlocksInput, Is.True);
                    int frames = 0;
                    while (SceneManager.GetActiveScene().name != OfftrackScenes.Title && frames < fps / 2)
                    {
                        SceneTransition.Advance(1.0 / fps);
                        frames++;
                        yield return null;
                    }
                    Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(OfftrackScenes.Title), $"{fps} fps");
                    Assert.That(SceneTransition.Opacity, Is.GreaterThan(0));
                    yield return FinishTransition(OfftrackScenes.Title);
                }
                finally
                {
                    SceneTransition.AutomaticTick = true;
                    if (SceneTransition.BlocksInput) SceneTransition.Advance(.4);
                }
            }
        }


        [UnityTest]
        public IEnumerator TitleIgnoresOutsideClicksAndJourneyRequestsOneWindow()
        {
            int requests = 0;
#if UNITY_EDITOR
            StandaloneWindow.SetTestWindowBackend(() => false, (width, height) => requests++);
#endif
            yield return SceneManager.LoadSceneAsync(OfftrackScenes.Title);
            var title = Object.FindAnyObjectByType<TitleController>();
            Assert.That(title.HandlePointer(new Vector2(-500, -500)), Is.False);
            yield return null;
            Assert.That(SceneTransition.BlocksInput, Is.False);
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(OfftrackScenes.Title));
            Canvas.ForceUpdateCanvases();
            var start = GameObject.Find("StartButton").GetComponent<RectTransform>();
            Assert.That(title.HandlePointer(RectTransformUtility.WorldToScreenPoint(Camera.main, start.position)), Is.True);
            yield return FinishTransition(OfftrackScenes.CutsceneA);
            foreach (string destination in new[] { OfftrackScenes.Main, OfftrackScenes.CutsceneB, OfftrackScenes.Title })
            {
                Assert.That(SceneTransition.GoTo(destination, new TransitionRules()), Is.True);
                yield return FinishTransition(destination);
            }
            Assert.That(StandaloneWindow.RequestCount, Is.EqualTo(1));
#if UNITY_EDITOR
            Assert.That(requests, Is.EqualTo(1));
#endif
        }
    }
}
