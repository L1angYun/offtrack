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
        public int CircleSegments = 192;
        public CutsceneTiming Timing = new CutsceneTiming();
    }
}
