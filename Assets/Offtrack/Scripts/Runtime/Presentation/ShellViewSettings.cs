using System;
using UnityEngine;

namespace Offtrack.Runtime.Presentation
{
    // Reference pixels: the prototype's 390 x 844 CSS frame, doubled.
    [Serializable]
    public sealed class ShellViewSettings
    {
        // V2-2: the plinth keeps its top edge and ends just under the ruler; the meter HUD takes
        // the freed band. Contents are plinth-local, so they shift with the new centre (75).
        public Vector2 StagePosition = new Vector2(0, 75);
        public Vector2 StageSize = new Vector2(732, 1050);
        public Vector2 RecordOrigin = new Vector2(-16, -180);
        public float RecordScale = .94f;
        [Header("唱机台座（参考像素）")]
        public float StageOutlineWidth = 2;
        public Vector2 ArmBaseSize = new Vector2(82, 82);
        public Vector2 StagePrintLeft = new Vector2(-170, 465);
        public Vector2 StagePrintRight = new Vector2(168, 465);
        public Vector2 StagePrintSize = new Vector2(300, 32);
        public int StagePrintFontSize = 18;
        public Vector2 StageStatusPosition = new Vector2(-132, -435);
        public Vector2 StageStatusSize = new Vector2(380, 36);
        public int StageStatusFontSize = 20;
        public Vector2 BeatLabelPosition = new Vector2(171, -435);
        public Vector2 BeatLabelSize = new Vector2(80, 32);
        public Vector2 BeatLightOrigin = new Vector2(238, -435);
        public Vector2 BeatLightSize = new Vector2(10, 16);
        public float BeatLightPitch = 22;
        public Vector2 ScaleOrigin = new Vector2(-315, -495);
        public float ScaleLength = 630;
        public float ScalePitch = 35;
        public Vector2 ScaleTickSize = new Vector2(2, 6);
        public float ScaleMajorTickHeight = 12;
        public int ScaleMajorEvery = 3;
        [Header("外壳文字")]
        public float MainTextBaselineEm = .12f;
        public float DialogTitleTracking = 3;
        public Vector2 ProgressPosition = new Vector2(0, -498);
        [Header("唱机仪表 HUD（V2-2）")]
        // The live value is right-aligned on a fixed edge; the remaining time sits in its own
        // fixed box, so neither moves while digits change.
        public Vector2 TimerPosition = new Vector2(96, -552);
        public Vector2 TimerSize = new Vector2(200, 60);
        public int TimerFontSize = 40;
        public Vector2 TimerRemainingPosition = new Vector2(286, -550);
        public Vector2 TimerRemainingSize = new Vector2(144, 40);
        public int TimerRemainingFontSize = 20;
        public Vector2 LivesPosition = new Vector2(-266, -552);
        public Vector2 LifeSize = new Vector2(40, 10);
        public float LifeGap = 8;
        public Vector2 LaneDotOrigin = new Vector2(-346, -628);
        public Vector2 LaneDotSize = new Vector2(12, 12);
        public float LaneDotPitch = 20;
        public Vector2 NeedleStatusPosition = new Vector2(-218, -628);
        public Vector2 NeedleStatusSize = new Vector2(150, 36);
        public int NeedleStatusFontSize = 20;
        public Vector2 CooldownPosition = new Vector2(120, -628);
        public Vector2 CooldownSize = new Vector2(476, 4);
        public Vector2 DamageNoticePosition = new Vector2(-178, -690);
        public Vector2 DamageNoticeSize = new Vector2(360, 36);
        public int DamageNoticeFontSize = 20;
        public float DamageNoticeSeconds = 2.5f;
        public int CompleteBodyFontSize = 34;
        // A static phone home indicator: part of the shell, closes the bottom edge of the frame.
        public Vector2 HomeIndicatorPosition = new Vector2(0, -826);
        public Vector2 HomeIndicatorSize = new Vector2(140, 5);
        public Vector2 PlayPosition = new Vector2(-324, -632);
        public Vector2 PlaySize = new Vector2(68, 68);
        public Vector2 HintPosition = new Vector2(-140, -628);
        public Vector2 HintSize = new Vector2(260, 64);
        public int HintFontSize = 24;
        public Vector2 SkipPosition = new Vector2(296, 764);
        public Vector2 SkipSize = new Vector2(124, 50);
        public int SkipFontSize = 20;
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
        public float DialogOutlineWidth = 2;
        public Vector2 CompleteTitlePosition = new Vector2(0, -722);
        public Vector2 CompleteBodyPosition = new Vector2(0, -778);
        public Vector2 BootProgressSize = new Vector2(300, 4);
        public float OutlineWidth = 2;
        // Persistent HUD sits right under the progress line (lives, time, how-to); the idle
        // call to action takes the bottom row, so hiding it during a run leaves no hole mid-HUD.
        public Vector2 InstructionPosition = new Vector2(-74, -716);
        public Vector2 InstructionSize = new Vector2(420, 40);
        public int InstructionFontSize = 24;
        // Letter spacing (reference pixels) for the dialog tag and buttons, as in the prototype.
        public float DisplayTracking = 8;
        [HideInInspector] public int MainScreenRevision;

        // Upgrade only fields whose old saved values predate the V2 main screen. Subsequent builds
        // preserve authored overrides instead of resetting the asset every time.
        public void UpgradeMainScreen()
        {
            if (MainScreenRevision >= 5) return;
            var defaults = new ShellViewSettings();
            if (MainScreenRevision >= 2)
            {
                // Revisions 3-4 were pre-release meter layouts; 5 spreads the meter rows.
                UpgradeMeter(defaults);
                MainScreenRevision = 5;
                return;
            }
            if (MainScreenRevision < 1)
            {
                RecordOrigin = defaults.RecordOrigin;
                RecordScale = defaults.RecordScale;
                SkipFontSize = defaults.SkipFontSize;
                DialogOutlineWidth = defaults.DialogOutlineWidth;
            }
            CompleteTitlePosition = defaults.CompleteTitlePosition;
            CompleteBodyPosition = defaults.CompleteBodyPosition;
            ScaleTickSize = defaults.ScaleTickSize;
            UpgradeMeter(defaults);
            MainScreenRevision = 5;
        }

        // V2-2 meter HUD: shorter plinth, larger timer and lives, CTA moved into the meter row.
        private void UpgradeMeter(ShellViewSettings defaults)
        {
            StagePosition = defaults.StagePosition;
            StageSize = defaults.StageSize;
            RecordOrigin = defaults.RecordOrigin;
            StagePrintLeft = defaults.StagePrintLeft;
            StagePrintRight = defaults.StagePrintRight;
            StageStatusPosition = defaults.StageStatusPosition;
            BeatLabelPosition = defaults.BeatLabelPosition;
            BeatLightOrigin = defaults.BeatLightOrigin;
            ScaleOrigin = defaults.ScaleOrigin;
            ProgressPosition = defaults.ProgressPosition;
            TimerPosition = defaults.TimerPosition;
            TimerSize = defaults.TimerSize;
            TimerFontSize = defaults.TimerFontSize;
            LivesPosition = defaults.LivesPosition;
            LifeSize = defaults.LifeSize;
            LifeGap = defaults.LifeGap;
            PlayPosition = defaults.PlayPosition;
            HintPosition = defaults.HintPosition;
            InstructionPosition = defaults.InstructionPosition;
            CompleteTitlePosition = defaults.CompleteTitlePosition;
            CompleteBodyPosition = defaults.CompleteBodyPosition;
        }
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
