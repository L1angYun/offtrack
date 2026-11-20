using System;
using System.Collections.Generic;

namespace Offtrack.Core
{
    // Authored beats and motion controls of the opening. The montage and the closing are scored
    // by PortraitScore; the runtime config serializes both.
    [Serializable]
    public sealed class CutsceneTiming
    {
        public double OpeningDuration = 16;
        public double StudentEnter = .8;
        public double OpeningWalkDuration = 1.6;
        public double OpeningSitStart = 2.4;
        public double OpeningSitEnd = 3.2;
        public double WritingStart = 3.2;
        public double WritingEnd = 6;
        // Replaced with actual serif glyph advances when the cutscene assets are generated.
        public double[] FormulaWidths = { 227, 228, 225, 143 };
        public double TeacherArmEnd = 6.2;
        public double[] BubbleStarts = { 6, 8, 10 };
        public double BubbleFadeEnd = 11.6;
        public double PhoneStart = 11.6;
        public double PhoneEnd = 13;
        public double PhoneAppStart = 13;
        public double CameraPushStart = 6;
        public double FloorY = 840;
        public double StudentStartX = 1720;
        public double StudentSeatX = 1180;
        public double StandingHipY = 690;
        public double SeatedHipY = 745;
        public double WalkBobAmplitude = 3.5;
        public double WalkSwingAmplitude = 28;
        public double WalkAngularSpeed = 9;
        public double BreathAmplitude = 1.5;
        public double BreathAngularSpeed = 2.2;
        public double SitAnticipationDuration = .16;
        public double SitAnticipationDepth = 7;
        public double SitReboundDuration = .28;
        public double SitReboundAmplitude = 1.5;
        public double PoseBlendDuration = .3;
    }

    public enum CutsceneInk
    {
        Ink, Red, Bone, DarkRed, DarkRed60, DarkRed40, DarkRed15,
        Bone60, Bone40, Bone15, Red60, Red40, Red15, Shadow30, Shadow15, Shadow08
    }
    public enum CutscenePrimitive { Rectangle, Outline, Circle, Ring, Line, Label, RotatedRectangle, ThoughtBubble }
    public enum CutsceneTextAlign { Left, Center, Right }
    public enum CutscenePose { Walking, Seated, Slouch, Clock, Reach, Phone }

    public struct CutsceneCommand
    {
        public CutscenePrimitive Primitive;
        public CutsceneInk Ink;
        public double X, Y, Width, Height, Stroke, Alpha, Scale;
        // Clockwise around a rectangle's center in the reference canvas's y-down coordinates.
        public double Rotation;
        // 1 student, 2 phone, 4 grade, 5 teacher, 6-8 passing shadows. Zero denotes scenery or typography.
        public int SubjectId;
        public bool ScreenSpace;
        public string Text;
        public int FontSize;
        public bool Serif, HasClip;
        public CutsceneTextAlign Align;
        public double ClipX, ClipY, ClipWidth, ClipHeight;
    }

    public struct CutsceneFrame
    {
        public bool StudentVisible, PhoneVisible;
        public double Time, StudentX, StudentBottom, WalkTime, Seated, Writing;
        public double HeadX, HeadY, RightHandX, RightHandY, PhoneX, PhoneY, Breath, WalkBob, ClockMinuteAngle;
        public CutscenePose Pose;
    }

    // A complete frame is a pure function of authored time, including its camera and joints.
    public static partial class CutsceneTimeline
    {
        private static readonly string[] FormulaText = { "∫ x² dx = x³ / 3", "lim  sinx / x = 1", "Σ 1/n² = π² / 6", "(x²)' = 2x" };
        // The board's rows as authored, before any camera: their reveal widths are measured from these.
        public const int FormulaFontSize = 34;
        public static IReadOnlyList<string> Formulas => FormulaText;
        private static readonly string[] BubbleText = { "好不想听啊", "能不能跳过。", "听会歌吧" };

        public static double Clamp(double value, double min, double max) => Math.Min(max, Math.Max(min, value));
        public static double Segment(double value, double start, double end) => end <= start ? (value >= end ? 1 : 0) : Clamp((value - start) / (end - start), 0, 1);
        public static double EaseOut(double p) => 1 - Math.Pow(1 - Clamp(p, 0, 1), 3);
        public static double EaseInOut(double p) { p = Clamp(p, 0, 1); return p * p * (3 - 2 * p); }
        public static double EaseIn(double p) { p = Clamp(p, 0, 1); return p * p; }
        internal static double Lerp(double a, double b, double p) => a + (b - a) * p;

