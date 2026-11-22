using System;
using System.Collections.Generic;

namespace Offtrack.Core
{
    public enum StoryScene { Opening, Montage, Closing }
    public enum StorySound
    {
        RoomA, Chalk, ClassAir, Phone, RoomC, Tick, ClockNoise, Whoosh, Stop, RoomB, Step, Paper, Piano, PhoneB,
        Bell, PaperA, Chair, Pack, Zip, Talk, Confetti
    }

    [Serializable]
    public sealed class PortraitScore
    {
        public const int Width = 780;
        public const int Height = 1688;
        public double ArmRamp = .35;
        public double BellAt = 1.12;
        public double PaperOpeningAt = 2.05;
        public double ChairAt = 2.6;
        public double PackAt = 10.58;
        public double ZipAt = 11.06;
        public double TalkAt = 11.54;
        // The teacher's arm keeps the same bone lengths as every other figure's resting arm.
        public double UpperArm = 53;
        public double Forearm = 58;
        public double ArmRestUpper = 1.7046;
        public double ArmRestLower = 1.6397;
        public double ChalkTargetX = -62;
        public double ChalkTargetY = -70;
        public double ChalkSweep = 9;
        public double BubbleEntry = .4;
        public double BubbleOffsetX = 0;
        public double MontageEnd = 6;
        public double SecondPose = 2;
        public double ThirdPose = 4;
        public double AccelerationStart = 1.5;
        public double AccelerationEnd = 4.5;
        public double Stop = 5.5;
        public double Tail = .3;
        public double EventRamp = .025;
        public double RoomRise = 5.6;
        public double RoomGain = .10;
        public double FinalRoomGain = .14;
        public double SlowTurn = 4;
        public double FastTurn = .3;
        public double SlowTick = 1;
        public double FastTick = 4;
        // Four dark spells, each half-period no longer than the one before; the light then holds.
        public double[] LightCuts = { 0, 1.5, 2, 2.46, 2.88, 3.28, 3.66, 4, 4.30 };
        public double[] Passes = { 1.65, 2.72, 3.52, 4.14, 4.65, 4.76 };
        public double PassDuration = .64;
        // Screen pixels between successive shadow layers; dimmer layers trail the leading one.
        public double ShadowSpacing = 4;
        public double ShadowStartX = -135;
        public double ShadowEndX = 915;
        public double ShadowStride = 25;
        public double ShadowSwing = 26;
        public double WhooshLayerDelay = .035;
        public double ClosingEnd = 13;
        public double TitleIn = .45;
        public double PaperAt = 5;
        public double PaperEntry = .45;
        public double GradeAt = 8;
        public double GradeEntry = .45;
        public double Reframe = .7;
        // Opening camera: the world point held at screen center and the zoom, per shot.
        public double OpeningBoardZoom = 1.85;
        public double OpeningBoardFocusX = 388;
        public double OpeningBoardFocusY = 510;
        public double OpeningDeskZoom = 2.7;
        public double OpeningDeskFocusX = 1183.5;
        public double OpeningDeskFocusY = 552.6;
        public double OpeningClockX = 1262;
        public double OpeningClockY = 365;
        public double OpeningDoorX = 1630;
        public double OpeningDoorTop = 380;
        public double OpeningDoorWidth = 180;
        public double PhoneFill = 1.02;
        // Room camera shared by the montage and the closing.
        public double RoomZoom = 1.5;
        public double RoomCenterX = 403.7;
        public double RoomFloorY = 1530;
        public double RoomDiscX = 430;
        public double RoomDiscY = 440;
        public double RoomDiscRadius = 185;
        public double GradeZoom = 4.34;
        public double PhoneZoom = 17;
        public double AtmosphereFade = .8;
        public double PhoneAt = 10.2;
        public double Exit = .3;
        public double[] Steps = { 2.6, 3.27, 3.9 };
        public double GradeRevealAt => Math.Max(PaperAt, GradeAt - GradeEntry);
        // Before the opening: on black, the line that tells players the skip button is there.
        public string SkipHintText = "点击右上角「跳过」可跳过动画";
        public double SkipHintIn = .5;
        public double SkipHintHold = 1.6;
        public double SkipHintOut = .4;
        // The classroom then fades up through the named steps.
        public double SkipHintReveal = .5;
        public double SkipHintDuration => SkipHintIn + SkipHintHold + SkipHintOut;

