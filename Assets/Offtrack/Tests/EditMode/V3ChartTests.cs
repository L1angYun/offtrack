using System;
using System.Linq;
using NUnit.Framework;
using Offtrack.Core;
using Offtrack.Runtime;
using Offtrack.Runtime.Presentation;
using UnityEditor;
using UnityEngine;

namespace Offtrack.Tests.EditMode
{
    public sealed class V3ChartTests
    {
        private GameConfig config;
        private RuleConfig rules;
        private RecordChart[] records;

        [OneTimeSetUp]
        public void Load()
        {
            config = AssetDatabase.LoadAssetAtPath<GameConfig>("Assets/Offtrack/Config/GameConfig.asset");
            rules = config.ToRules();
            records = config.Levels.Select(level => level.Load(rules)).ToArray();
            Assert.That(records.Length, Is.EqualTo(3));
        }

        [TestCase(0), TestCase(1), TestCase(2)]
        public void ContactsAndBoundariesOnEvenBeats(int index) => Assert.That(ChartFairness.EvenBeats(records[index]), Is.True);

        [TestCase(0), TestCase(1), TestCase(2)]
        public void QuietReversalsAndIsolatedIntroductions(int index) =>
            Assert.That(ChartFairness.ClearIntroductions(records[index], config.Levels[index].RunnerEnd), Is.True);

        [TestCase(0), TestCase(1), TestCase(2)]
        public void GapsAlwaysLeaveFooting(int index) => Assert.That(ChartFairness.Traversable(records[index], rules), Is.True);

        [TestCase(0), TestCase(1), TestCase(2)]
        public void EveryContactIsContinuouslyReadable(int index) =>
            Assert.That(ChartFairness.VisibleBeforeContacts(records[index], config.Levels[index].LeftBeats,
                config.Levels[index].RightBeats), Is.True);

        [TestCase(0), TestCase(1), TestCase(2)]
        public void FullDurabilityAcrossPlannerLeadRange(int index)
        {
            for (int milliseconds = 250; milliseconds <= 550; milliseconds++)
            {
                Assert.That(ChartFairness.PreservesDurability(records[index], rules, milliseconds / 1000.0),
                    Is.True, $"Lead {milliseconds} ms");
            }
        }

        [Test]
        public void NoInputFailsInFirstHalf()
        {
            for (int index = 0; index < records.Length; index++)
            {
                var record = records[index];
                Assert.That(ChartFairness.FailsBeforeHalfWithoutInput(record, rules, out double failure), Is.True, $"L{index + 1}");
                // The same judge stepped here without the planner: the reported moment is where an untouched run ends.
                var run = new GameSimulation(rules, record.Contacts);
                run.Start();
                while (run.State == RunState.Playing) run.Step(1.0 / 60);
                Assert.That(run.Result.Won, Is.False, $"L{index + 1}");
                Assert.That(run.Result.Lives, Is.Zero, $"L{index + 1}");
                Assert.That(failure, Is.EqualTo(run.Result.Time).Within(1e-9), $"L{index + 1}");
                Assert.That(failure, Is.LessThan(15), $"L{index + 1}");
                TestContext.WriteLine($"No input: {failure:F6} s");
            }
        }

        [TestCase(0), TestCase(1), TestCase(2)]
        public void ReportForcedJumpsBySection(int index)
        {
            var counts = ChartFairness.ForcedJumps(records[index], rules, .4, 16);
            var run = ChartPlanner.Simulate(rules, records[index].Contacts, .4, out var presses);
            Assert.That(counts, Has.Length.EqualTo(4));
            Assert.That(counts.Sum(), Is.EqualTo(run.Jumps).And.EqualTo(presses.Length));
            Assert.That(counts.Sum(), Is.GreaterThan(0));
            TestContext.WriteLine(string.Join(" / ", counts));
            int switches = ChartFairness.MinimumTrackSwitches(records[index], rules);
            TestContext.WriteLine($"Minimum switches: {switches}");
            if (index > 0) Assert.That(switches, Is.GreaterThanOrEqualTo(10));
        }

