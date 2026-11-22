using System;
using Offtrack.Core;

namespace Offtrack.Runtime
{
    [Serializable]
    public sealed class CutsceneSettings
    {
        // Durations share the authored timeline's source of truth.
        public double OpeningDuration { get => Timing.OpeningDuration; set => Timing.OpeningDuration = value; }
        public double EndFrameHold = .5;
        [UnityEngine.Tooltip("After the ending's ad close-up, seconds before the ad itself (Main) starts again.")]
        public double ClosingContinueAfter;
        public bool ResizeStandaloneWindow = true;
        [UnityEngine.Serialization.FormerlySerializedAs("MainWindowWidth")] public int JourneyWindowWidth = 585;
        [UnityEngine.Serialization.FormerlySerializedAs("MainWindowHeight")] public int JourneyWindowHeight = 1266;
        public float TitleDegreesPerSecond = 9;
        [UnityEngine.Tooltip("Credits at the bottom left of the title page.")]
        public string TitleCredits = "策划：柳演锡\n程序：Ryō & Vanty\n音乐：银斑";
        public UnityEngine.Vector2 TitleCreditsPosition = new UnityEngine.Vector2(-340, -790);
        public int TitleCreditsFontSize = 20;
        // Physical-pixel allowance for the captioned resizable Windows frame at 100-200% DPI.
        // Native AdjustWindowRectExForDpi sweep (96..192 DPI) is <=26x71; even 2x the
        // 96-DPI frame (32x78) fits this reserve. Taskbar insets are handled separately.
        public int WindowFrameWidth = 32;
        public int WindowFrameHeight = 96;
        [UnityEngine.Tooltip("Window sizes above are for this display height; taller displays scale them up proportionally.")]
        public int WindowReferenceHeight = 1080;
        public int CircleSegments = 192;
        [UnityEngine.Tooltip("Cutscene skip button: offset of its top-right corner from the frame's, in reference pixels.")]
        public UnityEngine.Vector2 SkipButtonOffset = new UnityEngine.Vector2(-32, -55);
        public UnityEngine.Vector2 SkipButtonSize = new UnityEngine.Vector2(124, 50);
        public int SkipButtonFontSize = 20;
        public float SkipButtonOutline = 2;
        public CutsceneTiming Timing = new CutsceneTiming();
        public PortraitScore Portrait = new PortraitScore();
        public TransitionRules Transition = new TransitionRules();
        public CutsceneMix Mix = new CutsceneMix();
    }

    [Serializable]
    public sealed class CutsceneMix
    {
        [UnityEngine.Range(0, 1)] public float OpeningGain = .6f;
        public float RoomA = .44f;
        public float ClassAir = .36f;
        public float Chalk = .48f;
        public float OpeningEvent = .55f;
        public float OpeningExit = .48f;
        public float ClockNoise = .34f;
        public float TickPitchStart = 1;
        public float TickPitchEnd = 1.35f;
        public float Tick = 1;
        public float Whoosh = 1;
        public float Thud = 1;
        public float RoomB = .37f;
        public float RoomBSoft = .27f;
        public double DspLookahead = .12;
        public float Step = 1;
        public float Paper = 1;
        public float Piano = 1;
        [UnityEngine.Range(0, 1)] public float Confetti = .65f;
        public float Phone = 1;
    }
}
