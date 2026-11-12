using System;
using UnityEngine;

namespace Offtrack.Runtime.Presentation
{
    // Reference pixels: the prototype's 390 x 844 CSS frame, doubled.
    [Serializable]
    public sealed class ShellViewSettings
    {
        public Vector2 StagePosition = new Vector2(0, 30);
        public Vector2 StageSize = new Vector2(732, 1140);
        public Vector2 RecordOrigin = new Vector2(0, -135);
        public float RecordScale = 1f;
        public Vector2 ProgressPosition = new Vector2(0, -570);
        public Vector2 TimerPosition = new Vector2(228, -614);
        public Vector2 TimerSize = new Vector2(260, 56);
        public int TimerFontSize = 26;
        public Vector2 LivesPosition = new Vector2(-297, -618);
        public Vector2 LifeSize = new Vector2(26, 6);
        public float LifeGap = 6;
        public Vector2 PlayPosition = new Vector2(-324, -746);
        public Vector2 PlaySize = new Vector2(68, 68);
        public Vector2 HintPosition = new Vector2(-140, -742);
        public Vector2 HintSize = new Vector2(260, 64);
        public int HintFontSize = 24;
        public Vector2 SkipPosition = new Vector2(296, 764);
        public Vector2 SkipSize = new Vector2(124, 50);
        public int SkipFontSize = 24;
        public Vector2 DialogPosition = new Vector2(0, 0);
        public Vector2 DialogSize = new Vector2(656, 440);
        public Vector2 DialogButtonSize = new Vector2(572, 88);
        public int DialogTitleFontSize = 30;
        public int DialogBodyFontSize = 25;
        public int DialogButtonFontSize = 28;
        // Each dialog's panel fits its content: 42 px padding at the bottom and sides, 20 px between
        // stacked buttons. Y values are dialog-local rect centres.
        public DialogLayout LotteryLayout = new DialogLayout { Height = 400, TagY = 154, TitleY = 100, BodyY = 28, PrimaryY = -114 };
        public DialogLayout ConfirmLayout = new DialogLayout { Height = 420, TitleY = 156, BodyY = 94, SecondaryY = -16, PrimaryY = -124 };
        public DialogLayout FailLayout = new DialogLayout { Height = 300, TitleY = 96, BodyY = 38, PrimaryY = -64 };
        public float DialogOutlineWidth = 3;
        // Opaque ink behind every dialog, from under the brand row to just above the progress line, so
        // the modal sits on a clean field instead of on the record (no dimming: no translucency allowed).
        public Vector2 DialogBackdropPosition = new Vector2(0, 22);
        public Vector2 DialogBackdropSize = new Vector2(780, 1156);
        public Vector2 CompleteTitlePosition = new Vector2(0, -416);
        public Vector2 CompleteBodyPosition = new Vector2(0, -472);
        public Vector2 BootProgressSize = new Vector2(300, 4);
        public float OutlineWidth = 2;
        // Persistent HUD sits right under the progress line (lives, time, how-to); the idle
        // call to action takes the bottom row, so hiding it during a run leaves no hole mid-HUD.
        public Vector2 InstructionPosition = new Vector2(-74, -682);
        public Vector2 InstructionSize = new Vector2(420, 40);
        public int InstructionFontSize = 24;
        // Letter spacing (reference pixels) for the dialog tag and buttons, as in the prototype.
        public float DisplayTracking = 8;
    }

    [Serializable]
    public sealed class DialogLayout
    {
        public float Height = 440;
        public float TagY;
        public float TitleY;
        public float BodyY;
        public float SecondaryY;
        public float PrimaryY;
    }
}
