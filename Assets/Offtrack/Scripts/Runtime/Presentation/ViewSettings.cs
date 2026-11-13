using System;
using UnityEngine;

namespace Offtrack.Runtime.Presentation
{
    [Serializable]
    public sealed class ViewSettings
    {
        [Header("画幅 / 几何（参考像素）")]
        public Vector2 ReferenceResolution = new Vector2(780, 1688);
        public float PixelsPerUnit = 100;
        public Vector2 DiscCenter = new Vector2(0, 100);
        public float DiscRadius = 326;
        public float[] LaneRadiusRatios = { .88f, .68f, .48f };
        public float LabelRadiusRatio = .295f;
        public float GrooveInnerRatio = .33f;
        public float GrooveOuterRatio = .95f;
        public int GrooveCount = 38;
        public int CircleSegments = 192;
        public float OutlineWidth = 2;
        public float GrooveWidth = 1;
        public float LaneWidth = 4;
        public float LaneClearance = 16;
        public float LaneBandWidth = 14;
        public float EdgeBandWidth = 12;
        public int EdgeNotchCount = 12;
        public int EdgeMajorNotchEvery = 3;
        public float EdgeNotchWidth = 3;
        public float EdgeNotchLength = 6;
        public float EdgeMajorNotchLength = 10;
        public float BrokenGrooveSweep = 330;
        public float LabelInnerRatio = .88f;
        public float SpindleRatio = .033f;
        public Vector2 ScratchSize = new Vector2(44, 3);
        public Vector2 ScratchCrossSize = new Vector2(32, 2);
        public float ScratchCrossAngle = 34;
        public Vector2 ScratchNotchSize = new Vector2(28, 1.5f);
        public float ScratchSecondOffset = 6;
        public float ScratchHighlightWidth = 4;
        public float ScratchGrowthSeconds = .25f;
        public float ScratchLookAheadRevolutions = .75f;
        public float ScratchHighlightBeats = 2;
        public float ApproachArcHalfAngle = 5;
        public float ApproachArcWidth = 5;

        [Header("固定接触位 / 唱臂")]
        public Vector2 ContactSize = new Vector2(20, 4);
        public float ContactRadialOffset = 11;
        public float ContactAngleDegrees = 98;
        public Vector2 ContactNumberSize = new Vector2(44, 40);
        public float ContactNumberDiscGap = 8;
        public Vector2 ArmPivot = new Vector2(310, 490);
        public float ArmWidth = 10;
        public float PivotRadius = 18;
        public Vector2 HeadSize = new Vector2(22, 30);
        public Vector2 HeadOffset = new Vector2(8, 24);
        public float TipRadius = 6;
        // Reference pixels. The prototype's lift was in CSS pixels; the 2x frame doubles it.
        public float LiftX = 20;
        public float LiftY = 42;

        [Header("唱臂机械件 / 跳针反馈")]
        public float ArmInlayWidth = 3;
        public float InlayInset = 14;
        public float BearingRadius = 26;
        public float BearingRingRadius = 17;
        public float BearingRingWidth = 3;
        public float HubRadius = 6;
        public Vector2 CounterweightSize = new Vector2(38, 24);
        public float CounterweightOffset = 40;
        public float HeadshellAngle = -16;
        public Vector2 HeadSlotSize = new Vector2(6, 16);
        public Vector2 FingerLiftSize = new Vector2(14, 4);
        public Vector2 FingerLiftOffset = new Vector2(15, 9);
        public float StylusWidth = 2;
        public float TipHitRadius = 9;
        // Parked stylus position relative to the bearing: on the rest beside the platter.
        public Vector2 ArmRestTip = new Vector2(30, -280);
        public float ArmRestPostRadius = 11;
        public float ArmRestPostShare = .72f;
        public float DropSwingShare = .7f;
        // Stop: hold the end pose (a fatal hit's red tip stays visible), lift, then swing to the rest.
        public float StopHoldShare = .25f;
        public float StopLiftShare = .25f;
        public float PressDuration = .07f;
        public float PressDepth = .1f;
        public float AirborneScale = .12f;
        public float NudgeDuration = .09f;
        public float NudgeDistance = 4;