        /// <summary>
        /// Where each press of the skip button lands: the start of the next shot. A press after the
        /// last of these ends the scene.
        /// </summary>
        public double[] Chapters(StoryScene scene, CutsceneTiming timing)
        {
            switch (scene)
            {
                case StoryScene.Opening: return new[] { timing.OpeningSitEnd, timing.CameraPushStart, timing.PhoneAppStart };
                case StoryScene.Montage: return new[] { SecondPose, ThirdPose };
                default: return new[] { PaperAt, GradeAt - Reframe, PhoneAt };
            }
        }

        public int Pose(double time) => time < SecondPose ? 0 : time < ThirdPose ? 1 : 2;
        public bool Bright(double time)
        {
            int cut = Array.BinarySearch(LightCuts, Math.Min(time, Stop));
            return ((cut < 0 ? ~cut - 1 : cut) & 1) == 0;
        }

        public double Turns(double time)
        {
            double until = Math.Min(time, Stop);
            double ramp = Math.Max(0, Math.Min(until, AccelerationEnd) - AccelerationStart);
            return until / SlowTurn + (1 / FastTurn - 1 / SlowTurn)
                * (ramp * ramp / (2 * (AccelerationEnd - AccelerationStart)) + Math.Max(0, until - AccelerationEnd));
        }

        public double EventGain(double time) => 1 - CutsceneTimeline.Segment(time, Stop, Stop + EventRamp);
        public double MontageRoomGain(double time) => CutsceneTimeline.Lerp(RoomGain, FinalRoomGain,
            CutsceneTimeline.Segment(time, RoomRise, MontageEnd));

        public IEnumerable<(double Time, StorySound Sound)> Cues(StoryScene scene, CutsceneTiming opening,
            bool playGradeConfetti = false)
        {
            switch (scene)
            {
                case StoryScene.Opening:
                    yield return (BellAt, StorySound.Bell);
                    yield return (PaperOpeningAt, StorySound.PaperA);
                    yield return (ChairAt, StorySound.Chair);
                    yield return (PackAt, StorySound.Pack);
                    yield return (ZipAt, StorySound.Zip);
                    yield return (TalkAt, StorySound.Talk);
                    yield return (opening.PhoneAppStart, StorySound.Phone);
                    break;
                case StoryScene.Montage:
                    for (double tick = 0; tick < Stop; tick += 1 / CutsceneTimeline.Lerp(SlowTick, FastTick,
                        CutsceneTimeline.Segment(tick, AccelerationStart, AccelerationEnd)))
                        yield return (tick, StorySound.Tick);
                    foreach (var pass in Passes)
                        for (int layer = 0; layer < 3; layer++) yield return (pass + layer * WhooshLayerDelay, StorySound.Whoosh);
                    yield return (Stop, StorySound.Stop);
                    break;
                default:
                    foreach (var step in Steps) yield return (step, StorySound.Step);
                    yield return (PaperAt, StorySound.Paper);
                    if (playGradeConfetti) yield return (GradeRevealAt, StorySound.Confetti);
                    yield return (GradeAt + AtmosphereFade, StorySound.Piano);
                    yield return (PhoneAt, StorySound.PhoneB);
                    break;
            }
        }
    }

    public struct StoryCamera
    {
        public double Zoom, OffsetX, OffsetY;
        public double X(double world) => world * Zoom + OffsetX;
        public double Y(double world) => world * Zoom + OffsetY;
    }

    public static partial class CutsceneTimeline
    {
        public const int PhoneScreenWidth = 40;
        public const int PhoneScreenHeight = 72;
        public const double RoomFloorWorldY = 1168;
        public const double RoomGradeX = 462, RoomGradeY = 849;
        public const double RoomPhoneX = 563, RoomPhoneY = 910;