        [TestCase(0), TestCase(1), TestCase(2)]
        public void ReversalsThreatenAllLanesAndForceAJumpAtEveryLead(int index)
        {
            var record = records[index];
            Assert.That(ChartFairness.CoversEveryTrackAfterReversals(record, rules.LaneCount), Is.True);
            foreach (double reverse in record.Reversals)
                Assert.That(ChartFairness.ReturnedContacts(record, reverse), Is.GreaterThanOrEqualTo(2));
            foreach (double lead in new[] { .25, .4, .55 })
            {
                var counts = ChartFairness.ForcedReturnJumps(record, rules, lead);
                Assert.That(counts, Has.Length.EqualTo(index == 2 ? 3 : 0));
                Assert.That(counts.All(count => count > 0), Is.True, $"{lead}s: {string.Join(" / ", counts)}");
            }
        }

        // The rejected scores below each break one clause of one check and nothing else: they pass as soon as that
        // clause is taken out, so no clause can turn into a tautology unnoticed. The accepted score next to each
        // one is its nearest legal neighbour; where a margin has to be pinned from both sides the pair sits one
        // beat apart, off the even-beat grid.

        [Test]
        public void EvenBeatsRejectsEachOddBoundary()
        {
            Assert.That(ChartFairness.EvenBeats(Score(new[] { Scratch(4, 0) }, 16)), Is.True);
            // Mirrored about beat 17 the scratch still lands on beats 4 and 30: only the reversal itself is odd.
            Assert.That(ChartFairness.EvenBeats(Score(new[] { Scratch(4, 0) }, 17)), Is.False, "odd reversal");
            Assert.That(ChartFairness.EvenBeats(Score(new[] { Gap(10, 12, 0) })), Is.True);
            Assert.That(ChartFairness.EvenBeats(Score(new[] { Gap(9, 12, 0) })), Is.False, "odd break start");
            Assert.That(ChartFairness.EvenBeats(Score(new[] { Gap(10, 11, 0) })), Is.False, "odd break end");
            Assert.That(ChartFairness.EvenBeats(Score(new[] { Scratch(5, 0) })), Is.False, "odd scratch");
        }

        [Test]
        public void ClearIntroductionsRejectsEachCrowdedIntroduction()
        {
            Assert.That(ChartFairness.ClearIntroductions(Score(new[] { Scratch(14, 1) }, 16), 2), Is.True);
            Assert.That(ChartFairness.ClearIntroductions(Score(new[] { Scratch(15, 1) }, 16), 2), Is.False, "contact one beat from a reversal");

            var runner = Score(new[] { Runner(20, 1, 0) });
            Assert.That(ChartFairness.ClearIntroductions(runner, 2), Is.True);
            Assert.That(ChartFairness.ClearIntroductions(runner, 1.5), Is.False, "runner still sliding 1.5 beats before contact");

            Assert.That(ChartFairness.ClearIntroductions(Score(new[] { Scratch(7, 1), Gap(10, 12, 0), Scratch(15, 1) }), 2), Is.True);
            Assert.That(ChartFairness.ClearIntroductions(Score(new[] { Gap(10, 12, 0), Scratch(14, 1) }), 2), Is.False,
                "hazard two beats after the first break");
            Assert.That(ChartFairness.ClearIntroductions(Score(new[] { Scratch(8, 1), Gap(10, 12, 0) }), 2), Is.False,
                "hazard two beats before the first break");

            Assert.That(ChartFairness.ClearIntroductions(Score(new[] { Scratch(17, 2), Runner(20, 1, 0), Scratch(23, 2) }), 2), Is.True);
            Assert.That(ChartFairness.ClearIntroductions(Score(new[] { Runner(20, 1, 0), Scratch(22, 2) }), 2), Is.False,
                "hazard two beats after the first runner");
            Assert.That(ChartFairness.ClearIntroductions(Score(new[] { Scratch(18, 2), Runner(20, 1, 0) }), 2), Is.False,
                "hazard two beats before the first runner");

            // Only the first of each kind is an introduction: later ones may sit two beats from their neighbours.
            Assert.That(ChartFairness.ClearIntroductions(Score(new[] { Gap(10, 12, 0), Runner(20, 1, 0), Gap(30, 32, 1), Scratch(34, 2),
                Runner(40, 1, 2), Scratch(42, 0) }), 2), Is.True);
        }

