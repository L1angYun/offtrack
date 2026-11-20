using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Offtrack.Core;
using Offtrack.Runtime;
using Offtrack.Runtime.Presentation;
using UnityEditor;
using UnityEngine;

namespace Offtrack.Tests.EditMode
{
    // Guards on the three portrait cutscenes, read from the same draw commands the runtime renders.
    // Every test runs against the code defaults and against the saved GameConfig asset that the
    // scenes actually load, and each check is paired with a deliberately broken score or frame.
    public sealed class V3StoryScoreTests
    {
        private const double Frame = 1d / 60;
        private const double W = PortraitScore.Width, H = PortraitScore.Height;

        private static void Load(bool saved, out CutsceneTiming timing, out PortraitScore score)
        {
            if (!saved)
            {
                timing = new CutsceneTiming();
                score = new PortraitScore();
                return;
            }
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>("Assets/Offtrack/Config/GameConfig.asset");
            Assert.That(config, Is.Not.Null);
            // Copies, so a test that bends a value can never touch the asset.
            timing = JsonUtility.FromJson<CutsceneTiming>(JsonUtility.ToJson(config.Cutscenes.Timing));
            score = JsonUtility.FromJson<PortraitScore>(JsonUtility.ToJson(config.Cutscenes.Portrait));
        }

        private static List<CutsceneCommand> Draw(StoryScene scene, double time, CutsceneTiming timing, PortraitScore score)
        {
            var commands = new List<CutsceneCommand>();
            if (scene == StoryScene.Opening) CutsceneTimeline.DrawOpeningPortrait(time, timing, score, commands, out _);
            else CutsceneTimeline.DrawPortraitRoom(scene, time, score, commands);
            return commands;
        }

        private static double Duration(StoryScene scene, CutsceneTiming timing, PortraitScore score) =>
            scene == StoryScene.Opening ? timing.OpeningDuration : scene == StoryScene.Montage ? score.MontageEnd : score.ClosingEnd;

        // ---------------------------------------------------------------- geometry of one command

        private struct Box
        {
            public double Left, Top, Right, Bottom;
            public bool OnScreen => Right > 0 && Left < W && Bottom > 0 && Top < H;
            public double CenterX => (Left + Right) / 2;
            public double CenterY => (Top + Bottom) / 2;
        }

        private static Box Bounds(CutsceneCommand c)
        {
            switch (c.Primitive)
            {
                case CutscenePrimitive.Circle:
                    return new Box { Left = c.X - c.Width, Right = c.X + c.Width, Top = c.Y - c.Width, Bottom = c.Y + c.Width };
                case CutscenePrimitive.Ring:
                    double outer = c.Width + c.Stroke / 2;
                    return new Box { Left = c.X - outer, Right = c.X + outer, Top = c.Y - outer, Bottom = c.Y + outer };
                case CutscenePrimitive.Line:
                    return new Box
                    {
                        Left = Math.Min(c.X, c.Width) - c.Stroke / 2, Right = Math.Max(c.X, c.Width) + c.Stroke / 2,
                        Top = Math.Min(c.Y, c.Height) - c.Stroke / 2, Bottom = Math.Max(c.Y, c.Height) + c.Stroke / 2
                    };
                case CutscenePrimitive.RotatedRectangle:
                    double radians = c.Rotation * Math.PI / 180;
                    double halfX = (Math.Abs(Math.Cos(radians)) * c.Width + Math.Abs(Math.Sin(radians)) * c.Height) / 2;
                    double halfY = (Math.Abs(Math.Sin(radians)) * c.Width + Math.Abs(Math.Cos(radians)) * c.Height) / 2;
                    double centerX = c.X + c.Width / 2, centerY = c.Y + c.Height / 2;
                    return new Box { Left = centerX - halfX, Right = centerX + halfX, Top = centerY - halfY, Bottom = centerY + halfY };
                case CutscenePrimitive.Label:
                    // Text sits on its baseline; a CJK glyph is about one em wide, a Latin one about half.
                    double width = c.Text.Sum(ch => ch > 0x2E7F ? 1 : .5) * c.FontSize;
                    double left = c.X - width * (c.Align == CutsceneTextAlign.Center ? .5 : c.Align == CutsceneTextAlign.Right ? 1 : 0);
                    return new Box { Left = left, Right = left + width, Top = c.Y - c.FontSize, Bottom = c.Y + c.FontSize * .2 };
                case CutscenePrimitive.ThoughtBubble:
                    double bubble = c.Text.Length * c.FontSize + 32 * c.FontSize / 24d;
                    double top = c.Y - 8 * c.FontSize / 24d - c.Height / 2;
                    return new Box { Left = c.X - bubble / 2, Right = c.X + bubble / 2, Top = top, Bottom = top + c.Height };
                default:
                    return new Box { Left = c.X, Right = c.X + c.Width, Top = c.Y, Bottom = c.Y + c.Height };
            }
        }

        private static bool Paints(CutsceneCommand c) =>
            c.Alpha > 0 && !CutsceneGraphic.ResolveInk(c.Ink, c.Alpha).Equals(Palette.Ink);

        // The tallest run of rows in which nothing but ink is drawn, as a share of the frame height.
        private static double LargestInkBand(List<CutsceneCommand> commands)
        {
            var covered = new bool[PortraitScore.Height];
            foreach (var command in commands)
            {
                if (!Paints(command)) continue;
                var box = Bounds(command);
                if (!box.OnScreen) continue;
                for (int row = (int)Math.Max(0, Math.Floor(box.Top)); row < Math.Min(H, Math.Ceiling(box.Bottom)); row++)
                    covered[row] = true;
            }
            int longest = 0, run = 0;
            foreach (bool row in covered)
            {
                run = row ? 0 : run + 1;
                longest = Math.Max(longest, run);
            }
            return longest / H;
        }

        private static Box Union(IEnumerable<CutsceneCommand> commands)
        {
            var boxes = commands.Select(Bounds).ToArray();
            return new Box
            {
                Left = boxes.Min(box => box.Left), Right = boxes.Max(box => box.Right),
                Top = boxes.Min(box => box.Top), Bottom = boxes.Max(box => box.Bottom)
            };
        }

        // The grade page is the one bone rectangle with a 148 : 189 sheet's proportions.
        private static bool IsGradePage(CutsceneCommand command) => command.Primitive == CutscenePrimitive.Rectangle
            && command.Ink == CutsceneInk.Bone && Math.Abs(command.Width / command.Height - 148d / 189) < 1e-6;

        private static (double X, double Y) BoneEnd(CutsceneCommand bone, int direction)
        {
            double angle = (bone.Rotation + 90) * Math.PI / 180;
            return (bone.X + bone.Width / 2 + direction * bone.Height / 2 * Math.Cos(angle),
                bone.Y + bone.Height / 2 + direction * bone.Height / 2 * Math.Sin(angle));
        }

        private static CutsceneCommand[] Bones(List<CutsceneCommand> commands, int subject) => commands
            .Where(command => command.SubjectId == subject && command.Primitive == CutscenePrimitive.RotatedRectangle).ToArray();

