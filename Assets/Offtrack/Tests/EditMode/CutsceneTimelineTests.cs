using NUnit.Framework;
using Offtrack.Core;

namespace Offtrack.Tests.EditMode
{
    // The opening's people as a pure function of time. Cameras and the two room scenes are
    // covered by V3StoryScoreTests.
    public sealed class CutsceneTimelineTests
    {
        private CutsceneTiming timing;
        private PortraitScore score;

        [SetUp]
        public void SetUp()
        {
            timing = new CutsceneTiming();
            score = new PortraitScore();
        }

        [Test]
        public void OpeningBeatsProgressFromEntranceThroughThoughtsToPhone()
        {
            Assert.That(timing.OpeningSitStart - timing.StudentEnter - timing.OpeningWalkDuration, Is.GreaterThanOrEqualTo(-1e-9));
            Assert.That(timing.OpeningSitEnd, Is.GreaterThan(timing.OpeningSitStart));
            Assert.That(timing.WritingStart, Is.GreaterThanOrEqualTo(timing.OpeningSitEnd));
            Assert.That(timing.BubbleStarts[0], Is.GreaterThanOrEqualTo(timing.WritingEnd));
            Assert.That(timing.BubbleStarts, Is.Ordered);
            Assert.That(timing.PhoneStart, Is.GreaterThan(timing.BubbleStarts[2]));
            Assert.That(timing.PhoneAppStart, Is.GreaterThanOrEqualTo(timing.PhoneEnd));
        }

        [Test]
        public void OpeningLastsSixteenSecondsAndClampsOutsideIt()
        {
            Assert.That(timing.OpeningDuration, Is.EqualTo(16));
            Assert.That(CutsceneTimeline.Evaluate(100, timing).Time, Is.EqualTo(16));
            Assert.That(CutsceneTimeline.Evaluate(-5, timing).Time, Is.Zero);
        }

        [Test]
        public void StudentWalksInSitsThenChangesPoseWithEachThought()
        {
            Assert.That(CutsceneTimeline.Evaluate(.5, timing).StudentVisible, Is.False);
            var entering = CutsceneTimeline.Evaluate(.8, timing);
            Assert.That(entering.StudentVisible, Is.True);
            Assert.That(entering.StudentX, Is.EqualTo(1720));
            Assert.That(CutsceneTimeline.Evaluate(1.6, timing).Pose, Is.EqualTo(CutscenePose.Walking));
            var seated = CutsceneTimeline.Evaluate(3.2, timing);
            Assert.That(seated.StudentX, Is.EqualTo(1180));
            Assert.That(seated.Seated, Is.EqualTo(1));
            Assert.That(seated.Pose, Is.EqualTo(CutscenePose.Seated));
            Assert.That(CutsceneTimeline.Evaluate(6.5, timing).Pose, Is.EqualTo(CutscenePose.Slouch));
            Assert.That(CutsceneTimeline.Evaluate(8.5, timing).Pose, Is.EqualTo(CutscenePose.Clock));
            Assert.That(CutsceneTimeline.Evaluate(10.5, timing).Pose, Is.EqualTo(CutscenePose.Reach));
            var phone = CutsceneTimeline.Evaluate(13, timing);
            Assert.That(phone.Pose, Is.EqualTo(CutscenePose.Phone));
            Assert.That(phone.PhoneVisible, Is.True);
            Assert.That(CutsceneTimeline.Evaluate(11.5, timing).PhoneVisible, Is.False);
        }

        [Test]
        public void ThoughtBubblesSpeakOneAtATimeAndLeaveBeforeThePhone()
        {
            for (var i = 0; i < timing.BubbleStarts.Length; i++)
            {
                var at = timing.BubbleStarts[i] + .5;
                Assert.That(CutsceneTimeline.BubbleOpacity(at, i, timing, score), Is.EqualTo(1));
                for (var j = 0; j < timing.BubbleStarts.Length; j++)
                    if (i != j) Assert.That(CutsceneTimeline.BubbleOpacity(at, j, timing, score), Is.Zero);
            }
            for (var i = 0; i < timing.BubbleStarts.Length; i++)
                Assert.That(CutsceneTimeline.BubbleOpacity(timing.PhoneEnd, i, timing, score), Is.Zero);
        }

        [Test]
        public void SeatedStudentKeepsBreathingAndTheClockKeepsRunning()
        {
            var first = CutsceneTimeline.Evaluate(4, timing);
            var second = CutsceneTimeline.Evaluate(4.6, timing);
            Assert.That(first.HeadY, Is.Not.EqualTo(second.HeadY));
            Assert.That(System.Math.Abs(first.HeadY - second.HeadY), Is.LessThanOrEqualTo(3));
            Assert.That(second.ClockMinuteAngle, Is.GreaterThan(first.ClockMinuteAngle));
        }
    }
}
