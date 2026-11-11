using System;
using NUnit.Framework;
using Offtrack.Core;

namespace Offtrack.Tests.EditMode
{
    public sealed class CoreRulesTests
    {
        internal static RuleConfig Rules() => new RuleConfig
        {
            LaneCount = 3, StartLane = 1, Lives = 4, JumpDuration = 0.22, SettleDuration = 0.35,
            HitInvincibility = 0.65, EarlyWindow = 0.115, LateWindow = 0.145, WindowSwitchTime = 12,
            EarlyLandingThreshold = 0.2, Duration = 36, Bpm = 112, BeatsPerRevolution = 12
        };

        [Test]
        public void JumpKeepsSourceLaneUntilLandingAndCooldownIsInclusive()
        {
            var rules = Rules();
            var simulation = new GameSimulation(rules, new ChartNote[0]);
            Assert.That(simulation.Move(-1).Result, Is.EqualTo("inactive"));
            simulation.Start();
            var move = simulation.Move(-1);
            Assert.That(move.Result, Is.EqualTo("jump"));
            Assert.That(simulation.Lane, Is.EqualTo(1));
            Assert.That(simulation.Jump.From, Is.EqualTo(1));
            Assert.That(simulation.Jump.To, Is.EqualTo(0));
            Assert.That(simulation.Jump.Start, Is.Zero);
            Assert.That(simulation.Jump.End, Is.EqualTo(0.22));
            Assert.That(simulation.Move(1).Result, Is.EqualTo("airborne"));
            simulation.Step(0.219);
            Assert.That(simulation.Lane, Is.EqualTo(1));
            simulation.Step(0.001);
            Assert.That(simulation.Lane, Is.EqualTo(0));
            Assert.That(simulation.Jump, Is.Null);
            Assert.That(simulation.LastLanding, Is.EqualTo(0.22));
            Assert.That(simulation.Move(-1).Result, Is.EqualTo("cooldown"), "cooldown precedes edge");
            simulation.Step(simulation.ReadyAt - simulation.Time);
            Assert.That(simulation.Move(-1).Result, Is.EqualTo("edge"));
            Assert.That(simulation.Move(1).Result, Is.EqualTo("jump"));
            Assert.That(simulation.Jumps, Is.EqualTo(2));
        }

        [Test]
        public void LandingInsideAWindowHitsAndUsesExactJumpEndForClassification()
        {
            var simulation = new GameSimulation(Rules(), new[] { new ChartNote(0.22, 0) });
            DamageEvent hit = null;
            simulation.DamageTaken += damage => hit = damage;
            simulation.Start();
            simulation.Move(-1);
            simulation.Step(0.219);
            Assert.That(simulation.Lives, Is.EqualTo(4));
            simulation.Step(0.081);
            Assert.That(simulation.Lane, Is.EqualTo(0));
            Assert.That(simulation.LastLanding, Is.EqualTo(0.22));
            Assert.That(hit, Is.Not.Null);
            Assert.That(hit.Kind, Is.EqualTo("landedTooEarly"));
            Assert.That(hit.Time, Is.EqualTo(0.3).Within(1e-15));
            Assert.That(simulation.Notes[0].Hit, Is.True);
            Assert.That(simulation.Notes[0].Passed, Is.False);
            simulation.Step(0.01);
            Assert.That(simulation.DamageCount, Is.EqualTo(1));
        }

        [Test]
        public void WindowBoundariesAreInclusiveAndSwitchUsesScratchTime()
        {
            var rules = Rules();
            rules.EarlyWindow = 0.125;
            rules.LateWindow = 0.25;
            var early = new GameSimulation(rules, new[] { new ChartNote(1, 1) });
            early.Start(); early.Step(0.875);
            Assert.That(early.Lives, Is.EqualTo(3));
            var after = new GameSimulation(rules, new[] { new ChartNote(1, 1) });
            after.Start(); after.Step(1.125);
            Assert.That(after.Lives, Is.EqualTo(3));
            var passed = new GameSimulation(rules, new[] { new ChartNote(1, 1) });
            passed.Start(); passed.Step(1.1250001);
            Assert.That(passed.Lives, Is.EqualTo(4));
            Assert.That(passed.Notes[0].Passed, Is.True);
            var switched = new GameSimulation(rules, new[] { new ChartNote(12, 1) });
            switched.Start(); switched.Step(11.75);
            Assert.That(switched.Lives, Is.EqualTo(3));
            var preceding = new GameSimulation(rules, new[] { new ChartNote(11.75, 1) });
            preceding.Start(); preceding.Step(12);
            Assert.That(preceding.Lives, Is.EqualTo(4));
            Assert.That(preceding.Notes[0].Passed, Is.True);
        }

