using System;
using System.Collections.Generic;

namespace Offtrack.Core
{
    // Authored keyframes from the two reference canvases. Serialized by the runtime config.
    [Serializable]
    public sealed class CutsceneTiming
    {
        public double OpeningIntroEnd = 1.2;
        public double StudentEnter = 2.0;
        public double OpeningWalkDuration = 2.9;
        public double OpeningSitStart = 5.0;
        public double OpeningSitEnd = 5.6;
        public double WritingStart = 5.6;
        public double WritingEnd = 8.6;
        public double TeacherArmEnd = 8.9;
        public double[] BubbleStarts = { 8.4, 9.4, 10.4 };
        public double BubbleGrowDuration = .22;
        public double BubbleFadeStart = 11.4;
        public double BubbleFadeEnd = 11.8;
        public double PhoneStart = 11.2;
        public double PhoneEnd = 11.8;
        public double PhoneAppStart = 12.0;
        public double PhoneAppEnd = 12.7;
        public double OpeningDimStart = 13.4;
        public double OpeningDimEnd = 14.2;
        public double ClosingTitleEnd = 1.5;
        public double ClosingWalkDuration = 2.2;
        public double ClosingSitStart = 4.2;
        public double ClosingSitEnd = 4.6;
        public double ReportStart = 4.6;
        public double ReportInEnd = 5.4;
        public double GradeStart = 5.6;
        public double GradeFadeEnd = 6.2;
        public double ShockStart = 6.6;
        public double ShockEnd = 6.8;
        public double GriefStart = 7.6;
        public double GriefEnd = 8.2;
        public double BlinkStart = 9.0;
        public double BlinkEnd = 9.9;
        public double BlinkInterval = .22;
        public double ReportOutStart = 9.9;
        public double ReportOutEnd = 10.5;
        public double ClosingDimStart = 10.5;
        public double ClosingDimEnd = 10.8;
    }

    public enum CutsceneInk { Ink, Red, Bone, DarkRed }
    public enum CutscenePrimitive { Rectangle, Outline, Circle, Ring, Line, Label }
    public enum CutsceneTextAlign { Left, Center, Right }

    public struct CutsceneCommand
    {
        public CutscenePrimitive Primitive;
        public CutsceneInk Ink;
        public double X, Y, Width, Height, Stroke, Alpha, Scale;
        public string Text;
        public int FontSize;
        public bool Serif, HasClip;
        public CutsceneTextAlign Align;
        public double ClipX, ClipY, ClipWidth, ClipHeight;
    }

    public struct CutsceneFrame
    {
        public bool Opening, ClassroomVisible, StudentVisible, TeacherArmUp, PhoneVisible, ReportVisible, GradeVisible;
        public double Time, IntroSize, StudentX, StudentBottom, LegHeight, WalkTime, Seated;
        public double Writing, PhoneAlpha, AppProgress, Dim, ReportY, GradeAlpha, Shock, Grief;
    }

    // Pure functions of t: neither simulation state nor Unity's clock affects a rendered frame.
    public static class CutsceneTimeline
    {
        private static readonly string[] FormulaText = { "∫ x² dx = x³ / 3", "lim  sinx / x = 1", "Σ 1/n² = π² / 6", "(x²)' = 2x" };
        private static readonly string[] BubbleText = { "好不想听啊", "能不能跳过。", "听会歌吧" };
        private static readonly double[] BubbleX = { 1300, 1240, 1180 };
        private static readonly double[] BubbleY = { 540, 470, 400 };
        private static readonly double[] BubbleWidth = { 156, 188, 142 };

        public static double Clamp(double value, double min, double max) => Math.Min(max, Math.Max(min, value));
        public static double Segment(double value, double start, double end) => Clamp((value - start) / (end - start), 0, 1);
        public static double EaseOut(double p) => 1 - Math.Pow(1 - p, 3);

