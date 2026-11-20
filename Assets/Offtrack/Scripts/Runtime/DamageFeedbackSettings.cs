using System;
using UnityEngine;

namespace Offtrack.Runtime
{
    [Serializable]
    public sealed class DamageFeedbackSettings
    {
        [Tooltip("Music multiplier at the instant damage is taken; 0.6 is a 40 percent reduction.")]
        [Range(0, 1)] public float MusicGain = .6f;
        [Tooltip("Total unpaused duration of the damage duck and red vignette, including recovery.")]
        [Min(0)] public float Duration = .6f;
        [Tooltip("Time at peak strength before smoothly recovering.")]
        [Min(0)] public float HoldSeconds = .12f;
        [Range(0, 1)] public float EdgeOpacity = .28f;
        [Range(0, 1)] public float CenterOpacity = .045f;

        public float Strength(double age)
        {
            if (double.IsNaN(age) || age < 0 || age >= Duration || Duration <= 0) return 0;
            if (age <= HoldSeconds) return 1;
            float fraction = Mathf.Clamp01((float)((age - HoldSeconds) / Math.Max(1e-6, Duration - HoldSeconds)));
            return 1 - fraction * fraction * (3 - 2 * fraction);
        }

        public float MusicMultiplier(double age) => 1 - (1 - MusicGain) * Strength(age);

        public void Validate()
        {
            if (float.IsNaN(Duration) || float.IsInfinity(Duration) || Duration < 0)
                throw new ArgumentOutOfRangeException(nameof(Duration));
            if (float.IsNaN(HoldSeconds) || float.IsInfinity(HoldSeconds) || HoldSeconds < 0 || HoldSeconds > Duration)
                throw new ArgumentOutOfRangeException(nameof(HoldSeconds));
            RequireUnit(MusicGain, nameof(MusicGain));
            RequireUnit(EdgeOpacity, nameof(EdgeOpacity));
            RequireUnit(CenterOpacity, nameof(CenterOpacity));
            if (CenterOpacity > EdgeOpacity) throw new ArgumentOutOfRangeException(nameof(CenterOpacity));
        }

        private static void RequireUnit(float value, string name)
        {
            if (float.IsNaN(value) || value < 0 || value > 1) throw new ArgumentOutOfRangeException(name);
        }
    }
}
