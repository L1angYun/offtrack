using System;

namespace Offtrack.Core
{
    [Serializable]
    public sealed class RuleConfig
    {
        public int LaneCount;
        public int StartLane;
        public int Lives;
        public double JumpDuration;
        public double SettleDuration;
        public double HitInvincibility;
        public double EarlyWindow;
        public double LateWindow;
        public double WindowSwitchTime;
        public double EarlyLandingThreshold;
        public double Duration;
        public double Bpm;
        public double BeatsPerRevolution;

        public void Validate()
        {
            if (LaneCount <= 0) throw new ArgumentOutOfRangeException(nameof(LaneCount));
            if (StartLane < 0 || StartLane >= LaneCount) throw new ArgumentOutOfRangeException(nameof(StartLane));
            if (Lives <= 0) throw new ArgumentOutOfRangeException(nameof(Lives));
            RequireFinitePositive(JumpDuration, nameof(JumpDuration));
            RequireFiniteNonNegative(SettleDuration, nameof(SettleDuration));
            RequireFiniteNonNegative(HitInvincibility, nameof(HitInvincibility));
            RequireFiniteNonNegative(EarlyWindow, nameof(EarlyWindow));
            RequireFiniteNonNegative(LateWindow, nameof(LateWindow));
            RequireFiniteNonNegative(WindowSwitchTime, nameof(WindowSwitchTime));
            RequireFiniteNonNegative(EarlyLandingThreshold, nameof(EarlyLandingThreshold));
            RequireFinitePositive(Duration, nameof(Duration));
            RequireFinitePositive(Bpm, nameof(Bpm));
            RequireFinitePositive(BeatsPerRevolution, nameof(BeatsPerRevolution));
        }

        internal RuleConfig Copy() => (RuleConfig)MemberwiseClone();

        private static void RequireFinitePositive(double value, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
                throw new ArgumentOutOfRangeException(name, "Must be finite and greater than zero.");
        }

        private static void RequireFiniteNonNegative(double value, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
                throw new ArgumentOutOfRangeException(name, "Must be finite and nonnegative.");
        }
    }
}