        [Test]
        public void TraversableRejectsEachBlockedPassage()
        {
            Assert.That(ChartFairness.Traversable(Score(new[] { Scratch(8, 0), Scratch(8, 1) }), rules), Is.True);
            Assert.That(ChartFairness.Traversable(Score(new[] { Scratch(8, 0), Scratch(8, 1), Scratch(8, 2) }), rules), Is.False,
                "all three lanes threatened at once");

            Assert.That(ChartFairness.Traversable(Score(new[] { Gap(10, 12, 0) }), rules), Is.True);
            Assert.That(ChartFairness.Traversable(Score(new[] { Gap(10, 14, 0) }), rules), Is.True);
            Assert.That(ChartFairness.Traversable(Score(new[] { Gap(10, 11, 0) }), rules), Is.False, "break shorter than two beats");
            Assert.That(ChartFairness.Traversable(Score(new[] { Gap(10, 15, 0) }), rules), Is.False, "break longer than four beats");

            Assert.That(ChartFairness.Traversable(Score(new[] { Scratch(8, 2), Gap(10, 12, 0), Scratch(14, 2) }), rules), Is.True);
            Assert.That(ChartFairness.Traversable(Score(new[] { Gap(10, 12, 0), Scratch(13, 2) }), rules), Is.False,
                "hazard one beat after a break");
            Assert.That(ChartFairness.Traversable(Score(new[] { Scratch(9, 2), Gap(10, 12, 0) }), rules), Is.False,
                "hazard one beat before a break");
            Assert.That(ChartFairness.Traversable(Score(new[] { Gap(10, 12, 0), Gap(10, 12, 1) }), rules), Is.False, "two breaks side by side");

            // With the saved windows a two-beat margin always keeps judgements apart, so the two clauses that compare
            // judgement times need windows wider than a beat before they are the only ones to object.
            var wide = config.ToRules();
            wide.EarlyWindow = wide.LateWindow = .5;
            var breaks = Score(new[] { Gap(6, 8, 1), Gap(10, 12, 0) });
            Assert.That(ChartFairness.Traversable(breaks, rules), Is.True);
            Assert.That(ChartFairness.Traversable(breaks, wide), Is.False, "two lanes broken during the same judgement");
            var sameLane = Score(new[] { Gap(10, 12, 0), Scratch(14, 0) });
            Assert.That(ChartFairness.Traversable(sameLane, rules), Is.True);
            Assert.That(ChartFairness.Traversable(sameLane, wide), Is.False, "scratch judged on a lane that is still broken");
        }

        [Test]
        public void VisibleBeforeContactsRejectsEachUnreadableApproach()
        {
            Assert.That(ChartFairness.VisibleBeforeContacts(Score(new[] { Scratch(4, 1) }), 12, .768), Is.True);
            Assert.That(ChartFairness.VisibleBeforeContacts(Score(new[] { Scratch(3, 1) }), 12, .768), Is.False,
                "contact three beats after the needle drops");
            Assert.That(ChartFairness.VisibleBeforeContacts(Score(new[] { Scratch(4, 1) }), 4, 6), Is.False,
                "window that opens exactly four beats ahead");

            // Phase 8 is back at beat 24 and readable from beat 20 on, but at the reversal it sits eight beats past
            // the contact point: outside a six-beat right side.
            var returning = Score(new[] { Scratch(8, 0) }, 16);
            Assert.That(ChartFairness.VisibleBeforeContacts(returning, 10, 8), Is.True);
            Assert.That(ChartFairness.VisibleBeforeContacts(returning, 10, 6), Is.False, "returning scratch off screen at the reversal");

            // First pass at beat 15. The record backs away from it between beats 10 and 12, so it is two beats out at
            // beat 11 and three beats out at the second reversal, inside its last four beats.
            var receding = Score(new[] { Scratch(11, 0) }, 10, 12);
            Assert.That(ChartFairness.VisibleBeforeContacts(receding, 3.5, 6), Is.True);
            Assert.That(ChartFairness.VisibleBeforeContacts(receding, 2.5, 6), Is.False, "scratch leaves the window at a reversal");
        }

