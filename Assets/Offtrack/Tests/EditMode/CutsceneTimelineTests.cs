using System.Collections.Generic;
using NUnit.Framework;
using Offtrack.Core;

namespace Offtrack.Tests.EditMode
{
    public sealed class CutsceneTimelineTests
    {
        private CutsceneTiming timing;

        [SetUp]
        public void SetUp() => timing = new CutsceneTiming();

        [Test]
        public void OpeningIntroAndStudentEntranceMatchReferenceBoundaries()
        {
            Assert.That(A(0).IntroSize, Is.Zero);
            Assert.That(A(1.199).ClassroomVisible, Is.False);
            Assert.That(A(1.2).ClassroomVisible, Is.True);
            Assert.That(A(1.999).StudentVisible, Is.False);
            Assert.That(A(2).StudentVisible, Is.True);
            Assert.That(A(2).StudentX, Is.EqualTo(1740));
            Assert.That(A(3.45).StudentX, Is.EqualTo(1550).Within(1e-9));
            Assert.That(A(4.9).StudentX, Is.EqualTo(1360).Within(1e-9));
        }

        [Test]
        public void OpeningStudentSitsWithTheOriginalCubicEasing()
        {
            Assert.That(A(5).Seated, Is.Zero);
            Assert.That(A(5).LegHeight, Is.EqualTo(67.6).Within(1e-9));
            Assert.That(A(5.3).Seated, Is.EqualTo(.875).Within(1e-9));
            Assert.That(A(5.6).Seated, Is.EqualTo(1));
            Assert.That(A(5.6).StudentBottom, Is.EqualTo(800));
            Assert.That(A(5.6).LegHeight, Is.Zero);
            Assert.That(A(5.6).WalkTime, Is.Zero);
        }

        [Test]
        public void FormulaWritingUsesSequentialClipsAndTeacherArmWindow()
        {
            Assert.That(A(5.599).TeacherArmUp, Is.False);
            Assert.That(A(5.6).TeacherArmUp, Is.True);
            Assert.That(A(8.899).TeacherArmUp, Is.True);
            Assert.That(A(8.9).TeacherArmUp, Is.False);
            var commands = new List<CutsceneCommand>();
            CutsceneTimeline.Draw(true, 6, timing, commands);
            var formula = commands.Find(c => c.Serif);
            Assert.That(formula.Text, Is.EqualTo("∫ x² dx = x³ / 3"));
            Assert.That(formula.HasClip, Is.True);
            Assert.That(formula.ClipX, Is.EqualTo(215));
            Assert.That(formula.ClipY, Is.EqualTo(142));
            Assert.That(formula.ClipWidth, Is.EqualTo(600 * (.4 / 3) / .23).Within(1e-9));
            Assert.That(commands.FindAll(c => c.Serif).Count, Is.EqualTo(1));
            CutsceneTimeline.Draw(true, 8.6, timing, commands);
            Assert.That(commands.FindAll(c => c.Serif).Count, Is.EqualTo(4));
            foreach (var c in commands.FindAll(c => c.Serif)) Assert.That(c.ClipWidth, Is.EqualTo(600));
        }

        [TestCase(0, 8.4)]
        [TestCase(1, 9.4)]
        [TestCase(2, 10.4)]
        public void EachBubbleAppearsAtItsOwnKeyframeAndFadesBeforeThePhone(int index, double start)
        {
            Assert.That(CutsceneTimeline.BubbleAlpha(start - .01, index, timing), Is.Zero);
            Assert.That(CutsceneTimeline.BubbleAlpha(start, index, timing), Is.Zero);
            Assert.That(CutsceneTimeline.BubbleAlpha(start + .11, index, timing), Is.EqualTo(.875).Within(1e-9));
            Assert.That(CutsceneTimeline.BubbleAlpha(start + .22, index, timing), Is.EqualTo(1).Within(1e-9));
            Assert.That(CutsceneTimeline.BubbleAlpha(11.6, index, timing), Is.EqualTo(.5).Within(1e-9));
            Assert.That(CutsceneTimeline.BubbleAlpha(11.8, index, timing), Is.Zero);
        }

        [Test]
        public void PhoneAndOpeningDimFollowTheOriginalAlphaAndAppReveal()
        {
            Assert.That(A(11.199).PhoneVisible, Is.False);
            Assert.That(A(11.2).PhoneVisible, Is.True);
            Assert.That(A(11.2).PhoneAlpha, Is.Zero);
            Assert.That(A(11.5).PhoneAlpha, Is.EqualTo(.875).Within(1e-9));
            Assert.That(A(11.8).PhoneAlpha, Is.EqualTo(1));
            Assert.That(A(12).AppProgress, Is.Zero);
            Assert.That(A(12.7).AppProgress, Is.EqualTo(1));
            Assert.That(A(13.4).Dim, Is.Zero);
            Assert.That(A(13.8).Dim, Is.EqualTo(.875).Within(1e-9));
            Assert.That(A(14.2).Dim, Is.EqualTo(1));
            var commands = new List<CutsceneCommand>();
            CutsceneTimeline.Draw(true, 15, timing, commands);
            Assert.That(commands[commands.Count - 1].Primitive, Is.EqualTo(CutscenePrimitive.Line));
            Assert.That(commands[commands.Count - 1].Ink, Is.EqualTo(CutsceneInk.Bone));
        }

