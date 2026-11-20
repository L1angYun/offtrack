using NUnit.Framework;
using Offtrack.Core;

namespace Offtrack.Tests.EditMode
{
    public sealed class V3TransitionTests
    {
        [Test]
        public void FragmentsPersistThroughFailureAndOnlyFinalClaimLeaves()
        {
            var rules = new ShellRules();
            var machine = new AdShellMachine(rules);
            machine.Advance(rules.BootDuration);
            machine.Advance(rules.SkipCountdownDuration);
            Assert.That(machine.AcceptLottery(), Is.True);
            for (int level = 1; level <= 3; level++)
            {
                Assert.That(machine.Level, Is.EqualTo(level));
                Assert.That(machine.State, Is.EqualTo(AdShellState.Guide));
                Assert.That(machine.RequestSkip(), Is.True);
                Assert.That(machine.Stay(), Is.True);
                Assert.That(machine.State, Is.EqualTo(AdShellState.Guide));
                Assert.That(machine.StartPlaying(), Is.True);
                Assert.That(machine.NotifyRunEnded(false), Is.True);
                Assert.That(machine.Fragments, Is.EqualTo(level - 1));
                Assert.That(machine.RequestAbandonAfterFailure(), Is.True);
                Assert.That(machine.Stay(), Is.True);
                Assert.That(machine.Retry(), Is.True);
                Assert.That(machine.StartPlaying(), Is.True);
                Assert.That(machine.NotifyRunEnded(true), Is.True);
                Assert.That(machine.Fragments, Is.EqualTo(level));
                Assert.That(machine.RequestSkip(), Is.False);
                machine.Advance(100);
                Assert.That(machine.State, Is.EqualTo(AdShellState.Fragment));
                Assert.That(machine.AcceptFragment(), Is.True);
                if (level < 3) Assert.That(machine.OpenNextGuide(), Is.True);
            }
            Assert.That(machine.State, Is.EqualTo(AdShellState.Leaving));
            Assert.That(machine.ClaimedMembership, Is.True);
        }

        [Test]
        public void FadeIsLinearAndClampedInBothDirections()
        {
            var transition = new TransitionRules();
            Assert.That(transition.InkOpacity(0, false), Is.Zero);
            Assert.That(transition.InkOpacity(.1, false), Is.EqualTo(.25));
            Assert.That(transition.InkOpacity(.1, true), Is.EqualTo(.75));
            Assert.That(transition.InkOpacity(.4, true), Is.Zero);
            Assert.That(transition.InkOpacity(10, false), Is.EqualTo(1));
            Assert.That(transition.BlackFrames, Is.EqualTo(2));
        }

        [Test]
        public void BootBarRemainsFullBeforeLotteryStarts()
        {
            var rules = new ShellRules();
            var machine = new AdShellMachine(rules);
            machine.Advance(rules.BootDuration - rules.BootHoldDuration);
            Assert.That(machine.BootProgress, Is.EqualTo(1));
            Assert.That(machine.State, Is.EqualTo(AdShellState.Boot));
            machine.Advance(rules.BootHoldDuration / 2);
            Assert.That(machine.State, Is.EqualTo(AdShellState.Boot));
            machine.Advance(rules.BootHoldDuration);
            Assert.That(machine.State, Is.EqualTo(AdShellState.Lottery));
        }
    }
}