        [Test]
        public void PlannerChecksRejectDamageAndLateFailure()
        {
            // One unavoidable hit still wins the run: only the durability clause can object.
            var blocked = Score(new[] { Scratch(8, 0), Scratch(8, 1), Scratch(8, 2) });
            var run = ChartPlanner.Simulate(rules, blocked.Contacts, .4, out _);
            Assert.That(run.Won, Is.True);
            Assert.That(run.DamageCount, Is.EqualTo(1));
            Assert.That(ChartFairness.PreservesDurability(blocked, rules, .4), Is.False);

            // Both neighbours of the start lane are still being judged when an early jump lands: the planner clears
            // this one at short leads only, so the lead it is given has to reach the run.
            var crowded = Score(new[] { Scratch(7.2, 0), Scratch(7.2, 2), Scratch(8, 1) });
            Assert.That(ChartFairness.PreservesDurability(crowded, rules, .25), Is.True);
            Assert.That(ChartFairness.PreservesDurability(crowded, rules, .4), Is.True);
            Assert.That(ChartFairness.PreservesDurability(crowded, rules, .55), Is.False, "early jump lands inside a judgement");

            var safe = Score(new[] { Scratch(4, 0), Scratch(6, 0) });
            Assert.That(ChartFairness.PreservesDurability(safe, rules, .4), Is.True);
            Assert.That(ChartFairness.FailsBeforeHalfWithoutInput(safe, rules, out _), Is.False, "start lane never threatened");
            Assert.That(ChartFairness.ForcedJumps(safe, rules, .4, 16), Is.EqualTo(new[] { 0, 0, 0, 0 }));
            // Away from beat 4 towards lane 0, whose next scratch is the later one; off it for beat 32; off the
            // start lane again for beat 52. The second press falls before beat 32 and still counts for the section
            // its contact opens.
            var sections = Score(new[] { Scratch(4, 1), Scratch(30, 2), Scratch(32, 0), Scratch(52, 1) });
            Assert.That(ChartFairness.ForcedJumps(sections, rules, .4, 16), Is.EqualTo(new[] { 1, 0, 1, 1 }));
            ChartPlanner.Simulate(rules, sections.Contacts, .4, out var presses);
            Assert.That(presses.Select(press => sections.Contacts[press.ContactIndex].Time), Is.EqualTo(new[] { 1.875, 15, 24.375 }));
            Assert.That(presses[1].Frame * ChartPlanner.Step, Is.InRange(14.5, 14.7));

            // Four hits on the start lane lose the run either way; only the moment differs.
            var early = Score(new[] { Scratch(4, 1), Scratch(6, 1), Scratch(8, 1), Scratch(10, 1) });
            Assert.That(ChartFairness.FailsBeforeHalfWithoutInput(early, rules, out double moment), Is.True);
            Assert.That(moment, Is.EqualTo(10 * .46875).Within(.115 + 1.0 / 60));
            var late = Score(new[] { Scratch(34, 1), Scratch(36, 1), Scratch(38, 1), Scratch(40, 1) });
            Assert.That(ChartFairness.FailsBeforeHalfWithoutInput(late, rules, out moment), Is.False, "run lost in its second half");
            Assert.That(moment, Is.EqualTo(40 * .46875).Within(.145 + 1.0 / 60));
        }

