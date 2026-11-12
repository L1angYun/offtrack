using System;
using Offtrack.Core;

namespace Offtrack.Runtime
{
    [Serializable]
    public sealed class CutsceneSettings
    {
        public double OpeningDuration = 15;
        public double ClosingDuration = 12;
        public double EndFrameHold = .5;
        public bool ResizeStandaloneWindow = true;
        public int WindowWidth = 1280;
        public int WindowHeight = 720;
        public int MainWindowWidth = 585;
        public int MainWindowHeight = 1266;
        // Physical-pixel allowance for the captioned resizable Windows frame at 100-200% DPI.
        // Native AdjustWindowRectExForDpi sweep (96..192 DPI) is <=26x71; even 2x the
        // 96-DPI frame (32x78) fits this reserve. Taskbar insets are handled separately.
        public int WindowFrameWidth = 32;
        public int WindowFrameHeight = 96;
        public int CircleSegments = 192;
        public CutsceneTiming Timing = new CutsceneTiming();
    }
}