        // ---------------------------------------------------------------- opening: framing

        // 0–13 s of the opening, before the push into the phone turns the whole frame red.
        private static double WorstOpeningInkBand(CutsceneTiming timing, PortraitScore score)
        {
            double worst = 0;
            for (double time = 0; time < timing.PhoneAppStart; time += Frame)
                worst = Math.Max(worst, LargestInkBand(Draw(StoryScene.Opening, time, timing, score)));
            return worst;
        }

        [TestCase(false)]
        [TestCase(true)]
        public void OpeningNeverLeavesAThirdOfThePortraitFrameAsBareInk(bool saved)
        {
            Load(saved, out var timing, out var score);
            Assert.That(WorstOpeningInkBand(timing, score), Is.LessThan(.30),
                "Every opening shot is framed for portrait: no bare-ink band may reach 30 % of the height.");

            // Broken on purpose: the landscape strip that left the top ~60 % black.
            score.OpeningBoardZoom = score.OpeningDeskZoom = .70;
            score.OpeningBoardFocusY = score.OpeningDeskFocusY = -480;
            Assert.That(WorstOpeningInkBand(timing, score), Is.GreaterThan(.5), "Negative control: a letterboxed strip must be caught.");

            Load(saved, out timing, out score);
            // Broken on purpose: one shared camera height for every shot, the desk pushed low.
            score.OpeningDeskFocusY = score.OpeningDeskFocusY - 290;
            Assert.That(WorstOpeningInkBand(timing, score), Is.GreaterThan(.30), "Negative control: a desk shot with an empty top half must be caught.");
        }

        private static List<string> OpeningSubjectProblems(CutsceneTiming timing, PortraitScore score)
        {
            var problems = new List<string>();
            for (double time = 0; time < timing.PhoneAppStart; time += Frame)
            {
                var commands = Draw(StoryScene.Opening, time, timing, score);
                bool boardShot = time < timing.StudentEnter || (time >= timing.OpeningSitEnd && time <= timing.CameraPushStart);
                bool studentShot = (time >= timing.StudentEnter && time <= timing.OpeningSitStart)
                    || (time >= timing.CameraPushStart + score.Reframe && time < timing.PhoneAppStart);
                if (boardShot)
                {
                    foreach (var text in commands.Where(command => command.Primitive == CutscenePrimitive.Label))
                    {
                        if (text.X < 16) problems.Add($"{time:F3}s: \"{text.Text}\" starts at x={text.X:F1}, off the left edge");
                        if (text.FontSize < 44) problems.Add($"{time:F3}s: \"{text.Text}\" is only {text.FontSize} px tall");
                    }
                    var heading = commands.Single(command => command.Text == "高等数学");
                    if (Bounds(heading).Right > W - 16) problems.Add($"{time:F3}s: board heading runs off the right edge");
                    var teacher = Union(commands.Where(command => command.SubjectId == 5));
                    if (teacher.Left < 0 || teacher.Right > W) problems.Add($"{time:F3}s: teacher is cut by the frame");
                }
                if (studentShot)
                {
                    var student = Union(commands.Where(command => command.SubjectId == 1));
                    var head = commands.Single(command => command.SubjectId == 1 && command.Primitive == CutscenePrimitive.Circle);
                    var headBox = Bounds(head);
                    if (headBox.Left < 0 || headBox.Right > W || headBox.Top < 0) problems.Add($"{time:F3}s: student's head is cut by the frame");
                    if (student.Bottom - student.Top < H * .40) problems.Add($"{time:F3}s: student is only {(student.Bottom - student.Top) / H:P0} of the height");
                    if (head.Width * 2 < 120) problems.Add($"{time:F3}s: student's head is only {head.Width * 2:F0} px across");
                }
            }
            return problems;
        }

        [TestCase(false)]
        [TestCase(true)]
        public void OpeningShotsKeepTheBoardTextAndThePeopleLargeAndInsideTheFrame(bool saved)
        {
            Load(saved, out var timing, out var score);
            Assert.That(OpeningSubjectProblems(timing, score), Is.Empty);

            // Broken on purpose: the framing that cropped the first character of the heading.
            score.OpeningBoardFocusX = 434;
            score.OpeningBoardZoom = 1.78;
            Assert.That(OpeningSubjectProblems(timing, score), Has.Some.Contains("off the left edge"));

            Load(saved, out timing, out score);
            // Broken on purpose: a wide shot that shrinks the student back to a quarter of the frame.
            score.OpeningDeskZoom = 1.2;
            Assert.That(OpeningSubjectProblems(timing, score), Has.Some.Contains("of the height"));
        }

        private static bool FloorSpansTheFrame(List<CutsceneCommand> commands, double floorY)
        {
            var floor = commands.Where(command => command.Primitive == CutscenePrimitive.Line && command.Ink == CutsceneInk.DarkRed
                && Math.Abs(command.Y - floorY) < 1e-6 && Math.Abs(command.Height - floorY) < 1e-6).ToArray();
            return floor.Length == 1 && Math.Min(floor[0].X, floor[0].Width) <= 0 && Math.Max(floor[0].X, floor[0].Width) >= W;
        }

        [TestCase(false)]
        [TestCase(true)]
        public void OpeningFloorLineCrossesTheWholeFrameWhereverTheCameraLooks(bool saved)
        {
            Load(saved, out var timing, out var score);
            int checkedFrames = 0;
            for (double time = 0; time <= timing.OpeningDuration; time += Frame)
            {
                var frame = CutsceneTimeline.Evaluate(time, timing);
                var camera = CutsceneTimeline.OpeningCamera(time, timing, score, frame);
                double floorY = camera.Y(timing.FloorY);
                var commands = Draw(StoryScene.Opening, time, timing, score);
                if (floorY < 0 || floorY > H) continue;
                Assert.That(FloorSpansTheFrame(commands, floorY), Is.True, $"floor line at {time:F3}s");
                var ground = commands[1];
                Assert.That(ground.Ink, Is.EqualTo(CutsceneInk.DarkRed15));
                Assert.That(ground.X, Is.Zero);
                Assert.That(ground.Width, Is.EqualTo(W));
                Assert.That(ground.Y, Is.EqualTo(floorY).Within(1e-6));
                Assert.That(ground.Y + ground.Height, Is.EqualTo(H).Within(1e-6));
                checkedFrames++;
            }
            Assert.That(checkedFrames, Is.GreaterThan(13 * 60));

            // Broken on purpose: the old line that stopped 1600 world pixels along the stage.
            var short_ = new List<CutsceneCommand>
            {
                new CutsceneCommand { Primitive = CutscenePrimitive.Line, Ink = CutsceneInk.DarkRed, X = -2672, Y = 1543, Width = 176, Height = 1543, Alpha = 1 }
            };
            Assert.That(FloorSpansTheFrame(short_, 1543), Is.False, "Negative control: a floor line ending mid-frame must be caught.");
        }

