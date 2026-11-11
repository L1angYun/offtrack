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
        public float GrooveOuterRatio = .97f;
        public int GrooveCount = 24;
        public int CircleSegments = 192;
        public float OutlineWidth = 2;
        public float GrooveWidth = 1;
        public float LaneWidth = 3;
        public float LabelInnerRatio = .88f;
        public float SpindleRatio = .033f;
        public Vector2 ScratchSize = new Vector2(49, 4);
        public Vector2 ScratchCrossSize = new Vector2(35, 3);
        public float ScratchCrossAngle = 12;

        [Header("固定接触位 / 唱臂")]
        public Vector2 ContactSize = new Vector2(20, 3);
        public float ContactNumberOffset = 45;
        public Vector2 ArmPivot = new Vector2(310, 490);
        public float ArmWidth = 9;
        public float PivotRadius = 15;
        public Vector2 HeadSize = new Vector2(19, 27);
        public Vector2 HeadOffset = new Vector2(8, 18);
        public float TipRadius = 4;
        public float LiftX = 10;
        public float LiftY = 21;

        [Header("HUD / 字号")]
        public Vector2 TimerPosition = new Vector2(0, 660);
        public Vector2 ProgressPosition = new Vector2(0, 610);
        public Vector2 ProgressSize = new Vector2(620, 5);
        public Vector2 LivesPosition = new Vector2(0, 555);
        public Vector2 LifeSize = new Vector2(48, 10);
        public float LifeGap = 12;
        public int TimerFontSize = 40;
        public int LabelFontSize = 23;
        public int LabelDetailFontSize = 12;
        public int ContactFontSize = 16;
        public int BodyFontSize = 30;
        public int ResultFontSize = 23;
        public Vector2 LabelTitlePosition = new Vector2(0, 29);
        public Vector2 LabelDetailPosition = new Vector2(0, -33);
        public Vector2 LabelRuleSize = new Vector2(118, 2);

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
