using System;
using System.Collections.Generic;
using NUnit.Framework;
using Offtrack.Core;

namespace Offtrack.Tests.EditMode
{
    public sealed class AdShellMachineTests
    {
        private static ShellRules Rules() => new ShellRules
        {
            BootDuration = 1, SkipCountdownDuration = 8, CompleteDuration = 3
        };

        private static AdShellMachine At(AdShellState state)
        {
            var machine = new AdShellMachine(Rules());
            if (state == AdShellState.Boot) return machine;
            machine.Advance(1);
            if (state == AdShellState.Lottery) return machine;
            machine.AcceptLottery();
            if (state == AdShellState.Idle) return machine;
            machine.StartPlaying();
            if (state == AdShellState.Playing) return machine;
            if (state == AdShellState.FailDialog) machine.NotifyRunEnded(false);
            else if (state == AdShellState.Complete) machine.NotifyRunEnded(true);
            else
            {
                machine.Advance(8);
                machine.RequestSkip();
                if (state == AdShellState.Leaving) machine.GiveUp();
            }
            return machine;
        }

        [Test]
        public void BootShowsProgressAndCountdownBeginsWhenLotteryAppears()
        {
            var machine = At(AdShellState.Boot);
            Assert.That(machine.SkipSecondsRemaining, Is.EqualTo(8));
            Assert.That(machine.CanSkip, Is.False);
            Assert.That(machine.RequestSkip(), Is.False);
            machine.Advance(0.5);
            Assert.That(machine.BootProgress, Is.EqualTo(0.5));
            Assert.That(machine.State, Is.EqualTo(AdShellState.Boot));
            Assert.That(machine.SkipSecondsRemaining, Is.EqualTo(8));
            machine.Advance(0.5);
            Assert.That(machine.State, Is.EqualTo(AdShellState.Lottery));
            Assert.That(machine.BootProgress, Is.EqualTo(1));
            Assert.That(machine.SkipSecondsRemaining, Is.EqualTo(8));
            machine.Advance(0.999);
            Assert.That(machine.SkipSecondsRemaining, Is.EqualTo(8));
            machine.Advance(0.001);
            Assert.That(machine.SkipSecondsRemaining, Is.EqualTo(7));
        }

        [Test]
        public void BootFrameOverrunCountsOnlyTimeAfterLotteryBecameVisible()
        {
            var machine = At(AdShellState.Boot);
            machine.Advance(1.5);
            Assert.That(machine.State, Is.EqualTo(AdShellState.Lottery));
            Assert.That(machine.SkipSecondsRemaining, Is.EqualTo(8));
            machine.Advance(0.5);
            Assert.That(machine.SkipSecondsRemaining, Is.EqualTo(7));
        }

        [Test]
        public void SkipUnlocksAtEightSecondsAndEarlyClicksHaveNoSideEffects()
        {
            var machine = At(AdShellState.Lottery);
            machine.Advance(7.999);
            Assert.That(machine.SkipSecondsRemaining, Is.EqualTo(1));
            Assert.That(machine.RequestSkip(), Is.False);
            Assert.That(machine.State, Is.EqualTo(AdShellState.Lottery));
            machine.Advance(0.001);
            Assert.That(machine.SkipSecondsRemaining, Is.Zero);
            Assert.That(machine.CanSkip, Is.True);
            Assert.That(machine.RequestSkip(), Is.True);
            Assert.That(machine.State, Is.EqualTo(AdShellState.ConfirmSkip));
            Assert.That(machine.RequestSkip(), Is.False);
        }