        public static CutsceneFrame Evaluate(bool opening, double time, CutsceneTiming timing)
        {
            var t = Math.Max(0, time);
            var f = new CutsceneFrame { Opening = opening, Time = t };
            f.ClassroomVisible = t >= (opening ? timing.OpeningIntroEnd : timing.ClosingTitleEnd);
            f.IntroSize = opening ? 14 * EaseOut(Segment(t, 0, timing.OpeningIntroEnd)) : 0;
            if (!f.ClassroomVisible) return f;
            f.StudentVisible = t >= timing.StudentEnter;
            var walk = Segment(t, timing.StudentEnter, timing.StudentEnter + (opening ? timing.OpeningWalkDuration : timing.ClosingWalkDuration));
            f.StudentX = 1740 - 380 * walk;
            f.Seated = EaseOut(Segment(t, opening ? timing.OpeningSitStart : timing.ClosingSitStart, opening ? timing.OpeningSitEnd : timing.ClosingSitEnd));
            f.LegHeight = 26 * 2.6 * (1 - f.Seated);
            var standBottom = 880 - 26 * 2.6;
            f.StudentBottom = standBottom + (800 - standBottom) * f.Seated;
            f.WalkTime = walk < 1 && (opening || t < timing.ClosingSitStart) ? t : 0;
            if (opening)
            {
                f.Writing = Segment(t, timing.WritingStart, timing.WritingEnd);
                f.TeacherArmUp = t >= timing.WritingStart && t < timing.TeacherArmEnd;
                f.PhoneVisible = t >= timing.PhoneStart;
                f.PhoneAlpha = EaseOut(Segment(t, timing.PhoneStart, timing.PhoneEnd));
                f.AppProgress = EaseOut(Segment(t, timing.PhoneAppStart, timing.PhoneAppEnd));
                f.Dim = EaseOut(Segment(t, timing.OpeningDimStart, timing.OpeningDimEnd));
            }
            else
            {
                f.ReportVisible = t >= timing.ReportStart;
                f.ReportY = 180 - (1 - EaseOut(Segment(t, timing.ReportStart, timing.ReportInEnd))) * 320
                    - EaseOut(Segment(t, timing.ReportOutStart, timing.ReportOutEnd)) * 900;
                f.GradeVisible = t >= timing.GradeStart;
                var blink = t >= timing.BlinkStart && t < timing.BlinkEnd && (int)Math.Floor((t - timing.BlinkStart) / timing.BlinkInterval) % 2 != 0 ? .28 : 1;
                f.GradeAlpha = EaseOut(Segment(t, timing.GradeStart, timing.GradeFadeEnd)) * blink;
                f.Shock = t < timing.GriefStart ? EaseOut(Segment(t, timing.ShockStart, timing.ShockEnd)) : 0;
                f.Grief = EaseOut(Segment(t, timing.GriefStart, timing.GriefEnd));
                f.Dim = EaseOut(Segment(t, timing.ClosingDimStart, timing.ClosingDimEnd));
            }
            return f;
        }

        public static double BubbleProgress(double t, int index, CutsceneTiming timing) => EaseOut(Segment(t, timing.BubbleStarts[index], timing.BubbleStarts[index] + timing.BubbleGrowDuration));
        public static double BubbleAlpha(double t, int index, CutsceneTiming timing) => BubbleProgress(t, index, timing) * (1 - Segment(t, timing.BubbleFadeStart, timing.BubbleFadeEnd));
        public static double FormulaProgress(double p, int index) => Segment(p, index / 4.0, (index + .92) / 4.0);

