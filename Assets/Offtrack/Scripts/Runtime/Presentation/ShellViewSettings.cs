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
        public Vector2 RecordOrigin = new Vector2(0, -60);
        public float RecordScale = .9f;
        public Vector2 ProgressPosition = new Vector2(0, -578);
        public Vector2 TimerPosition = new Vector2(230, -630);
        public Vector2 TimerSize = new Vector2(260, 56);
        public int TimerFontSize = 24;
        public Vector2 LivesPosition = new Vector2(-240, -630);
        public Vector2 LifeSize = new Vector2(26, 6);
        public float LifeGap = 6;
        public Vector2 PlayPosition = new Vector2(-324, -710);
        public Vector2 PlaySize = new Vector2(68, 68);
        public Vector2 HintPosition = new Vector2(-158, -710);
        public Vector2 HintSize = new Vector2(240, 64);
        public int HintFontSize = 22;
        public Vector2 SkipPosition = new Vector2(300, 764);
        public Vector2 SkipSize = new Vector2(116, 48);
        public int SkipFontSize = 22;
        public Vector2 DialogPosition = new Vector2(0, 0);
        public Vector2 DialogSize = new Vector2(656, 440);
        public Vector2 DialogButtonSize = new Vector2(572, 88);
        public int DialogTitleFontSize = 30;
        public int DialogBodyFontSize = 25;
        public int DialogButtonFontSize = 27;
        public Vector2 LotteryTitlePosition = new Vector2(0, 105);
        public Vector2 LotteryBodyPosition = new Vector2(0, 24);
        public Vector2 DialogTitlePosition = new Vector2(0, 148);
        public Vector2 DialogBodyPosition = new Vector2(0, 86);
        public Vector2 CompleteTitlePosition = new Vector2(0, -420);
        public Vector2 CompleteBodyPosition = new Vector2(0, -476);
        public Vector2 BootProgressSize = new Vector2(300, 4);
        public float OutlineWidth = 2;
    }
}
