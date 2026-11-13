using System;
using System.Collections.Generic;

namespace Offtrack.Core
{
    // Authored beats and motion controls. The runtime config serializes this complete score.
    [Serializable]
    public sealed class CutsceneTiming
    {
        public double OpeningDuration = 16;
        public double ClosingDuration = 12;
        public double OpeningIntroEnd = .8;
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
        public double BubbleGrowDuration = .22;
        public double BubbleFadeStart = 11.35;
        public double BubbleFadeEnd = 11.6;
        public double PhoneStart = 11.6;
        public double PhoneEnd = 13;
        public double PhoneAppStart = 13;
        public double PhoneAppEnd = 13.3;
        public double CameraPushStart = 6;
        public double CameraPushEnd = 11.6;
        public double CameraPushScale = 1.15;
        public double CameraFocusX = 1100;
        public double CameraFocusY = 580;
        public double PhoneZoomStart = 13;
        public double PhoneZoomEnd = 16;
        public double PhoneZoomScale = 8;
        public double PhoneCloseupStart = 13.8;
        public double PhoneApproachScale = 2.6;
        public double PhoneCloseupScale = 6;
        // Match the boot icon's center across the two different reference aspect ratios.
        public double PhoneMatchNormalizedX = .5;
        public double PhoneMatchNormalizedY = 758.0 / 1688.0;
        public double ClosingTitleEnd = 1.6;
        public double TitleFadeDuration = .4;
        public double ClosingStudentEnter = 1.6;
        public double ClosingWalkDuration = 1.6;
        public double ClosingSitStart = 3.2;
        public double ClosingSitEnd = 3.8;
        public double ReportStart = 3.8;
        public double ReportInEnd = 3.8;
        public double GradeStart = 3.8;
        public double GradeFadeEnd = 3.8;
        public double ShockStart = 6;
        public double ShockEnd = 6.6;
        public double GriefStart = 6.6;
        public double GriefEnd = 6.9;
        public double ClosingDimStart = 9.6;
        public double ClosingDimEnd = 10.8;
        public double ClosingPhoneStart = 10.8;
        public double ClosingCameraScale = 1.35;
        public double ClosingCameraX = 1180;
        public double ClosingCameraY = 650;
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
        public double GriefShoulderOffsetX = -54;
        public double GriefShoulderOffsetY = -52;
        public double GriefHeadOffsetX = -105;
        public double GriefHeadOffsetY = -60;
        public double GriefArmsOffsetY = -27;
        public double GriefLeftElbowOffsetX = -125;
        public double GriefLeftHandOffsetX = -47;
        public double GriefRightElbowOffsetX = -15;
        public double GriefRightHandOffsetX = -112;
        public double GriefSymbolOffsetX = -44;
        public double GriefSymbolOffsetY = -12;
        public double GriefSymbolWidth = 8;
        public double GriefSymbolHeight = 32;
        // Retained so earlier serialized configurations migrate without losing fields.
        public double OpeningDimStart = 13;
        public double OpeningDimEnd = 16;
        public double BlinkStart = 9;
        public double BlinkEnd = 9.9;
        public double BlinkInterval = .22;
        public double ReportOutStart = 6;
        public double ReportOutEnd = 6;
    }

    public enum CutsceneInk
    {
        Ink, Red, Bone, DarkRed, DarkRed60, DarkRed40, DarkRed15,
        Bone60, Bone40, Bone15, Red60, Red40, Red15
    }
    public enum CutscenePrimitive { Rectangle, Outline, Circle, Ring, Line, Label, RotatedRectangle, ThoughtBubble }
    public enum CutsceneTextAlign { Left, Center, Right }
    public enum CutscenePose { Walking, Seated, Slouch, Clock, Reach, Phone, Shock, Grief }

