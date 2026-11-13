using System;

namespace Offtrack.Core
{
    [Serializable]
    public sealed class ShellRules
    {
        public double BootDuration = 1;
        public double SkipCountdownDuration = 8;
        public double CompleteDuration = 3;
        public double StayResumeDelay = 0.54;

        public void Validate()
        {
            RequireFinitePositive(BootDuration, nameof(BootDuration));
            RequireFinitePositive(CompleteDuration, nameof(CompleteDuration));
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