        public static void DrawOpeningPortrait(double time, CutsceneTiming timing, PortraitScore score,
            List<CutsceneCommand> output, out CutsceneFrame frame)
        {
            frame = Evaluate(time, timing);
            output.Clear();
            Rect(output, 0, 0, PortraitScore.Width, PortraitScore.Height, CutsceneInk.Ink);
            var camera = OpeningCamera(time, timing, score, frame);
            Classroom(output, frame, timing, score, -camera.OffsetX / camera.Zoom,
                (PortraitScore.Width - camera.OffsetX) / camera.Zoom);
            if (frame.StudentVisible)
            {
                DrawRig(output, StudentRig(frame, timing), CutsceneInk.Bone, 1, false);
                if (frame.PhoneVisible) Phone(output, frame.PhoneX, frame.PhoneY, time >= timing.PhoneAppStart);
                for (int index = 0; index < BubbleText.Length; index++)
                {
                    var alpha = BubbleOpacity(time, index, timing, score);
                    if (alpha <= 0) continue;
                    int first = output.Count;
                    Bubble(output, frame, index, score.BubbleOffsetX);
                    for (int shape = first; shape < output.Count; shape++)
                    {
                        var command = output[shape];
                        command.Alpha = alpha;
                        output[shape] = command;
                    }
                }
            }
            Rect(output, 1000, 725, 500, 115, CutsceneInk.DarkRed);
            Line(output, 1000, 762, 1500, 762, 2, CutsceneInk.Ink);
            for (int index = 1; index < output.Count; index++)
            {
                var command = output[index];
                Project(ref command, camera.Zoom, camera.OffsetX, camera.OffsetY);
                output[index] = command;
            }
            // The ground is a screen-wide slab so no camera position can expose ink below the floor line.
            var floor = Math.Max(0, camera.Y(timing.FloorY));
            if (floor < PortraitScore.Height)
                output.Insert(1, new CutsceneCommand
                {
                    Primitive = CutscenePrimitive.Rectangle, X = 0, Y = floor, Width = PortraitScore.Width,
                    Height = PortraitScore.Height - floor, Ink = CutsceneInk.DarkRed15, Alpha = 1, Scale = 1
                });
            // Skipping is the controller's on-screen button, named by the hint before the opening.
        }

        /// <summary>The black frame before the opening, with its one line rising in and fading away.</summary>
        public static void DrawSkipHint(double time, PortraitScore score, List<CutsceneCommand> output)
        {
            output.Clear();
            Rect(output, 0, 0, PortraitScore.Width, PortraitScore.Height, CutsceneInk.Ink);
            var entry = EaseOut(Segment(time, 0, score.SkipHintIn));
            var line = Text(score.SkipHintText, PortraitScore.Width / 2d, PortraitScore.Height / 2d + 12 * (1 - entry), 30, CutsceneInk.Bone);
            line.Alpha = entry * (1 - EaseIn(Segment(time, score.SkipHintIn + score.SkipHintHold, score.SkipHintDuration)));
            output.Add(line);
        }

        public static double BubbleOpacity(double time, int index, CutsceneTiming timing, PortraitScore score)
        {
            var start = timing.BubbleStarts[index];
            var end = index + 1 < timing.BubbleStarts.Length ? timing.BubbleStarts[index + 1] : timing.BubbleFadeEnd;
            return EaseOut(Segment(time, start, start + score.BubbleEntry)) * (1 - EaseIn(Segment(time, end - score.Exit, end)));
        }