        // The opening's people as a pure function of authored time. Cameras live in PortraitScore.
        public static CutsceneFrame Evaluate(double time, CutsceneTiming timing)
        {
            if (timing == null) throw new ArgumentNullException(nameof(timing));
            if (double.IsNaN(time) || double.IsInfinity(time)) throw new ArgumentOutOfRangeException(nameof(time));
            var t = Clamp(time, 0, timing.OpeningDuration);
            var f = new CutsceneFrame
            {
                Time = t,
                ClockMinuteAngle = -90 + t * .35 + 6 * EaseInOut(Segment(t, timing.BubbleStarts[1], timing.BubbleStarts[1] + timing.PoseBlendDuration))
            };
            var walk = Segment(t, timing.StudentEnter, timing.StudentEnter + timing.OpeningWalkDuration);
            f.StudentVisible = t >= timing.StudentEnter;
            f.StudentX = Lerp(timing.StudentStartX, timing.StudentSeatX, EaseInOut(walk));
            f.WalkTime = walk > 0 && walk < 1 ? t - timing.StudentEnter : 0;
            f.WalkBob = f.WalkTime > 0 ? -Math.Abs(Math.Sin(f.WalkTime * timing.WalkAngularSpeed)) * timing.WalkBobAmplitude : 0;
            f.Breath = walk >= 1 ? Math.Sin(t * timing.BreathAngularSpeed) * timing.BreathAmplitude : 0;
            f.Seated = EaseInOut(Segment(t, timing.OpeningSitStart + timing.SitAnticipationDuration, timing.OpeningSitEnd));
            var anticipation = t >= timing.OpeningSitStart && t < timing.OpeningSitStart + timing.SitAnticipationDuration
                ? Math.Sin(Math.PI * Segment(t, timing.OpeningSitStart, timing.OpeningSitStart + timing.SitAnticipationDuration)) * timing.SitAnticipationDepth : 0;
            var rebound = t >= timing.OpeningSitEnd && t < timing.OpeningSitEnd + timing.SitReboundDuration
                ? Math.Sin(Math.PI * Segment(t, timing.OpeningSitEnd, timing.OpeningSitEnd + timing.SitReboundDuration)) * timing.SitReboundAmplitude : 0;
            f.StudentBottom = Lerp(timing.StandingHipY, timing.SeatedHipY, f.Seated) + f.WalkBob + f.Breath + anticipation - rebound;
            f.Pose = walk < 1 ? CutscenePose.Walking : CutscenePose.Seated;
            f.Writing = Segment(t, timing.WritingStart, timing.WritingEnd);
            f.PhoneVisible = t >= timing.PhoneStart;
            if (t >= timing.BubbleStarts[0]) f.Pose = CutscenePose.Slouch;
            if (t >= timing.BubbleStarts[1]) f.Pose = CutscenePose.Clock;
            if (t >= timing.BubbleStarts[2]) f.Pose = CutscenePose.Reach;
            if (t >= timing.PhoneStart) f.Pose = CutscenePose.Phone;
            var rig = StudentRig(f, timing);
            f.HeadX = rig.Head.X; f.HeadY = rig.Head.Y;
            f.RightHandX = rig.RightHand.X; f.RightHandY = rig.RightHand.Y;
            f.PhoneX = rig.RightHand.X; f.PhoneY = rig.RightHand.Y;
            return f;
        }

        public static double FormulaProgress(double p, int index) => Segment(p, index / 4.0, (index + .92) / 4.0);