    public struct CutsceneCommand
    {
        public CutscenePrimitive Primitive;
        public CutsceneInk Ink;
        public double X, Y, Width, Height, Stroke, Alpha, Scale;
        // Clockwise around a rectangle's center in the reference canvas's y-down coordinates.
        public double Rotation;
        // 1 student, 2 phone, 3 emotion, 4 grade, 5 teacher. Zero denotes scenery or typography.
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
        public bool Opening, ClassroomVisible, StudentVisible, TeacherArmUp, PhoneVisible, ReportVisible, GradeVisible, PhoneCloseup;
        public double Time, IntroSize, StudentX, StudentBottom, LegHeight, WalkTime, Seated;
        public double Writing, PhoneAlpha, AppProgress, Dim, ReportY, GradeAlpha, Shock, Grief;
        public double CameraScale, CameraX, CameraY, SceneOpacity, TitleOpacity;
        public double HeadX, HeadY, RightHandX, RightHandY, PhoneX, PhoneY, Breath, WalkBob, ClockMinuteAngle;
        public CutscenePose Pose;
    }

    // A complete frame is a pure function of authored time, including its camera and joints.
    public static class CutsceneTimeline
    {
        private static readonly string[] FormulaText = { "∫ x² dx = x³ / 3", "lim  sinx / x = 1", "Σ 1/n² = π² / 6", "(x²)' = 2x" };
        private static readonly string[] BubbleText = { "好不想听啊", "能不能跳过。", "听会歌吧" };

        public static double Clamp(double value, double min, double max) => Math.Min(max, Math.Max(min, value));
        public static double Segment(double value, double start, double end) => end <= start ? (value >= end ? 1 : 0) : Clamp((value - start) / (end - start), 0, 1);
        public static double EaseOut(double p) => 1 - Math.Pow(1 - Clamp(p, 0, 1), 3);
        public static double EaseInOut(double p) { p = Clamp(p, 0, 1); return p * p * (3 - 2 * p); }
        public static double EaseIn(double p) { p = Clamp(p, 0, 1); return p * p; }
        private static double Lerp(double a, double b, double p) => a + (b - a) * p;

        public static CutsceneFrame Evaluate(bool opening, double time, CutsceneTiming timing)
            => Evaluate(opening, time, timing, true);