        [Test]
        public void MinimumTrackSwitchesCountsOnlyForcedMoves()
        {
            Assert.That(ChartFairness.MinimumTrackSwitches(Score(new[] { Scratch(4, 0), Scratch(6, 2) }), rules), Is.Zero);
            Assert.That(ChartFairness.MinimumTrackSwitches(Score(new[] { Scratch(4, 1) }), rules), Is.EqualTo(1));
            Assert.That(ChartFairness.MinimumTrackSwitches(Score(new[] { Gap(10, 14, 1) }), rules), Is.EqualTo(1));
            Assert.That(ChartFairness.MinimumTrackSwitches(Score(new[] { Scratch(4, 1), Scratch(6, 0), Scratch(6, 2) }), rules), Is.EqualTo(2));
            Assert.That(ChartFairness.MinimumTrackSwitches(Score(new[] { Scratch(4, 1), Scratch(6, 0), Scratch(6, 2), Scratch(8, 1),
                Scratch(10, 0), Scratch(10, 2) }), rules), Is.EqualTo(4));
            Assert.That(ChartFairness.MinimumTrackSwitches(Score(new[] { Scratch(8, 0), Scratch(8, 1), Scratch(8, 2) }), rules), Is.EqualTo(-1));
        }

        [Test]
        public void ReversalChecksRejectUncoveredLanesAndIgnoreFirstPasses()
        {
            // Returns at beats 20, 22 and 24: the last one closes the eight-beat window on the third lane.
            Assert.That(ChartFairness.CoversEveryTrackAfterReversals(Score(new[] { Scratch(8, 2), Scratch(10, 0), Scratch(12, 1) }, 16),
                rules.LaneCount), Is.True);
            Assert.That(ChartFairness.CoversEveryTrackAfterReversals(Score(new[] { Scratch(10, 0), Scratch(12, 1) }, 16), rules.LaneCount),
                Is.False, "only two lanes come back");
            Assert.That(ChartFairness.CoversEveryTrackAfterReversals(Score(new[] { Scratch(6, 2), Scratch(10, 0), Scratch(12, 1) }, 16),
                rules.LaneCount), Is.False, "third lane comes back ten beats after the reversal");
            var noReturns = Score(new[] { Scratch(4, 0) }, 16);
            Assert.That(ChartFairness.CoversEveryTrackAfterReversals(noReturns, rules.LaneCount), Is.False, "nothing comes back in time");
            Assert.That(ChartFairness.ReturnedContacts(noReturns, 16), Is.Zero);
            Assert.That(ChartFairness.ForcedReturnJumps(noReturns, rules, .4), Is.EqualTo(new[] { 0 }));

            // Phase 14 is met at beats 14, 18 and 22; phase 18 is first met at beat 26. Each window holds one contact
            // that mirrors its own reversal and one that does not: 22 mirrors 18 about beat 20, not about beat 16.
            var mixed = Score(new[] { Scratch(14, 0), Scratch(18, 1) }, 16, 20);
            Assert.That(ChartFairness.ReturnedContacts(mixed, 16), Is.EqualTo(1));
            Assert.That(ChartFairness.ReturnedContacts(mixed, 20), Is.EqualTo(1));

            var safeReturns = Score(new[] { Scratch(8, 0), Scratch(10, 0), Scratch(12, 0), Scratch(14, 0) }, 16);
            Assert.That(ChartFairness.ReturnedContacts(safeReturns, 16), Is.EqualTo(4));
            Assert.That(ChartFairness.ForcedReturnJumps(safeReturns, rules, .4), Is.EqualTo(new[] { 0 }));

            // The planner waits on the start lane and presses once, inside the window of the second reversal, for
            // a scratch it has never met.
            var firstPass = Score(new[] { Scratch(18, 1) }, 16, 20);
            Assert.That(ChartFairness.ForcedJumps(firstPass, rules, .4, 16), Is.EqualTo(new[] { 0, 1, 0, 0 }));
            Assert.That(ChartFairness.ForcedReturnJumps(firstPass, rules, .4), Is.EqualTo(new[] { 0, 0 }));
            // Here it is driven off the start lane at beat 12 onto lane 0, where phase 10 returns at beat 22.
            var forced = Score(new[] { Scratch(10, 0), Scratch(12, 1), Scratch(14, 2) }, 16);
            Assert.That(ChartFairness.ForcedJumps(forced, rules, .4, 16), Is.EqualTo(new[] { 1, 1, 0, 0 }));
            Assert.That(ChartFairness.ForcedReturnJumps(forced, rules, .4), Is.EqualTo(new[] { 1 }));
            // The same escape onto lane 0, but its scratch is phase 6: it mirrors the reversal at beat 26, two beats
            // after the window has closed, so the jump it forces is not credited to the reversal.
            var overdue = Score(new[] { Scratch(6, 0), Scratch(10, 1), Scratch(14, 2) }, 16);
            Assert.That(ChartFairness.ReturnedContacts(overdue, 16), Is.EqualTo(2));
            Assert.That(ChartFairness.ForcedJumps(overdue, rules, .4, 16), Is.EqualTo(new[] { 1, 1, 0, 0 }));
            Assert.That(ChartFairness.ForcedReturnJumps(overdue, rules, .4), Is.EqualTo(new[] { 0 }));
        }