        private static void Classroom(List<CutsceneCommand> o, CutsceneFrame f, CutsceneTiming timing, PortraitScore score,
            double visibleLeft, double visibleRight)
        {
            Rect(o, 140, 180, 760, 400, CutsceneInk.DarkRed15);
            Rect(o, 140, 586, 760, 8, CutsceneInk.DarkRed);
            var heading = Text("高等数学", 190, 254, 28, CutsceneInk.Bone40, CutsceneTextAlign.Left);
            o.Add(heading);
            Line(o, 190, 278, 850, 278, 2, CutsceneInk.DarkRed60);
            for (var i = 0; i < FormulaText.Length; i++)
            {
                var k = FormulaProgress(f.Writing, i);
                if (k <= 0) continue;
                var y = 365 + i * 54;
                var text = Text(FormulaText[i], 215, y, FormulaFontSize, CutsceneInk.Bone60, CutsceneTextAlign.Left);
                text.Serif = true;
                text.HasClip = true; text.ClipX = 215; text.ClipY = y - 44; text.ClipWidth = timing.FormulaWidths[i] * k; text.ClipHeight = 62;
                o.Add(text);
            }
            Teacher(o, f, timing, score);
            Rect(o, 310, 704, 260, 136, CutsceneInk.DarkRed);
            Line(o, 310, 714, 570, 714, 2, CutsceneInk.Ink);
            // All seated and standing figures share the same floor, regardless of depth cues.
            BackgroundStudent(o, 720, timing.FloorY);
            BackgroundStudent(o, 920, timing.FloorY);
            Desk(o, 630, 735, 170, timing.FloorY);
            Desk(o, 830, 735, 170, timing.FloorY);
            Desk(o, 170, 775, 220, timing.FloorY);
            // A window on the wall between the door and the student's desk: the walk-in passes it.
            Rect(o, 1400, 300, 170, 190, CutsceneInk.DarkRed15);
            o.Add(new CutsceneCommand
            {
                Primitive = CutscenePrimitive.Outline, X = 1400, Y = 300, Width = 170, Height = 190, Stroke = 5,
                Ink = CutsceneInk.DarkRed60, Alpha = 1, Scale = 1
            });
            Line(o, 1485, 300, 1485, 490, 4, CutsceneInk.DarkRed60);
            Line(o, 1400, 395, 1570, 395, 4, CutsceneInk.DarkRed60);
            // The doorway the student walks in through, and the clock above the student's desk.
            o.Add(new CutsceneCommand
            {
                Primitive = CutscenePrimitive.Outline, X = score.OpeningDoorX, Y = score.OpeningDoorTop,
                Width = score.OpeningDoorWidth, Height = timing.FloorY - score.OpeningDoorTop, Stroke = 5,
                Ink = CutsceneInk.DarkRed60, Alpha = 1, Scale = 1
            });
            Circle(o, score.OpeningClockX, score.OpeningClockY, 48, CutsceneInk.DarkRed, ring: true, stroke: 4);
            ClockHand(o, score.OpeningClockX, score.OpeningClockY, 34, f.ClockMinuteAngle, 4);
            ClockHand(o, score.OpeningClockX, score.OpeningClockY, 23, 30 + f.Time * .025, 4);
            ClockHand(o, score.OpeningClockX, score.OpeningClockY, 38, -90 + f.Time * 6, 2);
            // The floor line spans exactly what this frame's camera can see, wherever it looks.
            Line(o, visibleLeft - 4, timing.FloorY, visibleRight + 4, timing.FloorY, 3, CutsceneInk.DarkRed);
        }

        private static void ClockHand(List<CutsceneCommand> o, double x, double y, double length, double degrees, double width)
        {
            var r = degrees * Math.PI / 180;
            Line(o, x, y, x + Math.Cos(r) * length, y + Math.Sin(r) * length, width, CutsceneInk.DarkRed);
        }

        private static void Desk(List<CutsceneCommand> o, double x, double y, double width, double floor)
        {
            Rect(o, x, y, width, 36, CutsceneInk.DarkRed);
            Rect(o, x + 12, y + 36, 8, floor - y - 36, CutsceneInk.DarkRed);
            Rect(o, x + width - 20, y + 36, 8, floor - y - 36, CutsceneInk.DarkRed);
        }

        private static void BackgroundStudent(List<CutsceneCommand> o, double x, double floor)
        {
            var rig = BaseRig(new Point(x, 767), 1, 0, floor);
            rig.Shoulder = new Point(x, 680); rig.Head = new Point(x, 642);
            rig.LeftElbow = new Point(x - 43, 716); rig.LeftHand = new Point(x - 54, 737);
            rig.RightElbow = new Point(x + 43, 716); rig.RightHand = new Point(x + 54, 737);
            DrawRig(o, rig, CutsceneInk.DarkRed, 0, false, .8);
        }

        private static void Teacher(List<CutsceneCommand> o, CutsceneFrame f, CutsceneTiming timing, PortraitScore score)
        {
            var rig = BaseRig(new Point(450, 698 + Math.Sin(f.Time * timing.BreathAngularSpeed) * timing.BreathAmplitude), 0, 0, timing.FloorY);
            var arm = TeacherArm(f.Time, f.Writing, timing, score);
            var origin = rig.Shoulder + new Point(-31, 8);
            rig.LeftElbow = origin + new Point(arm.ElbowX, arm.ElbowY);
            rig.LeftHand = origin + new Point(arm.HandX, arm.HandY);
            DrawRig(o, rig, CutsceneInk.DarkRed, 5, false);
        }