        [Test]
        public void LotteryAcceptanceAndPlayRequireExplicitSeparateActions()
        {
            var machine = At(AdShellState.Lottery);
            Assert.That(machine.StartPlaying(), Is.False);
            Assert.That(machine.AcceptLottery(), Is.True);
            Assert.That(machine.State, Is.EqualTo(AdShellState.Idle));
            Assert.That(machine.AllowsStart, Is.True);
            Assert.That(machine.AllowsNeedleInput, Is.False);
            Assert.That(machine.AcceptLottery(), Is.False);
            Assert.That(machine.StartPlaying(), Is.True);
            Assert.That(machine.State, Is.EqualTo(AdShellState.Playing));
            Assert.That(machine.AllowsNeedleInput, Is.True);
            Assert.That(machine.StartPlaying(), Is.False);
        }

        [TestCase(AdShellState.Lottery)]
        [TestCase(AdShellState.Idle)]
        [TestCase(AdShellState.Playing)]
        [TestCase(AdShellState.FailDialog)]
        [TestCase(AdShellState.Complete)]
        public void StayReturnsToExactPreviousState(AdShellState state)
        {
            var machine = At(state == AdShellState.Complete ? AdShellState.Playing : state);
            machine.Advance(8);
            if (state == AdShellState.Complete) machine.NotifyRunEnded(true);
            Assert.That(machine.RequestSkip(), Is.True);
            Assert.That(machine.PreviousState, Is.EqualTo(state));
            Assert.That(machine.IsModalOpen, Is.True);
            Assert.That(machine.AllowsNeedleInput, Is.False);
            Assert.That(machine.IsGamePaused, Is.EqualTo(state == AdShellState.Playing));
            Assert.That(machine.Stay(), Is.True);
            Assert.That(machine.State, Is.EqualTo(state));
            Assert.That(machine.IsGamePaused, Is.EqualTo(state == AdShellState.Playing));
            Assert.That(machine.ResumeDelayRemaining, Is.EqualTo(state == AdShellState.Playing ? Rules().StayResumeDelay : 0));
            Assert.That(machine.Stay(), Is.False);
        }

        [TestCase(0.25)]
        [TestCase(1.25)]
        public void StayingHoldsTheRunForTheConfiguredDelayAndRejectsRestart(double delay)
        {
            var rules = Rules();
            rules.StayResumeDelay = delay;
            var machine = new AdShellMachine(rules);
            machine.Advance(rules.BootDuration);
            machine.AcceptLottery();
            machine.StartPlaying();
            machine.Advance(rules.SkipCountdownDuration);
            machine.RequestSkip();
            Assert.That(machine.Stay(), Is.True);
            Assert.That(machine.State, Is.EqualTo(AdShellState.Playing));
            Assert.That(machine.IsModalOpen, Is.False, "The frozen record is visible during the recovery beat.");
            Assert.That(machine.IsGamePaused, Is.True);
            Assert.That(machine.AllowsNeedleInput, Is.False);
            Assert.That(machine.AllowsRestart, Is.False);
            Assert.That(machine.Restart(), Is.False);
            int resumed = 0;
            machine.PauseChanged += () => resumed++;

            machine.Advance(delay - 0.01);
            Assert.That(machine.ResumeDelayRemaining, Is.EqualTo(0.01).Within(1e-10));
            Assert.That(machine.IsGamePaused, Is.True);
            Assert.That(machine.AllowsNeedleInput, Is.False);
            Assert.That(resumed, Is.Zero);
            machine.Advance(0.011);
            Assert.That(machine.ResumeDelayRemaining, Is.Zero);
            Assert.That(machine.IsGamePaused, Is.False);
            Assert.That(machine.AllowsNeedleInput, Is.True);
            Assert.That(machine.AllowsRestart, Is.True);
            Assert.That(machine.State, Is.EqualTo(AdShellState.Playing));
            Assert.That(resumed, Is.EqualTo(1));
            machine.Advance(10);
            Assert.That(resumed, Is.EqualTo(1), "The runtime adapter must receive exactly one recovery completion.");
        }

