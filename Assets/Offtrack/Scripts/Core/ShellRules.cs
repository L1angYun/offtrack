using System;

namespace Offtrack.Core
{
    [Serializable]
    public sealed class ShellRules
    {
        public double BootDuration = 3.7;
        public double BootHoldDuration = 0.6;
        public double RetryBlackDuration = 0.2;
        public double SkipCountdownDuration = 8;
        public float DialogEntranceDuration = 0.18f;
        public float DialogExitDuration = 0.2f;
        public float DialogPressDuration = 0.08f;
        public float DialogDebounceDuration = 0.3f;
        public float DialogEntranceOffset = 12;
        public double StayResumeDelay = 0.54;

        public void Validate()
        {
            RequireFinitePositive(BootDuration, nameof(BootDuration));
            if (double.IsNaN(BootHoldDuration) || BootHoldDuration < 0 || BootHoldDuration >= BootDuration)
                throw new ArgumentOutOfRangeException(nameof(BootHoldDuration));
            RequireFinitePositive(RetryBlackDuration, nameof(RetryBlackDuration));
            if (double.IsNaN(StayResumeDelay) || double.IsInfinity(StayResumeDelay) || StayResumeDelay < 0)
                throw new ArgumentOutOfRangeException(nameof(StayResumeDelay));
            if (double.IsNaN(SkipCountdownDuration) || double.IsInfinity(SkipCountdownDuration)
                || SkipCountdownDuration < 0 || SkipCountdownDuration > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(SkipCountdownDuration));
        }

        internal ShellRules Copy() => (ShellRules)MemberwiseClone();

        private static void RequireFinitePositive(double value, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
                throw new ArgumentOutOfRangeException(name, "Must be finite and greater than zero.");
        }
    }
}