        // ---------------------------------------------------------------- one person, one red thing

        private static int BonePeople(List<CutsceneCommand> commands) => commands
            .Where(command => command.SubjectId > 0 && command.Alpha > 0 && Bounds(command).OnScreen
                && (command.Ink == CutsceneInk.Bone || command.Ink == CutsceneInk.Bone60)
                && (command.Primitive == CutscenePrimitive.RotatedRectangle || command.Primitive == CutscenePrimitive.Circle))
            .Select(command => command.SubjectId).Distinct().Count();

        private static int RedThings(List<CutsceneCommand> commands) => commands
            .Where(command => command.Alpha > 0 && Bounds(command).OnScreen
                && CutsceneGraphic.ResolveInk(command.Ink, command.Alpha).Equals(Palette.Red))
            .Select(command => command.SubjectId).Distinct().Count();

        [TestCase(StoryScene.Opening, false)]
        [TestCase(StoryScene.Opening, true)]
        [TestCase(StoryScene.Montage, false)]
        [TestCase(StoryScene.Montage, true)]
        [TestCase(StoryScene.Closing, false)]
        [TestCase(StoryScene.Closing, true)]
        public void EveryFrameHasAtMostOneBonePersonAndOneRedThing(StoryScene scene, bool saved)
        {
            Load(saved, out var timing, out var score);
            bool sawRed = false, sawPerson = false;
            List<CutsceneCommand> last = null;
            for (double time = 0; time <= Duration(scene, timing, score); time += Frame)
            {
                last = Draw(scene, time, timing, score);
                Assert.That(BonePeople(last), Is.LessThanOrEqualTo(1), $"bone people at {time:F3}s");
                Assert.That(RedThings(last), Is.LessThanOrEqualTo(1), $"red things at {time:F3}s");
                sawRed |= RedThings(last) == 1;
                sawPerson |= BonePeople(last) == 1;
                foreach (var red in last.Where(command => CutsceneGraphic.ResolveInk(command.Ink, command.Alpha).Equals(Palette.Red)))
                    Assert.That(red.SubjectId, Is.GreaterThan(0), $"a red shape belongs to a subject at {time:F3}s");
            }
            Assert.That(sawRed, Is.True);
            Assert.That(sawPerson, Is.EqualTo(scene != StoryScene.Closing), "The closing room is empty: the seat is vacant.");

            // Broken on purpose: a second red thing and a second bone person in the frame.
            last.Add(new CutsceneCommand { Primitive = CutscenePrimitive.Rectangle, Ink = CutsceneInk.Red, SubjectId = 9, X = 100, Y = 100, Width = 50, Height = 50, Alpha = 1 });
            last.Add(new CutsceneCommand { Primitive = CutscenePrimitive.Rectangle, Ink = CutsceneInk.Red, SubjectId = 2, X = 300, Y = 100, Width = 50, Height = 50, Alpha = 1 });
            Assert.That(RedThings(last), Is.EqualTo(2), "Negative control: two red things must be counted.");
            last.Add(new CutsceneCommand { Primitive = CutscenePrimitive.Circle, Ink = CutsceneInk.Bone, SubjectId = 7, X = 200, Y = 300, Width = 30, Alpha = 1 });
            last.Add(new CutsceneCommand { Primitive = CutscenePrimitive.Circle, Ink = CutsceneInk.Bone60, SubjectId = 1, X = 500, Y = 300, Width = 30, Alpha = 1 });
            Assert.That(BonePeople(last), Is.EqualTo(2), "Negative control: two bone people must be counted.");
        }

        [Test]
        public void TheGradeHandsItsRedToThePhoneInTheSameFrame()
        {
            var score = new PortraitScore();
            var timing = new CutsceneTiming();
            CutsceneCommand Grade(double time) => Draw(StoryScene.Closing, time, timing, score).Single(command => command.Text == "58");
            Assert.That(Grade(score.GradeAt).Ink, Is.EqualTo(CutsceneInk.Red));
            Assert.That(Grade(score.PhoneAt - .31).Alpha, Is.EqualTo(1));
            Assert.That(Grade(score.PhoneAt - .001).Ink, Is.EqualTo(CutsceneInk.Red));
            Assert.That(CutsceneGraphic.ResolveInk(CutsceneInk.Red, Grade(score.PhoneAt - .001).Alpha), Is.EqualTo(Palette.Red60));
            Assert.That(Grade(score.PhoneAt).Ink, Is.EqualTo(CutsceneInk.DarkRed));
            var lit = Draw(StoryScene.Closing, score.PhoneAt, timing, score);
            Assert.That(lit.Count(command => command.Ink == CutsceneInk.Red && command.SubjectId == 2), Is.EqualTo(1));
            Assert.That(Draw(StoryScene.Closing, score.PhoneAt - .001, timing, score).Any(command => command.SubjectId == 2), Is.False);
        }

        // ---------------------------------------------------------------- camera pushes

        private static bool ApproachesCenterInsideTheFrame(IReadOnlyList<(double X, double Y)> centers, out string problem)
        {
            problem = null;
            double previous = double.MaxValue;
            for (int index = 0; index < centers.Count; index++)
            {
                var (x, y) = centers[index];
                if (x < 0 || x > W || y < 0 || y > H) { problem = $"step {index}: center ({x:F0},{y:F0}) left the frame"; return false; }
                double distance = Math.Sqrt((x - W / 2) * (x - W / 2) + (y - H / 2) * (y - H / 2));
                // Half a pixel of slack absorbs the breathing of the hand that holds the phone.
                if (distance > previous + .5) { problem = $"step {index}: moved {distance - previous:F1} px away from center"; return false; }
                previous = distance;
            }
            return true;
        }