        [Test]
        public void ZeroConfiguredDelayRestoresInputImmediatelyWithoutARecoveryTick()
        {
            var rules = Rules();
            rules.StayResumeDelay = 0;
            var machine = new AdShellMachine(rules);
            machine.Advance(rules.BootDuration);
            machine.AcceptLottery();
            machine.StartPlaying();
            machine.Advance(rules.SkipCountdownDuration);
            machine.RequestSkip();
            machine.Stay();
            Assert.That(machine.ResumeDelayRemaining, Is.Zero);
            Assert.That(machine.IsGamePaused, Is.False);
            Assert.That(machine.AllowsNeedleInput, Is.True);
            Assert.That(machine.AllowsRestart, Is.True);
        }

        [Test]
        public void RecoveryConsumesOnlyForegroundTimeWhileAdCountdownKeepsRealTime()
        {
            var machine = At(AdShellState.Playing);
            machine.Advance(3, 0);
            Assert.That(machine.SkipSecondsRemaining, Is.EqualTo(5));
            machine.Advance(5, 0);
            Assert.That(machine.CanSkip, Is.True, "Background time still unlocks the advertisement's skip button.");
            machine.RequestSkip();
            machine.Stay();
            machine.Advance(Rules().StayResumeDelay / 3);
            double remaining = machine.ResumeDelayRemaining;
            int resumed = 0;
            machine.PauseChanged += () => resumed++;

            machine.Advance(100, 0);
            Assert.That(machine.ResumeDelayRemaining, Is.EqualTo(remaining));
            Assert.That(machine.IsGamePaused, Is.True);
            Assert.That(machine.AllowsNeedleInput, Is.False);
            Assert.That(resumed, Is.Zero);
            machine.Advance(10, remaining / 2);
            Assert.That(machine.ResumeDelayRemaining, Is.EqualTo(remaining / 2).Within(1e-10));
            machine.Advance(remaining, remaining / 2);
            Assert.That(machine.ResumeDelayRemaining, Is.Zero);
            Assert.That(machine.IsGamePaused, Is.False);
            Assert.That(resumed, Is.EqualTo(1));
        }

        [TestCase(-0.01)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(1.01)]
        public void InvalidForegroundDeltaCannotAdvanceEitherClock(double recoveryDelta)
        {
            var machine = At(AdShellState.Playing);
            Assert.Throws<ArgumentOutOfRangeException>(() => machine.Advance(1, recoveryDelta));
            Assert.That(machine.SkipSecondsRemaining, Is.EqualTo(8));
            Assert.That(machine.State, Is.EqualTo(AdShellState.Playing));
        }

        [Test]
        public void ReopeningConfirmationDuringTheHoldWaitsForAFreshRecoveryBeat()
        {
            var machine = At(AdShellState.Playing);
            machine.Advance(8);
            machine.RequestSkip();
            machine.Stay();
            machine.Advance(Rules().StayResumeDelay / 2);
            Assert.That(machine.RequestSkip(), Is.True);
            machine.Advance(10);
            Assert.That(machine.IsGamePaused, Is.True);
            machine.Stay();
            Assert.That(machine.ResumeDelayRemaining, Is.EqualTo(Rules().StayResumeDelay));
            machine.Advance(Rules().StayResumeDelay);
            Assert.That(machine.IsGamePaused, Is.False);
        }

        [TestCase(AdShellState.Lottery)]
        [TestCase(AdShellState.Idle)]
        [TestCase(AdShellState.Playing)]
        [TestCase(AdShellState.FailDialog)]
        [TestCase(AdShellState.Complete)]
        public void GivingUpFromEachAvailableStateLeavesWithoutStartingOrFailingRun(AdShellState state)
        {
            var machine = At(state == AdShellState.Complete ? AdShellState.Playing : state);
            machine.Advance(8);
            if (state == AdShellState.Complete) machine.NotifyRunEnded(true);
            Assert.That(machine.GiveUp(), Is.False, "give up needs an explicit confirmation first");
            Assert.That(machine.RequestSkip(), Is.True);
            Assert.That(machine.GiveUp(), Is.True);
            Assert.That(machine.State, Is.EqualTo(AdShellState.Leaving));
            Assert.That(machine.CanSkip, Is.False);
            Assert.That(machine.GiveUp(), Is.False);
            Assert.That(machine.Stay(), Is.False);
            machine.Advance(100);
            Assert.That(machine.State, Is.EqualTo(AdShellState.Leaving));
        }