        private static CutsceneFrame Evaluate(bool opening, double time, CutsceneTiming timing, bool applyPhoneZoom)
        {
            if (timing == null) throw new ArgumentNullException(nameof(timing));
            if (double.IsNaN(time) || double.IsInfinity(time)) throw new ArgumentOutOfRangeException(nameof(time));
            var t = Clamp(time, 0, opening ? timing.OpeningDuration : timing.ClosingDuration);
            var f = new CutsceneFrame
            {
                Opening = opening, Time = t, CameraScale = 1, CameraX = 800, CameraY = 450,
                SceneOpacity = opening ? EaseInOut(Segment(t, 0, timing.OpeningIntroEnd)) : 1,
                ClassroomVisible = opening || t >= timing.ClosingTitleEnd,
                ReportY = 90,
                ClockMinuteAngle = -90 + t * .35 + (opening ? 6 * EaseInOut(Segment(t, timing.BubbleStarts[1], timing.BubbleStarts[1] + timing.PoseBlendDuration)) : 0)
            };
            if (!opening)
            {
                f.TitleOpacity = EaseInOut(Segment(t, 0, timing.TitleFadeDuration)) *
                    (1 - EaseInOut(Segment(t, timing.ClosingTitleEnd - timing.TitleFadeDuration, timing.ClosingTitleEnd)));
                f.ReportVisible = t >= timing.ReportStart && t < timing.ShockStart;
                f.GradeVisible = f.ReportVisible && t >= timing.GradeStart;
                f.GradeAlpha = f.GradeVisible ? 1 : 0;
                f.Grief = EaseInOut(Segment(t, timing.GriefStart, timing.GriefEnd));
                f.Shock = EaseInOut(Segment(t, timing.ShockStart, timing.ShockEnd)) * (1 - f.Grief);
                f.SceneOpacity = f.ClassroomVisible ? 1 - EaseIn(Segment(t, timing.ClosingDimStart, timing.ClosingDimEnd)) : 0;
                f.Dim = 1 - f.SceneOpacity;
                f.PhoneVisible = t >= timing.ClosingPhoneStart;
                f.PhoneAlpha = f.PhoneVisible ? 1 : 0;
                f.AppProgress = f.PhoneAlpha;
                if (t >= timing.ShockStart)
                {
                    f.CameraScale = timing.ClosingCameraScale;
                    f.CameraX = timing.ClosingCameraX;
                    f.CameraY = timing.ClosingCameraY;
                }
            }
            var enter = opening ? timing.StudentEnter : timing.ClosingStudentEnter;
            var sitStart = opening ? timing.OpeningSitStart : timing.ClosingSitStart;
            var sitEnd = opening ? timing.OpeningSitEnd : timing.ClosingSitEnd;
            var walk = Segment(t, enter, enter + (opening ? timing.OpeningWalkDuration : timing.ClosingWalkDuration));
            f.StudentVisible = t >= enter && !f.ReportVisible && (opening || t < timing.ClosingPhoneStart);
            f.StudentX = Lerp(timing.StudentStartX, timing.StudentSeatX, EaseInOut(walk));
            f.WalkTime = walk > 0 && walk < 1 ? t - enter : 0;
            f.WalkBob = f.WalkTime > 0 ? -Math.Abs(Math.Sin(f.WalkTime * timing.WalkAngularSpeed)) * timing.WalkBobAmplitude : 0;
            f.Breath = walk >= 1 ? Math.Sin(t * timing.BreathAngularSpeed) * timing.BreathAmplitude : 0;
            f.Seated = EaseInOut(Segment(t, sitStart + timing.SitAnticipationDuration, sitEnd));
            var anticipation = t >= sitStart && t < sitStart + timing.SitAnticipationDuration
                ? Math.Sin(Math.PI * Segment(t, sitStart, sitStart + timing.SitAnticipationDuration)) * timing.SitAnticipationDepth : 0;
            var rebound = t >= sitEnd && t < sitEnd + timing.SitReboundDuration
                ? Math.Sin(Math.PI * Segment(t, sitEnd, sitEnd + timing.SitReboundDuration)) * timing.SitReboundAmplitude : 0;
            f.StudentBottom = Lerp(timing.StandingHipY, timing.SeatedHipY, f.Seated) + f.WalkBob + f.Breath + anticipation - rebound;
            f.LegHeight = timing.FloorY - f.StudentBottom;
            f.Pose = walk < 1 ? CutscenePose.Walking : CutscenePose.Seated;
            if (opening)
            {
                f.Writing = Segment(t, timing.WritingStart, timing.WritingEnd);
                f.TeacherArmUp = t >= timing.WritingStart && t < timing.TeacherArmEnd;
                f.PhoneVisible = t >= timing.PhoneStart;
                f.PhoneAlpha = EaseInOut(Segment(t, timing.PhoneStart, timing.PhoneEnd));
                f.AppProgress = EaseInOut(Segment(t, timing.PhoneAppStart, timing.PhoneAppEnd));
                if (t >= timing.BubbleStarts[0]) f.Pose = CutscenePose.Slouch;
                if (t >= timing.BubbleStarts[1]) f.Pose = CutscenePose.Clock;
                if (t >= timing.BubbleStarts[2]) f.Pose = CutscenePose.Reach;
                if (t >= timing.PhoneStart) f.Pose = CutscenePose.Phone;
                var push = EaseInOut(Segment(t, timing.CameraPushStart, timing.CameraPushEnd));
                f.CameraScale = Lerp(1, timing.CameraPushScale, push);
                f.CameraX = Lerp(800, timing.CameraFocusX, push);
                f.CameraY = Lerp(450, timing.CameraFocusY, push);
            }
            else if (t >= timing.ShockStart) f.Pose = t < timing.GriefStart ? CutscenePose.Shock : CutscenePose.Grief;
            var rig = StudentRig(f, timing);
            f.HeadX = rig.Head.X; f.HeadY = rig.Head.Y;
            f.RightHandX = rig.RightHand.X; f.RightHandY = rig.RightHand.Y;
            f.PhoneX = rig.RightHand.X; f.PhoneY = rig.RightHand.Y;
            if (opening && applyPhoneZoom && t > timing.PhoneZoomStart)
            {
                // Keep the phone's projected center on a direct path to screen center.
                // Interpolating zoom and world focus independently would sweep the phone
                // towards the viewport edge before returning. A fixed starting projection
                // also keeps breathing in the hand from introducing a second camera sway.
                var start = Evaluate(true, timing.PhoneZoomStart, timing, false);
                f.PhoneCloseup = t >= timing.PhoneCloseupStart;
                if (f.PhoneCloseup) { f.StudentVisible = false; f.ClassroomVisible = false; }
                var zoom = EaseInOut(Segment(t, timing.PhoneZoomStart, timing.PhoneCloseupStart));
                var targetX = timing.PhoneMatchNormalizedX * 1600;
                var targetY = timing.PhoneMatchNormalizedY * 900;
                var screenX = Lerp((start.PhoneX - start.CameraX) * start.CameraScale + 800, targetX, zoom);
                var screenY = Lerp((start.PhoneY - start.CameraY) * start.CameraScale + 450, targetY, zoom);
                // End the classroom shot before enlarging the geometric person's face.
                // The hard match cut contains only the phone, then closes on its red screen.
                f.CameraScale = f.PhoneCloseup
                    ? Lerp(timing.PhoneCloseupScale, timing.PhoneZoomScale,
                        EaseInOut(Segment(t, timing.PhoneCloseupStart, timing.PhoneZoomEnd)))
                    : Lerp(start.CameraScale, timing.PhoneApproachScale, zoom);
                f.CameraX = f.PhoneX - (screenX - 800) / f.CameraScale;
                f.CameraY = f.PhoneY - (screenY - 450) / f.CameraScale;
            }
            return f;
        }

