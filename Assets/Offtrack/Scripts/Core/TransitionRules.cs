using System;

namespace Offtrack.Core
{
    [Serializable]
    public sealed class TransitionRules
    {
        public double FadeSeconds = 0.4;
        public int BlackFrames = 2;

        public double InkOpacity(double elapsed, bool revealing)
        {
            double fraction = Math.Max(0, Math.Min(1, elapsed / FadeSeconds));
            return revealing ? 1 - fraction : fraction;
        }
    }
}