        private static Rig StudentRig(CutsceneFrame f, CutsceneTiming timing)
        {
            var hip = new Point(f.StudentX, f.StudentBottom);
            var swing = f.WalkTime > 0 ? Math.Sin(f.WalkTime * timing.WalkAngularSpeed) * timing.WalkSwingAmplitude : 0;
            var basic = BaseRig(hip, f.Seated, swing, timing.FloorY);
            if (f.Time < timing.BubbleStarts[0]) return basic;
            var slouch = PoseRig(basic, CutscenePose.Slouch);
            if (f.Time < timing.BubbleStarts[1]) return Rig.Mix(basic, slouch, EaseInOut(Segment(f.Time, timing.BubbleStarts[0], timing.BubbleStarts[0] + timing.PoseBlendDuration)));
            var clock = PoseRig(basic, CutscenePose.Clock);
            if (f.Time < timing.BubbleStarts[2]) return Rig.Mix(slouch, clock, EaseInOut(Segment(f.Time, timing.BubbleStarts[1], timing.BubbleStarts[1] + timing.PoseBlendDuration)));
            var reach = PoseRig(basic, CutscenePose.Reach);
            if (f.Time < timing.PhoneStart) return Rig.Mix(clock, reach, EaseInOut(Segment(f.Time, timing.BubbleStarts[2], timing.BubbleStarts[2] + timing.PoseBlendDuration)));
            return Rig.Mix(reach, PoseRig(basic, CutscenePose.Phone), EaseInOut(Segment(f.Time, timing.PhoneStart, timing.PhoneEnd)));
        }

        private static Rig BaseRig(Point hip, double seated, double swing, double floor)
        {
            var shoulder = hip + new Point(0, -130);
            return new Rig
            {
                Hip = hip, Shoulder = shoulder, Head = shoulder + new Point(0, -44),
                LeftElbow = shoulder + new Point(-38 - swing * .4, 60),
                LeftHand = Point.Mix(shoulder + new Point(-42 - swing, 118), new Point(hip.X - 70, hip.Y - 25), seated),
                RightElbow = shoulder + new Point(38 + swing * .4, 60),
                RightHand = Point.Mix(shoulder + new Point(42 + swing, 118), new Point(hip.X + 70, hip.Y - 25), seated),
                LeftKnee = new Point(hip.X - 14 - swing * .6 - 35 * seated, Lerp(hip.Y, floor, .5)),
                RightKnee = new Point(hip.X + 14 + swing * .6 + 35 * seated, Lerp(hip.Y, floor, .5)),
                LeftFoot = new Point(hip.X - 14 - swing - 35 * seated, floor - 5),
                RightFoot = new Point(hip.X + 14 + swing + 35 * seated, floor - 5)
            };
        }

        private static Rig PoseRig(Rig basic, CutscenePose pose)
        {
            var r = basic;
            var h = basic.Hip;
            if (pose == CutscenePose.Slouch)
            {
                r.Shoulder = h + new Point(-35, -110); r.Head = h + new Point(-46, -143);
                r.LeftElbow = h + new Point(-70, -55); r.LeftHand = r.Head + new Point(-18, 18);
                r.RightElbow = h + new Point(28, -53); r.RightHand = h + new Point(65, -25);
            }
            else if (pose == CutscenePose.Clock)
            {
                r.Shoulder = h + new Point(-6, -125); r.Head = h + new Point(12, -165);
                r.LeftElbow = h + new Point(-58, -55); r.LeftHand = h + new Point(-70, -25);
                r.RightElbow = h + new Point(48, -55); r.RightHand = h + new Point(70, -25);
            }
            else if (pose == CutscenePose.Reach)
            {
                r.Shoulder = h + new Point(10, -113); r.Head = h + new Point(6, -154);
                r.LeftElbow = h + new Point(-62, -55); r.LeftHand = h + new Point(-70, -25);
                r.RightElbow = h + new Point(50, -44); r.RightHand = h + new Point(45, 40);
            }
            else if (pose == CutscenePose.Phone)
            {
                r.Shoulder = h + new Point(0, -130); r.Head = h + new Point(10, -177);
                r.LeftElbow = h + new Point(-58, -55); r.LeftHand = h + new Point(-70, -25);
                r.RightElbow = h + new Point(68, -70); r.RightHand = h + new Point(104, -155);
            }
            return r;
        }