        // Board shot, a walk-in that tracks the student at the desk shot's own framing, the board
        // again while the teacher writes, the desk, then a push into the phone the student holds.
        public static StoryCamera OpeningCamera(double time, CutsceneTiming timing, PortraitScore score, CutsceneFrame frame)
        {
            var toBoard = EaseInOut(Segment(time, timing.OpeningSitEnd - score.Reframe, timing.OpeningSitEnd));
            var toDesk = EaseInOut(Segment(time, timing.CameraPushStart, timing.CameraPushStart + score.Reframe));
            var board = time < timing.StudentEnter ? 1 : toBoard * (1 - toDesk);
            var trackX = score.OpeningDeskFocusX + Math.Max(0, frame.StudentX - timing.StudentSeatX);
            var focusX = Lerp(trackX, score.OpeningBoardFocusX, board);
            var focusY = Lerp(score.OpeningDeskFocusY, score.OpeningBoardFocusY, board);
            var shot = Lerp(score.OpeningDeskZoom, score.OpeningBoardZoom, board);
            var push = EaseInOut(Segment(time, timing.PhoneAppStart, timing.PhoneAppStart + score.Reframe));
            var fill = Math.Max(PortraitScore.Width / (double)PhoneScreenWidth,
                PortraitScore.Height / (double)PhoneScreenHeight) * score.PhoneFill;
            // The phone's projection travels straight to screen center, so it can never leave the frame.
            return Toward(frame.PhoneX, frame.PhoneY,
                new StoryCamera { Zoom = shot, OffsetX = PortraitScore.Width / 2d - focusX * shot, OffsetY = PortraitScore.Height / 2d - focusY * shot },
                fill, push);
        }

        private static StoryCamera Toward(double worldX, double worldY, StoryCamera from, double zoom, double progress)
        {
            var result = new StoryCamera { Zoom = Lerp(from.Zoom, zoom, progress) };
            result.OffsetX = Lerp(from.X(worldX), PortraitScore.Width / 2d, progress) - worldX * result.Zoom;
            result.OffsetY = Lerp(from.Y(worldY), PortraitScore.Height / 2d, progress) - worldY * result.Zoom;
            return result;
        }

        public static (double ElbowX, double ElbowY, double HandX, double HandY) TeacherArm(
            double time, double writing, CutsceneTiming timing, PortraitScore score)
        {
            double raised = EaseInOut(Segment(time, timing.WritingStart - score.ArmRamp, timing.WritingStart))
                * (1 - EaseInOut(Segment(time, timing.TeacherArmEnd, timing.TeacherArmEnd + score.ArmRamp)));
            double targetX = score.ChalkTargetX + score.ChalkSweep * Math.Sin(8 * Math.PI * writing);
            double targetY = score.ChalkTargetY;
            double distance = Math.Sqrt(targetX * targetX + targetY * targetY);
            var bearing = Math.Atan2(targetY, targetX);
            var split = Math.Acos(Clamp((distance * distance + score.UpperArm * score.UpperArm - score.Forearm * score.Forearm)
                / (2 * distance * score.UpperArm), -1, 1));
            var reach = bearing - split;
            var forearm = Math.Atan2(targetY - Math.Sin(reach) * score.UpperArm, targetX - Math.Cos(reach) * score.UpperArm);
            var upper = ShortestAngle(score.ArmRestUpper, reach, raised);
            var lower = ShortestAngle(score.ArmRestLower, forearm, raised);
            var elbow = new Point(Math.Cos(upper) * score.UpperArm, Math.Sin(upper) * score.UpperArm);
            return (elbow.X, elbow.Y, elbow.X + Math.Cos(lower) * score.Forearm, elbow.Y + Math.Sin(lower) * score.Forearm);
        }

        private static double ShortestAngle(double from, double to, double progress) =>
            from + Math.Atan2(Math.Sin(to - from), Math.Cos(to - from)) * progress;

        private static void Project(ref CutsceneCommand command, double scale, double offsetX, double offsetY)
        {
            command.X = command.X * scale + offsetX;
            command.Y = command.Y * scale + offsetY;
            command.Width = command.Width * scale + (command.Primitive == CutscenePrimitive.Line ? offsetX : 0);
            command.Height = command.Height * scale + (command.Primitive == CutscenePrimitive.Line ? offsetY : 0);
            command.Stroke *= scale;
            command.ClipX = command.ClipX * scale + offsetX;
            command.ClipY = command.ClipY * scale + offsetY;
            command.ClipWidth *= scale;
            command.ClipHeight *= scale;
            command.FontSize = (int)Math.Max(1, Math.Round(command.FontSize * scale));
        }