        [Test]
        public void InvincibilityExpiresInclusivelyAndUnhitScratchCanStillHitLater()
        {
            var rules = Rules();
            rules.HitInvincibility = 0.5;
            rules.EarlyWindow = 0.125;
            var simulation = new GameSimulation(rules, new[] { new ChartNote(1, 1), new ChartNote(1.5, 1) });
            simulation.Start(); simulation.Step(1);
            Assert.That(simulation.Lives, Is.EqualTo(3));
            simulation.Step(0.375);
            Assert.That(simulation.Lives, Is.EqualTo(3));
            Assert.That(simulation.Notes[1].Hit, Is.False);
            simulation.Step(0.125);
            Assert.That(simulation.Lives, Is.EqualTo(2));
            Assert.That(simulation.Notes[1].Hit, Is.True);
        }

        [Test]
        public void LethalHitAtDurationEndsBeforeWinAndStopsFollowingScratch()
        {
            var rules = Rules(); rules.Lives = 1; rules.Duration = 1; rules.HitInvincibility = 0;
            var simulation = new GameSimulation(rules, new[] { new ChartNote(1, 1), new ChartNote(1, 1) });
            int events = 0;
            simulation.Ended += result => events++;
            simulation.Start(); simulation.Step(5);
            Assert.That(simulation.Time, Is.EqualTo(1));
            Assert.That(simulation.Result.Won, Is.False);
            Assert.That(simulation.DamageCount, Is.EqualTo(1));
            Assert.That(simulation.Notes[1].Hit, Is.False);
            simulation.Step(1);
            Assert.That(events, Is.EqualTo(1));
            Assert.That(simulation.Move(1).Result, Is.EqualTo("inactive"));
        }

        [Test]
        public void RestartResetsAllMutableRunStateAndOriginalChartIsNotShared()
        {
            var chart = new[] { new ChartNote(0.3, 0) };
            var simulation = new GameSimulation(Rules(), chart);
            simulation.Start(); simulation.Move(-1); simulation.Step(0.3);
            Assert.That(simulation.DamageCount, Is.EqualTo(1));
            simulation.Start();
            Assert.That(simulation.State, Is.EqualTo(RunState.Playing));
            Assert.That(simulation.Time, Is.Zero);
            Assert.That(simulation.Lane, Is.EqualTo(1));
            Assert.That(simulation.Lives, Is.EqualTo(4));
            Assert.That(simulation.Jumps, Is.Zero);
            Assert.That(simulation.DamageCount, Is.Zero);
            Assert.That(simulation.ReadyAt, Is.Zero);
            Assert.That(simulation.InvincibleUntil, Is.Zero);
            Assert.That(simulation.LastLanding, Is.EqualTo(-100));
            Assert.That(simulation.Jump, Is.Null);
            Assert.That(simulation.Result, Is.Null);
            Assert.That(simulation.Notes[0].Hit, Is.False);
            Assert.That(simulation.Notes[0].Passed, Is.False);
        }

        [TestCase(-1)]
        [TestCase(3)]
        public void ChartRejectsOutOfRangeLanes(int lane)
        {
            Assert.Throws<ArgumentException>(() => ChartTools.Validate(new[] { new ChartNote(1, lane) }, 3));
        }

        [Test]
        public void ChartRejectsUnsortedAndNonFiniteTimesButAllowsSimultaneousNotes()
        {
            Assert.Throws<ArgumentException>(() => ChartTools.Validate(new[] { new ChartNote(2, 0), new ChartNote(1, 1) }, 3));
            Assert.Throws<ArgumentException>(() => ChartTools.Validate(new[] { new ChartNote(double.NaN, 0) }, 3));
            Assert.Throws<ArgumentException>(() => ChartTools.Validate(new[] { new ChartNote(double.PositiveInfinity, 0) }, 3));
            Assert.Throws<ArgumentException>(() => ChartTools.Validate(new[] { new ChartNote(-0.01, 0) }, 3));
            Assert.DoesNotThrow(() => ChartTools.Validate(new[] { new ChartNote(1, 0), new ChartNote(1, 1) }, 3));
        }