        [Test]
        public void FailureRetryReturnsToIdleAndDoesNotResetTheCountdown()
        {
            var machine = At(AdShellState.Playing);
            machine.Advance(3);
            Assert.That(machine.NotifyRunEnded(false), Is.True);
            Assert.That(machine.State, Is.EqualTo(AdShellState.FailDialog));
            machine.Advance(2);
            Assert.That(machine.Retry(), Is.True);
            Assert.That(machine.State, Is.EqualTo(AdShellState.Idle));
            Assert.That(machine.SkipSecondsRemaining, Is.EqualTo(3));
            Assert.That(machine.AllowsNeedleInput, Is.False);
            Assert.That(machine.Retry(), Is.False);
            machine.StartPlaying();
            machine.NotifyRunEnded(false);
            machine.Advance(3);
            machine.Retry();
            Assert.That(machine.CanSkip, Is.True);
            Assert.That(machine.SkipSecondsRemaining, Is.Zero);
        }

        [Test]
        public void CompleteWaitsThreeSecondsBeforeOneLeavingTransition()
        {
            var machine = At(AdShellState.Playing);
            var states = new List<AdShellState>();
            machine.StateChanged += states.Add;
            Assert.That(machine.NotifyRunEnded(true), Is.True);
            machine.Advance(2.999);
            Assert.That(machine.State, Is.EqualTo(AdShellState.Complete));
            machine.Advance(0.001);
            Assert.That(machine.State, Is.EqualTo(AdShellState.Leaving));
            machine.Advance(100);
            CollectionAssert.AreEqual(new[] { AdShellState.Complete, AdShellState.Leaving }, states);
        }

        [Test]
        public void CompleteTimerPausesDuringConfirmationAndResumesItsRemainder()
        {
            var machine = At(AdShellState.Playing);
            machine.Advance(8);
            machine.NotifyRunEnded(true);
            machine.Advance(1);
            machine.RequestSkip();
            machine.Advance(100);
            Assert.That(machine.State, Is.EqualTo(AdShellState.ConfirmSkip));
            machine.Stay();
            machine.Advance(1.999);
            Assert.That(machine.State, Is.EqualTo(AdShellState.Complete));
            machine.Advance(0.001);
            Assert.That(machine.State, Is.EqualTo(AdShellState.Leaving));
        }

        [TestCase(AdShellState.Boot)]
        [TestCase(AdShellState.Lottery)]
        [TestCase(AdShellState.ConfirmSkip)]
        [TestCase(AdShellState.FailDialog)]
        [TestCase(AdShellState.Leaving)]
        public void BlockingStatesRejectKeyboardRestartStartAndNeedleInput(AdShellState state)
        {
            var machine = At(state);
            Assert.That(machine.AllowsNeedleInput, Is.False);
            Assert.That(machine.AllowsStart, Is.False);
            Assert.That(machine.AllowsRestart, Is.False);
            Assert.That(machine.StartPlaying(), Is.False);
            Assert.That(machine.Restart(), Is.False);
            Assert.That(machine.NotifyRunEnded(false), Is.False);
            Assert.That(machine.NotifyRunEnded(true), Is.False);
            Assert.That(machine.State, Is.EqualTo(state));
        }

        [TestCase(AdShellState.Idle)]
        [TestCase(AdShellState.Playing)]
        [TestCase(AdShellState.Complete)]
        public void DeveloperRestartStartsPlayingOnlyInNonModalRunStates(AdShellState state)
        {
            var machine = At(state);
            Assert.That(machine.AllowsRestart, Is.True);
            Assert.That(machine.Restart(), Is.True);
            Assert.That(machine.State, Is.EqualTo(AdShellState.Playing));
            Assert.That(machine.AllowsNeedleInput, Is.True);
            machine.Advance(3);
            Assert.That(machine.State, Is.EqualTo(AdShellState.Playing), "old Complete timer must be cancelled");
        }