        // shoulderSpan is half the distance between the arm joints: 31 for a figure seen from the
        // front, a few pixels for one seen from the side.
        private static void DrawRig(List<CutsceneCommand> o, Rig r, CutsceneInk ink, int subject, bool outline, double size = 1,
            double shoulderSpan = 31)
        {
            var leftShoulder = r.Shoulder + new Point(-shoulderSpan * size, 8 * size);
            var rightShoulder = r.Shoulder + new Point(shoulderSpan * size, 8 * size);
            Bone(o, r.Hip + new Point(-13, 0), r.LeftKnee, 14 * size, ink, subject, outline);
            Bone(o, r.LeftKnee, r.LeftFoot, 12 * size, ink, subject, outline);
            Bone(o, r.Hip + new Point(13, 0), r.RightKnee, 14 * size, ink, subject, outline);
            Bone(o, r.RightKnee, r.RightFoot, 12 * size, ink, subject, outline);
            Rect(o, r.LeftFoot.X - 17, r.LeftFoot.Y - 1, 25, 6, ink, subject);
            Rect(o, r.RightFoot.X - 8, r.RightFoot.Y - 1, 25, 6, ink, subject);
            Bone(o, leftShoulder, r.LeftElbow, 12 * size, ink, subject, outline);
            Bone(o, r.LeftElbow, r.LeftHand, 11 * size, ink, subject, outline);
            Bone(o, rightShoulder, r.RightElbow, 12 * size, ink, subject, outline);
            Bone(o, r.RightElbow, r.RightHand, 11 * size, ink, subject, outline);
            Bone(o, r.Shoulder, r.Hip, 46 * size, ink, subject, outline);
            // A neck ends at the head center; the head covers the join.
            Bone(o, r.Shoulder + new Point(0, 6), r.Head, 14 * size, ink, subject, outline);
            if (outline) Circle(o, r.Head.X, r.Head.Y, 31 * size, CutsceneInk.Ink, subject);
            Circle(o, r.Head.X, r.Head.Y, 28 * size, ink, subject);
        }

        private static void Bone(List<CutsceneCommand> o, Point a, Point b, double width, CutsceneInk ink, int subject, bool outline)
        {
            var dx = b.X - a.X; var dy = b.Y - a.Y;
            var length = Math.Sqrt(dx * dx + dy * dy);
            var angle = Math.Atan2(dy, dx) * 180 / Math.PI - 90;
            if (outline) RotatedRect(o, (a.X + b.X - width - 4) / 2, (a.Y + b.Y - length - 4) / 2, width + 4, length + 4, angle, CutsceneInk.Ink, subject);
            RotatedRect(o, (a.X + b.X - width) / 2, (a.Y + b.Y - length) / 2, width, length, angle, ink, subject);
        }

        private static void Bubble(List<CutsceneCommand> o, CutsceneFrame f, int index, double offsetX)
        {
            var centerX = f.HeadX + offsetX;
            var centerY = f.HeadY - 118;
            o.Add(new CutsceneCommand { Primitive = CutscenePrimitive.ThoughtBubble, Text = BubbleText[index], FontSize = 24,
                X = centerX, Y = centerY + 8, Height = 44, Stroke = 2, Ink = CutsceneInk.Bone, Alpha = 1, Scale = 1 });
            o.Add(Text(BubbleText[index], centerX, centerY + 8, 24, CutsceneInk.Bone));
            var start = new Point(centerX + 24, centerY + 31);
            var end = new Point(f.HeadX - 8, f.HeadY - 39);
            for (var i = 0; i < 3; i++)
            {
                var p = Point.Mix(start, end, i / 2.0);
                var side = 9 - i * 2;
                Rect(o, p.X - side / 2, p.Y - side / 2, side, side, CutsceneInk.Bone);
            }
        }

        private static void Phone(List<CutsceneCommand> o, double x, double y, bool lit)
        {
            Rect(o, x - 25, y - 43, 50, 86, CutsceneInk.DarkRed, 2);
            Rect(o, x - 20, y - 36, 40, 72, lit ? CutsceneInk.Red : CutsceneInk.Ink, 2);
        }

