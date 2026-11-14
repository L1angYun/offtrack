using System;
using System.Collections.Generic;
using System.Linq;
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
        public void OpeningBeatsProgressFromEntranceThroughThoughtsToPhone()
        {
            Assert.That(timing.StudentEnter, Is.GreaterThanOrEqualTo(timing.OpeningIntroEnd));
            Assert.That(timing.OpeningSitStart - timing.StudentEnter - timing.OpeningWalkDuration, Is.GreaterThanOrEqualTo(-1e-9));
            Assert.That(timing.OpeningSitEnd, Is.GreaterThan(timing.OpeningSitStart));
            Assert.That(timing.WritingStart, Is.GreaterThanOrEqualTo(timing.OpeningSitEnd));
            Assert.That(timing.BubbleStarts[0], Is.GreaterThanOrEqualTo(timing.WritingEnd));
            Assert.That(timing.BubbleStarts, Is.Ordered);
            Assert.That(timing.PhoneStart, Is.GreaterThan(timing.BubbleStarts[2]));
            Assert.That(timing.PhoneAppStart, Is.GreaterThanOrEqualTo(timing.PhoneEnd));
        }

        [Test]
        public void AuthoredDurationsProvideSixteenSecondOpeningAndTwelveSecondClosing()
        {
            Assert.That(Number(timing, "OpeningDuration"), Is.EqualTo(16));
            Assert.That(Number(timing, "ClosingDuration"), Is.EqualTo(12));
            Assert.That(CutsceneTimeline.Evaluate(true, 100, timing).Time, Is.EqualTo(16));
            Assert.That(CutsceneTimeline.Evaluate(false, 100, timing).Time, Is.EqualTo(12));
        }

        [Test]
        public void ClosingAllowsAFullGriefHoldAndAtLeastASecondToFade()
        {
            Assert.That(timing.ClosingWalkDuration, Is.EqualTo(1.6).Within(.1));
            Assert.That(timing.ReportStart, Is.GreaterThanOrEqualTo(timing.ClosingSitEnd));
            Assert.That(timing.ShockStart, Is.GreaterThan(timing.ReportStart));
            Assert.That(timing.GriefEnd - timing.GriefStart, Is.EqualTo(.3).Within(.05));
            Assert.That(timing.ClosingDimStart - timing.GriefEnd, Is.GreaterThanOrEqualTo(2));
            Assert.That(timing.ClosingDimEnd - timing.ClosingDimStart, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void ReportCloseUpReplacesTheStudentAndKeepsGradeStationary()
        {
            var first = CutsceneTimeline.Evaluate(false, timing.ReportStart + .8, timing);
            var later = CutsceneTimeline.Evaluate(false, timing.ReportStart + 1.5, timing);
            Assert.That(first.ReportVisible, Is.True);
            Assert.That(first.StudentVisible, Is.False);
            Assert.That(later.ReportVisible, Is.True);
            Assert.That(later.StudentVisible, Is.False);
            Assert.That(first.GradeVisible && later.GradeVisible, Is.True);
            Assert.That(first.GradeAlpha, Is.EqualTo(1));
            Assert.That(later.GradeAlpha, Is.EqualTo(first.GradeAlpha));
            Assert.That(later.ReportY, Is.EqualTo(first.ReportY));
            var grade = Draw(false, timing.ReportStart + 1).Single(c => c.Text == "58");
            Assert.That(grade.FontSize, Is.InRange(180, 220));
        }

        [Test]
        public void ThoughtBubblesSpeakOneAtATimeAndLeaveBeforeThePhone()
        {
            for (var i = 0; i < timing.BubbleStarts.Length; i++)
            {
                var at = timing.BubbleStarts[i] + .5;
                Assert.That(CutsceneTimeline.BubbleAlpha(at, i, timing), Is.GreaterThan(0));
                for (var j = 0; j < timing.BubbleStarts.Length; j++)
                    if (i != j) Assert.That(CutsceneTimeline.BubbleAlpha(at, j, timing), Is.Zero);
            }
            for (var i = 0; i < timing.BubbleStarts.Length; i++)
                Assert.That(CutsceneTimeline.BubbleAlpha(timing.PhoneEnd, i, timing), Is.Zero);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void GeometryIsOpaqueAndUsesOnlyNamedPaletteInks(bool opening)
        {
            var duration = opening ? 16 : 12;
            for (var t = 0d; t <= duration; t += .125)
                foreach (var command in Draw(opening, t))
                {
                    Assert.That(Enum.IsDefined(typeof(CutsceneInk), command.Ink), Is.True, $"t={t}");
                    if (command.Primitive != CutscenePrimitive.Label)
                        Assert.That(command.Alpha, Is.EqualTo(1), $"Occluder opacity at t={t}, {command.Primitive}");
                }
        }

        [TestCase(true, 16)]
        [TestCase(false, 12)]
        public void EndingRetainsARedPhoneInsteadOfAnOpaqueBlackCover(bool opening, double time)
        {
            var commands = Draw(opening, time);
            var lastRed = commands.FindLastIndex(c => c.Ink == CutsceneInk.Red && c.Alpha > 0);
            Assert.That(lastRed, Is.GreaterThan(0));
            Assert.That(commands.Skip(lastRed + 1).Any(c => c.Primitive == CutscenePrimitive.Rectangle &&
                c.Ink == CutsceneInk.Ink && c.Width >= 1600 && c.Height >= 900 && c.Alpha >= 1), Is.False);
        }

        [Test]
        public void BoardIsQuietAndChalkRemainsSeparateFromTheSolidTeacher()
        {
            var commands = Draw(true, timing.WritingEnd);
            Assert.That(commands.Any(c => c.Primitive == CutscenePrimitive.Rectangle && c.Ink == CutsceneInk.DarkRed15 &&
                c.Width >= 700 && c.Height >= 300 && c.Alpha == 1), Is.True);
            var formulas = commands.Where(c => c.Serif).ToArray();
            Assert.That(formulas, Is.Not.Empty);
            Assert.That(formulas.All(c => c.Ink == CutsceneInk.Bone60 && c.SubjectId == 0), Is.True);
            Assert.That(formulas.All(c => c.HasClip), Is.True);
            Assert.That(commands.Where(c => c.SubjectId == 5).All(c => c.Ink == CutsceneInk.DarkRed), Is.True,
                "The teacher must use its solid silhouette without a black outline.");
        }

        [TestCase(true, 16)]
        [TestCase(false, 12)]
        public void EverySampleHasAtMostOneBonePersonAndOneRedSubject(bool opening, double duration)
        {
            for (var t = 0d; t <= duration; t += .05)
            {
                var commands = Draw(opening, t);
                var people = commands.Where(c => c.Ink == CutsceneInk.Bone && c.SubjectId > 0).Select(c => c.SubjectId).Distinct().ToArray();
                var red = commands.Where(c => c.Ink == CutsceneInk.Red).ToArray();
                Assert.That(people.Length, Is.LessThanOrEqualTo(1), $"Bone people at {t}");
                Assert.That(red.All(c => c.SubjectId > 0), Is.True, $"A red shape must belong to a subject at {t}");
                Assert.That(red.Select(c => c.SubjectId).Distinct().Count(), Is.LessThanOrEqualTo(1), $"Red subjects at {t}");
            }
        }

        [Test]
        public void OpeningCameraPushesDuringThoughtsThenCentersThePhone()
        {
            var start = CutsceneTimeline.Evaluate(true, timing.CameraPushStart, timing);
            var middle = CutsceneTimeline.Evaluate(true, (timing.CameraPushStart + timing.CameraPushEnd) / 2, timing);
            var pushed = CutsceneTimeline.Evaluate(true, timing.CameraPushEnd, timing);
            var end = CutsceneTimeline.Evaluate(true, 16, timing);
            Assert.That(start.CameraScale, Is.EqualTo(1));
            Assert.That(middle.CameraScale, Is.GreaterThan(start.CameraScale).And.LessThan(pushed.CameraScale));
            Assert.That(pushed.CameraScale, Is.EqualTo(1.15).Within(.01));
            Assert.That(end.CameraScale, Is.GreaterThan(pushed.CameraScale));
            Assert.That(end.CameraX, Is.EqualTo(end.PhoneX));
            var redScreen = Draw(true, 16).Single(c => c.Ink == CutsceneInk.Red);
            Assert.That((redScreen.X + redScreen.Width / 2 - end.CameraX) * end.CameraScale + 800,
                Is.EqualTo(timing.PhoneMatchNormalizedX * 1600).Within(1e-9));
            Assert.That((redScreen.Y + redScreen.Height / 2 - end.CameraY) * end.CameraScale + 450,
                Is.EqualTo(timing.PhoneMatchNormalizedY * 900).Within(1e-9));
        }

        [Test]
        public void PhonePushApproachesScreenCenterMonotonicallyAndKeepsTheRedScreenInsideTheViewport()
        {
            var previousXDistance = double.PositiveInfinity;
            var previousYDistance = double.PositiveInfinity;
            for (var sample = 0; sample <= 120; sample++)
            {
                var time = timing.PhoneZoomStart + (timing.PhoneZoomEnd - timing.PhoneZoomStart) * sample / 120;
                var commands = new List<CutsceneCommand>();
                var frame = CutsceneTimeline.Draw(true, time, timing, commands);
                var screenX = (frame.PhoneX - frame.CameraX) * frame.CameraScale + 800;
                var screenY = (frame.PhoneY - frame.CameraY) * frame.CameraScale + 450;
                var xDistance = Math.Abs(screenX - timing.PhoneMatchNormalizedX * 1600);
                var yDistance = Math.Abs(screenY - timing.PhoneMatchNormalizedY * 900);
                Assert.That(xDistance, Is.LessThanOrEqualTo(previousXDistance + 1e-7), $"Phone moves away horizontally at {time}.");
                Assert.That(yDistance, Is.LessThanOrEqualTo(previousYDistance + 1e-7), $"Phone moves away vertically at {time}.");
                previousXDistance = xDistance;
                previousYDistance = yDistance;
                foreach (var screen in commands.Where(c => c.SubjectId == 2 && c.Ink == CutsceneInk.Red))
                {
                    var left = (screen.X - frame.CameraX) * frame.CameraScale + 800;
                    var right = (screen.X + screen.Width - frame.CameraX) * frame.CameraScale + 800;
                    Assert.That(left, Is.GreaterThanOrEqualTo(0), $"Red phone screen leaves the left edge at {time}.");
                    Assert.That(right, Is.LessThanOrEqualTo(1600), $"Red phone screen leaves the right edge at {time}.");
                }
            }
            Assert.That(previousXDistance, Is.LessThan(1e-7));
            Assert.That(previousYDistance, Is.LessThan(1e-7));
        }

        [Test]
        public void PhoneCloseupCutsAwayThePersonBeforeThreefoldMagnification()
        {
            for (var t = timing.PhoneZoomStart; t <= timing.OpeningDuration; t += .025)
            {
                var frame = CutsceneTimeline.Evaluate(true, t, timing);
                var commands = Draw(true, t);
                if (commands.Any(command => command.SubjectId == 1))
                    Assert.That(frame.CameraScale, Is.LessThan(3), $"Oversized person at {t}");
                if (t >= timing.PhoneCloseupStart)
                {
                    Assert.That(frame.PhoneCloseup, Is.True);
                    Assert.That(frame.StudentVisible, Is.False);
                    Assert.That(commands.All(command => command.SubjectId == 2 || command.ScreenSpace), Is.True);
                }
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ClassroomFloorCoversBothViewportEdgesAfterCameraMovement(bool opening)
        {
            for (var t = 0d; t <= (opening ? timing.OpeningDuration : timing.ClosingDuration); t += .125)
            {
                var frame = CutsceneTimeline.Evaluate(opening, t, timing);
                var floor = Draw(opening, t).Where(command => command.Primitive == CutscenePrimitive.Line &&
                    command.Y == timing.FloorY && command.Height == timing.FloorY).ToArray();
                if (floor.Length == 0) continue;
                Assert.That(floor.Length, Is.EqualTo(1));
                Assert.That((floor[0].X - frame.CameraX) * frame.CameraScale + 800, Is.EqualTo(0).Within(1e-7));
                Assert.That((floor[0].Width - frame.CameraX) * frame.CameraScale + 800, Is.EqualTo(1600).Within(1e-7));
            }
        }

        [TestCase(8.4)]
        [TestCase(10.2)]
        public void GriefHeadRestsOnConnectedOverlappingForearms(double time)
        {
            var frame = CutsceneTimeline.Evaluate(false, time, timing);
            var commands = Draw(false, time);
            var head = commands.Single(command => command.SubjectId == 1 && command.Primitive == CutscenePrimitive.Circle);
            var forearms = commands.Where(command => command.SubjectId == 1 && command.Primitive == CutscenePrimitive.RotatedRectangle &&
                Math.Abs(Math.Abs(command.Rotation) - 90) < 1e-7 && command.Width == 11).ToArray();
            Assert.That(forearms.Length, Is.EqualTo(2));
            var armsY = frame.StudentBottom + timing.GriefArmsOffsetY;
            Assert.That(head.Y + head.Width, Is.EqualTo(armsY - 5.5).Within(1), "Head must touch the crossed arms.");
            Assert.That(forearms.All(command => Math.Abs(command.Y + command.Height / 2 - armsY) < 1e-7), Is.True);
            var firstMin = forearms[0].X + forearms[0].Width / 2 - forearms[0].Height / 2;
            var firstMax = forearms[0].X + forearms[0].Width / 2 + forearms[0].Height / 2;
            var secondMin = forearms[1].X + forearms[1].Width / 2 - forearms[1].Height / 2;
            var secondMax = forearms[1].X + forearms[1].Width / 2 + forearms[1].Height / 2;
            Assert.That(Math.Min(firstMax, secondMax) - Math.Max(firstMin, secondMin), Is.GreaterThan(30));
            Assert.That(armsY + 5.5, Is.LessThan(725), "The forearms rest visibly on top of the desk.");
        }

        [Test]
        public void ClassroomAndTitleHaveIndependentTimedFades()
        {
            var zero = CutsceneTimeline.Evaluate(true, 0, timing);
            var half = CutsceneTimeline.Evaluate(true, timing.OpeningIntroEnd / 2, timing);
            var full = CutsceneTimeline.Evaluate(true, timing.OpeningIntroEnd, timing);
            Assert.That(zero.SceneOpacity, Is.Zero);
            Assert.That(half.SceneOpacity, Is.InRange(.01, .99));
            Assert.That(full.SceneOpacity, Is.EqualTo(1));
            Assert.That(CutsceneTimeline.Evaluate(false, 0, timing).TitleOpacity, Is.Zero);
            Assert.That(CutsceneTimeline.Evaluate(false, .4, timing).TitleOpacity, Is.EqualTo(1));
            Assert.That(CutsceneTimeline.Evaluate(false, 1.2, timing).TitleOpacity, Is.EqualTo(1).Within(1e-9));
            Assert.That(CutsceneTimeline.Evaluate(false, 1.6, timing).TitleOpacity, Is.Zero);
        }

        [Test]
        public void ThoughtPosesChangeTheHeadAndReachBeforeThePhoneIsRaised()
        {
            var slouch = CutsceneTimeline.Evaluate(true, timing.BubbleStarts[0] + .7, timing);
            var clock = CutsceneTimeline.Evaluate(true, timing.BubbleStarts[1] + .7, timing);
            var reach = CutsceneTimeline.Evaluate(true, timing.BubbleStarts[2] + .7, timing);
            var phone = CutsceneTimeline.Evaluate(true, timing.PhoneEnd, timing);
            Assert.That(slouch.Pose, Is.EqualTo(CutscenePose.Slouch));
            Assert.That(clock.Pose, Is.EqualTo(CutscenePose.Clock));
            Assert.That(clock.HeadY, Is.LessThan(slouch.HeadY));
            Assert.That(clock.HeadX, Is.GreaterThan(slouch.HeadX));
            Assert.That(reach.RightHandY, Is.GreaterThan(725), "The hand reaches under the solid desktop.");
            Assert.That(phone.RightHandY, Is.LessThan(725));
            Assert.That(phone.PhoneX, Is.EqualTo(phone.RightHandX));
            Assert.That(phone.PhoneY, Is.EqualTo(phone.RightHandY));
            Assert.That(Draw(true, timing.PhoneEnd).Any(c => c.SubjectId == 1 && c.Primitive == CutscenePrimitive.RotatedRectangle && Math.Abs(c.Rotation) > 5), Is.True);
        }

        [Test]
        public void GriefBlendsWithShockAndTheHoldStillBreathes()
        {
            var transition = CutsceneTimeline.Evaluate(false, (timing.GriefStart + timing.GriefEnd) / 2, timing);
            Assert.That(transition.Shock, Is.InRange(.01, .99));
            Assert.That(transition.Grief, Is.InRange(.01, .99));
            var first = CutsceneTimeline.Evaluate(false, timing.GriefEnd + .2, timing);
            var second = CutsceneTimeline.Evaluate(false, timing.GriefEnd + .8, timing);
            Assert.That(first.Grief, Is.EqualTo(1));
            Assert.That(second.Grief, Is.EqualTo(1));
            Assert.That(first.HeadY, Is.Not.EqualTo(second.HeadY));
            Assert.That(Math.Abs(first.HeadY - second.HeadY), Is.LessThanOrEqualTo(3));
            Assert.That(first.ClockMinuteAngle, Is.Not.EqualTo(second.ClockMinuteAngle));
        }

        [Test]
        public void ForegroundDeskIsPaintedAfterTheStudentAndReachingPhone()
        {
            var commands = Draw(true, timing.PhoneStart + .05);
            var lastStudent = commands.FindLastIndex(c => c.SubjectId == 1);
            var lastPhone = commands.FindLastIndex(c => c.SubjectId == 2);
            var desk = commands.FindIndex(c => c.Primitive == CutscenePrimitive.Rectangle && c.SubjectId == 0 &&
                c.Ink == CutsceneInk.DarkRed && c.X <= 1000 && c.Y == 725 && c.Width >= 500 && c.Height >= 100);
            Assert.That(lastStudent, Is.GreaterThan(0));
            Assert.That(lastPhone, Is.GreaterThan(lastStudent));
            Assert.That(desk, Is.GreaterThan(lastPhone));
            Assert.That(commands[desk].Alpha, Is.EqualTo(1));
        }

        [Test]
        public void TeacherHandTracksEachFormulaUsingItsConfiguredGlyphAdvance()
        {
            timing.FormulaWidths = new[] { 260d, 230, 290, 170 };
            for (var row = 0; row < 4; row++)
            {
                var time = timing.WritingStart + (timing.WritingEnd - timing.WritingStart) * (row + .45) / 4;
                var commands = Draw(true, time);
                var active = commands.Where(c => c.Serif).Last();
                Assert.That(active.ClipWidth, Is.GreaterThan(0).And.LessThan(timing.FormulaWidths[row]));
                var chalkX = active.ClipX + active.ClipWidth;
                var chalkY = active.Y - 12;
                Assert.That(commands.Any(c => c.SubjectId == 5 && c.Primitive == CutscenePrimitive.RotatedRectangle &&
                    c.Ink == CutsceneInk.DarkRed && EndsAt(c, chalkX, chalkY)), Is.True, $"Teacher hand follows row {row}.");
            }
            var full = Draw(true, timing.WritingEnd).Where(c => c.Serif).ToArray();
            for (var row = 0; row < full.Length; row++)
                Assert.That(full[row].ClipWidth, Is.EqualTo(timing.FormulaWidths[row]));
        }

        [TestCase(true, 1.6)]
        [TestCase(true, 6.8)]
        [TestCase(true, 8.8)]
        [TestCase(true, 10.8)]
        [TestCase(true, 12.8)]
        [TestCase(true, 16)]
        [TestCase(false, .7)]
        [TestCase(false, 4.8)]
        [TestCase(false, 8)]
        [TestCase(false, 12)]
        public void SeekingBackwardAndForwardProducesExactlyTheSameFrameAndCommands(bool opening, double t)
        {
            var before = new List<CutsceneCommand>();
            var after = new List<CutsceneCommand>();
            var firstFrame = CutsceneTimeline.Draw(opening, t, timing, before);
            CutsceneTimeline.Draw(opening, t + 3, timing, after);
            CutsceneTimeline.Draw(opening, 0, timing, after);
            var lastFrame = CutsceneTimeline.Draw(opening, t, timing, after);
            Assert.That(lastFrame, Is.EqualTo(firstFrame));
            Assert.That(after, Is.EqualTo(before));
        }

        // M4 ending: the lit phone appears beside the still-slumped student, then a close-up shows
        // the game's own splash ad on its screen; its loading bar runs until the cut into Main.
        [Test]
        public void EndingShowsTheSplashAdOnThePhoneBesideTheStudent()
        {
            var beside = Draw(false, (timing.ClosingPhoneStart + timing.ClosingCloseupStart) / 2);
            Assert.That(beside.Any(c => c.SubjectId == 1), Is.True, "The student is still at his desk.");
            Assert.That(beside.Count(c => c.Ink == CutsceneInk.Red && c.SubjectId == 2), Is.EqualTo(1), "The phone on the desk lights up.");
            Assert.That(beside.Any(c => c.SubjectId == 3), Is.False, "The lit phone replaces the grief mark as the red thing.");

            var start = Draw(false, timing.ClosingCloseupStart + .01);
            var end = Draw(false, timing.ClosingDuration);
            foreach (var frame in new[] { start, end })
            {
                Assert.That(frame.Any(c => c.SubjectId == 1), Is.False, "The close-up shows only the phone.");
                Assert.That(frame.Any(c => c.Primitive == CutscenePrimitive.Circle && c.Ink == CutsceneInk.Red && c.Width >= 40), Is.True,
                    "The red music app icon fills the phone screen.");
                foreach (var copy in new[] { "网易云音乐", "正在加载…", "广告" })
                    Assert.That(frame.Any(c => c.Primitive == CutscenePrimitive.Label && c.Text == copy), Is.True, copy);
            }
            double Bar(List<CutsceneCommand> frame) => frame.Where(c => c.Primitive == CutscenePrimitive.Rectangle && c.Ink == CutsceneInk.Red)
                .Select(c => c.Width).DefaultIfEmpty(0).Max();
            Assert.That(Bar(end), Is.GreaterThan(Bar(start)), "The ad keeps loading.");
            Assert.That(CutsceneTimeline.Evaluate(false, timing.ClosingDuration, timing).AppProgress, Is.EqualTo(1));
        }

        private List<CutsceneCommand> Draw(bool opening, double time)
        {
            var commands = new List<CutsceneCommand>();
            CutsceneTimeline.Draw(opening, time, timing, commands);
            return commands;
        }

        private static double Number(object value, string fieldName)
        {
            var field = value.GetType().GetField(fieldName);
            Assert.That(field, Is.Not.Null, $"The authored timeline must expose {fieldName}.");
            return Convert.ToDouble(field.GetValue(value));
        }

        private static bool EndsAt(CutsceneCommand command, double x, double y)
        {
            var radians = command.Rotation * Math.PI / 180;
            var dx = -Math.Sin(radians) * command.Height / 2;
            var dy = Math.Cos(radians) * command.Height / 2;
            var cx = command.X + command.Width / 2;
            var cy = command.Y + command.Height / 2;
            return Math.Abs(cx + dx - x) < 1e-7 && Math.Abs(cy + dy - y) < 1e-7 ||
                Math.Abs(cx - dx - x) < 1e-7 && Math.Abs(cy - dy - y) < 1e-7;
        }
    }
}