        [TestCase(0), TestCase(1), TestCase(2)]
        public void GeneratedDataMatchesItsBeatsSource(int index)
        {
            var sourceAsset = AssetDatabase.LoadAssetAtPath<TextAsset>($"Assets/Offtrack/Charts/side-a.l{index + 1}.beats.json");
            var source = JsonUtility.FromJson<RecordChartSource>(sourceAsset.text);
            var generated = JsonUtility.FromJson<GeneratedChart>(config.Levels[index].Chart.text);
            Assert.That(JsonUtility.ToJson(generated.source), Is.EqualTo(JsonUtility.ToJson(source)));
        }

        [TestCase(0, 12, .768), TestCase(1, 12, .768), TestCase(2, 10, 6)]
        public void SavedVisibilityWindowsMatchTheLevelDesign(int index, double left, double right)
        {
            Assert.That(config.Levels[index].LeftBeats, Is.EqualTo(left).Within(1e-9));
            Assert.That(config.Levels[index].RightBeats, Is.EqualTo(right).Within(1e-9));
        }

        [Test]
        public void ForwardLevelsKeepTheWindowOfTheTimeBasedScratchPool()
        {
            Assert.That(config.BeatsPerRevolution * config.View.ScratchLookAheadRevolutions, Is.EqualTo(12).Within(1e-9));
            Assert.That(config.ScratchRetireDelay / (60 / config.Bpm), Is.EqualTo(.768).Within(1e-9));
            // The first level is still drawn by the time-based path: 12 beats and 0.768 beats in seconds.
            Assert.That(ScratchPool.LookAhead(config), Is.EqualTo(5.625).Within(1e-9));
            Assert.That(config.ScratchRetireDelay, Is.EqualTo(.36).Within(1e-9));
        }

        [Test]
        public void LevelTwoMixStaysInRangeAndItsCollapseIsDenserThanItsTightening()
        {
            var second = records[1];
            Assert.That(second.Hazards.Count(note => note.kind == HazardKind.Scratch), Is.InRange(12, 14));
            Assert.That(second.Hazards.Count(note => note.kind == HazardKind.RunningScratch), Is.InRange(5, 6));
            Assert.That(second.Hazards.Count(note => note.kind == HazardKind.Break), Is.InRange(4, 5));
            Assert.That(ContactsWithin(second, 48, 64), Is.GreaterThanOrEqualTo(ContactsWithin(second, 32, 48) + 2));
            foreach (double lead in new[] { .25, .4, .55 })
            foreach (bool byPress in new[] { false, true })
                Assert.That(ForcedWithin(second, lead, 48, 64, byPress), Is.GreaterThanOrEqualTo(ForcedWithin(second, lead, 32, 48, byPress) + 1),
                    $"{lead}s, {(byPress ? "by press" : "by contact")}");

            // The level before its collapse was rebuilt: seven contacts in either section, and the one extra jump was
            // for a scratch on the last beat of the run. Neither way of attributing jumps may call that denser.
            var previous = PreviousSecondLevel();
            Assert.That(ContactsWithin(previous, 32, 48), Is.EqualTo(7));
            Assert.That(ContactsWithin(previous, 48, 64), Is.EqualTo(7));
            Assert.That(ForcedWithin(previous, .4, 32, 48, false), Is.EqualTo(3));
            Assert.That(ForcedWithin(previous, .4, 48, 64, false), Is.EqualTo(3));
            Assert.That(ForcedWithin(previous, .4, 32, 48, true), Is.EqualTo(4));
            Assert.That(ForcedWithin(previous, .4, 48, 64, true), Is.EqualTo(3));
            // The report table files the jump for beat 64 under the last section, which is why the comparison above
            // counts by beat instead.
            Assert.That(ChartFairness.ForcedJumps(previous, rules, .4, 16), Is.EqualTo(new[] { 1, 2, 3, 4 }));
        }