        [Test]
        public void ClosingTitleIsTheOnlyForegroundBeforeOnePointFiveSeconds()
        {
            var commands = new List<CutsceneCommand>();
            CutsceneTimeline.Draw(false, 1.499, timing, commands);
            Assert.That(commands.Count, Is.EqualTo(2));
            Assert.That(commands[1].Text, Is.EqualTo("三个月后"));
            Assert.That(B(1.499).ClassroomVisible, Is.False);
            Assert.That(B(1.5).ClassroomVisible, Is.True);
            Assert.That(B(1.999).StudentVisible, Is.False);
            Assert.That(B(2).StudentVisible, Is.True);
            Assert.That(B(4.2).Seated, Is.Zero);
            Assert.That(B(4.6).Seated, Is.EqualTo(1));
        }

        [Test]
        public void ReportSlidesInThenGradeFadesInAtFivePointSix()
        {
            Assert.That(B(4.599).ReportVisible, Is.False);
            Assert.That(B(4.6).ReportVisible, Is.True);
            Assert.That(B(4.6).ReportY, Is.EqualTo(-140));
            Assert.That(B(5).ReportY, Is.EqualTo(140).Within(1e-9));
            Assert.That(B(5.4).ReportY, Is.EqualTo(180));
            Assert.That(B(5.599).GradeVisible, Is.False);
            Assert.That(B(5.6).GradeVisible, Is.True);
            Assert.That(B(5.6).GradeAlpha, Is.Zero);
            Assert.That(B(6.2).GradeAlpha, Is.EqualTo(1));
        }

        [Test]
        public void StudentShockChangesToGriefWithoutFacialDetails()
        {
            Assert.That(B(6.6).Shock, Is.Zero);
            Assert.That(B(6.8).Shock, Is.EqualTo(1));
            Assert.That(B(7).Grief, Is.Zero);
            Assert.That(B(7.6).Shock, Is.Zero);
            Assert.That(B(7.6).Grief, Is.Zero);
            Assert.That(B(8.2).Grief, Is.EqualTo(1));
            var commands = new List<CutsceneCommand>();
            CutsceneTimeline.Draw(false, 7, timing, commands);
            Assert.That(commands.Exists(c => c.Primitive == CutscenePrimitive.Rectangle && c.Ink == CutsceneInk.Red && c.Width == 8 && c.Height == 40), Is.True);
            CutsceneTimeline.Draw(false, 8.5, timing, commands);
            Assert.That(commands.Exists(c => c.Primitive == CutscenePrimitive.Rectangle && c.Ink == CutsceneInk.Red && c.Width == 6 && c.Height == 110), Is.True);
        }

        [Test]
        public void GradeBlinksOnlyInTheAuthoredWindowThenReportLeavesAndFrameDims()
        {
            Assert.That(B(8.99).GradeAlpha, Is.EqualTo(1));
            Assert.That(B(9).GradeAlpha, Is.EqualTo(1));
            Assert.That(B(9.23).GradeAlpha, Is.EqualTo(.28));
            Assert.That(B(9.45).GradeAlpha, Is.EqualTo(1));
            Assert.That(B(9.67).GradeAlpha, Is.EqualTo(.28));
            Assert.That(B(9.89).GradeAlpha, Is.EqualTo(1));
            Assert.That(B(9.9).GradeAlpha, Is.EqualTo(1));
            Assert.That(B(9.9).ReportY, Is.EqualTo(180));
            Assert.That(B(10.5).ReportY, Is.EqualTo(-720));
            Assert.That(B(10.5).Dim, Is.Zero);
            Assert.That(B(10.65).Dim, Is.EqualTo(.875).Within(1e-9));
            Assert.That(B(10.8).Dim, Is.EqualTo(1));
        }

        [TestCase(true, 6)]
        [TestCase(true, 10.6)]
        [TestCase(true, 12.8)]
        [TestCase(false, 1)]
        [TestCase(false, 7)]
        [TestCase(false, 8.5)]
        public void SeekingBackwardAndForwardProducesExactlyTheSameCommands(bool opening, double t)
        {
            var before = new List<CutsceneCommand>();
            var after = new List<CutsceneCommand>();
            CutsceneTimeline.Draw(opening, t, timing, before);
            CutsceneTimeline.Draw(opening, t + 3, timing, after);
            CutsceneTimeline.Draw(opening, 0, timing, after);
            CutsceneTimeline.Draw(opening, t, timing, after);
            Assert.That(after, Is.EqualTo(before));
        }

        private CutsceneFrame A(double t) => CutsceneTimeline.Evaluate(true, t, timing);
        private CutsceneFrame B(double t) => CutsceneTimeline.Evaluate(false, t, timing);
    }
}