        public static double BubbleProgress(double t, int index, CutsceneTiming timing) => EaseInOut(Segment(t, timing.BubbleStarts[index], timing.BubbleStarts[index] + timing.BubbleGrowDuration));
        public static double BubbleAlpha(double t, int index, CutsceneTiming timing)
        {
            var end = index + 1 < timing.BubbleStarts.Length ? timing.BubbleStarts[index + 1] : timing.BubbleFadeEnd;
            if (t < timing.BubbleStarts[index] || t >= end) return 0;
            return BubbleProgress(t, index, timing) * (index == timing.BubbleStarts.Length - 1 ? 1 - Segment(t, timing.BubbleFadeStart, timing.BubbleFadeEnd) : 1);
        }
        public static double FormulaProgress(double p, int index) => Segment(p, index / 4.0, (index + .92) / 4.0);

        public static CutsceneFrame Draw(bool opening, double t, CutsceneTiming timing, List<CutsceneCommand> output)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));
            output.Clear();
            var f = Evaluate(opening, t, timing);
            Rect(output, 0, 0, 1600, 900, CutsceneInk.Ink, screen: true);
            if (opening && f.PhoneCloseup)
            {
                Phone(output, f.PhoneX, f.PhoneY, true);
                return f;
            }
            if (!f.ClassroomVisible)
            {
                var title = Text("三个月后", 800, 466, 40, CutsceneInk.Bone);
                title.Alpha = f.TitleOpacity; title.ScreenSpace = true;
                output.Add(title);
                return f;
            }
            if (f.ReportVisible)
            {
                Report(output, f);
                return f;
            }
            if (!opening && f.PhoneVisible)
            {
                EndingPhone(output);
                return f;
            }
            Classroom(output, f, timing);
            if (f.StudentVisible)
            {
                var rig = StudentRig(f, timing);
                DrawRig(output, rig, CutsceneInk.Bone, 1, false);
                if (opening && f.PhoneVisible) Phone(output, f.PhoneX, f.PhoneY, f.AppProgress > 0);
            }
            // These solid faces are after the person and phone, so reaching below a desk really occludes them.
            Rect(output, 1000, 725, 500, 115, CutsceneInk.DarkRed);
            Rect(output, 1000, 725, 500, 10, CutsceneInk.DarkRed);
            Line(output, 1000, 762, 1500, 762, 2, CutsceneInk.Ink);
            if (opening && f.StudentVisible)
            {
                for (var i = 0; i < BubbleText.Length; i++)
                    if (BubbleAlpha(f.Time, i, timing) > 0) Bubble(output, f, i);
            }
            else if (f.StudentVisible && f.Shock + f.Grief > 0)
            {
                var x = Lerp(f.HeadX + 56, f.HeadX + timing.GriefSymbolOffsetX, f.Grief);
                var y = Lerp(f.HeadY - 70, f.HeadY + timing.GriefSymbolOffsetY, f.Grief);
                var width = Lerp(12, timing.GriefSymbolWidth, f.Grief);
                var height = Lerp(50, timing.GriefSymbolHeight, f.Grief);
                RotatedRect(output, x - width / 2, y - height / 2, width, height, Lerp(-12, 24, f.Grief), CutsceneInk.Red, 3);
            }
            return f;
        }

        private static void Classroom(List<CutsceneCommand> o, CutsceneFrame f, CutsceneTiming timing)
        {
            Rect(o, 140, 180, 760, 400, CutsceneInk.DarkRed15);
            Rect(o, 140, 586, 760, 8, CutsceneInk.DarkRed);
            var heading = Text("高等数学", 190, 254, 28, CutsceneInk.Bone40, CutsceneTextAlign.Left);
            o.Add(heading);
            Line(o, 190, 278, 850, 278, 2, CutsceneInk.DarkRed60);
            if (f.Opening)
            {
                for (var i = 0; i < FormulaText.Length; i++)
                {
                    var k = FormulaProgress(f.Writing, i);
                    if (k <= 0) continue;
                    var y = 365 + i * 54;
                    var text = Text(FormulaText[i], 215, y, 34, CutsceneInk.Bone60, CutsceneTextAlign.Left);
                    text.Serif = true;
                    text.HasClip = true; text.ClipX = 215; text.ClipY = y - 44; text.ClipWidth = timing.FormulaWidths[i] * k; text.ClipHeight = 62;
                    o.Add(text);
                }
                Teacher(o, f, timing);
                Rect(o, 310, 704, 260, 136, CutsceneInk.DarkRed);
                Line(o, 310, 714, 570, 714, 2, CutsceneInk.Ink);
            }
            // All seated and standing figures share the same floor, regardless of depth cues.
            if (f.Opening)
            {
                BackgroundStudent(o, 720, timing.FloorY);
                BackgroundStudent(o, 920, timing.FloorY);
            }
            Desk(o, 630, 735, 170, timing.FloorY);
            Desk(o, 830, 735, 170, timing.FloorY);
            Desk(o, 170, 775, 220, timing.FloorY);
            Circle(o, 1400, 175, 48, CutsceneInk.DarkRed, ring: true, stroke: 4);
            ClockHand(o, 1400, 175, 34, f.ClockMinuteAngle, 4);
            ClockHand(o, 1400, 175, 23, 30 + f.Time * .025, 4);
            ClockHand(o, 1400, 175, 38, -90 + f.Time * 6, 2);
            // The camera may expose world coordinates beyond the original 1600-pixel stage.
            // Derive both endpoints from its visible span, keeping the floor continuous.
            Line(o, f.CameraX - 800 / f.CameraScale, timing.FloorY,
                f.CameraX + 800 / f.CameraScale, timing.FloorY, 3, CutsceneInk.DarkRed);
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

        private static void Teacher(List<CutsceneCommand> o, CutsceneFrame f, CutsceneTiming timing)
        {
            var rig = BaseRig(new Point(450, 698 + Math.Sin(f.Time * timing.BreathAngularSpeed) * timing.BreathAmplitude), 0, 0, timing.FloorY);
            if (f.TeacherArmUp)
            {
                var index = Math.Min(3, (int)(f.Writing * 4));
                var progress = FormulaProgress(f.Writing, index);
                rig.LeftHand = new Point(215 + timing.FormulaWidths[index] * progress, 365 + index * 54 - 12);
                rig.LeftElbow = Point.Mix(new Point(rig.Shoulder.X - 42, rig.Shoulder.Y + 12), rig.LeftHand, .5) + new Point(-25, -24);
            }
            DrawRig(o, rig, CutsceneInk.DarkRed, 5, false);
        }

        private static Rig StudentRig(CutsceneFrame f, CutsceneTiming timing)
        {
            var hip = new Point(f.StudentX, f.StudentBottom);
            var swing = f.WalkTime > 0 ? Math.Sin(f.WalkTime * timing.WalkAngularSpeed) * timing.WalkSwingAmplitude : 0;
            var basic = BaseRig(hip, f.Seated, swing, timing.FloorY);
            if (f.Opening)
            {
                if (f.Time < timing.BubbleStarts[0]) return basic;
                var slouch = PoseRig(basic, CutscenePose.Slouch, timing);
                if (f.Time < timing.BubbleStarts[1]) return Rig.Mix(basic, slouch, EaseInOut(Segment(f.Time, timing.BubbleStarts[0], timing.BubbleStarts[0] + timing.PoseBlendDuration)));
                var clock = PoseRig(basic, CutscenePose.Clock, timing);
                if (f.Time < timing.BubbleStarts[2]) return Rig.Mix(slouch, clock, EaseInOut(Segment(f.Time, timing.BubbleStarts[1], timing.BubbleStarts[1] + timing.PoseBlendDuration)));
                var reach = PoseRig(basic, CutscenePose.Reach, timing);
                if (f.Time < timing.PhoneStart) return Rig.Mix(clock, reach, EaseInOut(Segment(f.Time, timing.BubbleStarts[2], timing.BubbleStarts[2] + timing.PoseBlendDuration)));
                return Rig.Mix(reach, PoseRig(basic, CutscenePose.Phone, timing), EaseInOut(Segment(f.Time, timing.PhoneStart, timing.PhoneEnd)));
            }
            if (f.Time < timing.ShockStart) return basic;
            var shock = Rig.Mix(basic, PoseRig(basic, CutscenePose.Shock, timing), EaseInOut(Segment(f.Time, timing.ShockStart, timing.ShockEnd)));
            return Rig.Mix(shock, PoseRig(basic, CutscenePose.Grief, timing), f.Grief);
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

        private static Rig PoseRig(Rig basic, CutscenePose pose, CutsceneTiming timing)
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
            else if (pose == CutscenePose.Shock)
            {
                r.Shoulder = h + new Point(12, -145); r.Head = h + new Point(5, -197);
                r.LeftElbow = h + new Point(-62, -75); r.LeftHand = h + new Point(-68, -114);
                r.RightElbow = h + new Point(68, -75); r.RightHand = h + new Point(72, -114);
            }
            else if (pose == CutscenePose.Grief)
            {
                // A single readable silhouette: head rests on overlapping horizontal forearms,
                // joined through a lowered shoulder and a forward-leaning torso.
                r.Shoulder = h + new Point(timing.GriefShoulderOffsetX, timing.GriefShoulderOffsetY);
                r.Head = h + new Point(timing.GriefHeadOffsetX, timing.GriefHeadOffsetY);
                r.LeftElbow = h + new Point(timing.GriefLeftElbowOffsetX, timing.GriefArmsOffsetY);
                r.LeftHand = h + new Point(timing.GriefLeftHandOffsetX, timing.GriefArmsOffsetY);
                r.RightElbow = h + new Point(timing.GriefRightElbowOffsetX, timing.GriefArmsOffsetY);
                r.RightHand = h + new Point(timing.GriefRightHandOffsetX, timing.GriefArmsOffsetY);
            }
            return r;
        }

        private static void DrawRig(List<CutsceneCommand> o, Rig r, CutsceneInk ink, int subject, bool outline, double size = 1)
        {
            var leftShoulder = r.Shoulder + new Point(-31 * size, 8 * size);
            var rightShoulder = r.Shoulder + new Point(31 * size, 8 * size);
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
            // A neck ends at the head center. The head covers the join, including a dropped grief pose.
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

        private static void Bubble(List<CutsceneCommand> o, CutsceneFrame f, int index)
        {
            var centerX = f.HeadX - 34;
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

        private static void Report(List<CutsceneCommand> o, CutsceneFrame f)
        {
            Rect(o, 240, f.ReportY, 1120, 720, CutsceneInk.Bone, screen: true);
            var course = Text("高等数学", 800, 232, 36, CutsceneInk.Ink); course.ScreenSpace = true; o.Add(course);
            Line(o, 340, 280, 1260, 280, 3, CutsceneInk.DarkRed, screen: true);
            Line(o, 340, 644, 1260, 644, 3, CutsceneInk.DarkRed, screen: true);
            if (f.GradeVisible)
            {
                var grade = Text("58", 800, 520, 200, CutsceneInk.Red);
                grade.SubjectId = 4; grade.ScreenSpace = true; o.Add(grade);
            }
        }

        private static void EndingPhone(List<CutsceneCommand> o)
        {
            Rect(o, 450, 600, 700, 150, CutsceneInk.DarkRed, screen: true);
            Line(o, 450, 613, 1150, 613, 3, CutsceneInk.Ink, screen: true);
            Rect(o, 745, 544, 110, 56, CutsceneInk.DarkRed, 2, true);
            Rect(o, 753, 551, 94, 42, CutsceneInk.Red, 2, true);
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