        [Header("HUD / 字号")]
        public Vector2 TimerPosition = new Vector2(0, 660);
        public Vector2 ProgressPosition = new Vector2(0, 610);
        public Vector2 ProgressSize = new Vector2(716, 4);
        public Vector2 LivesPosition = new Vector2(0, 555);
        public Vector2 LifeSize = new Vector2(48, 10);
        public float LifeGap = 12;
        public int TimerFontSize = 40;
        public int LabelFontSize = 20;
        public int LabelDetailFontSize = 13;
        public int ContactFontSize = 24;
        public float LabelTracking = 1.1f;
        public float LabelPrintWidth = 1;
        public Vector2 LabelStampPosition = new Vector2(0, -62);
        public float LabelStampRadius = 11;
        public int LabelStampFontSize = 12;
        public int BodyFontSize = 30;
        public int ResultFontSize = 23;
        public Vector2 LabelTitlePosition = new Vector2(0, 29);
        public Vector2 LabelDetailPosition = new Vector2(0, -33);
        public Vector2 LabelRuleSize = new Vector2(118, 1);

        // One-time migration of the shipped V1 defaults. Subsequent builder runs preserve tuning.
        [HideInInspector] public int MainScreenVisualVersion;
        public void UpgradeMainScreenVisuals()
        {
            if (MainScreenVisualVersion >= 4) return;
            if (MainScreenVisualVersion >= 2)
            {
                // Version 3 was a pre-release tonearm; 4 moves the rest inside the dialog's width.
                UpgradeTonearm();
                MainScreenVisualVersion = 4;
                return;
            }
            if (MainScreenVisualVersion < 1)
            {
                GrooveInnerRatio = .33f; GrooveOuterRatio = .95f; GrooveCount = 38; GrooveWidth = 1;
                ScratchSize = new Vector2(44, 3); ScratchCrossSize = new Vector2(32, 2); ScratchCrossAngle = 34;
                LabelFontSize = 20; LabelDetailFontSize = 13; LabelRuleSize = new Vector2(118, 1);
            }
            var defaults = new ViewSettings();
            LaneClearance = defaults.LaneClearance;
            ContactSize = defaults.ContactSize;
            ContactRadialOffset = defaults.ContactRadialOffset;
            ContactAngleDegrees = defaults.ContactAngleDegrees;
            ContactNumberSize = defaults.ContactNumberSize;
            ContactNumberDiscGap = defaults.ContactNumberDiscGap;
            ContactFontSize = defaults.ContactFontSize;
            UpgradeTonearm();
            MainScreenVisualVersion = 4;
        }

        // V2-2: the mechanical arm and the doubled lift replace the V1 stick arm values.
        private void UpgradeTonearm()
        {
            var defaults = new ViewSettings();
            ArmWidth = defaults.ArmWidth;
            HeadSize = defaults.HeadSize;
            LiftX = defaults.LiftX;
            LiftY = defaults.LiftY;
            ArmRestTip = defaults.ArmRestTip;
            StopHoldShare = defaults.StopHoldShare;
            StopLiftShare = defaults.StopLiftShare;
        }

        // A readable two-digit label is taller than the untouched annulus between two blade
        // sweeps. Put the entire label outside the record instead of testing only its centre.
        public Vector2 ContactNumberPosition(int lane)
        {
            var strokeWidth = Mathf.Max(ScratchSize.y, ScratchHighlightWidth);
            var mainExtent = new Vector2(ScratchSize.x / 2, strokeWidth / 2).magnitude;
            var crossWidth = Mathf.Max(ScratchCrossSize.y, ScratchCrossSize.y * ScratchHighlightWidth / ScratchSize.y);
            var crossExtent = new Vector2(ScratchCrossSize.x / 2, crossWidth / 2).magnitude;
            var secondExtent = new Vector2(ScratchCrossSize.x / 2, Mathf.Abs(ScratchSecondOffset) + strokeWidth / 2).magnitude;
            var scratchExtent = Mathf.Max(mainExtent, crossExtent, secondExtent, ScratchNotchSize.magnitude / 2);
            var outerLane = 0f;
            foreach (var ratio in LaneRadiusRatios) outerLane = Mathf.Max(outerLane, ratio * DiscRadius);
            var safeRadius = Mathf.Max(DiscRadius, outerLane + scratchExtent) + ContactNumberDiscGap + ContactNumberSize.magnitude / 2;
            var y = DiscRadius * LaneRadiusRatios[lane];
            return DiscCenter + new Vector2(-Mathf.Sqrt(safeRadius * safeRadius - y * y), y);
        }

        [Header("待机 / 极简结算")]
        public Vector2 PlayPosition = new Vector2(0, -410);
        public float PlayRadius = 54;
        public Vector2 PlayTriangleSize = new Vector2(29, 36);
        public Vector2 IdleTextPosition = new Vector2(0, -515);
        public Vector2 ResultTitlePosition = new Vector2(0, -350);
        public Vector2 ResultBodyPosition = new Vector2(0, -408);
        public Vector2 ReplayPosition = new Vector2(0, -535);
        public Vector2 ReplaySize = new Vector2(352, 90);
    }
}