        public static CutsceneFrame Draw(bool opening, double t, CutsceneTiming timing, List<CutsceneCommand> output)
        {
            output.Clear();
            var f = Evaluate(opening, t, timing);
            Rect(output, 0, 0, 1600, 900, CutsceneInk.Ink);
            if (!f.ClassroomVisible)
            {
                if (opening) Rect(output, 800 - f.IntroSize / 2, 450 - f.IntroSize / 2, f.IntroSize, f.IntroSize, CutsceneInk.Red);
                else Label(output, "三个月后", 800, 470, 40, CutsceneInk.Bone);
                return f;
            }
            Board(output, !opening);
            if (opening)
            {
                for (var i = 0; i < FormulaText.Length; i++)
                {
                    var k = FormulaProgress(f.Writing, i);
                    if (k <= 0) continue;
                    var y = 200 + i * 84;
                    var c = Text(FormulaText[i], 215, y, 46, CutsceneInk.Bone, CutsceneTextAlign.Left);
                    c.Serif = true;
                    c.HasClip = true; c.ClipX = 215; c.ClipY = y - 58; c.ClipWidth = 600 * k; c.ClipHeight = 78;
                    output.Add(c);
                }
                Person(output, 740, 690 - 26 * 2.4, 26 * 2.4, 2.4, CutsceneInk.DarkRed, armUp: f.TeacherArmUp);
                Rect(output, 640, 600, 200, 110, CutsceneInk.DarkRed);
                Line(output, 640, 600, 840, 600, 5, CutsceneInk.Bone);
                Line(output, 640, 600, 640, 710, 4, CutsceneInk.DarkRed, .9);
                Line(output, 840, 600, 840, 710, 4, CutsceneInk.DarkRed, .9);
            }
            Desk(output, 850, 650, 150, opening ? 1.15 : 0);
            Desk(output, 1020, 650, 150, opening ? 1.15 : 0);
            Desk(output, 920, 730, 165, opening ? 1.35 : 0);
            Desk(output, 180, 800, 240, 0);
            output.Add(new CutsceneCommand { Primitive = CutscenePrimitive.Ring, X = 1430, Y = 150, Width = 48, Stroke = 4, Ink = CutsceneInk.DarkRed, Alpha = 1, Scale = 1 });
            Line(output, 1430, 150, 1430, 118, 4, CutsceneInk.DarkRed);
            Line(output, 1430, 150, 1458, 164, 4, CutsceneInk.DarkRed);
            Line(output, 0, 862, 1600, 862, 3, CutsceneInk.DarkRed, .85);
            if (f.StudentVisible)
            {
                var headDy = -3 * f.Shock + 6 * f.Grief;
                var squash = 4 * f.Grief;
                Person(output, f.StudentX, f.StudentBottom, f.LegHeight, 2.6, CutsceneInk.Bone, f.WalkTime, false, squash, headDy);
                if (opening)
                {
                    for (var i = 0; i < BubbleText.Length; i++)
                    {
                        if (t < timing.BubbleStarts[i]) continue;
                        var k = BubbleProgress(t, i, timing);
                        var alpha = BubbleAlpha(t, i, timing);
                        if (alpha <= 0) continue;
                        var scale = .9 + .1 * k;
                        var x = BubbleX[i]; var y = BubbleY[i] + (1 - k) * 12;
                        Outline(output, x - BubbleWidth[i] / 2 * scale, y - 22 * scale, BubbleWidth[i] * scale, 44 * scale, 4 * scale, CutsceneInk.Bone, alpha);
                        var text = Text(BubbleText[i], x, y + 8 * scale, 24, CutsceneInk.Bone);
                        text.Scale = scale; text.Alpha = alpha; output.Add(text);
                    }
                    if (f.PhoneVisible) Phone(output, f.PhoneAlpha, f.AppProgress, t >= timing.PhoneAppStart);
                }
                else
                {
                    var headTop = f.StudentBottom - 36 * 2.6 + squash * 2.6 - 4 * 2.6 - 28 * 2.6 + headDy * 2.6;
                    if (f.Shock > .02) Rect(output, f.StudentX - 4, headTop - 52, 8, 40, CutsceneInk.Red, f.Shock);
                    if (f.Grief > .02) Rect(output, f.StudentX + 62, f.StudentBottom - 140, 6, 110, CutsceneInk.Red, f.Grief);
                }
            }
            Rect(output, 1140, 780, 460, 120, CutsceneInk.DarkRed, .9);
            Line(output, 1140, 780, 1600, 780, 5, CutsceneInk.Bone);
            Line(output, 1140, 816, 1600, 816, 2, CutsceneInk.Ink, .45);
            if (!opening && f.ReportVisible)
            {
                Rect(output, 390, f.ReportY, 320, 240, CutsceneInk.Bone);
                Line(output, 422, f.ReportY + 74, 678, f.ReportY + 74, 2, CutsceneInk.DarkRed);
                Line(output, 422, f.ReportY + 142, 678, f.ReportY + 142, 2, CutsceneInk.DarkRed);
                if (f.GradeVisible)
                {
                    Label(output, "高等数学", 422, f.ReportY + 208, 34, CutsceneInk.Red, CutsceneTextAlign.Left, f.GradeAlpha);
                    Label(output, "58", 678, f.ReportY + 214, 56, CutsceneInk.Red, CutsceneTextAlign.Right, f.GradeAlpha);
                }
            }
            if (f.Dim > 0) Rect(output, 0, 0, 1600, 900, CutsceneInk.Ink, f.Dim);
            if (opening && f.Dim > .98) Phone(output, 1, 1, true);
            return f;
        }

        private static void Board(List<CutsceneCommand> o, bool erased)
        {
            Outline(o, 150, 100, 800, 400, 5, CutsceneInk.Bone);
            if (!erased) Rect(o, 156, 106, 788, 388, CutsceneInk.DarkRed, .36);
            Rect(o, 150, 504, 800, 8, CutsceneInk.DarkRed, .9);
        }