        [TestCase(false)]
        [TestCase(true)]
        public void OpeningPhoneLightsInPlaceThenFillsTheFrameInPointSevenSeconds(bool saved)
        {
            Load(saved, out var timing, out var score);
            Assert.That(score.Reframe, Is.InRange(.6, .8));
            CutsceneCommand Screen(double time, CutsceneInk ink) => Draw(StoryScene.Opening, time, timing, score)
                .Single(command => command.SubjectId == 2 && command.Ink == ink);
            var dark = Screen(timing.PhoneAppStart - .001, CutsceneInk.Ink);
            var lit = Screen(timing.PhoneAppStart, CutsceneInk.Red);
            Assert.That(lit.X, Is.EqualTo(dark.X).Within(1));
            Assert.That(lit.Y, Is.EqualTo(dark.Y).Within(1));
            Assert.That(lit.Width, Is.EqualTo(dark.Width).Within(.001));
            Assert.That(Bounds(lit).Left, Is.GreaterThan(0));
            Assert.That(Bounds(lit).Right, Is.LessThan(W));
            Assert.That(Draw(StoryScene.Opening, timing.PhoneAppStart, timing, score).Any(command => command.SubjectId == 1), Is.True,
                "The student is still in the shot when the phone lights.");

            var centers = new List<(double, double)>();
            double width = 0;
            for (double time = timing.PhoneAppStart; time <= timing.PhoneAppStart + score.Reframe + 1e-9; time += Frame / 4)
            {
                var screen = Screen(time, CutsceneInk.Red);
                centers.Add((Bounds(screen).CenterX, Bounds(screen).CenterY));
                Assert.That(screen.Width, Is.GreaterThanOrEqualTo(width - 1e-6), $"the screen only grows, at {time:F3}s");
                width = screen.Width;
            }
            Assert.That(ApproachesCenterInsideTheFrame(centers, out var problem), Is.True, problem);
            var full = Bounds(Screen(timing.PhoneAppStart + score.Reframe, CutsceneInk.Red));
            Assert.That(full.Left, Is.LessThan(0));
            Assert.That(full.Top, Is.LessThan(0));
            Assert.That(full.Right, Is.GreaterThan(W));
            Assert.That(full.Bottom, Is.GreaterThan(H));
            for (double time = timing.PhoneAppStart + score.Reframe; time <= timing.OpeningDuration; time += Frame)
            {
                var hold = Bounds(Screen(time, CutsceneInk.Red));
                Assert.That(hold.Left < 0 && hold.Top < 0 && hold.Right > W && hold.Bottom > H, Is.True, $"red fills the frame at {time:F3}s");
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ClosingPushesKeepTheirTargetInFrameAndOnAStraightPathToCenter(bool saved)
        {
            Load(saved, out var timing, out var score);
            CutsceneCommand Body(double time) => Draw(StoryScene.Closing, time, timing, score)
                .Single(command => command.Primitive == CutscenePrimitive.Rectangle && command.Ink == CutsceneInk.DarkRed40
                    && Math.Abs(command.Width / command.Height - 38d / 68) < 1e-6);
            CutsceneCommand Page(double time) => Draw(StoryScene.Closing, time, timing, score).Single(IsGradePage);

            var phone = new List<(double, double)>();
            for (double time = score.PhoneAt; time <= score.PhoneAt + score.Reframe + 1e-9; time += Frame / 4)
            {
                var body = Bounds(Body(time));
                phone.Add((body.CenterX, body.CenterY));
                Assert.That(body.OnScreen, Is.True, $"phone on screen at {time:F3}s");
            }
            Assert.That(ApproachesCenterInsideTheFrame(phone, out var problem), Is.True, problem);
            var filled = Body(score.PhoneAt + score.Reframe);
            Assert.That(Bounds(filled).CenterX, Is.EqualTo(W / 2).Within(.01));
            Assert.That(Bounds(filled).CenterY, Is.EqualTo(H / 2).Within(.01));
            Assert.That(filled.Width, Is.EqualTo(38 * 17).Within(.01), "The phone ends 17 times its drawn size.");

            var page = new List<(double, double)>();
            for (double time = score.GradeAt - score.Reframe; time <= score.GradeAt + 1e-9; time += Frame / 4)
                page.Add((Bounds(Page(time)).CenterX, Bounds(Page(time)).CenterY));
            Assert.That(ApproachesCenterInsideTheFrame(page, out problem), Is.True, problem);
            var close = Bounds(Page(score.GradeAt));
            Assert.That(close.CenterX, Is.EqualTo(W / 2).Within(.01));
            Assert.That(close.CenterY, Is.EqualTo(H / 2).Within(.01));
            Assert.That(close.Left, Is.GreaterThan(0));
            Assert.That(close.Right, Is.LessThan(W));
            Assert.That(close.Right - close.Left, Is.GreaterThan(W * .7), "The grade page fills most of the width in its close-up.");

            // Broken on purpose: the path that interpolated zoom and world focus separately. It threw
            // the phone out past the right edge and showed a screen-filling grade instead.
            var swept = new List<(double, double)>();
            for (int step = 0; step <= 42; step++)
            {
                double p = CutsceneTimeline.EaseInOut(step / 42d);
                swept.Add((390 + 173 * (1 - p) * (1 + 16 * p), 844 + 66 * (1 - p) * (1 + 16 * p)));
            }
            Assert.That(ApproachesCenterInsideTheFrame(swept, out problem), Is.False, "Negative control: the swept path must be caught.");
            Assert.That(problem, Does.Contain("away from center").Or.Contain("left the frame"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CameraMovesAreEasedInAndOutOverPointSevenSeconds(bool saved)
        {
            Load(saved, out var timing, out var score);
            Assert.That(score.Reframe, Is.EqualTo(.7));
            double Opening(double time) => CutsceneTimeline.OpeningCamera(time, timing, score, CutsceneTimeline.Evaluate(time, timing)).Zoom;
            double Room(double time) => CutsceneTimeline.RoomCamera(StoryScene.Closing, time, score).Zoom;
            void Eased(Func<double, double> zoom, double start, double from, double to, string name)
            {
                double Progress(double time) => (zoom(time) - from) / (to - from);
                Assert.That(Progress(start), Is.EqualTo(0).Within(1e-9), name + " start");
                Assert.That(Progress(start + .175), Is.EqualTo(.15625).Within(1e-9), name + " quarter (ease-in)");
                Assert.That(Progress(start + .35), Is.EqualTo(.5).Within(1e-9), name + " midpoint");
                Assert.That(Progress(start + .525), Is.EqualTo(.84375).Within(1e-9), name + " three quarters (ease-out)");
                Assert.That(Progress(start + .7), Is.EqualTo(1).Within(1e-9), name + " end");
            }
            Eased(Opening, 2.5, 2.7, 1.85, "desk to board");
            Eased(Opening, 6, 1.85, 2.7, "board to desk");
            Eased(Opening, 13, 2.7, Math.Max(780 / 40d, 1688 / 72d) * 1.02, "into the phone");
            Eased(Room, 7.3, 1.5, 4.34, "into the grade");
            Eased(Room, 9.5, 4.34, 1.5, "back to the room");
            Eased(Room, 10.2, 1.5, 17, "into the closing phone");
            Assert.That(Room(0), Is.EqualTo(1.5));
            Assert.That(CutsceneTimeline.RoomCamera(StoryScene.Montage, 3, score).Zoom, Is.EqualTo(1.5), "The montage camera never moves.");
            Assert.That(CutsceneTimeline.RoomCamera(StoryScene.Montage, 3, score).OffsetX,
                Is.EqualTo(CutsceneTimeline.RoomCamera(StoryScene.Closing, 0, score).OffsetX), "The closing opens on the montage's own camera.");
            Assert.That(CutsceneTimeline.RoomCamera(StoryScene.Montage, 3, score).OffsetY,
                Is.EqualTo(CutsceneTimeline.RoomCamera(StoryScene.Closing, 0, score).OffsetY));
        }

        // ---------------------------------------------------------------- element entrances

        [TestCase(false)]
        [TestCase(true)]
        public void ElementsEnterEasedOutAndLeaveEasedIn(bool saved)
        {
            Load(saved, out var timing, out var score);
            // ease-out reaches 1-(1-.5)^3 = .875 at half time; ease-in has dropped .5^2 = .25 at half time.
            for (int index = 0; index < 3; index++)
            {
                double start = timing.BubbleStarts[index], end = index < 2 ? timing.BubbleStarts[index + 1] : timing.BubbleFadeEnd;
                CutsceneCommand Bubble(double time) => Draw(StoryScene.Opening, time, timing, score)
                    .Single(command => command.Primitive == CutscenePrimitive.ThoughtBubble);
                Assert.That(Bubble(start + .2).Alpha, Is.EqualTo(.875).Within(1e-9), $"bubble {index} entering");
                Assert.That(Bubble(start + .4).Alpha, Is.EqualTo(1).Within(1e-9), $"bubble {index} in after .4 s");
                Assert.That(Bubble(end - .15).Alpha, Is.EqualTo(.75).Within(1e-9), $"bubble {index} leaving");
                Assert.That(Draw(StoryScene.Opening, start - .001, timing, score).Count(command =>
                    command.Primitive == CutscenePrimitive.ThoughtBubble && command.Text == Bubble(start + .2).Text), Is.Zero);
            }
            CutsceneCommand Caption(double time) => Draw(StoryScene.Closing, time, timing, score).Single(command => command.Text == "三个月后");
            Assert.That(Caption(0).Alpha, Is.Zero);
            Assert.That(Caption(.225).Alpha, Is.EqualTo(.875).Within(1e-9));
            Assert.That(Caption(.45).Alpha, Is.EqualTo(1).Within(1e-9));
            Assert.That(Caption(4.85).Alpha, Is.EqualTo(.75).Within(1e-9));
            Assert.That(Draw(StoryScene.Closing, 5, timing, score).Any(command => command.Text == "三个月后"), Is.False);

            // The page drops 230 world pixels onto the table, ease-out over .45 s.
            double PageTop(double time)
            {
                var camera = CutsceneTimeline.RoomCamera(StoryScene.Closing, time, score);
                var page = Draw(StoryScene.Closing, time, timing, score).Single(IsGradePage);
                return (page.Y - camera.OffsetY) / camera.Zoom;
            }
            double rest = PageTop(5.45);
            Assert.That(rest - PageTop(5), Is.EqualTo(230).Within(1e-6));
            Assert.That(rest - PageTop(5.225), Is.EqualTo(230 * .125).Within(1e-6));
            Assert.That(PageTop(9), Is.EqualTo(rest).Within(1e-6), "The page stays on the table.");
            Assert.That(PageTop(12.9), Is.EqualTo(rest).Within(1e-6));

            // The grade is printed in during the push: it never pops from nothing to full strength.
            CutsceneCommand Grade(double time) => Draw(StoryScene.Closing, time, timing, score).Single(command => command.Text == "58");
            Assert.That(Grade(7.5).Alpha, Is.Zero);
            Assert.That(Grade(7.775).Alpha, Is.EqualTo(.875).Within(1e-9));
            Assert.That(Grade(8).Alpha, Is.EqualTo(1).Within(1e-9));
            double before = 0;
            for (double time = 7; time <= 8.2; time += Frame)
            {
                double alpha = Grade(time).Alpha;
                Assert.That(alpha, Is.GreaterThanOrEqualTo(before - 1e-9), $"grade never fades back at {time:F3}s");
                Assert.That(alpha - before, Is.LessThan(.2), $"grade never jumps in at {time:F3}s");
                before = alpha;
            }
        }

        // ---------------------------------------------------------------- the teacher's arm

        private static List<string> TeacherArmProblems(CutsceneTiming timing, PortraitScore score)
        {
            var problems = new List<string>();
            (double X, double Y)? lastHand = null;
            double minimum = double.MaxValue, maximum = double.MinValue, lastRotation = 0;
            for (int step = 0; step <= 600; step++)
            {
                double time = step * Frame;
                var commands = Draw(StoryScene.Opening, time, timing, score);
                var camera = CutsceneTimeline.OpeningCamera(time, timing, score, CutsceneTimeline.Evaluate(time, timing));
                var bones = Bones(commands, 5);
                if (bones.Length != 10) { problems.Add($"{time:F3}s: teacher has {bones.Length} bones"); break; }
                var upper = bones[4];
                var lower = bones[5];
                var torso = bones[8];
                double upperLength = upper.Height / camera.Zoom, lowerLength = lower.Height / camera.Zoom, torsoLength = torso.Height / camera.Zoom;
                if (upperLength > 70 || lowerLength > 70) problems.Add($"{time:F3}s: arm bones {upperLength:F0}/{lowerLength:F0} are longer than 70");
                if (upperLength + lowerLength > torsoLength) problems.Add($"{time:F3}s: the arm is longer than the torso");
                var elbowA = BoneEnd(upper, 1);
                var elbowB = BoneEnd(lower, -1);
                if (Math.Abs(elbowA.X - elbowB.X) > 1e-6 || Math.Abs(elbowA.Y - elbowB.Y) > 1e-6) problems.Add($"{time:F3}s: the elbow is disjointed");
                var shoulder = BoneEnd(upper, -1);
                var handScreen = BoneEnd(lower, 1);
                var hand = ((handScreen.X - shoulder.X) / camera.Zoom, (handScreen.Y - shoulder.Y) / camera.Zoom);
                bool writing = time >= timing.WritingStart && time <= timing.WritingEnd;
                if (lastHand.HasValue)
                {
                    double moved = Math.Sqrt(Math.Pow(hand.Item1 - lastHand.Value.X, 2) + Math.Pow(hand.Item2 - lastHand.Value.Y, 2));
                    if (moved > (writing ? 3 : 25)) problems.Add($"{time:F3}s: the hand jumps {moved:F1} px in one frame");
                    if (Math.Abs(Math.IEEERemainder(lower.Rotation - lastRotation, 360)) > 20) problems.Add($"{time:F3}s: the forearm snaps round");
                }
                lastHand = hand;
                lastRotation = lower.Rotation;
                if (writing)
                {
                    minimum = Math.Min(minimum, lower.Rotation);
                    maximum = Math.Max(maximum, lower.Rotation);
                }
                // Raising and lowering stay on the board's side and never swing over the shoulder.
                if (hand.Item2 < -90) problems.Add($"{time:F3}s: the hand rises {-hand.Item2:F0} px above the shoulder");
                if (hand.Item1 > 20) problems.Add($"{time:F3}s: the hand swings {hand.Item1:F0} px away from the board");
            }
            if (maximum - minimum > 30) problems.Add($"the forearm sweeps {maximum - minimum:F0} degrees while writing");
            if (maximum - minimum < 2) problems.Add("the forearm does not move while writing");
            return problems;
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TeacherWritesWithAHumanSizedArmThatStaysAtTheBoard(bool saved)
        {
            Load(saved, out var timing, out var score);
            Assert.That(TeacherArmProblems(timing, score), Is.Empty);

            // Broken on purpose: the stretched arm that reached a far target.
            score.UpperArm = 158; score.Forearm = 155; score.ChalkTargetX = -111; score.ChalkTargetY = -166;
            Assert.That(TeacherArmProblems(timing, score), Has.Some.Contains("longer than 70").And.Some.Contains("longer than the torso"));

            Load(saved, out timing, out score);
            // Broken on purpose: the hand chasing each formula along the board.
            score.ChalkSweep = 60;
            Assert.That(TeacherArmProblems(timing, score), Has.Some.Contains("degrees while writing").Or.Some.Contains("jumps"));

            Load(saved, out timing, out score);
            // Broken on purpose: a resting pose from which the arm lifts over the head, away from the board.
            score.ArmRestUpper = -1.2; score.ArmRestLower = -.2;
            Assert.That(TeacherArmProblems(timing, score), Has.Some.Contains("above the shoulder").Or.Some.Contains("away from the board"));
        }

        // ---------------------------------------------------------------- montage: light, shadows, figure

        private static int DarkSpells(PortraitScore score)
        {
            int spells = 0;
            bool bright = true;
            for (double time = 0; time <= score.MontageEnd; time += .001)
            {
                bool now = score.Bright(time);
                if (bright && !now) spells++;
                bright = now;
            }
            return spells;
        }

        private static bool HalfPeriodsNeverLengthen(double[] cuts)
        {
            for (int index = 3; index < cuts.Length; index++)
                if (cuts[index] - cuts[index - 1] > cuts[index - 1] - cuts[index - 2] + 1e-9) return false;
            return true;
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MontageLightCyclesFourTimesEachNoSlowerThanTheLast(bool saved)
        {
            Load(saved, out _, out var score);
            Assert.That(score.LightCuts[1], Is.EqualTo(1.5), "The light first changes at 1.5 s.");
            Assert.That(DarkSpells(score), Is.InRange(3, 4), "The spec asks for three or four light cycles.");
            Assert.That(HalfPeriodsNeverLengthen(score.LightCuts), Is.True);
            foreach (var cut in new[] { score.SecondPose, score.ThirdPose })
            {
                Assert.That(score.Pose(cut), Is.EqualTo(score.Pose(cut - .001) + 1));
                Assert.That(score.Bright(cut), Is.Not.EqualTo(score.Bright(cut - .001)), "Each pose change lands on a light change.");
            }
            Assert.That(score.Bright(0), Is.True);
            Assert.That(score.Bright(score.LightCuts[score.LightCuts.Length - 1]), Is.True, "The light ends at its brightest.");
            Assert.That(score.Bright(score.Stop), Is.True);
            Assert.That(score.Bright(score.MontageEnd), Is.True);

            // Broken on purpose: the first delivery lengthened a half-period; the second ran six cycles.
            Assert.That(HalfPeriodsNeverLengthen(new[] { 0, 1.5, 2, 2.8, 3.45, 4, 4.55, 5.03, 5.42 }), Is.False);
            score.LightCuts = new[] { 0, 1.5, 2, 2.46, 2.89, 3.29, 3.66, 4, 4.32, 4.62, 4.90, 5.16, 5.40 };
            Assert.That(DarkSpells(score), Is.EqualTo(6), "Negative control: six cycles must be counted as six.");
        }

        [Test]
        public void MontageClockAcceleratesThenFreezesWithTheRoomTone()
        {
            var score = new PortraitScore();
            Assert.That(score.Pose(0), Is.Zero);
            Assert.That(score.Turns(1), Is.EqualTo(.25));
            Assert.That(score.Turns(5.4) - score.Turns(5.1), Is.EqualTo(1).Within(1e-9));
            Assert.That(score.Turns(score.Stop), Is.EqualTo(score.Turns(score.MontageEnd)));
            Assert.That(score.MontageRoomGain(5.95), Is.GreaterThan(score.MontageRoomGain(1)));
            Assert.That(score.MontageEnd, Is.EqualTo(6));
            Assert.That(score.ClosingEnd, Is.EqualTo(13));
        }

        private static double Luminance(Color32 color)
        {
            double Channel(byte value)
            {
                double c = value / 255d;
                return c <= .04045 ? c / 12.92 : Math.Pow((c + .055) / 1.055, 2.4);
            }
            return .2126 * Channel(color.r) + .7152 * Channel(color.g) + .0722 * Channel(color.b);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MontageShadowsAreThreeFadingLayersTrailingFourPixelsApart(bool saved)
        {
            Load(saved, out var timing, out var score);
            var inks = new[] { CutsceneInk.Shadow30, CutsceneInk.Shadow15, CutsceneInk.Shadow08 };
            foreach (int pass in new[] { 0, 1 })
            {
                var commands = Draw(StoryScene.Montage, score.Passes[pass] + score.PassDuration / 2, timing, score);
                var heads = commands.Where(command => command.Primitive == CutscenePrimitive.Circle && command.SubjectId is >= 6 and <= 8)
                    .OrderBy(command => command.SubjectId).ToArray();
                Assert.That(heads.Select(head => head.Ink), Is.EqualTo(inks));
                // Odd passes cross right to left, so their dimmer layers trail on the right.
                double trail = pass % 2 == 0 ? -4 : 4;
                Assert.That(heads[1].X - heads[0].X, Is.EqualTo(trail).Within(1e-6), $"pass {pass}: second layer trails by 4 px");
                Assert.That(heads[2].X - heads[0].X, Is.EqualTo(trail * 2).Within(1e-6), $"pass {pass}: third layer trails by 8 px");
            }
            // Each layer is brighter than the wall behind it and dimmer than the layer in front.
            double wall = Luminance(CutsceneGraphic.ResolveInk(CutsceneInk.DarkRed15));
            double first = Luminance(CutsceneGraphic.ResolveInk(CutsceneInk.Shadow30));
            double second = Luminance(CutsceneGraphic.ResolveInk(CutsceneInk.Shadow15));
            double third = Luminance(CutsceneGraphic.ResolveInk(CutsceneInk.Shadow08));
            Assert.That(first, Is.GreaterThan(second));
            Assert.That(second, Is.GreaterThan(third));
            Assert.That(third, Is.GreaterThan(wall));
            Assert.That(CutsceneGraphic.ResolveInk(CutsceneInk.Shadow30), Is.EqualTo(new Color32(0x3F, 0x1C, 0x1C, 255)));
            Assert.That(CutsceneGraphic.ResolveInk(CutsceneInk.Shadow15), Is.EqualTo(new Color32(0x2F, 0x15, 0x15, 255)));
            Assert.That(CutsceneGraphic.ResolveInk(CutsceneInk.Shadow08), Is.EqualTo(new Color32(0x27, 0x12, 0x12, 255)));
            // Broken on purpose: the swatches that vanished into the wall.
            Assert.That(Luminance(new Color32(0x1E, 0x0F, 0x0F, 255)), Is.Not.GreaterThan(wall));
            Assert.That(Luminance(new Color32(0x15, 0x0B, 0x0B, 255)), Is.LessThan(wall));
        }

        private struct Figure
        {
            public (double X, double Y) Hip, Shoulder, Head, LeftKnee, RightKnee, LeftFoot, RightFoot, LeftHand, RightHand, Phone;
            public double[] ArmBones;
        }

        private static Figure SeatedFigure(double time, CutsceneTiming timing, PortraitScore score)
        {
            var commands = Draw(StoryScene.Montage, time, timing, score);
            var camera = CutsceneTimeline.RoomCamera(StoryScene.Montage, time, score);
            (double, double) World((double X, double Y) point) => ((point.X - camera.OffsetX) / camera.Zoom, (point.Y - camera.OffsetY) / camera.Zoom);
            var bones = Bones(commands, 1);
            Assert.That(bones.Length, Is.EqualTo(10));
            var head = commands.Single(command => command.SubjectId == 1 && command.Primitive == CutscenePrimitive.Circle);
            var phone = commands.Single(command => command.SubjectId == 2 && command.Ink == CutsceneInk.Red);
            return new Figure
            {
                Shoulder = World(BoneEnd(bones[8], -1)), Hip = World(BoneEnd(bones[8], 1)), Head = World((head.X, head.Y)),
                LeftKnee = World(BoneEnd(bones[0], 1)), LeftFoot = World(BoneEnd(bones[1], 1)),
                RightKnee = World(BoneEnd(bones[2], 1)), RightFoot = World(BoneEnd(bones[3], 1)),
                LeftHand = World(BoneEnd(bones[5], 1)), RightHand = World(BoneEnd(bones[7], 1)),
                Phone = World((Bounds(phone).CenterX, Bounds(phone).CenterY)),
                ArmBones = new[] { bones[4], bones[5], bones[6], bones[7] }.Select(bone => bone.Height / camera.Zoom).ToArray()
            };
        }

        private static double Distance((double X, double Y) a, (double X, double Y) b) =>
            Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

        private static List<string> SeatedFigureProblems(Figure figure)
        {
            var problems = new List<string>();
            // The seat is the 155 x 17 slab whose top is at world y = 1059, from x = 167.
            if (figure.Hip.Y < 1048 || figure.Hip.Y > 1062 || figure.Hip.X < 190 || figure.Hip.X > 300) problems.Add("the hip is not on the seat");
            if (Distance(figure.Shoulder, figure.Head) > 50) problems.Add($"the neck is {Distance(figure.Shoulder, figure.Head):F0} px long");
            double torso = Distance(figure.Shoulder, figure.Hip);
            if (torso < 115 || torso > 135) problems.Add($"the torso is {torso:F0} px long");
            foreach (var knee in new[] { figure.LeftKnee, figure.RightKnee })
                if (knee.X - figure.Hip.X < 55 || Math.Abs(knee.Y - figure.Hip.Y) > 14) problems.Add("a thigh does not lie along the seat");
            foreach (var foot in new[] { figure.LeftFoot, figure.RightFoot })
                if (Math.Abs(foot.Y - (CutsceneTimeline.RoomFloorWorldY - 5)) > 1e-6) problems.Add("a foot is off the floor");
            if (figure.ArmBones.Any(length => length > 75)) problems.Add($"an arm bone is {figure.ArmBones.Max():F0} px long");
            return problems;
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MontageFigureSitsOnTheChairThroughTheThreeSpecifiedPoses(bool saved)
        {
            Load(saved, out var timing, out var score);
            var first = SeatedFigure(.5, timing, score);
            var second = SeatedFigure(2.5, timing, score);
            var third = SeatedFigure(4.5, timing, score);
            foreach (var figure in new[] { first, second, third }) Assert.That(SeatedFigureProblems(figure), Is.Empty);

            // 1: both hands on the phone at the chest, head a little forward and down.
            Assert.That(first.Phone.Y, Is.InRange(first.Shoulder.Y, first.Hip.Y), "pose 1: phone at chest height");
            Assert.That(first.Phone.Y - 43, Is.GreaterThan(first.Head.Y), "pose 1: the phone does not cover the face");
            Assert.That(Distance(first.LeftHand, first.Phone), Is.LessThan(50), "pose 1: left hand on the phone");
            Assert.That(Distance(first.RightHand, first.Phone), Is.LessThan(50), "pose 1: right hand on the phone");
            Assert.That(first.Head.X, Is.GreaterThan(first.Shoulder.X), "pose 1: head tipped forward");
            // 2: one hand holds the phone low, the other props the head.
            Assert.That(Distance(second.LeftHand, second.Head), Is.LessThan(30), "pose 2: a hand at the head");
            Assert.That(Distance(second.RightHand, second.Head), Is.GreaterThan(60), "pose 2: only one hand at the head");
            Assert.That(second.Phone.Y, Is.GreaterThan(second.Shoulder.Y + 40), "pose 2: phone held low");
            Assert.That(second.Shoulder.X, Is.GreaterThan(first.Shoulder.X + 20), "pose 2: slumped forward");
            // 3: leaning back, legs pushed out, phone up at eye level.
            Assert.That(third.Shoulder.X, Is.LessThan(third.Hip.X - 20), "pose 3: leaning back");
            Assert.That(third.LeftFoot.X, Is.GreaterThan(first.LeftFoot.X + 50), "pose 3: legs pushed out");
            Assert.That(third.RightFoot.X, Is.GreaterThan(first.RightFoot.X + 50));
            Assert.That(Math.Abs(third.Phone.Y - third.Head.Y), Is.LessThan(30), "pose 3: phone at eye level");

            // Broken on purpose: the first delivery's figure, with its head lifted 82 px off the
            // shoulders and its thighs hanging straight down from the seat.
            var stalk = first;
            stalk.Head = (stalk.Shoulder.X + 23, stalk.Shoulder.Y - 79);
            Assert.That(SeatedFigureProblems(stalk), Has.Some.Contains("neck"));
            var standing = first;
            standing.LeftKnee = (standing.Hip.X - 49, standing.Hip.Y + 53);
            Assert.That(SeatedFigureProblems(standing), Has.Some.Contains("thigh"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MontageLightChangesOnlyTheSeatedFigureAndTableTop(bool saved)
        {
            Load(saved, out var timing, out var score);
            var opposite = JsonUtility.FromJson<PortraitScore>(JsonUtility.ToJson(score));
            const double time = 1.9;
            Assert.That(score.Bright(time), Is.False);
            opposite.LightCuts = new[] { 0d };
            var bright = Draw(StoryScene.Montage, time, timing, opposite);
            var dark = Draw(StoryScene.Montage, time, timing, score);
            Assert.That(bright.Count, Is.EqualTo(dark.Count));
            int tableLayers = 0, figureShapes = 0;
            for (int index = 0; index < bright.Count; index++)
            {
                var command = bright[index];
                if (command.SubjectId == 1)
                {
                    Assert.That(command.Ink, Is.EqualTo(CutsceneInk.Bone));
                    Assert.That(dark[index].Ink, Is.EqualTo(CutsceneInk.Bone60));
                    figureShapes++;
                }
                else if (command.Ink != dark[index].Ink)
                {
                    Assert.That(command.Primitive, Is.EqualTo(CutscenePrimitive.Rectangle));
                    Assert.That(command.Ink, Is.EqualTo(CutsceneInk.DarkRed));
                    Assert.That(dark[index].Ink, Is.EqualTo(CutsceneInk.DarkRed60).Or.EqualTo(CutsceneInk.DarkRed40));
                    tableLayers++;
                }
                command.Ink = dark[index].Ink;
                Assert.That(command, Is.EqualTo(dark[index]));
            }
            Assert.That(tableLayers, Is.EqualTo(2), "Only the table top and the book on it catch the light.");
            Assert.That(figureShapes, Is.GreaterThan(10));
        }

        // ---------------------------------------------------------------- closing: the same room, empty

        [TestCase(false)]
        [TestCase(true)]
        public void ClosingOpensOnTheMontageRoomWithTheChairEmpty(bool saved)
        {
            Load(saved, out var timing, out var score);
            var montage = Draw(StoryScene.Montage, 0, timing, score);
            var closing = Draw(StoryScene.Closing, 0, timing, score);
            CutsceneCommand Seat(List<CutsceneCommand> commands) => commands.Single(command => command.Primitive == CutscenePrimitive.Rectangle
                && command.Ink == CutsceneInk.DarkRed && command.Width / command.Height is > 9 and < 9.2);
            Assert.That(Seat(closing), Is.EqualTo(Seat(montage)));
            Assert.That(montage.Any(command => command.SubjectId == 1), Is.True);
            Assert.That(closing.Any(command => command.SubjectId == 1), Is.False, "The seat is empty three months later.");
            // Wall, floor line, disc, chair: the first shapes of both scenes are the same shapes in the same places.
            for (int index = 0; index < 12; index++)
            {
                var a = montage[index];
                var b = closing[index];
                if (a.Primitive == CutscenePrimitive.Line && a.Ink == CutsceneInk.DarkRed) continue; // the disc's pointer turns
                Assert.That(b, Is.EqualTo(a), $"room shape {index}");
            }
            foreach (double time in new[] { score.GradeAt, score.PhoneAt, score.PhoneAt + score.Reframe })
            {
                var commands = Draw(StoryScene.Closing, time, timing, score);
                Assert.That(commands.Count(command => command.Text == "58"), Is.EqualTo(1), "The grade page stays on the table.");
                Assert.That(Seat(commands).Width == Seat(closing).Width, Is.EqualTo(time == score.PhoneAt),
                    "The whole room moves with the camera and is back in place when the phone lights.");
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RoomScenesFillTheFrameFromWallToFloor(bool saved)
        {
            Load(saved, out var timing, out var score);
            foreach (var scene in new[] { StoryScene.Montage, StoryScene.Closing })
                for (double time = 0; time <= Duration(scene, timing, score); time += Frame)
                {
                    var commands = Draw(scene, time, timing, score);
                    var wall = commands[1];
                    Assert.That(wall.Ink, Is.EqualTo(CutsceneInk.DarkRed15));
                    Assert.That(wall.X, Is.Zero);
                    Assert.That(wall.Y, Is.Zero, "The wall reaches the top of the frame.");
                    Assert.That(wall.Width, Is.EqualTo(W));
                    Assert.That(LargestInkBand(commands), Is.LessThan(.15), $"{scene} at {time:F3}s");
                }
            // Broken on purpose: the first delivery's band of wall between two slabs of ink.
            var band = new List<CutsceneCommand>
            {
                new CutsceneCommand { Primitive = CutscenePrimitive.Rectangle, Ink = CutsceneInk.Ink, Width = W, Height = H, Alpha = 1 },
                new CutsceneCommand { Primitive = CutscenePrimitive.Rectangle, Ink = CutsceneInk.DarkRed15, Y = 268, Width = W, Height = 900, Alpha = 1 }
            };
            Assert.That(LargestInkBand(band), Is.GreaterThan(.30), "Negative control: a banded room must be caught.");
        }

        // ---------------------------------------------------------------- no blackout

        private static bool HasBlackCover(IEnumerable<CutsceneCommand> commands) => commands.Skip(1).Any(command =>
            command.Primitive == CutscenePrimitive.Rectangle && command.Ink == CutsceneInk.Ink && command.Alpha >= 1
            && command.X <= 0 && command.Y <= 0 && command.X + command.Width >= W && command.Y + command.Height >= H);

        [TestCase(StoryScene.Opening)]
        [TestCase(StoryScene.Montage)]
        [TestCase(StoryScene.Closing)]
        public void NoSceneEverCoversThePortraitWithAnAuthoredBlackRectangle(StoryScene scene)
        {
            Load(true, out var timing, out var score);
            var commands = new List<CutsceneCommand>();
            for (double time = 0; time <= Duration(scene, timing, score); time += Frame)
            {
                commands = Draw(scene, time, timing, score);
                Assert.That(commands[0].Ink, Is.EqualTo(CutsceneInk.Ink), "The first shape is the ink ground.");
                Assert.That(HasBlackCover(commands), Is.False, $"at {time:F3}s");
            }
            commands.Add(new CutsceneCommand { Ink = CutsceneInk.Ink, Primitive = CutscenePrimitive.Rectangle, Width = W, Height = H, Alpha = 1 });
            Assert.That(HasBlackCover(commands), Is.True, "Negative control: injected portrait blackout.");
        }

        [Test]
        public void SeekingToTheSameTimeRedrawsExactlyTheSameCommands()
        {
            Load(true, out var timing, out var score);
            foreach (var scene in new[] { StoryScene.Opening, StoryScene.Montage, StoryScene.Closing })
                foreach (double time in new[] { .7, 2.2, 4.8, 5.9, 10.4, 12.8 })
                {
                    if (time > Duration(scene, timing, score)) continue;
                    var first = Draw(scene, time, timing, score);
                    Draw(scene, Duration(scene, timing, score), timing, score);
                    Draw(scene, 0, timing, score);
                    Assert.That(Draw(scene, time, timing, score), Is.EqualTo(first), $"{scene} at {time}s");
                }
        }

        [Test]
        public void ForegroundDeskIsPaintedOverTheStudentAndThePhone()
        {
            Load(true, out var timing, out var score);
            for (double time = timing.StudentEnter; time < timing.PhoneAppStart; time += Frame)
            {
                var commands = Draw(StoryScene.Opening, time, timing, score);
                int lastStudent = commands.FindLastIndex(command => command.SubjectId == 1 || command.SubjectId == 2);
                int desk = commands.FindLastIndex(command => command.Primitive == CutscenePrimitive.Rectangle && command.Ink == CutsceneInk.DarkRed
                    && Math.Abs(command.Width / command.Height - 500d / 115) < 1e-6);
                Assert.That(desk, Is.GreaterThan(lastStudent), $"at {time:F3}s");
            }
        }
    }
}