        [Test]
        public void EveryJudgementClosesBeforeTheRunEnds()
        {
            // The run ends on beat 64 (30 s): a contact there is judged for the early half of its 0.145 s window only.
            foreach (var record in records) Assert.That(LastContactEnd(record), Is.LessThan(30 - .145));
            Assert.That(LastContactEnd(Score(new[] { Scratch(62, 1) })), Is.EqualTo(29.0625));
            Assert.That(LastContactEnd(Score(new[] { Scratch(64, 2) })), Is.EqualTo(30));
            Assert.That(LastContactEnd(Score(new[] { Scratch(4, 1), Gap(60, 64, 2) })), Is.EqualTo(30));
            Assert.That(LastContactEnd(PreviousSecondLevel()), Is.EqualTo(30));
        }

        private RecordChart Score(RecordHazard[] hazards, params double[] reversals) =>
            new RecordChart(new RecordChartSource { hazards = hazards, reversals = reversals }, rules);

        private static RecordHazard Scratch(double beat, int lane) => new RecordHazard { phase = beat, lane = lane };
        private static RecordHazard Runner(double beat, int from, int to) =>
            new RecordHazard { kind = HazardKind.RunningScratch, phase = beat, lane = from, targetLane = to };
        private static RecordHazard Gap(double beat, double end, int lane) =>
            new RecordHazard { kind = HazardKind.Break, phase = beat, endPhase = end, lane = lane };

        private RecordChart PreviousSecondLevel() => Score(new[]
        {
            Scratch(4, 1), Scratch(6, 1), Gap(10, 12, 1), Scratch(16, 1), Runner(20, 1, 0), Scratch(24, 2), Gap(26, 28, 0), Scratch(30, 1),
            Runner(32, 2, 1), Scratch(34, 0), Scratch(36, 2), Gap(38, 40, 1), Runner(42, 1, 0), Scratch(44, 2), Scratch(46, 0),
            Runner(48, 0, 1), Gap(50, 52, 2), Scratch(54, 1), Scratch(56, 0), Runner(58, 1, 2), Scratch(60, 1), Scratch(62, 0), Scratch(64, 2)
        });

        private static int ContactsWithin(RecordChart chart, double from, double to) =>
            chart.Contacts.Count(note => note.Time >= from * chart.BeatSeconds && note.Time < to * chart.BeatSeconds);

        // Planner presses inside beats [from, to), placed either at the press itself or at the contact it avoids.
        private int ForcedWithin(RecordChart chart, double lead, double from, double to, bool byPress)
        {
            ChartPlanner.Simulate(rules, chart.Contacts, lead, out var presses);
            return presses.Count(press =>
            {
                double time = byPress ? press.Frame * ChartPlanner.Step : chart.Contacts[press.ContactIndex].Time;
                return time >= from * chart.BeatSeconds && time < to * chart.BeatSeconds;
            });
        }

        private static double LastContactEnd(RecordChart chart) => chart.Contacts.Max(note => note.EndTime);

        [Test]
        public void LevelOneMatchesTheFixtureChartExactly()
        {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Offtrack/Charts/side-a.json");
            var baseline = ChartService.Load(asset, rules.LaneCount);
            Assert.That(records[0].Contacts.Select(note => (note.Time, note.Lane)),
                Is.EqualTo(baseline.Select(note => (note.Time, note.Lane))));
        }