        public static StoryCamera RoomCamera(StoryScene scene, double time, PortraitScore score)
        {
            var room = new StoryCamera
            {
                Zoom = score.RoomZoom,
                OffsetX = PortraitScore.Width / 2d - score.RoomCenterX * score.RoomZoom,
                OffsetY = score.RoomFloorY - RoomFloorWorldY * score.RoomZoom
            };
            if (scene != StoryScene.Closing) return room;
            var grade = EaseInOut(Segment(time, score.GradeAt - score.Reframe, score.GradeAt))
                * (1 - EaseInOut(Segment(time, score.PhoneAt - score.Reframe, score.PhoneAt)));
            var phone = EaseInOut(Segment(time, score.PhoneAt, score.PhoneAt + score.Reframe));
            return phone > 0 ? Toward(RoomPhoneX, RoomPhoneY, room, score.PhoneZoom, phone)
                : Toward(RoomGradeX, RoomGradeY, room, score.GradeZoom, grade);
        }

        // Side view of one person on the chair, facing the table. The hip never leaves the seat.
        private static Rig MontageRig(int pose)
        {
            var hip = new Point(238, 1056);
            var rig = new Rig
            {
                Hip = hip,
                LeftKnee = hip + new Point(74, 3), LeftFoot = new Point(hip.X + 76, RoomFloorWorldY - 5),
                RightKnee = hip + new Point(84, -1), RightFoot = new Point(hip.X + 94, RoomFloorWorldY - 5)
            };
            if (pose == 0)
            {
                // Upright, both hands holding the phone at the chest, head slightly lowered.
                rig.Shoulder = hip + new Point(8, -128);
                rig.Head = rig.Shoulder + new Point(14, -40);
                rig.LeftElbow = rig.Shoulder + new Point(18, 64); rig.LeftHand = rig.Shoulder + new Point(66, 58);
                rig.RightElbow = rig.Shoulder + new Point(24, 60); rig.RightHand = rig.Shoulder + new Point(72, 54);
            }
            else if (pose == 1)
            {
                // Slumped forward: one elbow on the table edge with the cheek on that hand,
                // the other hand holding the phone low over the knees.
                rig.Shoulder = hip + new Point(48, -116);
                rig.Head = rig.Shoulder + new Point(36, -30);
                rig.LeftElbow = new Point(352, 944); rig.LeftHand = rig.Head + new Point(14, 20);
                rig.RightElbow = rig.Shoulder + new Point(-4, 58); rig.RightHand = rig.Shoulder + new Point(34, 96);
            }
            else
            {
                // Leaning back against the chair, legs pushed out, phone raised to eye level.
                rig.Shoulder = hip + new Point(-34, -122);
                rig.Head = rig.Shoulder + new Point(-8, -42);
                rig.LeftElbow = rig.Shoulder + new Point(20, 62); rig.LeftHand = rig.Shoulder + new Point(62, 100);
                rig.RightElbow = rig.Shoulder + new Point(46, 22); rig.RightHand = rig.Shoulder + new Point(84, -14);
                rig.LeftKnee = hip + new Point(84, 10); rig.LeftFoot = new Point(hip.X + 150, RoomFloorWorldY - 5);
                rig.RightKnee = hip + new Point(92, 6); rig.RightFoot = new Point(hip.X + 172, RoomFloorWorldY - 5);
            }
            return rig;
        }