        private static void AppNote(List<CutsceneCommand> o, double cx, double cy, double unit)
        {
            void Part(double x, double y, double w, double h, double rotation = 0) => o.Add(new CutsceneCommand
            {
                Primitive = rotation == 0 ? CutscenePrimitive.Rectangle : CutscenePrimitive.RotatedRectangle,
                X = cx + (x - w / 2) * unit, Y = cy - (y + h / 2) * unit, Width = w * unit, Height = h * unit,
                Rotation = rotation, Ink = CutsceneInk.Bone, Alpha = 1, Scale = 1, ScreenSpace = true
            });
            void Dot(double x, double y, double d) => o.Add(new CutsceneCommand
            {
                Primitive = CutscenePrimitive.Circle, X = cx + x * unit, Y = cy - y * unit, Width = d / 2 * unit,
                Ink = CutsceneInk.Bone, Alpha = 1, Scale = 1, ScreenSpace = true
            });
            Part(-2.4, 0, 2.4, 10.8);
            Part(5.2, 3, 2.4, 10.4);
            Part(1.4, 5.6, 9.5, 2.4, -12);
            Dot(-4.8, -5.4, 4.8);
            Dot(2.8, -1.4, 4.8);
        }


        private static CutsceneCommand Text(string text, double x, double y, int size, CutsceneInk ink, CutsceneTextAlign align = CutsceneTextAlign.Center)
            => new CutsceneCommand { Primitive = CutscenePrimitive.Label, Text = text, X = x, Y = y, FontSize = size, Ink = ink, Align = align, Alpha = 1, Scale = 1 };
        private static void Rect(List<CutsceneCommand> o, double x, double y, double w, double h, CutsceneInk ink, int subject = 0, bool screen = false)
            => o.Add(new CutsceneCommand { Primitive = CutscenePrimitive.Rectangle, X = x, Y = y, Width = w, Height = h, Ink = ink, Alpha = 1, Scale = 1, SubjectId = subject, ScreenSpace = screen });
        private static void RotatedRect(List<CutsceneCommand> o, double x, double y, double w, double h, double rotation, CutsceneInk ink, int subject)
            => o.Add(new CutsceneCommand { Primitive = CutscenePrimitive.RotatedRectangle, X = x, Y = y, Width = w, Height = h, Rotation = rotation, Ink = ink, Alpha = 1, Scale = 1, SubjectId = subject });
        private static void Circle(List<CutsceneCommand> o, double x, double y, double radius, CutsceneInk ink, int subject = 0, bool ring = false, double stroke = 0)
            => o.Add(new CutsceneCommand { Primitive = ring ? CutscenePrimitive.Ring : CutscenePrimitive.Circle, X = x, Y = y, Width = radius, Stroke = stroke, Ink = ink, Alpha = 1, Scale = 1, SubjectId = subject });
        private static void Line(List<CutsceneCommand> o, double x1, double y1, double x2, double y2, double width, CutsceneInk ink, int subject = 0, bool screen = false)
            => o.Add(new CutsceneCommand { Primitive = CutscenePrimitive.Line, X = x1, Y = y1, Width = x2, Height = y2, Stroke = width, Ink = ink, Alpha = 1, Scale = 1, SubjectId = subject, ScreenSpace = screen });

        private struct Point
        {
            public double X, Y;
            public Point(double x, double y) { X = x; Y = y; }
            public static Point operator +(Point a, Point b) => new Point(a.X + b.X, a.Y + b.Y);
            public static Point Mix(Point a, Point b, double p) => new Point(Lerp(a.X, b.X, p), Lerp(a.Y, b.Y, p));
        }

        private struct Rig
        {
            public Point Hip, Shoulder, Head, LeftElbow, LeftHand, RightElbow, RightHand, LeftKnee, RightKnee, LeftFoot, RightFoot;
            public static Rig Mix(Rig a, Rig b, double p) => new Rig
            {
                Hip = Point.Mix(a.Hip, b.Hip, p), Shoulder = Point.Mix(a.Shoulder, b.Shoulder, p), Head = Point.Mix(a.Head, b.Head, p),
                LeftElbow = Point.Mix(a.LeftElbow, b.LeftElbow, p), LeftHand = Point.Mix(a.LeftHand, b.LeftHand, p),
                RightElbow = Point.Mix(a.RightElbow, b.RightElbow, p), RightHand = Point.Mix(a.RightHand, b.RightHand, p),
                LeftKnee = Point.Mix(a.LeftKnee, b.LeftKnee, p), RightKnee = Point.Mix(a.RightKnee, b.RightKnee, p),
                LeftFoot = Point.Mix(a.LeftFoot, b.LeftFoot, p), RightFoot = Point.Mix(a.RightFoot, b.RightFoot, p)
            };
        }
    }
}