        [Test]
        public void BeatConversionUsesOneDoublePrecisionSecondsPerBeat()
        {
            var chart = ChartTools.FromBeats(112, new[] { new BeatNote(6, 1), new BeatNote(65, 0) }, 3);
            Assert.That(chart[0].Time, Is.EqualTo(3.2142857142857144).Within(1e-15));
            Assert.That(chart[1].Time, Is.EqualTo(34.82142857142857).Within(1e-14));
            Assert.That(chart[0].Lane, Is.EqualTo(1));
            Assert.Throws<ArgumentOutOfRangeException>(() => ChartTools.FromBeats(0, new BeatNote[0], 3));
        }

        [Test]
        public void InvalidStepCannotCorruptTheClock()
        {
            var simulation = new GameSimulation(Rules(), new ChartNote[0]); simulation.Start();
            Assert.Throws<ArgumentOutOfRangeException>(() => simulation.Step(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => simulation.Step(double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => simulation.Step(double.PositiveInfinity));
            Assert.That(simulation.Time, Is.Zero);
        }

        [TestCase(0, -1)]
        [TestCase(389.999, -1)]
        [TestCase(390, 1)]
        [TestCase(780, 1)]
        public void PointerUsesScreenHalfAndTheMiddleBelongsToTheRight(double x, int direction)
        {
            Assert.That(InputRules.PointerDirection(x, 780), Is.EqualTo(direction));
        }

        [Test]
        public void InvalidPointerGeometryIsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => InputRules.PointerDirection(0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => InputRules.PointerDirection(0, double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => InputRules.PointerDirection(double.PositiveInfinity, 780));
        }

        [Test]
        public void LandingAtTheEarlyThresholdIsClassifiedAsJumpedTooLate()
        {
            var rules = Rules();
            rules.JumpDuration = 0.25; rules.EarlyWindow = 0.5; rules.EarlyLandingThreshold = 0.25;
            var simulation = new GameSimulation(rules, new[] { new ChartNote(0.5, 0) });
            DamageEvent hit = null;
            simulation.DamageTaken += damage => hit = damage;
            simulation.Start(); simulation.Move(-1); simulation.Step(0.5);
            Assert.That(hit, Is.Not.Null);
            Assert.That(hit.Kind, Is.EqualTo("jumpedTooLate"));
        }

        [Test]
        public void SimulationSnapshotsChartAndConfigurationAtConstruction()
        {
            var rules = Rules();
            var chart = new[] { new ChartNote(1, 1) };
            var simulation = new GameSimulation(rules, chart);
            rules.Lives = 100; rules.StartLane = 2; rules.Duration = 0.1;
            chart[0] = new ChartNote(5, 0);
            simulation.Start(); simulation.Step(1);
            Assert.That(simulation.State, Is.EqualTo(RunState.Playing));
            Assert.That(simulation.Lives, Is.EqualTo(3));
            Assert.That(simulation.Lane, Is.EqualTo(1));
            Assert.That(simulation.Notes[0].Time, Is.EqualTo(1));
        }

        [Test]
        public void InvalidRuleConfigurationCannotStartASimulation()
        {
            var noLane = Rules(); noLane.LaneCount = 0;
            var outside = Rules(); outside.StartLane = 3;
            var noLife = Rules(); noLife.Lives = 0;
            var nonFinite = Rules(); nonFinite.JumpDuration = double.NaN;
            var negative = Rules(); negative.SettleDuration = -1;
            Assert.Throws<ArgumentOutOfRangeException>(() => new GameSimulation(noLane, new ChartNote[0]));
            Assert.Throws<ArgumentOutOfRangeException>(() => new GameSimulation(outside, new ChartNote[0]));
            Assert.Throws<ArgumentOutOfRangeException>(() => new GameSimulation(noLife, new ChartNote[0]));
            Assert.Throws<ArgumentOutOfRangeException>(() => new GameSimulation(nonFinite, new ChartNote[0]));
            Assert.Throws<ArgumentOutOfRangeException>(() => new GameSimulation(negative, new ChartNote[0]));
        }
    }
}