        public static void DrawPortraitRoom(StoryScene scene, double time, PortraitScore score, List<CutsceneCommand> output,
            JourneyPerformance performance = null)
        {
            output.Clear();
            bool montage = scene == StoryScene.Montage;
            bool bright = montage && score.Bright(time);
            var camera = RoomCamera(scene, time, score);
            Rect(output, 0, 0, PortraitScore.Width, PortraitScore.Height, CutsceneInk.Ink);
            double discX = score.RoomDiscX, discY = score.RoomDiscY, disc = score.RoomDiscRadius;
            Circle(output, discX, discY, disc, CutsceneInk.DarkRed40);
            for (int ring = 0; ring < 4; ring++)
                Circle(output, discX, discY, disc * (.92 - ring * .176), CutsceneInk.DarkRed60, ring: true, stroke: 2);
            var angle = montage ? score.Turns(time) * 2 * Math.PI : 0;
            Line(output, discX, discY, discX + Math.Sin(angle) * disc * .757, discY - Math.Cos(angle) * disc * .757, 7, CutsceneInk.DarkRed);
            Circle(output, discX, discY, 9, CutsceneInk.DarkRed);
            Rect(output, 168, 891, 19, 185, CutsceneInk.DarkRed60);
            Rect(output, 167, 1059, 155, 17, CutsceneInk.DarkRed);
            Rect(output, 180, 1076, 14, 92, CutsceneInk.DarkRed60);
            Rect(output, 296, 1076, 14, 92, CutsceneInk.DarkRed60);

            if (montage)
            {
                var shadowInks = new[] { CutsceneInk.Shadow30, CutsceneInk.Shadow15, CutsceneInk.Shadow08 };
                for (int pass = 0; pass < score.Passes.Length; pass++)
                    for (int layer = 2; layer >= 0; layer--)
                    {
                        double progress = (Math.Min(time, score.Stop) - score.Passes[pass]) / score.PassDuration;
                        if (progress < 0 || progress >= 1) continue;
                        bool forward = pass % 2 == 0;
                        double x = Lerp(score.ShadowStartX, score.ShadowEndX, forward ? progress : 1 - progress)
                            - (forward ? 1 : -1) * layer * score.ShadowSpacing / score.RoomZoom;
                        DrawRig(output, BaseRig(new Point(x, 1000), 0, Math.Sin(progress * score.ShadowStride) * score.ShadowSwing, RoomFloorWorldY),
                            shadowInks[layer], 6 + layer, false);
                    }
                int pose = score.Pose(time);
                var rig = MontageRig(pose);
                DrawRig(output, rig, bright ? CutsceneInk.Bone : CutsceneInk.Bone60, 1, false, 1, 5);
                var phone = pose == 0 ? rig.Shoulder + new Point(74, 20) : rig.RightHand + new Point(0, -30);
                Phone(output, phone.X, phone.Y, true);
            }
            Rect(output, 353, 945, 310, 24, bright ? CutsceneInk.DarkRed : CutsceneInk.DarkRed60);
            Rect(output, 383, 969, 18, 199, CutsceneInk.DarkRed60);
            Rect(output, 621, 969, 18, 199, CutsceneInk.DarkRed60);
            Rect(output, 509, 928, 90, 17, bright ? CutsceneInk.DarkRed : CutsceneInk.DarkRed40);
            if (!montage)
            {
                Rect(output, RoomPhoneX - 19, RoomPhoneY - 34, 38, 68, CutsceneInk.DarkRed40);
                Rect(output, RoomPhoneX - 17, RoomPhoneY - 32, 34, 64, CutsceneInk.Ink);
                if (time < score.PaperAt)
                {
                    var caption = Text("三个月后", score.RoomCenterX, 724, 50, CutsceneInk.Bone);
                    caption.Alpha = EaseOut(Segment(time, 0, score.TitleIn)) * (1 - EaseIn(Segment(time, score.PaperAt - score.Exit, score.PaperAt)));
                    output.Add(caption);
                }
                else
                {
                    var drop = EaseOut(Segment(time, score.PaperAt, score.PaperAt + score.PaperEntry));
                    var width = 148d;
                    var height = 189d;
                    var y = RoomGradeY - 230 * (1 - drop);
                    Rect(output, RoomGradeX - width / 2, y - height / 2, width, height, CutsceneInk.Bone);
                    output.Add(Text("高等数学", RoomGradeX, y - height * .30, 10, CutsceneInk.Ink));
                    Line(output, RoomGradeX - width * .38, y - height * .20, RoomGradeX + width * .38, y - height * .20, 2, CutsceneInk.DarkRed);
                    var gradeInk = time < score.PhoneAt ? CutsceneInk.Red : CutsceneInk.DarkRed;
                    if (performance?.Grade == 0)
                        gradeInk = performance.SkippedGame ? CutsceneInk.White : CutsceneInk.Gold;
                    var gradeText = performance == null ? "58" : FormattableString.Invariant($"{performance.Grade}");
                    var grade = Text(gradeText, RoomGradeX - (performance == null ? 0 : 31), y + height * .19, 49, gradeInk);
                    grade.SubjectId = 4;
                    grade.Alpha = EaseOut(Segment(time, score.GradeAt - score.GradeEntry, score.GradeAt))
                        * (time < score.PhoneAt ? Lerp(1, .6, EaseIn(Segment(time, score.PhoneAt - score.Exit, score.PhoneAt))) : 1);
                    if (performance?.EarnedGradeCelebration == true)
                        DrawGradeConfetti(output, y - 18, time - score.GradeRevealAt);
                    output.Add(grade);
                    if (performance != null)
                    {
                        var acc = Text(FormattableString.Invariant($"Acc {performance.Accuracy:0.##}%"),
                            RoomGradeX + 32, grade.Y - 7, 13, CutsceneInk.DarkRed);
                        acc.Alpha = grade.Alpha;
                        output.Add(acc);
                    }
                }
                if (time >= score.PhoneAt)
                {
                    Circle(output, RoomPhoneX, RoomPhoneY - 9, 7, CutsceneInk.Red, 2);
                    AppNote(output, RoomPhoneX, RoomPhoneY - 9, 14d / 48);
                    output.Add(Text("冈亦芸音乐", RoomPhoneX, RoomPhoneY + 3, 3, CutsceneInk.Bone));
                    output.Add(Text("广告 · 正在加载…", RoomPhoneX, RoomPhoneY + 8, 2, CutsceneInk.Bone40));
                }
            }
            for (int index = 1; index < output.Count; index++)
            {
                var command = output[index];
                Project(ref command, camera.Zoom, camera.OffsetX, camera.OffsetY);
                output[index] = command;
            }
            // Wall above the floor line and dark ground below it, each spanning the whole frame.
            var floor = Clamp(camera.Y(RoomFloorWorldY), 0, PortraitScore.Height);
            output.Insert(1, new CutsceneCommand
            {
                Primitive = CutscenePrimitive.Rectangle, X = 0, Y = 0, Width = PortraitScore.Width, Height = floor,
                Ink = CutsceneInk.DarkRed15, Alpha = 1, Scale = 1
            });
            output.Insert(2, new CutsceneCommand
            {
                Primitive = CutscenePrimitive.Line, X = 0, Y = floor, Width = PortraitScore.Width, Height = floor,
                Stroke = 4 * camera.Zoom, Ink = CutsceneInk.DarkRed40, Alpha = 1, Scale = 1
            });
        }

        private static void DrawGradeConfetti(List<CutsceneCommand> output, double y, double age)
        {
            const double lifetime = .9;
            if (age <= 0 || age >= lifetime) return;
            double size = EaseOut(age / .045) * (1 - EaseIn(Segment(age, .42, lifetime)));
            for (int piece = 0; piece < 24; piece++)
            {
                int side = piece % 2 == 0 ? -1 : 1;
                int strand = piece / 2;
                double speed = 30 + 4 * ((strand * 7 + piece % 2) % 11);
                double rise = 70 + 8 * (strand % 6) + 7 * (piece % 2);
                double x = RoomGradeX + side * (38 + speed * age);
                double height = (6 + 3 * (strand % 3)) * size;
                double width = (3 + 2 * (strand % 2)) * size;
                double centerY = y + 4 * (strand % 3) - rise * age + 94 * age * age;
                var ink = piece % 3 == 0 ? CutsceneInk.Red : piece % 3 == 1 ? CutsceneInk.Gold : CutsceneInk.DarkRed;
                RotatedRect(output, x - width / 2, centerY - height / 2, width, height,
                    side * (20 + 29 * strand + 220 * age), ink, 0);
            }
        }
    }
}