        [Test]
        public void ReversedContactsComeBackInOppositeOrderWithSameIdentity()
        {
            var record = records[2];
            Assert.That(record.Hazards.Count, Is.InRange(14, 15));
            Assert.That(record.Contacts.Count, Is.InRange(22, 25));
            Assert.That(record.Reversals, Is.EqualTo(new[] { 16d, 24d, 56d }));
            foreach (double reverse in record.Reversals)
            {
                var before = record.Contacts.Where(note => Math.Abs(note.Time / record.BeatSeconds - (reverse - 4)) < 2.01).ToArray();
                var after = record.Contacts.Where(note => Math.Abs(note.Time / record.BeatSeconds - (reverse + 4)) < 2.01).ToArray();
                Assert.That(before.Length, Is.GreaterThanOrEqualTo(2));
                Assert.That(after.Select(note => note.HazardId), Is.EqualTo(before.Reverse().Select(note => note.HazardId)));
                Assert.That(record.LabelDirection(reverse - 2.01, 2), Is.Not.EqualTo(record.LabelDirection(reverse - 2, 2)));
                Assert.That(record.CrossedReversal(reverse - 1.01, reverse - 1, 1), Is.True);
                Assert.That(record.CrossedReversal(reverse - .01, reverse, 0), Is.True);
            }
        }

        [Test]
        public void RunnerOnlyJudgesItsDestinationAndBreakCanOnlyHitOnce()
        {
            var record = records[1];
            var gap = record.Contacts.First(note => note.EndTime > note.Time);
            var runner = record.Hazards.First(hazard => hazard.kind == HazardKind.RunningScratch);
            Assert.That(runner.RadialLane(3, 3, 2), Is.EqualTo(runner.lane));
            Assert.That(runner.RadialLane(2.75, 3, 2), Is.EqualTo(runner.lane + .15625 * (runner.targetLane - runner.lane)));
            Assert.That(runner.RadialLane(2.5, 3, 2), Is.EqualTo((runner.lane + runner.targetLane) / 2.0));
            Assert.That(runner.RadialLane(2, 3, 2), Is.EqualTo(runner.targetLane));
            Assert.That(record.Contacts.Single(note => record.Hazards[note.HazardId] == runner).Lane, Is.EqualTo(runner.targetLane));

            var run = new GameSimulation(rules, new[] { gap });
            DamageEvent damage = null;
            run.DamageTaken += hit => damage = hit;
            run.Start();
            run.Step(gap.Time);
            Assert.That(damage?.Kind, Is.EqualTo(JudgeRules.ClassifyLanding(gap.Time, -100, rules.EarlyLandingThreshold)));
            run.Step(gap.EndTime - gap.Time);
            Assert.That(run.DamageCount, Is.EqualTo(1));
            run.Start();
            run.Move(-1);
            run.Step(gap.Time - rules.JumpDuration / 2);
            run.Move(1);
            run.Step(rules.JumpDuration);
            Assert.That(damage?.Kind, Is.EqualTo(JudgeRules.ClassifyLanding(run.Time, run.Time, rules.EarlyLandingThreshold)));
            double start = gap.Time - JudgeRules.WindowFor(gap.Time, rules);
            double end = gap.EndTime + JudgeRules.WindowFor(gap.EndTime, rules);
            run.Start();
            run.Step(start - .0001);
            Assert.That(run.DamageCount, Is.Zero);
            run.Step(.0002);
            Assert.That(run.DamageCount, Is.EqualTo(1));
            run.Start();
            run.Step(end - .0001);
            Assert.That(run.DamageCount, Is.EqualTo(1));
            run.Start();
            run.Step(end + .0001);
            Assert.That(run.DamageCount, Is.Zero);
            run.Start();
            run.Step(start - .05);
            Assert.That(run.Move(-1).Result, Is.EqualTo("jump"));
            run.Step(.1);
            Assert.That(run.Jump, Is.Not.Null);
            Assert.That(run.DamageCount, Is.Zero);
        }
    }
}