        private static void Desk(List<CutsceneCommand> o, double x, double y, double w, double scale)
        {
            if (scale > 0) Person(o, x + w / 2, y + 12, 0, scale, CutsceneInk.DarkRed);
            Rect(o, x, y, w, 12, CutsceneInk.DarkRed, .8);
            Rect(o, x + 14, y + 12, 5, 42, CutsceneInk.DarkRed, .8);
            Rect(o, x + w - 19, y + 12, 5, 42, CutsceneInk.DarkRed, .8);
        }

        private static void Person(List<CutsceneCommand> o, double x, double bottom, double legHeight, double s,
            CutsceneInk ink, double walk = 0, bool armUp = false, double squash = 0, double headDy = 0)
        {
            var bodyW = 22 * s; var bodyH = 36 * s; var armW = Math.Max(3, 5 * s);
            squash *= s; headDy *= s;
            var top = bottom - bodyH + squash;
            if (legHeight > .5)
            {
                var swing = walk != 0 ? Math.Sin(walk * 9) * 4 * s : 0;
                Rect(o, x - 9 * s + swing, bottom, 4 * s, legHeight, ink);
                Rect(o, x + 5 * s - swing, bottom, 4 * s, legHeight, ink);
            }
            Rect(o, x - bodyW / 2 - armW, top + 3 * s, armW, 24 * s, ink);
            if (armUp) Line(o, x + bodyW / 2, top + 8 * s, x + bodyW / 2 + 16 * s, top - 14 * s, armW, ink);
            else Rect(o, x + bodyW / 2, top + 3 * s, armW, 24 * s, ink);
            Rect(o, x - bodyW / 2, top, bodyW, bodyH - squash, ink);
            o.Add(new CutsceneCommand { Primitive = CutscenePrimitive.Circle, X = x, Y = top - 4 * s - 14 * s + headDy, Width = 14 * s, Ink = ink, Alpha = 1, Scale = 1 });
        }

        private static void Phone(List<CutsceneCommand> o, double alpha, double app, bool appVisible)
        {
            Outline(o, 1442, 645, 56, 90, 5, CutsceneInk.Bone, alpha);
            Rect(o, 1447, 650, 46, 76, CutsceneInk.Red, alpha);
            if (!appVisible) return;
            var side = 32 * app;
            Rect(o, 1470 - side / 2, 650, side, side, CutsceneInk.Ink, alpha);
            Line(o, 1454, 698, 1486, 698, 3, CutsceneInk.Bone, alpha * app);
            Line(o, 1454, 710, 1476, 710, 3, CutsceneInk.Bone, alpha * app);
        }

        private static CutsceneCommand Text(string text, double x, double y, int size, CutsceneInk ink, CutsceneTextAlign align = CutsceneTextAlign.Center)
            => new CutsceneCommand { Primitive = CutscenePrimitive.Label, Text = text, X = x, Y = y, FontSize = size, Ink = ink, Align = align, Alpha = 1, Scale = 1 };
        private static void Label(List<CutsceneCommand> o, string text, double x, double y, int size, CutsceneInk ink, CutsceneTextAlign align = CutsceneTextAlign.Center, double alpha = 1)
        { var c = Text(text, x, y, size, ink, align); c.Alpha = alpha; o.Add(c); }
        private static void Rect(List<CutsceneCommand> o, double x, double y, double w, double h, CutsceneInk ink, double alpha = 1)
            => o.Add(new CutsceneCommand { Primitive = CutscenePrimitive.Rectangle, X = x, Y = y, Width = w, Height = h, Ink = ink, Alpha = alpha, Scale = 1 });
        private static void Outline(List<CutsceneCommand> o, double x, double y, double w, double h, double stroke, CutsceneInk ink, double alpha = 1)
            => o.Add(new CutsceneCommand { Primitive = CutscenePrimitive.Outline, X = x, Y = y, Width = w, Height = h, Stroke = stroke, Ink = ink, Alpha = alpha, Scale = 1 });
        private static void Line(List<CutsceneCommand> o, double x1, double y1, double x2, double y2, double width, CutsceneInk ink, double alpha = 1)
            => o.Add(new CutsceneCommand { Primitive = CutscenePrimitive.Line, X = x1, Y = y1, Width = x2, Height = y2, Stroke = width, Ink = ink, Alpha = alpha, Scale = 1 });
    }
}