        [Test]
        public void DuplicateAndOutOfOrderActionsCannotLeaveBootOrIdle()
        {
            var machine = At(AdShellState.Boot);
            Assert.That(machine.AcceptLottery(), Is.False);
            Assert.That(machine.Retry(), Is.False);
            Assert.That(machine.Stay(), Is.False);
            Assert.That(machine.GiveUp(), Is.False);
            machine.Advance(1);
            machine.AcceptLottery();
            Assert.That(machine.NotifyRunEnded(false), Is.False);
            Assert.That(machine.NotifyRunEnded(true), Is.False);
            Assert.That(machine.State, Is.EqualTo(AdShellState.Idle));
        }

        [Test]
        public void ConfigurationIsSnapshottedAndAllDurationsAreConfigurable()
        {
            var rules = new ShellRules { BootDuration = 2, SkipCountdownDuration = 4, CompleteDuration = 5, StayResumeDelay = 0.75 };
            var machine = new AdShellMachine(rules);
            rules.BootDuration = 100;
            rules.SkipCountdownDuration = 100;
            rules.CompleteDuration = 100;
            rules.StayResumeDelay = 100;
            machine.Advance(2);
            Assert.That(machine.State, Is.EqualTo(AdShellState.Lottery));
            machine.Advance(4);
            Assert.That(machine.CanSkip, Is.True);
            machine.AcceptLottery();
            machine.StartPlaying();
            machine.RequestSkip();
            machine.Stay();
            Assert.That(machine.ResumeDelayRemaining, Is.EqualTo(0.75), "The active session owns a copy of its timing configuration.");
            machine.Advance(0.75);
            Assert.That(machine.IsGamePaused, Is.False);
            machine.NotifyRunEnded(true);
            machine.Advance(5);
            Assert.That(machine.State, Is.EqualTo(AdShellState.Leaving));
        }

        [Test]
        public void ZeroSkipDurationUnlocksOnlyAfterBoot()
        {
            var rules = Rules(); rules.SkipCountdownDuration = 0;
            var machine = new AdShellMachine(rules);
            Assert.That(machine.CanSkip, Is.False);
            machine.Advance(1);
            Assert.That(machine.CanSkip, Is.True);
        }

        [Test]
        public void InvalidDeltasAndConfigurationFailBeforeMutatingState()
        {
            var machine = At(AdShellState.Playing);
            Assert.Throws<ArgumentOutOfRangeException>(() => machine.Advance(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => machine.Advance(double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => machine.Advance(double.PositiveInfinity));
            Assert.That(machine.SkipSecondsRemaining, Is.EqualTo(8));
            Assert.That(machine.State, Is.EqualTo(AdShellState.Playing));
            Assert.Throws<ArgumentNullException>(() => new AdShellMachine(null));
            var badBoot = Rules(); badBoot.BootDuration = 0;
            var badSkip = Rules(); badSkip.SkipCountdownDuration = -1;
            var badComplete = Rules(); badComplete.CompleteDuration = double.NaN;
            Assert.Throws<ArgumentOutOfRangeException>(() => new AdShellMachine(badBoot));
            Assert.Throws<ArgumentOutOfRangeException>(() => new AdShellMachine(badSkip));
            Assert.Throws<ArgumentOutOfRangeException>(() => new AdShellMachine(badComplete));
        }

        [TestCase(-0.1)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        public void RecoveryDelayMustBeFiniteAndNonNegative(double value)
        {
            var rules = Rules();
            rules.StayResumeDelay = value;
            Assert.Throws<ArgumentOutOfRangeException>(() => new AdShellMachine(rules));
        }
    }
}
